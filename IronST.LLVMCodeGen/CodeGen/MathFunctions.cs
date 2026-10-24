using IronST.IRBuilder.IR;
using IronST.Common.Types;
using IronST.Frontend.AST;
using IronST.Common.Diagnostics;
using IronST.LLVMCodeGen.Exceptions;

namespace IronST.LLVMCodeGen.CodeGen;

/// <summary>
/// IEC 标准数学函数实现。
/// 通过声明 extern C 数学库函数 + 参数提升到 LREAL 实现。
/// </summary>
public static class MathFunctions
{
    /// <summary>
    /// ST 函数名 → C extern 函数名的映射。
    /// </summary>
    private static readonly Dictionary<string, string> _externNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SQRT"] = "sqrt",
        ["LN"]   = "log",
        ["LOG"]  = "log10",
        ["EXP"]  = "exp",
        ["SIN"]  = "sin",
        ["COS"]  = "cos",
        ["TAN"]  = "tan",
        ["ASIN"] = "asin",
        ["ACOS"] = "acos",
        ["ATAN"] = "atan",
    };

    public static Value Emit(
        CodeGenContext ctx,
        ExpressionEmitter exprEmitter,
        string name,
        List<CallParamNode> arguments,
        SourceSpan span)
    {
        return name switch
        {
            "ABS" => EmitABS(ctx, exprEmitter, arguments, span),

            "SQRT" or "LN" or "LOG" or "EXP" or
            "SIN"  or "COS" or "TAN" or
            "ASIN" or "ACOS" or "ATAN"
                => EmitUnaryMath(ctx, exprEmitter, name, arguments, span),

            "ATAN2" => EmitATAN2(ctx, exprEmitter, arguments, span),
            "EXPT"  => EmitEXPT(ctx, exprEmitter, arguments, span),

            _ => throw new CodeGenException($"未实现的数学函数: {name}",
                ctx.Module.SourceFilename ?? "", span.Line),
        };
    }

    // ════════════════════════════════════════════
    //  ABS — 整数 IR 内联 / 浮点 extern fabs
    // ════════════════════════════════════════════

    private static Value EmitABS(
        CodeGenContext ctx, ExpressionEmitter exprEmitter,
        List<CallParamNode> args, SourceSpan span)
    {
        var argVal = exprEmitter.Emit(args[0].Value);
        var argType = args[0].Value.ResolvedType ?? IECType.DInt;

        if (argType.IsInteger)
        {
            // (x < 0) ? (0 - x) : x
            var zero = new ConstantInt(ctx.TypeMapper.Map(argType), 0);
            var cmp = ctx.Builder.CreateICmp(ICmpPredicate.Slt, argVal, zero, "abs.cmp");
            var neg = ctx.Builder.CreateSub(zero, argVal, "abs.neg");
            var sel = new SelectInst(cmp, neg, argVal, "abs.result");
            ctx.Builder.AddInstruction(sel);
            return sel;
        }

        if (argType.IsReal)
        {
            var fabsFunc = EnsureExternDecl(ctx, "fabs",
                IrType.Double, [IrType.Double]);
            var argD = exprEmitter.EnsureType(argVal, argType, IECType.LReal);
            return ctx.Builder.CreateCall(fabsFunc.FunctionType, fabsFunc, [argD], "fabs");
        }

        throw new CodeGenException("ABS: 不支持的参数类型",
            ctx.Module.SourceFilename ?? "", span.Line);
    }

    // ════════════════════════════════════════════
    //  单参数浮点数学函数
    // ════════════════════════════════════════════

    private static Value EmitUnaryMath(
        CodeGenContext ctx, ExpressionEmitter exprEmitter,
        string name, List<CallParamNode> args, SourceSpan span)
    {
        if (!_externNames.TryGetValue(name, out var externName))
            throw new CodeGenException($"未知的数学函数: {name}",
                ctx.Module.SourceFilename ?? "", span.Line);

        var argVal = exprEmitter.Emit(args[0].Value);
        var argType = args[0].Value.ResolvedType ?? IECType.LReal;
        var argD = exprEmitter.EnsureType(argVal, argType, IECType.LReal);

        var func = EnsureExternDecl(ctx, externName, IrType.Double, [IrType.Double]);
        return ctx.Builder.CreateCall(func.FunctionType, func, [argD], name.ToLowerInvariant());
    }

    // ════════════════════════════════════════════
    //  ATAN2(y, x)
    // ════════════════════════════════════════════

    private static Value EmitATAN2(
        CodeGenContext ctx, ExpressionEmitter exprEmitter,
        List<CallParamNode> args, SourceSpan span)
    {
        var arg0Val = exprEmitter.Emit(args[0].Value);
        var arg1Val = exprEmitter.Emit(args[1].Value);
        var arg0Type = args[0].Value.ResolvedType ?? IECType.LReal;
        var arg1Type = args[1].Value.ResolvedType ?? IECType.LReal;

        var yD = exprEmitter.EnsureType(arg0Val, arg0Type, IECType.LReal);
        var xD = exprEmitter.EnsureType(arg1Val, arg1Type, IECType.LReal);

        var func = EnsureExternDecl(ctx, "atan2",
            IrType.Double, [IrType.Double, IrType.Double]);
        return ctx.Builder.CreateCall(func.FunctionType, func, [yD, xD], "atan2");
    }

    // ════════════════════════════════════════════
    //  EXPT(base, exponent) → pow
    // ════════════════════════════════════════════

    private static Value EmitEXPT(
        CodeGenContext ctx, ExpressionEmitter exprEmitter,
        List<CallParamNode> args, SourceSpan span)
    {
        var baseVal = exprEmitter.Emit(args[0].Value);
        var expVal  = exprEmitter.Emit(args[1].Value);
        var baseType = args[0].Value.ResolvedType ?? IECType.LReal;
        var expType  = args[1].Value.ResolvedType ?? IECType.LReal;

        var baseD = exprEmitter.EnsureType(baseVal, baseType, IECType.LReal);
        var expD  = exprEmitter.EnsureType(expVal, expType, IECType.LReal);

        var func = EnsureExternDecl(ctx, "pow",
            IrType.Double, [IrType.Double, IrType.Double]);
        return ctx.Builder.CreateCall(func.FunctionType, func, [baseD, expD], "pow");
    }

    // ════════════════════════════════════════════
    //  extern 声明缓存
    // ════════════════════════════════════════════

    private static Function EnsureExternDecl(
        CodeGenContext ctx, string externName,
        IrType retType, IrType[] paramTypes)
    {
        if (ctx.TryResolveFunction(externName, out var existing))
            return existing;

        var ft = new FunctionType(retType, paramTypes, false);
        var func = new Function(externName, ft, LinkageType.External);
        ctx.Module.AddFunction(func);
        ctx.RegisterFunction(externName, func);
        return func;
    }
}