using IronST.Common.Diagnostics;
using IronST.Common.Symbol;
using IronST.Common.Types;
using IronST.Frontend.AST;
using IronST.IRBuilder.IR;
using IronST.LLVMCodeGen.Exceptions;
using AstBinOp = IronST.Frontend.AST.BinaryOp;
using AstUnaryOp = IronST.Frontend.AST.UnaryOp;
using IrBinOp = IronST.IRBuilder.IR.BinaryOperator;
using IrBuilder = IronST.IRBuilder.IR.IRBuilder;

namespace IronST.LLVMCodeGen.CodeGen;

public sealed class ExpressionEmitter
{
    private readonly CodeGenContext _ctx;
    private int _stringCounter;

    public ExpressionEmitter(CodeGenContext ctx)
    {
        _ctx = ctx;
    }

    // ════════════════════════════════════════════
    //  公共入口
    // ════════════════════════════════════════════

    public Value Emit(ExpressionNode expr)
    {
        var value = expr switch
        {
            LiteralExprNode lit => EmitLiteral(lit),
            TypedLiteralExprNode tl => EmitTypedLiteral(tl),
            VariableExprNode varExpr => EmitVariableLoad(varExpr),
            BinaryExprNode bin => EmitBinaryExpr(bin),
            UnaryExprNode unary => EmitUnaryExpr(unary),
            FunctionCallExprNode call => EmitFunctionCallExpr(call),
            _ => throw new CodeGenException(
                $"未支持的表达式类型: {expr.GetType().Name}",
                _ctx.Module.SourceFilename ?? "",
                expr.Span.Line
            ),
        };

        AttachDebug(value);
        return value;
    }

    public Value EmitAddress(ExpressionNode lvalue)
    {
        return lvalue switch
        {
            VariableExprNode varExpr => ComputeVariableAddress(varExpr),
            _ => throw new CodeGenException(
                $"不支持的赋值目标: {lvalue.GetType().Name}",
                _ctx.Module.SourceFilename ?? "",
                lvalue.Span.Line
            ),
        };
    }

    public Value EmitCall(string name, List<CallParamNode> arguments, SourceSpan span) =>
        EmitCallImpl(name, arguments, span);

    // ════════════════════════════════════════════
    //  字面量
    // ════════════════════════════════════════════

    private Value EmitLiteral(LiteralExprNode lit)
    {
        var resolvedType = lit.ResolvedType ?? IECType.DInt;
        var irType = _ctx.TypeMapper.Map(resolvedType);

        return lit.Kind switch
        {
            LiteralKind.Integer or LiteralKind.BasedInteger => new ConstantInt(
                irType,
                lit.ParsedValue is long l ? l : 0
            ),
            LiteralKind.Real => new ConstantFP(irType, lit.ParsedValue is double d ? d : 0.0),
            LiteralKind.Boolean => new ConstantInt(
                IrType.Int1,
                lit.ParsedValue is bool b && b ? 1 : 0
            ),
            LiteralKind.String => EmitStringConstant(lit.ParsedValue as string ?? ""),

            // ═══ 修改：使用 TimeLiteralParser 的解析结果 ═══
            LiteralKind.Time or LiteralKind.Date or LiteralKind.TimeOfDay or LiteralKind.DateTime =>
                new ConstantInt(IrType.Int64, (long)(lit.ParsedValue ?? 0L)),

            _ => throw new CodeGenException($"未支持的字面量种类: {lit.Kind}"),
        };
    }

    private Value EmitTypedLiteral(TypedLiteralExprNode tl)
    {
        var resolvedType =
            IECType.LookupBuiltin(tl.TypeName) ?? _ctx.GetTypeDef(tl.TypeName) ?? IECType.DInt;

        var irType = _ctx.TypeMapper.Map(resolvedType);

        if (resolvedType.IsInteger)
        {
            if (long.TryParse(tl.RawValue, out var intVal))
                return new ConstantInt(irType, intVal);
        }
        if (resolvedType.IsReal)
        {
            if (double.TryParse(tl.RawValue, out var floatVal))
                return new ConstantFP(irType, floatVal);
        }
        if (resolvedType.Kind == IECType.KindTag.BOOL)
        {
            bool bVal = string.Equals(tl.RawValue, "TRUE", StringComparison.OrdinalIgnoreCase);
            return new ConstantInt(IrType.Int1, bVal ? 1 : 0);
        }

        throw new CodeGenException($"无法解析类型化字面量: {tl.TypeName}#{tl.RawValue}");
    }

    private Value EmitStringConstant(string value)
    {
        var globalName = $"str.{++_stringCounter}";
        var gv = _ctx.Module.AddGlobalString(globalName, value);
        _ctx.RegisterGlobalString(globalName, gv);
        return gv;
    }

    // ════════════════════════════════════════════
    //  变量访问
    // ════════════════════════════════════════════

    private Value EmitVariableLoad(VariableExprNode varExpr)
    {
        if (varExpr.Segments.Count == 0)
            throw new CodeGenException(
                "空变量表达式",
                _ctx.Module.SourceFilename ?? "",
                varExpr.Span.Line
            );

        // ═══ 枚举成员常量检查（含 null 回退）═══
        if (TryEmitEnumConstant(varExpr, out var enumValue))
            return enumValue;

        // 快速路径：单段、无后缀 → 简单变量读取
        if (varExpr.Segments.Count == 1 && varExpr.Segments[0].Suffixes.Count == 0)
        {
            var name = varExpr.Segments[0].Name;
            var alloca = _ctx.GetAlloca(name);
            var resolvedType = varExpr.ResolvedType ?? _ctx.GetVarType(name);
            var irType = _ctx.TypeMapper.Map(resolvedType);
            return _ctx.Builder.CreateLoad(irType, alloca, $"{name}.ld");
        }

        // 复杂路径：计算地址后加载
        var ptr = ComputeVariableAddress(varExpr);
        var resultType = varExpr.ResolvedType;
        if (resultType == null)
            resultType = _ctx.GetVarType(varExpr.Segments[0].Name);
        var resultIrType = _ctx.TypeMapper.Map(resultType);
        return _ctx.Builder.CreateLoad(resultIrType, ptr, "val");
    }

    /// <summary>
    /// 尝试将变量表达式作为枚举成员常量发射。
    /// 处理两种情况：
    /// 1. ResolvedType 已设置为 ENUM（标准路径）
    /// 2. ResolvedType 为 null，但第一段是已注册的枚举类型名（回退路径）
    /// </summary>
    private bool TryEmitEnumConstant(VariableExprNode varExpr, out Value value)
    {
        value = null!;

        IECType? enumType = varExpr.ResolvedType;

        // 回退：ResolvedType 为 null 时，检查第一段是否为枚举类型名
        if (
            (enumType == null || enumType.Kind != IECType.KindTag.ENUM)
            && varExpr.Segments.Count >= 2
        )
        {
            var typeDef = _ctx.GetTypeDef(varExpr.Segments[0].Name);
            if (typeDef is { Kind: IECType.KindTag.ENUM })
                enumType = typeDef;
        }

        if (
            enumType is not { Kind: IECType.KindTag.ENUM }
            || enumType.EnumMemberValues is not { } members
        )
            return false;

        var memberName = varExpr.Segments[^1].Name;
        if (!members.TryGetValue(memberName, out var val))
            return false;

        value = new ConstantInt(IrType.Int32, val);
        return true;
    }

    private Value ComputeVariableAddress(VariableExprNode varExpr)
    {
        var firstName = varExpr.Segments[0].Name;
        Value currentPtr = _ctx.GetAlloca(firstName);
        IECType currentType = _ctx.GetVarType(firstName);

        foreach (var suffix in varExpr.Segments[0].Suffixes)
            (currentPtr, currentType) = ApplySuffix(currentPtr, currentType, suffix);

        for (int i = 1; i < varExpr.Segments.Count; i++)
        {
            var seg = varExpr.Segments[i];

            var structIrType = _ctx.TypeMapper.Map(currentType);
            var fieldIdx = _ctx.TypeMapper.GetFieldIndex(currentType, seg.Name);
            currentPtr = _ctx.Builder.CreateGEP(
                structIrType,
                currentPtr,
                [new ConstantInt(IrType.Int32, 0), new ConstantInt(IrType.Int32, fieldIdx)],
                "field",
                false
            );
            currentType = _ctx.TypeMapper.GetFieldType(currentType, seg.Name);

            foreach (var suffix in seg.Suffixes)
                (currentPtr, currentType) = ApplySuffix(currentPtr, currentType, suffix);
        }

        return currentPtr;
    }

    private (Value ptr, IECType type) ApplySuffix(
        Value currentPtr,
        IECType currentType,
        VariableSuffixNode suffix
    )
    {
        if (suffix.IsDereference)
        {
            var loadedPtr = _ctx.Builder.CreateLoad(IrType.Pointer, currentPtr, "deref.ptr");
            return (loadedPtr, currentType.ReferencedType!);
        }

        if (suffix.Subscripts is { Count: > 0 })
        {
            var arrayIrType = _ctx.TypeMapper.Map(currentType);
            var indices = new List<Value> { new ConstantInt(IrType.Int32, 0) };

            for (int d = 0; d < suffix.Subscripts.Count; d++)
            {
                var idxExpr = suffix.Subscripts[d];
                var idxValue = Emit(idxExpr);

                if (currentType.Dimensions is { Count: > 0 } && d < currentType.Dimensions.Count)
                {
                    int lowerBound = currentType.Dimensions[d].Lower;
                    if (lowerBound != 0)
                    {
                        var idxIrType = _ctx.TypeMapper.Map(idxExpr.ResolvedType ?? IECType.DInt);
                        idxValue = _ctx.Builder.CreateSub(
                            idxValue,
                            new ConstantInt(idxIrType, lowerBound),
                            "idx.adj"
                        );
                    }
                }
                indices.Add(idxValue);
            }

            var resultPtr = _ctx.Builder.CreateGEP(
                arrayIrType,
                currentPtr,
                indices.ToArray(),
                "gep",
                false
            );
            return (resultPtr, currentType.ElementType!);
        }

        return (currentPtr, currentType);
    }

    // ════════════════════════════════════════════
    //  二元运算
    // ════════════════════════════════════════════

    private Value EmitBinaryExpr(BinaryExprNode bin)
    {
        if (
            bin.Op
            is AstBinOp.Eq
                or AstBinOp.Neq
                or AstBinOp.Lt
                or AstBinOp.Gt
                or AstBinOp.Lte
                or AstBinOp.Gte
        )
            return EmitComparison(bin);

        var left = Emit(bin.Left);
        var right = Emit(bin.Right);

        var resultType = ResolveExprType(bin);

        if (resultType.IsInteger)
        {
            EnsureSameNumericDomain(left, right, bin);
            return bin.Op switch
            {
                AstBinOp.Add => _ctx.Builder.CreateAdd(left, right, "add"),
                AstBinOp.Sub => _ctx.Builder.CreateSub(left, right, "sub"),
                AstBinOp.Mul => _ctx.Builder.CreateMul(left, right, "mul"),
                AstBinOp.Div => _ctx.Builder.CreateSDiv(left, right, "sdiv"),
                AstBinOp.Mod => CreateSRem(left, right),
                AstBinOp.And => _ctx.Builder.CreateAnd(left, right, "and"),
                AstBinOp.Or => _ctx.Builder.CreateOr(left, right, "or"),
                AstBinOp.Xor => _ctx.Builder.CreateXor(left, right, "xor"),
                AstBinOp.Power => EmitIntPower(left, right, bin.Left.ResolvedType ?? IECType.DInt),
                _ => throw new CodeGenException($"未支持的整数运算: {bin.Op}"),
            };
        }

        if (resultType.IsReal)
        {
            // 源⑥：实数运算的**两个**操作数必须同为实数。整数操作数未提升时，
            // 结果类型会随左操作数退化成 i16（实测 `fdiv i16 %sum.ld, 2`），
            // 存进 float 槽后读出垃圾值。两边都要提升。
            left = PromoteToReal(left, bin.Left, resultType);
            right = PromoteToReal(right, bin.Right, resultType);
            EnsureSameNumericDomain(left, right, bin);

            return bin.Op switch
            {
                AstBinOp.Add => _ctx.Builder.CreateFAdd(left, right, "fadd"),
                AstBinOp.Sub => _ctx.Builder.CreateFSub(left, right, "fsub"),
                AstBinOp.Mul => _ctx.Builder.CreateFMul(left, right, "fmul"),
                AstBinOp.Div => CreateFDiv(left, right),
                AstBinOp.Mod => CreateFRem(left, right),
                AstBinOp.Power => EmitFloatPower(left, right),
                _ => throw new CodeGenException($"未支持的浮点运算: {bin.Op}"),
            };
        }

        if (resultType.Kind == IECType.KindTag.BOOL)
        {
            return bin.Op switch
            {
                AstBinOp.And => _ctx.Builder.CreateAnd(left, right, "and"),
                AstBinOp.Or => _ctx.Builder.CreateOr(left, right, "or"),
                AstBinOp.Xor => _ctx.Builder.CreateXor(left, right, "xor"),
                _ => throw new CodeGenException($"未支持的布尔运算: {bin.Op}"),
            };
        }

        throw new CodeGenException($"不支持的二元运算类型: {resultType.Kind}");
    }

    private IECType ResolveExprType(ExpressionNode expr)
    {
        if (expr.ResolvedType != null)
            return expr.ResolvedType;

        if (expr is VariableExprNode varExpr && varExpr.Segments.Count > 0)
        {
            var name = varExpr.Segments[0].Name;
            if (_ctx.TryGetVarType(name, out var type))
                return type;
        }

        return IECType.DInt;
    }

    private Value EmitComparison(BinaryExprNode bin)
    {
        var leftType = ResolveExprType(bin.Left);
        var rightType = ResolveExprType(bin.Right);

        // A1/F5：结构体按值比较。前后端分工：
        //   前端负责「同类型 / 只用 = 与 <> / 字段类型可比较」的裁定；
        //   这里负责把比较**降级成逐字段的标量比较**，因此字节码侧
        //   永远不会出现拿两个结构体基槽号去做 IEQ/BEQ 的路径。
        if (leftType.Kind == IECType.KindTag.STRUCT || rightType.Kind == IECType.KindTag.STRUCT)
            return EmitStructComparison(bin, leftType, rightType);

        // 源⑥：整数 ↔ 实数混合比较必须**统一到实数域**。
        //
        // 后端的比较指令是**强类型**的：IEQ/INE 只比 IntValue、FEQ 只比 FloatValue。
        // 一侧是 float、另一侧是 int 时，两边比的是**不同字段** ——
        // `5 = 5.0` 会因 5.0 的 IntValue 字段是 0 而得 FALSE。
        // 统一到实数域后两侧都是 float，比较才有意义。
        if (IsNumericValue(leftType)
            && IsNumericValue(rightType)
            && leftType.IsReal != rightType.IsReal)
        {
            var realType = leftType.IsReal ? leftType : rightType;
            var leftReal = EnsureType(Emit(bin.Left), leftType, realType);
            var rightReal = EnsureType(Emit(bin.Right), rightType, realType);
            return EmitRealComparison(bin.Op, leftReal, rightReal);
        }

        var left = Emit(bin.Left);
        var right = Emit(bin.Right);

        if (
            leftType.IsInteger
            || leftType.Kind == IECType.KindTag.BOOL
            || leftType.Kind == IECType.KindTag.ENUM
        )
        {
            var pred = bin.Op switch
            {
                AstBinOp.Eq => ICmpPredicate.Eq,
                AstBinOp.Neq => ICmpPredicate.Ne,
                AstBinOp.Lt => ICmpPredicate.Slt,
                AstBinOp.Gt => ICmpPredicate.Sgt,
                AstBinOp.Lte => ICmpPredicate.Sle,
                AstBinOp.Gte => ICmpPredicate.Sge,
                _ => throw new CodeGenException($"未支持的比较: {bin.Op}"),
            };
            return _ctx.Builder.CreateICmp(pred, left, right, "cmp");
        }

        if (leftType.IsReal)
            return EmitRealComparison(bin.Op, left, right);

        throw new CodeGenException($"不支持的比较类型: {leftType.Kind}");
    }

    /// <summary>实数比较（两侧都已是实数）。</summary>
    private Value EmitRealComparison(AstBinOp op, Value left, Value right)
    {
        var pred = op switch
        {
            AstBinOp.Eq => FCmpPredicate.Oeq,
            AstBinOp.Neq => FCmpPredicate.One,
            AstBinOp.Lt => FCmpPredicate.Olt,
            AstBinOp.Gt => FCmpPredicate.Ogt,
            AstBinOp.Lte => FCmpPredicate.Ole,
            AstBinOp.Gte => FCmpPredicate.Oge,
            _ => throw new CodeGenException($"未支持的比较: {op}"),
        };
        return _ctx.Builder.CreateFCmp(pred, left, right, "fcmp");
    }

    // ════════════════════════════════════════════
    //  结构体按值比较（A1/F5）
    // ════════════════════════════════════════════

    /// <summary>
    /// 结构体 <c>=</c> / <c>&lt;&gt;</c> 的值语义比较。
    ///
    /// <para>
    /// <b>为什么按地址逐字段展开，而不是对结构体 SSA 值做 extractvalue：</b>
    /// 字节码后端把结构体/数组<b>扁平化</b>到 VM 栈槽，聚合“值”在寄存器里
    /// 只是基槽号（见 <c>FunctionEmitter.FlatSlotCount</c>），并不是
    /// <c>ValueTag.Struct</c>。因此唯一同时在 IR 与字节码两侧都成立的表示，
    /// 是“取两个操作数的地址，再按字段/元素 GEP + load 出标量”。
    /// </para>
    /// <para>
    /// <b>为什么放进独立的辅助函数：</b> 字节码后端的寄存器分配器在一个函数内
    /// <b>只增不减</b>（<c>_nextReg</c> 从 R8 起，到 250 为止）。如果比较序列
    /// 内联在调用者里，一个几十叶的结构体会把调用者自己的寄存器预算吃光 ——
    /// 表现为「Out of registers while materializing ...」。生成一个
    /// <c>i1 (ptr, ptr)</c> 的私有辅助函数后，调用者每处比较只花两个实参 +
    /// 一个返回值寄存器，比较序列在辅助函数自己的预算里展开。
    /// </para>
    /// <para>
    /// 首版只接受可寻址左值（变量、字段、数组元素）。结构体返回值等非左值
    /// 需要先落到临时 alloca，暂不做 —— 代码生成在这里明确报错，
    /// 不会静默给出错误比较结果。
    /// </para>
    /// </summary>
    private Value EmitStructComparison(BinaryExprNode bin, IECType leftType, IECType rightType)
    {
        var file = _ctx.Module.SourceFilename ?? "";
        var line = bin.Span.Line;

        if (bin.Op is not (AstBinOp.Eq or AstBinOp.Neq))
            throw new CodeGenException($"结构体只支持 = 和 <> 比较，不支持 {bin.Op}", file, line);

        if (
            leftType.Kind != IECType.KindTag.STRUCT
            || rightType.Kind != IECType.KindTag.STRUCT
            || !string.Equals(leftType.Name, rightType.Name, StringComparison.OrdinalIgnoreCase)
        )
            throw new CodeGenException(
                $"结构体比较要求两侧类型相同：'{leftType.Name}' 与 '{rightType.Name}'",
                file,
                line
            );

        if (bin.Left is not VariableExprNode || bin.Right is not VariableExprNode)
            throw new CodeGenException(
                "结构体比较要求两侧都是可寻址的变量、字段或数组元素",
                file,
                line
            );

        var leftPtr = EmitAddress(bin.Left);
        var rightPtr = EmitAddress(bin.Right);
        var irType = _ctx.TypeMapper.Map(leftType);
        var helper = GetOrCreateStructCompareHelper(
            leftType,
            irType,
            equals: bin.Op == AstBinOp.Eq,
            bin.Span
        );

        return _ctx.Builder.CreateCall(
            helper.FunctionType,
            helper,
            [leftPtr, rightPtr],
            "struct.cmp"
        );
    }

    /// <summary>
    /// 取得（或生成）某个结构体 + 比较方向的比较辅助函数。
    /// 缓存键含结构体名与方向：<c>=</c> 与 <c>&lt;&gt;</c> 的浮点语义不同
    /// （<c>Oeq</c> vs <c>One</c>），不能共用同一个函数再取反。
    /// </summary>
    private Function GetOrCreateStructCompareHelper(
        IECType structType,
        IrType irType,
        bool equals,
        SourceSpan span
    )
    {
        var key = $"{structType.Name}|{(equals ? "eq" : "ne")}";
        if (_ctx.TryGetStructCompareHelper(key, out var cached))
            return cached;

        var helper = new Function(
            _ctx.NextStructCompareHelperName(),
            new FunctionType(IrType.Int1, [IrType.Pointer, IrType.Pointer], false),
            LinkageType.Internal
        );
        helper.Parameters[0].Name = "lhs";
        helper.Parameters[1].Name = "rhs";

        var entry = new BasicBlock("entry");
        helper.AppendBasicBlock(entry);
        var builder = new IrBuilder(entry);

        var result = EmitAggregateCompare(
            builder,
            helper.Parameters[0],
            irType,
            helper.Parameters[1],
            equals,
            span
        );
        builder.CreateRet(result);

        _ctx.Module.AddFunction(helper);
        _ctx.RegisterStructCompareHelper(key, helper);
        return helper;
    }

    /// <summary>
    /// 递归比较两个同类型聚合的地址。返回单个 i1：
    /// <paramref name="equality"/> 为 true 时“全部相等”，false 时“存在不等”。
    /// </summary>
    private Value EmitAggregateCompare(
        IrBuilder builder,
        Value leftPtr,
        IrType irType,
        Value rightPtr,
        bool equality,
        SourceSpan span
    )
    {
        switch (irType)
        {
            case NamedStructType named when IsStringNamedType(named):
                throw new CodeGenException(
                    $"结构体比较暂不支持 {named.Name} 字段",
                    _ctx.Module.SourceFilename ?? "",
                    span.Line
                );

            case NamedStructType named:
                if (named.Body is null)
                    throw new CodeGenException(
                        $"结构体类型 '{named.Name}' 缺少布局，无法比较",
                        _ctx.Module.SourceFilename ?? "",
                        span.Line
                    );
                return EmitStructBodyCompare(
                    builder,
                    leftPtr,
                    named,
                    named.Body,
                    rightPtr,
                    equality,
                    span
                );

            case StructType st:
                return EmitStructBodyCompare(builder, leftPtr, st, st, rightPtr, equality, span);

            case ArrayType at:
                return EmitArrayCompare(builder, leftPtr, at, rightPtr, equality, span);

            case IntegerType:
            case FloatType:
                return EmitScalarCompare(builder, leftPtr, irType, rightPtr, equality);

            case PointerType:
                throw new CodeGenException(
                    "结构体比较暂不支持 REFERENCE 字段",
                    _ctx.Module.SourceFilename ?? "",
                    span.Line
                );

            default:
                throw new CodeGenException(
                    $"结构体比较暂不支持字段类型: {irType.ToIRString()}",
                    _ctx.Module.SourceFilename ?? "",
                    span.Line
                );
        }
    }

    private Value EmitStructBodyCompare(
        IrBuilder builder,
        Value leftPtr,
        IrType containerIrType,
        StructType body,
        Value rightPtr,
        bool equality,
        SourceSpan span
    )
    {
        Value result = BoolConstant(equality);

        for (int i = 0; i < body.ElementTypes.Count; i++)
        {
            var leftField = builder.CreateGEP(
                containerIrType,
                leftPtr,
                [Int32Constant(0), Int32Constant(i)],
                "cmp.l.field",
                false
            );
            var rightField = builder.CreateGEP(
                containerIrType,
                rightPtr,
                [Int32Constant(0), Int32Constant(i)],
                "cmp.r.field",
                false
            );
            var fieldResult = EmitAggregateCompare(
                builder,
                leftField,
                body.ElementTypes[i],
                rightField,
                equality,
                span
            );

            result = equality
                ? builder.CreateAnd(result, fieldResult, "cmp.and")
                : builder.CreateOr(result, fieldResult, "cmp.or");
        }

        return result;
    }

    private Value EmitArrayCompare(
        IrBuilder builder,
        Value leftPtr,
        ArrayType arrayType,
        Value rightPtr,
        bool equality,
        SourceSpan span
    )
    {
        Value result = BoolConstant(equality);

        // ST 数组是定长数组；Length == 0 只可能来自不完整/未知维度，
        // 按“空聚合”处理（= 恒真、<> 恒假），与全量展开的语义一致。
        for (int i = 0; i < arrayType.Length; i++)
        {
            var leftElem = builder.CreateGEP(
                arrayType,
                leftPtr,
                [Int32Constant(0), Int32Constant(i)],
                "cmp.l.elem",
                false
            );
            var rightElem = builder.CreateGEP(
                arrayType,
                rightPtr,
                [Int32Constant(0), Int32Constant(i)],
                "cmp.r.elem",
                false
            );
            var elemResult = EmitAggregateCompare(
                builder,
                leftElem,
                arrayType.ElementType,
                rightElem,
                equality,
                span
            );

            result = equality
                ? builder.CreateAnd(result, elemResult, "cmp.and")
                : builder.CreateOr(result, elemResult, "cmp.or");
        }

        return result;
    }

    private Value EmitScalarCompare(
        IrBuilder builder,
        Value leftPtr,
        IrType irType,
        Value rightPtr,
        bool equality
    )
    {
        var left = builder.CreateLoad(irType, leftPtr, "cmp.l");
        var right = builder.CreateLoad(irType, rightPtr, "cmp.r");

        if (irType is FloatType)
            return builder.CreateFCmp(
                equality ? FCmpPredicate.Oeq : FCmpPredicate.One,
                left,
                right,
                "cmp.f"
            );

        return builder.CreateICmp(
            equality ? ICmpPredicate.Eq : ICmpPredicate.Ne,
            left,
            right,
            "cmp.i"
        );
    }

    private static ConstantInt BoolConstant(bool value) => new(IrType.Int1, value ? 1 : 0);

    private static ConstantInt Int32Constant(long value) => new(IrType.Int32, value);

    /// <summary>
    /// 字符串在 IR 里是命名结构体 <c>%STRING_n</c> / <c>%WSTRING_n</c>
    /// （<c>TypeMapper.MapString</c>）；结构体比较首版拒绝这类字段。
    /// </summary>
    private static bool IsStringNamedType(NamedStructType type) =>
        type.Name.StartsWith("STRING_", StringComparison.Ordinal)
        || type.Name.StartsWith("WSTRING_", StringComparison.Ordinal);

    // ════════════════════════════════════════════
    //  幂运算
    // ════════════════════════════════════════════

    private Value EmitIntPower(Value left, Value right, IECType operandType)
    {
        var powFunc = EnsurePowDeclaration();
        var leftD = InsertCast(CastInst.CreateSIToFP(left, IrType.Double, "left.fp"));
        var rightD = InsertCast(CastInst.CreateSIToFP(right, IrType.Double, "right.fp"));
        var result = _ctx.Builder.CreateCall(powFunc.FunctionType, powFunc, [leftD, rightD], "pow");
        var targetIrType = _ctx.TypeMapper.Map(operandType);
        return InsertCast(CastInst.CreateFPToSI(result, targetIrType, "pow.int"));
    }

    private Value EmitFloatPower(Value left, Value right)
    {
        var powFunc = EnsurePowDeclaration();
        var leftD = EnsureDouble(left);
        var rightD = EnsureDouble(right);
        return _ctx.Builder.CreateCall(powFunc.FunctionType, powFunc, [leftD, rightD], "pow");
    }

    private Function EnsurePowDeclaration()
    {
        if (_ctx.TryResolveFunction("pow", out var existing))
            return existing;

        var funcType = new FunctionType(IrType.Double, [IrType.Double, IrType.Double], false);
        var func = new Function("pow", funcType, LinkageType.External);
        _ctx.Module.AddFunction(func);
        _ctx.RegisterFunction("pow", func);
        return func;
    }

    private static Value EnsureDouble(Value v)
    {
        if (v is ConstantFP cf)
            return cf.Type == IrType.Double ? v : new ConstantFP(IrType.Double, cf.Value);
        return v;
    }

    // ════════════════════════════════════════════
    //  一元运算
    // ════════════════════════════════════════════

    private Value EmitUnaryExpr(UnaryExprNode unary)
    {
        var operand = Emit(unary.Operand);

        return unary.Op switch
        {
            AstUnaryOp.Neg when unary.Operand.ResolvedType!.IsInteger => _ctx.Builder.CreateSub(
                new ConstantInt(_ctx.TypeMapper.Map(unary.Operand.ResolvedType!), 0),
                operand,
                "neg"
            ),

            AstUnaryOp.Neg when unary.Operand.ResolvedType!.IsReal => _ctx.Builder.CreateFSub(
                new ConstantFP(_ctx.TypeMapper.Map(unary.Operand.ResolvedType!), 0.0),
                operand,
                "fneg"
            ),

            AstUnaryOp.Not => _ctx.Builder.CreateXor(
                new ConstantInt(IrType.Int1, 1),
                operand,
                "not"
            ),

            _ => throw new CodeGenException($"未支持的一元运算: {unary.Op}"),
        };
    }

    // ════════════════════════════════════════════
    //  函数调用
    // ════════════════════════════════════════════

    private Value EmitFunctionCallExpr(FunctionCallExprNode call) =>
        EmitCallImpl(call.Name, call.Arguments, call.Span);

    private Value EmitCallImpl(string name, List<CallParamNode> arguments, SourceSpan span)
    {
        // FB 实例调用检查
        if (IsFBInstanceCall(name))
            return EmitFBInstanceCall(name, arguments);

        // ═══ 新增：内置函数分发（类型转换 / 数学 / 字符串）═══
        // 宿主注入优先：调用方已为这些内建函数提供宿主实现（HCALL）时，不内联展开，
        // 改走下面的声明调用路径（对 IR 里已注册的外部库声明发 Call，后端转 HCALL）。
        if (_ctx.HostInjectedBuiltins is null || !_ctx.HostInjectedBuiltins.Contains(name))
        {
            var builtinResult = BuiltinFunctions.TryEmit(_ctx, this, name, arguments, span);
            if (builtinResult != null)
                return builtinResult;
        }

        // 用户定义函数（原有逻辑）
        var func = _ctx.ResolveFunction(name);
        var formalParams = _ctx.GetFormalParams(name);
        var args = ResolveCallArguments(arguments, formalParams);

        return _ctx.Builder.CreateCall(
            func.FunctionType,
            func,
            args.ToArray(),
            func.FunctionType.ReturnType == IrType.Void ? null : "call"
        );
    }

    private bool IsFBInstanceCall(string name)
    {
        if (!_ctx.TryGetVarType(name, out var varType))
            return false;
        return _ctx.TryResolveFunction(varType.Name, out _);
    }

    private Value EmitFBInstanceCall(string instanceName, List<CallParamNode> arguments)
    {
        var varType = _ctx.GetVarType(instanceName);
        var fbFunc = _ctx.ResolveFunction(varType.Name);
        var instancePtr = _ctx.GetAlloca(instanceName);
        var instanceType = _ctx.GetFBInstanceType(varType.Name);
        var formalParams = _ctx.GetFormalParams(varType.Name);

        // 调用实参 = `__instance` + 显式提供的 VAR_OUTPUT / VAR_IN_OUT 地址。
        // VAR_INPUT **不在**实参列表里：它由调用方**直写实例字段**（见 EmitFBArgument），
        // 从而随实例存储保留（§6.3.10 / §6.3.11）。
        var args = new List<Value> { instancePtr };

        if (arguments.Count == 0)
            return _ctx.Builder.CreateCall(fbFunc.FunctionType, fbFunc, args.ToArray(), null);

        bool hasNamed = arguments.Any(a => a.Name != null);

        if (!hasNamed)
        {
            // 位置实参按 FB **完整成员序**（IN + OUT + IN_OUT，声明序）映射。
            for (int i = 0; i < arguments.Count; i++)
            {
                var formal = i < formalParams.Count ? formalParams[i] : null;
                EmitFBArgument(instanceType, instancePtr, formal, arguments[i], args);
            }
        }
        else
        {
            foreach (var arg in arguments)
            {
                int idx = formalParams.FindIndex(p =>
                    string.Equals(p.Name, arg.Name, StringComparison.OrdinalIgnoreCase)
                );
                if (idx < 0)
                    throw new CodeGenException($"未找到参数: {arg.Name}");
                EmitFBArgument(instanceType, instancePtr, formalParams[idx], arg, args);
            }
        }

        return _ctx.Builder.CreateCall(fbFunc.FunctionType, fbFunc, args.ToArray(), null);
    }

    /// <summary>
    /// 处理单个 FB 调用实参：
    /// · 显式提供的 <c>VAR_INPUT</c> ⇒ 调用方直写实例字段（不占调用实参）；
    /// · <c>VAR_OUTPUT</c> / <c>VAR_IN_OUT</c> ⇒ 取地址传参；
    /// · 外部 FB（实例结构体无该字段）的按值 <c>IN</c> ⇒ 保留旧路（取值传参）。
    /// </summary>
    private void EmitFBArgument(
        IECType? instanceType,
        Value instancePtr,
        VariableSymbol? formal,
        CallParamNode arg,
        List<Value> args)
    {
        if (formal is not null
            && formal.Scope == VarScope.Input
            && instanceType?.Fields is { } fields
            && fields.ContainsKey(formal.Name))
        {
            var fieldIrType = _ctx.TypeMapper.Map(formal.Type);
            var fieldIndex = _ctx.TypeMapper.GetFieldIndex(instanceType, formal.Name);
            var structIrType = _ctx.TypeMapper.Map(instanceType);
            var fieldPtr = _ctx.Builder.CreateGEP(
                structIrType, instancePtr,
                [
                    new ConstantInt(IrType.Int32, 0),
                    new ConstantInt(IrType.Int32, fieldIndex),
                ],
                $"{formal.Name}.in", false);
            _ctx.Builder.CreateStore(
                Emit(arg.Value), fieldPtr, CodeGenContext.DefaultAlign(fieldIrType));
            return;
        }

        if (arg.IsOutputBinding || formal?.Scope is VarScope.Output or VarScope.InOut)
            args.Add(EmitAddress(arg.Value));
        else
            args.Add(Emit(arg.Value));
    }

    private List<Value> ResolveCallArguments(
        List<CallParamNode> arguments,
        List<VariableSymbol> formalParams
    )
    {
        if (arguments.Count == 0)
            return [];

        bool hasNamed = arguments.Any(a => a.Name != null);

        if (!hasNamed)
        {
            var result = new List<Value>();
            for (int i = 0; i < arguments.Count; i++)
            {
                var arg = arguments[i];
                var formal = i < formalParams.Count ? formalParams[i] : null;

                if (arg.IsOutputBinding || formal?.Scope is VarScope.Output or VarScope.InOut)
                    result.Add(EmitAddress(arg.Value));
                else
                    result.Add(Emit(arg.Value));
            }
            return result;
        }

        var matched = new Value?[formalParams.Count];
        foreach (var arg in arguments)
        {
            int idx = formalParams.FindIndex(p =>
                string.Equals(p.Name, arg.Name, StringComparison.OrdinalIgnoreCase)
            );
            if (idx < 0)
                throw new CodeGenException($"未找到参数: {arg.Name}");

            var formal = formalParams[idx];
            if (arg.IsOutputBinding || formal.Scope is VarScope.Output or VarScope.InOut)
                matched[idx] = EmitAddress(arg.Value);
            else
                matched[idx] = Emit(arg.Value);
        }
        return matched.ToList()!;
    }

    // ════════════════════════════════════════════
    //  类型转换
    // ════════════════════════════════════════════

    /// <summary>
    /// 数值边界判定：整数（**不含 BOOL**）与实数算「数值」。
    /// BOOL 虽在 <see cref="IECType.IsInteger"/> 内，但它是 1 位逻辑量，不参与数值转换。
    /// </summary>
    private static bool IsNumericValue(IECType type) =>
        type.IsReal || (type.IsInteger && type.Kind != IECType.KindTag.BOOL);

    /// <summary>
    /// 把整数操作数提升为结果的实数类型（源⑥）。非整数操作数原样返回。
    /// </summary>
    private Value PromoteToReal(Value value, ExpressionNode operand, IECType realType)
    {
        var operandType = ResolveExprType(operand);
        if (!IsNumericValue(operandType) || !operandType.IsInteger)
            return value;
        return EnsureType(value, operandType, realType);
    }

    /// <summary>
    /// IR 自洽校验：二元运算的两个操作数必须同属一个数值域（缺陷登记簿 §3.9 附带缺口）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 修复前存在过 <c>fdiv i16 %sum.ld, 2</c> 这种**操作数类型不一致**的非法 IR ——
    /// 它一路流到后端，全程无拦截，直到端到端跑出垃圾值才被撞出来。
    /// </para>
    /// <para>
    /// 这里在**发射点**当场拦住：整数运算的两个操作数必须都是整数，
    /// 实数运算的两个操作数必须都是浮点。
    /// </para>
    /// </remarks>
    private void EnsureSameNumericDomain(Value left, Value right, BinaryExprNode bin)
    {
        var lt = left.Type;
        var rt = right.Type;

        bool leftReal = lt is FloatType;
        bool rightReal = rt is FloatType;
        if (leftReal == rightReal)
            return;

        throw new CodeGenException(
            $"二元运算的操作数类型不一致: {lt.ToIRString()} vs {rt.ToIRString()}",
            _ctx.Module.SourceFilename ?? "",
            bin.Span.Line
        );
    }

    /// <summary>
    /// 按**目标变量类型**转换赋值右值（源⑥）。
    ///
    /// <para>
    /// 只在「整数 ↔ 实数」这个边界上转换：
    /// · 整数 → 实数：<c>SIToFP</c> / <c>UIToFP</c>（否则混合算术按整数算）。
    /// · 实数 → 整数：<c>FPToSI</c> / <c>FPToUI</c>（截断向零）。
    /// </para>
    ///
    /// <para>
    /// 整数内部（<c>INT</c>↔<c>DINT</c>…）与实数内部（<c>REAL</c>↔<c>LREAL</c>）**不转换** ——
    /// VM 槽是统一 64 位且保留运行时 tag，位宽差异不影响值语义；
    /// 字节码后端对 <c>SExt</c>/<c>ZExt</c>/<c>Trunc</c>/<c>FPExt</c>/<c>FPTrunc</c>
    /// 也都是空操作，徒增指令、扰动产物字节。
    /// </para>
    /// </summary>
    public Value ConvertToTargetType(
        Value value,
        ExpressionNode valueExpr,
        IECType? targetType)
    {
        if (targetType is null || !IsNumericValue(targetType))
            return value;

        var valueType = ResolveExprType(valueExpr);
        if (!IsNumericValue(valueType))
            return value;

        // 同一侧（整数↔整数、实数↔实数）无需转换。
        if (targetType.IsReal == valueType.IsReal)
            return value;

        return EnsureType(value, valueType, targetType);
    }

    public Value EnsureType(Value value, IECType fromType, IECType toType)
    {
        if (fromType.Kind == toType.Kind)
            return value;

        var fromIr = _ctx.TypeMapper.Map(fromType);
        var toIr = _ctx.TypeMapper.Map(toType);

        if (fromType.IsInteger && toType.IsInteger)
        {
            if (toType.BitWidth > fromType.BitWidth)
                return fromType.IsSigned
                    ? InsertCast(CastInst.CreateSExt(value, toIr, "sext"))
                    : InsertCast(CastInst.CreateZExt(value, toIr, "zext"));
            if (toType.BitWidth < fromType.BitWidth)
                return InsertCast(CastInst.CreateTrunc(value, toIr, "trunc"));
            return value;
        }

        if (fromType.IsInteger && toType.IsReal)
            return fromType.IsSigned
                ? InsertCast(CastInst.CreateSIToFP(value, toIr, "sitofp"))
                : InsertCast(CastInst.CreateUIToFP(value, toIr, "uitofp"));

        if (fromType.IsReal && toType.IsInteger)
            return toType.IsSigned
                ? InsertCast(CastInst.CreateFPToSI(value, toIr, "fptosi"))
                : InsertCast(CastInst.CreateFPToUI(value, toIr, "fptoui"));

        if (fromType.IsReal && toType.IsReal)
            return toType.BitWidth > fromType.BitWidth
                ? InsertCast(CastInst.CreateFPExt(value, toIr, "fpext"))
                : InsertCast(CastInst.CreateFPTrunc(value, toIr, "fptrunc"));

        throw new CodeGenException($"不支持的类型转换: {fromType.Kind} → {toType.Kind}");
    }

    // ════════════════════════════════════════════
    //  IRBuilder 未直接暴露的二元指令
    // ════════════════════════════════════════════

    private Value CreateSRem(Value left, Value right)
    {
        var inst = IrBinOp.CreateSRem(left, right, "srem");
        _ctx.Builder.AddInstruction(inst);
        return inst;
    }

    private Value CreateFDiv(Value left, Value right)
    {
        var inst = IrBinOp.CreateFDiv(left, right, "fdiv");
        _ctx.Builder.AddInstruction(inst);
        return inst;
    }

    private Value CreateFRem(Value left, Value right)
    {
        var inst = IrBinOp.CreateFRem(left, right, "frem");
        _ctx.Builder.AddInstruction(inst);
        return inst;
    }

    // ════════════════════════════════════════════
    //  辅助
    // ════════════════════════════════════════════

    private void AttachDebug(Value value)
    {
        if (_ctx.Debug.IsEnabled && !_ctx.IsInPrologue && value is Instruction inst)
            _ctx.Debug.AttachLocation(inst);
    }

    private Value InsertCast(CastInst inst)
    {
        _ctx.Builder.AddInstruction(inst);
        return inst;
    }
}
