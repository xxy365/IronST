using IronST.IRBuilder.IR;
using IronST.Common.Types;
using IronST.Frontend.AST;
using IronST.Common.Diagnostics;
using IronST.LLVMCodeGen.Exceptions;

namespace IronST.LLVMCodeGen.CodeGen;

/// <summary>
/// IEC 标准类型转换函数实现。
/// INT(x) / REAL(x) / BOOL(x) 等。
/// </summary>
public static class TypeConversionFunctions
{
    public static Value Emit(
        CodeGenContext ctx,
        ExpressionEmitter exprEmitter,
        string name,
        List<CallParamNode> arguments,
        SourceSpan span)
    {
        if (arguments.Count == 0)
            throw new CodeGenException($"类型转换函数 {name} 缺少参数",
                ctx.Module.SourceFilename ?? "", span.Line);

        var argVal = exprEmitter.Emit(arguments[0].Value);
        var argType = arguments[0].Value.ResolvedType ?? IECType.DInt;

        var toType = IECType.LookupBuiltin(name);
        if (toType == null)
            throw new CodeGenException($"未知的类型转换函数: {name}",
                ctx.Module.SourceFilename ?? "", span.Line);

        // BOOL 特殊处理：任意类型 → i1 比较
        if (toType.Kind == IECType.KindTag.BOOL)
        {
            if (argType.IsInteger || argType.Kind == IECType.KindTag.BOOL)
            {
                var zero = new ConstantInt(ctx.TypeMapper.Map(argType), 0);
                return ctx.Builder.CreateICmp(ICmpPredicate.Ne, argVal, zero, "tobool");
            }
            if (argType.IsReal)
            {
                var zero = new ConstantFP(ctx.TypeMapper.Map(argType), 0.0);
                return ctx.Builder.CreateFCmp(FCmpPredicate.One, argVal, zero, "tobool");
            }
            throw new CodeGenException($"BOOL 转换不支持源类型: {argType.Kind}",
                ctx.Module.SourceFilename ?? "", span.Line);
        }

        // 通用数值转换：复用 EnsureType
        return exprEmitter.EnsureType(argVal, argType, toType);
    }
}