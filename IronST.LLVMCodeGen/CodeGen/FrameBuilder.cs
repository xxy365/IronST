using IronST.Common.Symbol;
using IronST.Common.Types;
using IronST.Frontend.AST;
using IronST.IRBuilder.IR;
using IRBuild = IronST.IRBuilder.IR.IRBuilder;

namespace IronST.LLVMCodeGen.CodeGen;

public static class FrameBuilder
{
    /// <summary>
    /// 为函数/程序/FB 构建序言。
    /// </summary>
    public static IRBuild BuildPrologue(
        CodeGenContext ctx,
        Function func,
        IReadOnlyList<VarDeclBlockNode> varBlocks,
        IECType? returnType,
        string? functionName,
        bool isFunctionBlock = false
    )
    {
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);

        var builder = new IRBuild(entry);
        ctx.Builder = builder;
        ctx.IsInPrologue = true;
        ctx.Debug.ClearLocation();

        int paramIndex = 0;

        // F3：局部显式初值**延后**到序言末尾统一发射。
        // 延后的理由：alloca 必须先全部创建并绑定，发射初值时才不会报「未绑定的变量」。
        // （前端目前按声明序解析符号，初值引用后声明的变量会先被 E2001 拦下 ——
        //   这里是代码生成侧的自我保护，不是对前端的放宽。）
        var pendingLocalInitializers = new List<
            (VarDeclItemNode Item, Value Alloca, IECType Type, IrType IrType)>();

        // 1. 返回值 alloca（非 void 函数）
        if (returnType != null && returnType.Kind != IECType.KindTag.VOID && functionName != null)
        {
            var retIrType = ctx.TypeMapper.Map(returnType);
            var retAlloca = builder.CreateAlloca(
                retIrType, null, functionName, CodeGenContext.DefaultAlign(retIrType));
            ctx.BindAlloca(functionName, retAlloca);
            ctx.BindVarType(functionName, returnType);
        }

        // 2. FB 实例指针（第一个参数）
        if (isFunctionBlock)
        {
            var instanceParam = func.Parameters[paramIndex++];
            ctx.BindAlloca("__instance", instanceParam);
        }

        // 3. 按 VarBlock 源码顺序处理
        foreach (var vb in varBlocks)
        {
            foreach (var item in vb.Items)
            {
                var resolvedType = ResolveItemType(ctx, item);

                switch (vb.Scope)
                {
                    case VarScope.Input:
                    {
                        if (isFunctionBlock)
                        {
                            // FB 的 VAR_INPUT 是**实例状态**：与 VAR 同路，经实例 GEP 访问，
                            // 不再从形参拷入临时 alloca（§6.3.10：省略 ⇒ 保留）。
                            foreach (var vn in item.Names)
                                BindFbInstanceField(ctx, builder, functionName!, vn.Name, resolvedType);
                        }
                        else
                        {
                            var irType = ctx.TypeMapper.Map(resolvedType);
                            foreach (var vn in item.Names)
                            {
                                var alloca = builder.CreateAlloca(
                                    irType, null, $"{vn.Name}.addr",
                                    CodeGenContext.DefaultAlign(irType));
                                var arg = func.Parameters[paramIndex++];
                                builder.CreateStore(arg, alloca, CodeGenContext.DefaultAlign(irType));
                                ctx.BindAlloca(vn.Name, alloca);
                                ctx.BindVarType(vn.Name, resolvedType);
                            }
                        }
                        break;
                    }

                    case VarScope.Output:
                    case VarScope.InOut:
                    {
                        foreach (var vn in item.Names)
                        {
                            var arg = func.Parameters[paramIndex++];
                            ctx.BindAlloca(vn.Name, arg);
                            ctx.BindVarType(vn.Name, resolvedType);
                        }
                        break;
                    }

                    case VarScope.Local:
                    case VarScope.Temp:
                    {
                        if (isFunctionBlock)
                        {
                            // FB VAR 成员：通过 GEP 从实例指针访问
                            foreach (var vn in item.Names)
                                BindFbInstanceField(ctx, builder, functionName!, vn.Name, resolvedType);
                        }
                        else
                        {
                            var irType = ctx.TypeMapper.Map(resolvedType);
                            foreach (var vn in item.Names)
                            {
                                var alloca = builder.CreateAlloca(
                                    irType, null, vn.Name,
                                    CodeGenContext.DefaultAlign(irType));
                                ctx.BindAlloca(vn.Name, alloca);
                                ctx.BindVarType(vn.Name, resolvedType);
                                // 若该局部量是 FB 实例，则在实例创建处初始化其 VAR_INPUT 字段为
                                // 类型默认值（首次调用前省略输入 ⇒ 读到类型默认值，§6.3.10）。
                                EmitInstanceInputDefaults(ctx, builder, vn.Name, alloca, resolvedType);
                                // F3：源码里的显式初始值写进局部槽（延后到序言末尾统一发射）。
                                pendingLocalInitializers.Add((item, alloca, resolvedType, irType));
                            }
                        }
                        break;
                    }

                    // ══════════════════════════════════════════════
                    //  新增：VAR_GLOBAL / VAR_EXTERNAL 处理
                    // ══════════════════════════════════════════════
                    case VarScope.Global:
                    case VarScope.External:
                    {
                        foreach (var vn in item.Names)
                        {
                            var gv = ctx.GetGlobalVariable(vn.Name);
                            ctx.BindAlloca(vn.Name, gv);
                            ctx.BindVarType(vn.Name, resolvedType);
                        }
                        break;
                    }
                }
            }
        }

        // F3：序言末尾统一发射局部初值（此时所有 alloca 都已绑定）。
        foreach (var (item, alloca, type, irType) in pendingLocalInitializers)
            EmitLocalInitializer(ctx, builder, item, alloca, type, irType);

        ctx.IsInPrologue = false;
        return builder;
    }

    /// <summary>
    /// F3：把变量的显式初始值（<c>VAR x : INT := 5;</c>）写进局部槽。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 发射位置是**序言**：每次进入函数都重新写一次，所以初值是“每次调用生效”，
    /// 而不是编译期常量折叠。
    /// </para>
    /// <para>
    /// 初值表达式是任意的（可以是其它变量），因此走完整的表达式发射路径；
    /// 右值按**目标变量类型**转换（整数 ↔ 实数边界，见 §3.9）。
    /// </para>
    /// </remarks>
    private static void EmitLocalInitializer(
        CodeGenContext ctx,
        IRBuild builder,
        VarDeclItemNode item,
        Value alloca,
        IECType declaredType,
        IrType irType)
    {
        // A10 层 1：无显式初始化时发射类型默认值（IEC 61131-3 语义）
        if (item.InitialValue is not ExpressionNode init)
        {
            // 没有显式初始值 → 按 IECType 发射类型默认值
            Constant defaultValue = GetTypeDefaultConstant(declaredType, irType);
            if (defaultValue != null)
            {
                builder.CreateStore(defaultValue, alloca, CodeGenContext.DefaultAlign(irType));
            }
            return;
        }

        var emitter = new ExpressionEmitter(ctx);
        var value = emitter.Emit(init);
        value = emitter.ConvertToTargetType(value, init, declaredType);
        builder.CreateStore(value, alloca, CodeGenContext.DefaultAlign(irType));
    }

    /// <summary>
    /// 把 FB 成员名绑定到 <c>GEP(__instance, 0, fieldIdx)</c> —— 实例存储中的字段地址。
    /// </summary>
    private static void BindFbInstanceField(
        CodeGenContext ctx, IRBuild builder, string fbName, string memberName, IECType memberType)
    {
        var fbInstanceType = ctx.GetFBInstanceType(fbName);
        if (fbInstanceType == null)
            return;

        var instancePtr = ctx.GetAlloca("__instance");
        var structIrType = ctx.TypeMapper.Map(fbInstanceType);
        var fieldIdx = ctx.TypeMapper.GetFieldIndex(fbInstanceType, memberName);
        var gepPtr = builder.CreateGEP(
            structIrType, instancePtr,
            [
                new ConstantInt(IrType.Int32, 0),
                new ConstantInt(IrType.Int32, fieldIdx),
            ],
            $"{memberName}.addr", false);
        ctx.BindAlloca(memberName, gepPtr);
        ctx.BindVarType(memberName, memberType);
    }

    /// <summary>
    /// 若 <paramref name="localType"/> 是 FB 实例，则在实例 alloca 处：
    /// <list type="bullet">
    /// <item>为每个 <c>VAR_INPUT</c> 字段发射「写类型默认值」的 store；</item>
    /// <item>（F3）为带**显式初值**的成员发射「写初值」的 store。</item>
    /// </list>
    ///
    /// <para>
    /// 两者都发生在**实例创建处**（调用方）—— 不是 FB 的序言。
    /// FB 的 VAR 成员是**实例状态**，每次调用重写会把跨调用的状态冲掉。
    /// </para>
    /// </summary>
    private static void EmitInstanceInputDefaults(
        CodeGenContext ctx, IRBuild builder, string varName, Value alloca, IECType localType)
    {
        var fbType = ctx.GetFBInstanceType(localType.Name);
        if (fbType == null)
            return;

        var structIrType = ctx.TypeMapper.Map(fbType);

        var inputFields = ctx.GetFBInputFields(localType.Name);
        if (inputFields is not null)
        {
            foreach (var (fieldIndex, fieldType) in inputFields)
            {
                var fieldIrType = ctx.TypeMapper.Map(fieldType);
                var fieldPtr = builder.CreateGEP(
                    structIrType, alloca,
                    [
                        new ConstantInt(IrType.Int32, 0),
                        new ConstantInt(IrType.Int32, fieldIndex),
                    ],
                    $"{varName}.{fieldIndex}.init", false);
                builder.CreateStore(
                    ZeroConstant(fieldIrType), fieldPtr, CodeGenContext.DefaultAlign(fieldIrType));
            }
        }

        // F3：成员的显式初值（编译期常量）。
        var varInits = ctx.GetFBVarInitializers(localType.Name);
        if (varInits is null)
            return;

        foreach (var (fieldIndex, fieldType, intValue, realValue, isReal) in varInits)
        {
            var fieldIrType = ctx.TypeMapper.Map(fieldType);
            var fieldPtr = builder.CreateGEP(
                structIrType, alloca,
                [
                    new ConstantInt(IrType.Int32, 0),
                    new ConstantInt(IrType.Int32, fieldIndex),
                ],
                $"{varName}.{fieldIndex}.varinit", false);

            Constant value = isReal
                ? new ConstantFP(fieldIrType, realValue)
                : new ConstantInt(fieldIrType, intValue);

            builder.CreateStore(value, fieldPtr, CodeGenContext.DefaultAlign(fieldIrType));
        }
    }

    private static Constant ZeroConstant(IrType type) =>
        type is IntegerType ? new ConstantInt(type, 0)
        : type == IrType.Float || type == IrType.Double ? new ConstantFP(type, 0.0)
        : type == IrType.Pointer ? new ConstantPointerNull()
        : new ConstantAggregateZero(type);

    /// <summary>A10 层 1：按 IECType 获取类型默认值常量（无显式初始化的局部变量发射该值）。</summary>
    private static Constant? GetTypeDefaultConstant(IECType declaredType, IrType irType)
    {
        switch (declaredType.Kind)
        {
            case IECType.KindTag.DINT:
            case IECType.KindTag.INT:
                return new ConstantInt(irType, 0);
            case IECType.KindTag.REAL:
                return new ConstantFP(irType, 0.0);
            case IECType.KindTag.BOOL:
                return new ConstantInt(irType, 0); // FALSE => 0
            case IECType.KindTag.STRING:
            case IECType.KindTag.WSTRING:
                // STRING 默认值：空字符串槽（由运行时初始化为空）
                return new ConstantPointerNull(); // 防御：用空指针表示空字符串，运行时处理 ?? ""
            default:
                return null; // 未知类型或结构体/数组：不发射默认值（由运行时或用户初始化处理）
        }
    }

    // ── 类型解析工具 ──

    private static IECType ResolveItemType(CodeGenContext ctx, VarDeclItemNode item) =>
        ResolveDataTypeNode(ctx, item.TypeName) ?? IECType.DInt;

    internal static IECType? ResolveDataTypeNode(CodeGenContext ctx, DataTypeNode? node) =>
        node switch
        {
            SimpleDataTypeNode s => ResolveSimpleDataType(ctx, s),
            ArrayDataTypeNode a => ResolveArrayType(ctx, a),
            ReferenceDataTypeNode r => IECType.MakeReference(
                ResolveTypeName(ctx, r.ReferencedTypeName) ?? IECType.DInt,
                r.ReferencedTypeName),
            _ => null,
        };

    private static IECType? ResolveSimpleDataType(CodeGenContext ctx, SimpleDataTypeNode s)
    {
        if (s.SizeParam != null)
        {
            int? size = EvaluateSizeParam(s.SizeParam);
            if (size.HasValue)
                return IECType.MakeString(
                    size.Value,
                    s.TypeName.Contains("WSTRING", StringComparison.OrdinalIgnoreCase));
        }
        return ResolveTypeName(ctx, s.TypeName);
    }

    private static IECType ResolveArrayType(CodeGenContext ctx, ArrayDataTypeNode a)
    {
        var element = ResolveDataTypeNode(ctx, a.ElementType) ?? IECType.DInt;
        var dims = new List<(int, int)>();
        foreach (var (lower, upper) in a.Dimensions)
            dims.Add((EvaluateIntLiteral(lower), EvaluateIntLiteral(upper)));
        return IECType.MakeArray(element, dims);
    }

    internal static IECType? ResolveTypeName(CodeGenContext ctx, string name) =>
        IECType.LookupBuiltin(name) ?? ctx.GetTypeDef(name);

    private static int? EvaluateSizeParam(ExpressionNode expr)
    {
        if (expr is LiteralExprNode lit)
            return lit.ParsedValue switch
            {
                long l => (int)l,
                int i => i,
                _ => null,
            };
        return null;
    }

    private static int EvaluateIntLiteral(ExpressionNode expr)
    {
        if (expr is LiteralExprNode lit)
            return lit.ParsedValue switch
            {
                long l => (int)l,
                int i => i,
                _ => 0,
            };
        return 0;
    }
}