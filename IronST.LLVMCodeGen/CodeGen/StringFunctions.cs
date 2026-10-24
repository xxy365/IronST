using IronST.IRBuilder.IR;
using IronST.Common.Types;
using IronST.Frontend.AST;
using IronST.Common.Diagnostics;
using IronST.LLVMCodeGen.Exceptions;

namespace IronST.LLVMCodeGen.CodeGen;

/// <summary>
/// IEC 标准字符串函数实现。
/// 字符串在 IR 中为 {i32, [N x i8]} 结构体。
/// 所有函数通过 GEP + memcpy/循环内联实现。
/// </summary>
public static class StringFunctions
{
    public static Value Emit(
        CodeGenContext ctx,
        ExpressionEmitter exprEmitter,
        string name,
        List<CallParamNode> arguments,
        SourceSpan span)
    {
        return name switch
        {
            "LEN"     => EmitLEN(ctx, exprEmitter, arguments, span),
            "LEFT"    => EmitLEFT(ctx, exprEmitter, arguments, span),
            "RIGHT"   => EmitRIGHT(ctx, exprEmitter, arguments, span),
            "MID"     => EmitMID(ctx, exprEmitter, arguments, span),
            "CONCAT"  => EmitCONCAT(ctx, exprEmitter, arguments, span),
            "FIND"    => EmitFIND(ctx, exprEmitter, arguments, span),
            "INSERT"  => EmitINSERT(ctx, exprEmitter, arguments, span),
            "DELETE"  => EmitDELETE(ctx, exprEmitter, arguments, span),
            "REPLACE" => EmitREPLACE(ctx, exprEmitter, arguments, span),
            _ => throw new CodeGenException($"未实现的字符串函数: {name}",
                ctx.Module.SourceFilename ?? "", span.Line),
        };
    }

    // ════════════════════════════════════════════
    //  LEN(input) → INT
    // ════════════════════════════════════════════

    private static Value EmitLEN(
        CodeGenContext ctx, ExpressionEmitter exprEmitter,
        List<CallParamNode> args, SourceSpan span)
    {
        var strPtr = EnsureStringPointer(ctx, exprEmitter, args[0].Value);
        var strIrType = GetStringIrType(ctx, args[0].Value);
        return GetStringLength(ctx, strPtr, strIrType);
    }

    // ════════════════════════════════════════════
    //  LEFT(input, length) → STRING
    // ════════════════════════════════════════════

    private static Value EmitLEFT(
        CodeGenContext ctx, ExpressionEmitter exprEmitter,
        List<CallParamNode> args, SourceSpan span)
    {
        var strPtr = EnsureStringPointer(ctx, exprEmitter, args[0].Value);
        var n = exprEmitter.Emit(args[1].Value);
        var strIrType = GetStringIrType(ctx, args[0].Value);

        var srcLen = GetStringLength(ctx, strPtr, strIrType);
        var copyLen = SelectMin(ctx, n, srcLen, "left.len");

        var resAlloca = ctx.Builder.CreateAlloca(strIrType, null, "left.res",
            CodeGenContext.DefaultAlign(strIrType));

        var srcData = GetStringDataPtr(ctx, strPtr, strIrType);
        var resData = GetStringDataPtr(ctx, resAlloca, strIrType);

        EmitMemcpy(ctx, resData, srcData, copyLen);
        SetStringLength(ctx, resAlloca, strIrType, copyLen);

        return ctx.Builder.CreateLoad(strIrType, resAlloca, "left.val");
    }

    // ════════════════════════════════════════════
    //  RIGHT(input, length) → STRING
    // ════════════════════════════════════════════

    private static Value EmitRIGHT(
        CodeGenContext ctx, ExpressionEmitter exprEmitter,
        List<CallParamNode> args, SourceSpan span)
    {
        var strPtr = EnsureStringPointer(ctx, exprEmitter, args[0].Value);
        var n = exprEmitter.Emit(args[1].Value);
        var strIrType = GetStringIrType(ctx, args[0].Value);

        var srcLen = GetStringLength(ctx, strPtr, strIrType);
        var copyLen = SelectMin(ctx, n, srcLen, "right.len");
        var srcOffset = ctx.Builder.CreateSub(srcLen, copyLen, "right.off");

        var resAlloca = ctx.Builder.CreateAlloca(strIrType, null, "right.res",
            CodeGenContext.DefaultAlign(strIrType));

        var srcData = GetStringDataPtr(ctx, strPtr, strIrType);
        var srcOffsetPtr = ctx.Builder.CreateGEP(IrType.Int8, srcData,
            [srcOffset], "right.src", false);
        var resData = GetStringDataPtr(ctx, resAlloca, strIrType);

        EmitMemcpy(ctx, resData, srcOffsetPtr, copyLen);
        SetStringLength(ctx, resAlloca, strIrType, copyLen);

        return ctx.Builder.CreateLoad(strIrType, resAlloca, "right.val");
    }

    // ════════════════════════════════════════════
    //  MID(input, length, start) → STRING
    // ════════════════════════════════════════════

    private static Value EmitMID(
        CodeGenContext ctx, ExpressionEmitter exprEmitter,
        List<CallParamNode> args, SourceSpan span)
    {
        var strPtr = EnsureStringPointer(ctx, exprEmitter, args[0].Value);
        var reqLen = exprEmitter.Emit(args[1].Value);
        var startPos = exprEmitter.Emit(args[2].Value); // 1-based
        var strIrType = GetStringIrType(ctx, args[0].Value);

        var srcLen = GetStringLength(ctx, strPtr, strIrType);

        // 0-based offset
        var offset = ctx.Builder.CreateSub(startPos,
            new ConstantInt(IrType.Int32, 1), "mid.off");
        var availLen = ctx.Builder.CreateSub(srcLen, offset, "mid.avail");
        var copyLen = SelectMin(ctx, reqLen, availLen, "mid.len");

        var resAlloca = ctx.Builder.CreateAlloca(strIrType, null, "mid.res",
            CodeGenContext.DefaultAlign(strIrType));

        var srcData = GetStringDataPtr(ctx, strPtr, strIrType);
        var srcOffsetPtr = ctx.Builder.CreateGEP(IrType.Int8, srcData,
            [offset], "mid.src", false);
        var resData = GetStringDataPtr(ctx, resAlloca, strIrType);

        EmitMemcpy(ctx, resData, srcOffsetPtr, copyLen);
        SetStringLength(ctx, resAlloca, strIrType, copyLen);

        return ctx.Builder.CreateLoad(strIrType, resAlloca, "mid.val");
    }

    // ════════════════════════════════════════════
    //  CONCAT(in1, in2) → STRING
    // ════════════════════════════════════════════

    private static Value EmitCONCAT(
        CodeGenContext ctx, ExpressionEmitter exprEmitter,
        List<CallParamNode> args, SourceSpan span)
    {
        var ptr1 = EnsureStringPointer(ctx, exprEmitter, args[0].Value);
        var ptr2 = EnsureStringPointer(ctx, exprEmitter, args[1].Value);
        var strIrType1 = GetStringIrType(ctx, args[0].Value);
        var strIrType2 = GetStringIrType(ctx, args[1].Value);

        var len1 = GetStringLength(ctx, ptr1, strIrType1);
        var len2 = GetStringLength(ctx, ptr2, strIrType2);
        var totalLen = ctx.Builder.CreateAdd(len1, len2, "cat.len");

        var resIrType = strIrType1;
        var resAlloca = ctx.Builder.CreateAlloca(resIrType, null, "cat.res",
            CodeGenContext.DefaultAlign(resIrType));

        var data1 = GetStringDataPtr(ctx, ptr1, strIrType1);
        var data2 = GetStringDataPtr(ctx, ptr2, strIrType2);
        var resData = GetStringDataPtr(ctx, resAlloca, resIrType);

        EmitMemcpy(ctx, resData, data1, len1);

        var resOffPtr = ctx.Builder.CreateGEP(IrType.Int8, resData,
            [len1], "cat.off", false);
        EmitMemcpy(ctx, resOffPtr, data2, len2);

        SetStringLength(ctx, resAlloca, resIrType, totalLen);
        return ctx.Builder.CreateLoad(resIrType, resAlloca, "cat.val");
    }

    // ════════════════════════════════════════════
    //  FIND(in1, in2) → INT  (1-based, 0 = not found)
    // ════════════════════════════════════════════

    private static Value EmitFIND(
        CodeGenContext ctx, ExpressionEmitter exprEmitter,
        List<CallParamNode> args, SourceSpan span)
    {
        var ptr1 = EnsureStringPointer(ctx, exprEmitter, args[0].Value);
        var ptr2 = EnsureStringPointer(ctx, exprEmitter, args[1].Value);
        var strIrType1 = GetStringIrType(ctx, args[0].Value);
        var strIrType2 = GetStringIrType(ctx, args[1].Value);

        var len1 = GetStringLength(ctx, ptr1, strIrType1);
        var len2 = GetStringLength(ctx, ptr2, strIrType2);
        var data1 = GetStringDataPtr(ctx, ptr1, strIrType1);
        var data2 = GetStringDataPtr(ctx, ptr2, strIrType2);

        var func = ctx.CurrentFunction;

        var resultAlloca = ctx.Builder.CreateAlloca(IrType.Int32, null, "find.res", 4);
        ctx.Builder.CreateStore(new ConstantInt(IrType.Int32, 0), resultAlloca);

        var iAlloca = ctx.Builder.CreateAlloca(IrType.Int32, null, "find.i", 4);
        ctx.Builder.CreateStore(new ConstantInt(IrType.Int32, 0), iAlloca);

        var condBlock  = new BasicBlock(ctx.NameGen.NextBlock("find.cond"));
        var bodyBlock  = new BasicBlock(ctx.NameGen.NextBlock("find.body"));
        var foundBlock = new BasicBlock(ctx.NameGen.NextBlock("find.found"));
        var incBlock   = new BasicBlock(ctx.NameGen.NextBlock("find.inc"));
        var endBlock   = new BasicBlock(ctx.NameGen.NextBlock("find.end"));

        func.AppendBasicBlock(condBlock);
        func.AppendBasicBlock(bodyBlock);
        func.AppendBasicBlock(foundBlock);
        func.AppendBasicBlock(incBlock);
        func.AppendBasicBlock(endBlock);

        ctx.Builder.CreateBr(condBlock);

        // cond: i <= len1 - len2
        ctx.Builder.InsertBlock = condBlock;
        var iVal = ctx.Builder.CreateLoad(IrType.Int32, iAlloca, "find.i.val");
        var maxI = ctx.Builder.CreateSub(len1, len2, "find.maxi");
        var cmp = ctx.Builder.CreateICmp(ICmpPredicate.Sle, iVal, maxI, "find.cmp");
        ctx.Builder.CreateCondBr(cmp, bodyBlock, endBlock);

        // body: memcmp(data1 + i, data2, len2) == 0
        ctx.Builder.InsertBlock = bodyBlock;
        var srcOffPtr = ctx.Builder.CreateGEP(IrType.Int8, data1,
            [iVal], "find.src", false);
        var memcmpFunc = EnsureMemcmpDeclaration(ctx);
        var memcmpResult = ctx.Builder.CreateCall(memcmpFunc.FunctionType, memcmpFunc,
            [srcOffPtr, data2, len2], "memcmp");
        var isMatch = ctx.Builder.CreateICmp(ICmpPredicate.Eq,
            memcmpResult, new ConstantInt(IrType.Int32, 0), "find.ismatch");
        ctx.Builder.CreateCondBr(isMatch, foundBlock, incBlock);

        // found: result = i + 1 (1-based)
        ctx.Builder.InsertBlock = foundBlock;
        var oneBased = ctx.Builder.CreateAdd(iVal,
            new ConstantInt(IrType.Int32, 1), "find.pos");
        ctx.Builder.CreateStore(oneBased, resultAlloca);
        ctx.Builder.CreateBr(endBlock);

        // inc: i++, goto cond
        ctx.Builder.InsertBlock = incBlock;
        var iNext = ctx.Builder.CreateAdd(iVal,
            new ConstantInt(IrType.Int32, 1), "find.next");
        ctx.Builder.CreateStore(iNext, iAlloca);
        ctx.Builder.CreateBr(condBlock);

        // end
        ctx.Builder.InsertBlock = endBlock;
        return ctx.Builder.CreateLoad(IrType.Int32, resultAlloca, "find.result");
    }

    // ════════════════════════════════════════════
    //  INSERT(in1, in2, position) → STRING
    // ════════════════════════════════════════════

    private static Value EmitINSERT(
        CodeGenContext ctx, ExpressionEmitter exprEmitter,
        List<CallParamNode> args, SourceSpan span)
    {
        var ptr1 = EnsureStringPointer(ctx, exprEmitter, args[0].Value);
        var ptr2 = EnsureStringPointer(ctx, exprEmitter, args[1].Value);
        var pos = exprEmitter.Emit(args[2].Value); // 1-based
        var strIrType1 = GetStringIrType(ctx, args[0].Value);
        var strIrType2 = GetStringIrType(ctx, args[1].Value);

        var len1 = GetStringLength(ctx, ptr1, strIrType1);
        var len2 = GetStringLength(ctx, ptr2, strIrType2);
        var totalLen = ctx.Builder.CreateAdd(len1, len2, "ins.total");

        var offset = ctx.Builder.CreateSub(pos,
            new ConstantInt(IrType.Int32, 1), "ins.off");

        var resIrType = strIrType1;
        var resAlloca = ctx.Builder.CreateAlloca(resIrType, null, "ins.res",
            CodeGenContext.DefaultAlign(resIrType));

        var data1 = GetStringDataPtr(ctx, ptr1, strIrType1);
        var data2 = GetStringDataPtr(ctx, ptr2, strIrType2);
        var resData = GetStringDataPtr(ctx, resAlloca, resIrType);

        // Part 1: in1[0..offset-1]
        EmitMemcpy(ctx, resData, data1, offset);

        // Part 2: in2[0..len2-1]
        var resP2 = ctx.Builder.CreateGEP(IrType.Int8, resData,
            [offset], "ins.p2", false);
        EmitMemcpy(ctx, resP2, data2, len2);

        // Part 3: in1[offset..end]
        var srcP3 = ctx.Builder.CreateGEP(IrType.Int8, data1,
            [offset], "ins.src3", false);
        var tailLen = ctx.Builder.CreateSub(len1, offset, "ins.tail");
        var resP3Off = ctx.Builder.CreateAdd(offset, len2, "ins.p3off");
        var resP3 = ctx.Builder.CreateGEP(IrType.Int8, resData,
            [resP3Off], "ins.p3", false);
        EmitMemcpy(ctx, resP3, srcP3, tailLen);

        SetStringLength(ctx, resAlloca, resIrType, totalLen);
        return ctx.Builder.CreateLoad(resIrType, resAlloca, "ins.val");
    }

    // ════════════════════════════════════════════
    //  DELETE(input, length, start) → STRING
    // ════════════════════════════════════════════

    private static Value EmitDELETE(
        CodeGenContext ctx, ExpressionEmitter exprEmitter,
        List<CallParamNode> args, SourceSpan span)
    {
        var strPtr = EnsureStringPointer(ctx, exprEmitter, args[0].Value);
        var delLen = exprEmitter.Emit(args[1].Value);
        var startPos = exprEmitter.Emit(args[2].Value); // 1-based
        var strIrType = GetStringIrType(ctx, args[0].Value);

        var srcLen = GetStringLength(ctx, strPtr, strIrType);
        var offset = ctx.Builder.CreateSub(startPos,
            new ConstantInt(IrType.Int32, 1), "del.off");
        var tailStart = ctx.Builder.CreateAdd(offset, delLen, "del.tstart");
        var tailLen = ctx.Builder.CreateSub(srcLen, tailStart, "del.tlen");
        var resLen = ctx.Builder.CreateSub(srcLen, delLen, "del.rlen");

        var resAlloca = ctx.Builder.CreateAlloca(strIrType, null, "del.res",
            CodeGenContext.DefaultAlign(strIrType));
        var srcData = GetStringDataPtr(ctx, strPtr, strIrType);
        var resData = GetStringDataPtr(ctx, resAlloca, strIrType);

        // Part 1: in1[0..offset-1]
        EmitMemcpy(ctx, resData, srcData, offset);

        // Part 2: in1[delEnd..end]
        var srcTail = ctx.Builder.CreateGEP(IrType.Int8, srcData,
            [tailStart], "del.stail", false);
        var resTail = ctx.Builder.CreateGEP(IrType.Int8, resData,
            [offset], "del.rtail", false);
        EmitMemcpy(ctx, resTail, srcTail, tailLen);

        SetStringLength(ctx, resAlloca, strIrType, resLen);
        return ctx.Builder.CreateLoad(strIrType, resAlloca, "del.val");
    }

    // ════════════════════════════════════════════
    //  REPLACE(in1, in2, length, start) → STRING
    // ════════════════════════════════════════════

    private static Value EmitREPLACE(
        CodeGenContext ctx, ExpressionEmitter exprEmitter,
        List<CallParamNode> args, SourceSpan span)
    {
        var ptr1 = EnsureStringPointer(ctx, exprEmitter, args[0].Value);
        var ptr2 = EnsureStringPointer(ctx, exprEmitter, args[1].Value);
        var repLen = exprEmitter.Emit(args[2].Value);
        var startPos = exprEmitter.Emit(args[3].Value); // 1-based
        var strIrType1 = GetStringIrType(ctx, args[0].Value);
        var strIrType2 = GetStringIrType(ctx, args[1].Value);

        var len1 = GetStringLength(ctx, ptr1, strIrType1);
        var len2 = GetStringLength(ctx, ptr2, strIrType2);

        var offset = ctx.Builder.CreateSub(startPos,
            new ConstantInt(IrType.Int32, 1), "rep.off");
        var tailStart = ctx.Builder.CreateAdd(offset, repLen, "rep.tstart");
        var tailLen = ctx.Builder.CreateSub(len1, tailStart, "rep.tlen");
        var midLen = ctx.Builder.CreateAdd(offset, len2, "rep.midlen");
        var resLen = ctx.Builder.CreateAdd(midLen, tailLen, "rep.rlen");

        var resIrType = strIrType1;
        var resAlloca = ctx.Builder.CreateAlloca(resIrType, null, "rep.res",
            CodeGenContext.DefaultAlign(resIrType));
        var data1 = GetStringDataPtr(ctx, ptr1, strIrType1);
        var data2 = GetStringDataPtr(ctx, ptr2, strIrType2);
        var resData = GetStringDataPtr(ctx, resAlloca, resIrType);

        // Part 1: in1[0..offset-1]
        EmitMemcpy(ctx, resData, data1, offset);

        // Part 2: in2 (replacement)
        var resP2 = ctx.Builder.CreateGEP(IrType.Int8, resData,
            [offset], "rep.p2", false);
        EmitMemcpy(ctx, resP2, data2, len2);

        // Part 3: in1[repEnd..end]
        var srcP3 = ctx.Builder.CreateGEP(IrType.Int8, data1,
            [tailStart], "rep.src3", false);
        var resP3 = ctx.Builder.CreateGEP(IrType.Int8, resData,
            [midLen], "rep.p3", false);
        EmitMemcpy(ctx, resP3, srcP3, tailLen);

        SetStringLength(ctx, resAlloca, resIrType, resLen);
        return ctx.Builder.CreateLoad(resIrType, resAlloca, "rep.val");
    }

    // ════════════════════════════════════════════
    //  共用辅助方法
    // ════════════════════════════════════════════

    /// <summary>
    /// 获取字符串表达式的 IR 类型（NamedStructType）。
    /// </summary>
    private static IrType GetStringIrType(CodeGenContext ctx, ExpressionNode expr)
    {
        var iecType = expr.ResolvedType ?? IECType.DefaultString;
        return ctx.TypeMapper.Map(iecType);
    }

    /// <summary>
    /// 获取字符串的 ptr。变量取地址，字面量/全局变量直接返回。
    /// </summary>
    private static Value EnsureStringPointer(
        CodeGenContext ctx, ExpressionEmitter exprEmitter, ExpressionNode expr)
    {
        // 变量：直接取地址（避免加载整个结构体）
        if (expr is VariableExprNode)
            return exprEmitter.EmitAddress(expr);

        // 其他表达式：求值后判断
        var val = exprEmitter.Emit(expr);
        if (val is GlobalVariable || val is AllocaInst)
            return val;

        // 结构体值：alloca + store 获得指针
        var strType = expr.ResolvedType ?? IECType.DefaultString;
        var strIrType = ctx.TypeMapper.Map(strType);
        var tmp = ctx.Builder.CreateAlloca(strIrType, null, "str.tmp",
            CodeGenContext.DefaultAlign(strIrType));
        ctx.Builder.CreateStore(val, tmp);
        return tmp;
    }

    /// <summary>
    /// GEP 取字符串长度字段（字段 0）→ load。
    /// </summary>
    private static Value GetStringLength(
        CodeGenContext ctx, Value strPtr, IrType stringIrType)
    {
        var lenPtr = ctx.Builder.CreateGEP(stringIrType, strPtr,
            [new ConstantInt(IrType.Int32, 0), new ConstantInt(IrType.Int32, 0)],
            "len.ptr", false);
        return ctx.Builder.CreateLoad(IrType.Int32, lenPtr, "len");
    }

    /// <summary>
    /// GEP 取字符串数据区首字节指针（字段 1 的 [0] 元素）。
    /// </summary>
    private static Value GetStringDataPtr(
        CodeGenContext ctx, Value strPtr, IrType stringIrType)
    {
        return ctx.Builder.CreateGEP(stringIrType, strPtr,
            [
                new ConstantInt(IrType.Int32, 0),
                new ConstantInt(IrType.Int32, 1),
                new ConstantInt(IrType.Int32, 0),
            ],
            "data.ptr", false);
    }

    /// <summary>
    /// GEP 取字符串长度字段 → store 新值。
    /// </summary>
    private static void SetStringLength(
        CodeGenContext ctx, Value strPtr, IrType stringIrType, Value length)
    {
        var lenPtr = ctx.Builder.CreateGEP(stringIrType, strPtr,
            [new ConstantInt(IrType.Int32, 0), new ConstantInt(IrType.Int32, 0)],
            "len.ptr", false);
        ctx.Builder.CreateStore(length, lenPtr);
    }

    /// <summary>
    /// 调用 @llvm.memcpy.p0.p0.i32。
    /// </summary>
    private static void EmitMemcpy(
        CodeGenContext ctx, Value dst, Value src, Value len)
    {
        var memcpyFunc = EnsureMemcpyDeclaration(ctx);
        ctx.Builder.CreateCall(memcpyFunc.FunctionType, memcpyFunc,
            [dst, src, len, new ConstantInt(IrType.Int1, 0)], null);
    }

    /// <summary>
    /// select min(a, b) — 整数比较。
    /// </summary>
    private static Value SelectMin(CodeGenContext ctx, Value a, Value b, string hint = "min")
    {
        var cmp = ctx.Builder.CreateICmp(ICmpPredicate.Slt, a, b, $"{hint}.cmp");
        var sel = new SelectInst(cmp, a, b, hint);
        ctx.Builder.AddInstruction(sel);
        return sel;
    }

    // ════════════════════════════════════════════
    //  extern 声明
    // ════════════════════════════════════════════

    private static Function EnsureMemcpyDeclaration(CodeGenContext ctx)
    {
        const string name = "llvm.memcpy.p0.p0.i32";
        if (ctx.TryResolveFunction(name, out var existing))
            return existing;

        var ft = new FunctionType(IrType.Void,
            [IrType.Pointer, IrType.Pointer, IrType.Int32, IrType.Int1], false);
        var func = new Function(name, ft, LinkageType.External);
        ctx.Module.AddFunction(func);
        ctx.RegisterFunction(name, func);
        return func;
    }

    private static Function EnsureMemcmpDeclaration(CodeGenContext ctx)
    {
        const string name = "memcmp";
        if (ctx.TryResolveFunction(name, out var existing))
            return existing;

        var ft = new FunctionType(IrType.Int32,
            [IrType.Pointer, IrType.Pointer, IrType.Int32], false);
        var func = new Function(name, ft, LinkageType.External);
        ctx.Module.AddFunction(func);
        ctx.RegisterFunction(name, func);
        return func;
    }
}