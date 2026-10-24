using IronST.Common.Symbol;
using IronST.Common.Types;
using IronST.Frontend.Analysis;
using IronST.Frontend.AST;
using IronST.IRBuilder.IR;
using IronST.LLVMCodeGen.Debug;
using IronST.LLVMCodeGen.TypeMapping;

namespace IronST.LLVMCodeGen.CodeGen;

public sealed class IRGenerator
{
    private readonly IDebugEmitter _debug;

    /// <summary>宿主注入的内建函数名集合（<c>null</c> = 全部内联，既有行为）。</summary>
    private readonly IReadOnlySet<string>? _hostInjectedBuiltins;

    /// <param name="debug">调试发射器（<c>null</c> ⇒ <see cref="NullDebugEmitter"/>）。</param>
    /// <param name="hostInjectedBuiltins">
    /// 由宿主提供实现的内建函数名（如字符串标准库）。给定后，这些名字不再内联展开，
    /// 改发对声明的调用，由字节码后端转成 HCALL。为 <c>null</c> 时保持既有内联行为。
    /// </param>
    public IRGenerator(
        IDebugEmitter? debug = null,
        IEnumerable<string>? hostInjectedBuiltins = null)
    {
        _debug = debug ?? new NullDebugEmitter();
        _hostInjectedBuiltins = hostInjectedBuiltins is null
            ? null
            : new HashSet<string>(hostInjectedBuiltins, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 执行代码生成，返回完整的 IR Module。
    /// </summary>
    public Module Generate(AnalysisResult analysis)
    {
        var firstPou = analysis.AST.POUs.FirstOrDefault();
        var sourceFileName = firstPou?.Span.FileName ?? "unknown.st";
        var sourceDir = Path.GetDirectoryName(sourceFileName) ?? "";

        var module = new Module
        {
            SourceFilename = Path.GetFileName(sourceFileName),
            TargetDataLayout =
                "e-m:w-p270:32:32-p271:32:32-p272:64:64-"
                + "i64:64-i128:128-f80:128-n8:16:32:64-S128",
        };

        var typeMapper = new TypeMapper(module);
        var ctx = new CodeGenContext(module, typeMapper, _debug);
        ctx.HostInjectedBuiltins = _hostInjectedBuiltins;

        // ── 1. 调试：编译单元 ──
        _debug.Initialize(module, Path.GetFileName(sourceFileName), sourceDir);

        // ── 2. 注册自定义类型（TYPE 声明）──
        foreach (var td in analysis.AST.TypeDeclarations) {
            var sym = analysis.Symbols.Resolve(td.Name);
            if (sym is TypeSymbol ts) {
                ctx.RegisterTypeDef(td.Name, ts.Type);
                if (ts.Type.Kind == IECType.KindTag.STRUCT)
                    typeMapper.Map(ts.Type);
            }
        }

        // ── 3. 注册 FB 结构体类型 ──
        foreach (var pou in analysis.AST.POUs) {
            if (pou is FunctionBlockNode fb) {
                var fbStructType = BuildFBStructType(ctx, fb);
                ctx.RegisterFBInstanceType(fb.Name, fbStructType);
                ctx.RegisterTypeDef(fb.Name, fbStructType);
                typeMapper.Map(fbStructType);
                ctx.RegisterFBInputFields(fb.Name, BuildFBInputFields(ctx, fb, fbStructType, typeMapper));
                ctx.RegisterFBVarInitializers(
                    fb.Name, BuildFBVarInitializers(ctx, fb, fbStructType, typeMapper));
            }
        }

        // ── 3.5 注册全局变量（VAR_GLOBAL）──
        RegisterGlobalVariables(ctx, analysis);

        // ── 4. 第一遍：创建所有函数声明（建立前向引用）──
        //     只处理 AST 中有节点的 POU
        foreach (var pou in analysis.AST.POUs) {
            switch (pou) {
                case FunctionNode fn: {
                        var funcSym = (FunctionSymbol)analysis.Symbols.Resolve(fn.Name)!;
                        var func = CreateFunctionDeclaration(typeMapper, funcSym);
                        module.AddFunction(func);
                        ctx.RegisterFunction(fn.Name, func);
                        ctx.RegisterFormalParams(fn.Name, funcSym.Parameters);
                        break;
                    }
                case ProgramNode prog: {
                        var funcType = new FunctionType(IrType.Void, [], false);
                        var func = new Function(prog.Name, funcType, LinkageType.External);
                        module.AddFunction(func);
                        ctx.RegisterFunction(prog.Name, func);
                        ctx.RegisterFormalParams(prog.Name, []);
                        break;
                    }
                case FunctionBlockNode fb: {
                        var fbSym = (FunctionBlockSymbol)analysis.Symbols.Resolve(fb.Name)!;
                        var formalParams = BuildFBFormalParams(fb, fbSym);
                        var paramIrTypes = BuildFBParamIrTypesExcludingInput(formalParams);

                        var funcType = new FunctionType(IrType.Void, paramIrTypes, false);
                        var func = new Function(fb.Name, funcType, LinkageType.External);

                        func.Parameters[0].Name = "__instance";
                        NameFBFormalParams(func, formalParams);

                        module.AddFunction(func);
                        ctx.RegisterFunction(fb.Name, func);
                        ctx.RegisterFormalParams(fb.Name, formalParams);

                        foreach (var item in fb.BodyItems) {
                            if (item is FBActionItemNode actionItem) {
                                var actName = $"{fb.Name}.{actionItem.Action.Name}";
                                var actFunc = new Function(actName, funcType, LinkageType.External);

                                actFunc.Parameters[0].Name = "__instance";
                                NameFBFormalParams(actFunc, formalParams);

                                module.AddFunction(actFunc);
                                ctx.RegisterFunction(actName, actFunc);
                                ctx.RegisterFormalParams(actName, formalParams);
                            }
                        }
                        break;
                    }
            }
        }

        // ══════════════════════════════════════════════
        //  新增：4.5 注册外部库函数声明
        //  处理通过 libraries 注入、AST 中没有节点的 POU
        // ══════════════════════════════════════════════
        RegisterExternalLibraryFunctions(ctx, module, typeMapper, analysis);

        // ── 5. 第二遍：发射函数体 ──
        foreach (var pou in analysis.AST.POUs) {
            switch (pou) {
                case FunctionNode fn: EmitFunctionBody(ctx, fn); break;
                case ProgramNode prog: EmitProgramBody(ctx, prog); break;
                case FunctionBlockNode fb: EmitFBBody(ctx, fb); break;
            }
        }

        // ── 6. 调试：完成 ──
        _debug.Complete();

        return module;
    }

    // ══════════════════════════════════════════════
    //  新增：外部库函数注册
    // ══════════════════════════════════════════════

    /// <summary>
    /// 为通过 libraries 注入的、AST 中没有对应节点的 POU 创建 IR 函数声明。
    /// </summary>
    private static void RegisterExternalLibraryFunctions(
        CodeGenContext ctx,
        Module module,
        TypeMapper typeMapper,
        AnalysisResult analysis)
    {
        foreach (var pouInfo in analysis.POUs) {
            // 已通过 AST 注册的跳过
            if (ctx.TryResolveFunction(pouInfo.Name, out _))
                continue;

            switch (pouInfo.Kind) {
                case POUKind.Function:
                    RegisterExternalFunction(ctx, module, typeMapper, pouInfo);
                    break;

                case POUKind.FunctionBlock:
                    RegisterExternalFunctionBlock(ctx, module, typeMapper, pouInfo);
                    break;

                case POUKind.Program:
                    // Program 不会从外部注入，跳过
                    break;
            }
        }
    }

    private static void RegisterExternalFunction(
        CodeGenContext ctx,
        Module module,
        TypeMapper typeMapper,
        POUInfo pouInfo)
    {
        // 解析返回类型
        var retType = !string.IsNullOrEmpty(pouInfo.ReturnTypeName)
            ? ResolveExternalType(typeMapper, ctx, pouInfo.ReturnTypeName)
            : IrType.Void;

        // 解析参数类型
        var paramIrTypes = pouInfo.Parameters
            .Select(p => p.Direction == "VAR_INPUT"
                ? ResolveExternalType(typeMapper, ctx, p.TypeName)
                : IrType.Pointer)
            .ToArray();

        var funcType = new FunctionType(retType, paramIrTypes, false);
        var func = new Function(pouInfo.Name, funcType, LinkageType.External);

        for (int i = 0; i < pouInfo.Parameters.Count; i++) {
            if (i < func.Parameters.Count)
                func.Parameters[i].Name = pouInfo.Parameters[i].Name;
        }

        module.AddFunction(func);
        ctx.RegisterFunction(pouInfo.Name, func);

        // 注册形参
        var formalParams = pouInfo.Parameters.Select(p => new VariableSymbol
        {
            Name = p.Name,
            Type = ResolveExternalIECType(ctx, p.TypeName),
            Scope = p.Direction switch
            {
                "VAR_INPUT" => VarScope.Input,
                "VAR_OUTPUT" => VarScope.Output,
                "VAR_IN_OUT" => VarScope.InOut,
                _ => VarScope.Input,
            },
        }).ToList();
        ctx.RegisterFormalParams(pouInfo.Name, formalParams);
    }

    private static void RegisterExternalFunctionBlock(
        CodeGenContext ctx,
        Module module,
        TypeMapper typeMapper,
        POUInfo pouInfo)
    {
        // 外部 FB 也需要结构体类型
        // 先用 POUInfo 中的参数构建一个结构体
        var internalFields = new Dictionary<string, IECType>(StringComparer.OrdinalIgnoreCase);

        var formalParams = new List<VariableSymbol>();
        foreach (var p in pouInfo.Parameters) {
            var iecType = ResolveExternalIECType(ctx, p.TypeName);
            var scope = p.Direction switch
            {
                "VAR_INPUT" => VarScope.Input,
                "VAR_OUTPUT" => VarScope.Output,
                "VAR_IN_OUT" => VarScope.InOut,
                _ => VarScope.Input,
            };
            formalParams.Add(new VariableSymbol
            {
                Name = p.Name,
                Type = iecType,
                Scope = scope,
            });
        }

        // 外部 FB 没有内部变量的详细信息，建一个空结构体
        var fbStructType = IECType.MakeStruct(pouInfo.Name, internalFields);
        ctx.RegisterFBInstanceType(pouInfo.Name, fbStructType);
        ctx.RegisterTypeDef(pouInfo.Name, fbStructType);
        typeMapper.Map(fbStructType);

        // 构建 IR 参数类型：第一个是 __instance (pointer)，后面是形参
        var paramIrTypes = BuildFBParamIrTypes(typeMapper, formalParams);
        var funcType = new FunctionType(IrType.Void, paramIrTypes, false);
        var func = new Function(pouInfo.Name, funcType, LinkageType.External);

        func.Parameters[0].Name = "__instance";
        for (int i = 0; i < formalParams.Count; i++) {
            if (i + 1 < func.Parameters.Count)
                func.Parameters[i + 1].Name = formalParams[i].Name;
        }

        module.AddFunction(func);
        ctx.RegisterFunction(pouInfo.Name, func);
        ctx.RegisterFormalParams(pouInfo.Name, formalParams);
    }

    /// <summary>将类型名字符串解析为 IR 类型</summary>
    private static IrType ResolveExternalType(
        TypeMapper typeMapper, CodeGenContext ctx, string typeName)
    {
        var iecType = ResolveExternalIECType(ctx, typeName);
        return typeMapper.Map(iecType);
    }

    /// <summary>将类型名字符串解析为 IECType</summary>
    private static IECType ResolveExternalIECType(CodeGenContext ctx, string typeName)
    {
        var builtin = IECType.LookupBuiltin(typeName);
        if (builtin != null)
            return builtin;

        var typeDef = ctx.GetTypeDef(typeName);
        if (typeDef != null)
            return typeDef;

        if (typeName.StartsWith("WSTRING", StringComparison.OrdinalIgnoreCase))
            return IECType.DefaultWString;
        if (typeName.StartsWith("STRING", StringComparison.OrdinalIgnoreCase))
            return IECType.DefaultString;

        return IECType.DInt;
    }

    // ══════════════════════════════════════════════
    //  以下为原有代码，不变
    // ══════════════════════════════════════════════

    private static void RegisterGlobalVariables(CodeGenContext ctx, AnalysisResult analysis)
    {
        foreach (var varInfo in analysis.Variables) {
            if (varInfo.VarBlockKind != "VAR_GLOBAL")
                continue;

            // A8：优先用语义层已解析的声明类型。`TypeName` 只是显示字符串
            // （数组形如 "ARRAY[0..1] OF P"），按名字回退只能得到 DInt ——
            // 全局数组会因此退化成单个 i32，字段/元素读写落到错误槽位。
            var iecType = varInfo.DeclaredType ?? ResolveTypeFromVarInfo(ctx, varInfo.TypeName);
            var irType = ctx.TypeMapper.Map(iecType);

            if (ctx.TryGetGlobalVariable(varInfo.Name, out _))
                continue;

            var zero = CreateZeroConstant(irType);
            var gv = new GlobalVariable(
                varInfo.Name, irType, false, LinkageType.External, zero);
            gv.Alignment = CodeGenContext.DefaultAlign(irType);

            // F3：源码里的显式初始值（前端已求值为编译期常量）。
            // 没写初值的全局仍是零初始化 —— 字节码层零变动。
            if (varInfo.InitialValue is { } init)
            {
                var initConst = ToIRConstant(init, irType);
                if (initConst is not null)
                {
                    gv.Initializer = initConst;
                    gv.HasExplicitInitializer = true;
                }
            }

            ctx.Module.AddGlobalVariable(gv);
            ctx.RegisterGlobalVariable(varInfo.Name, gv);
        }
    }

    /// <summary>
    /// 把前端求值出的常量落成 IR 常量，类型对齐到目标全局的 IR 类型。
    /// </summary>
    private static Constant? ToIRConstant(ConstantInitializer init, IrType irType)
    {
        if (init.IsReal)
        {
            return irType == IrType.Float || irType == IrType.Double
                ? new ConstantFP(irType, init.RealValue)
                : null;
        }

        return irType is IntegerType ? new ConstantInt(irType, init.IntValue) : null;
    }

    private static IECType ResolveTypeFromVarInfo(CodeGenContext ctx, string typeName)
    {
        var builtin = IECType.LookupBuiltin(typeName);
        if (builtin != null)
            return builtin;

        if (typeName.StartsWith("WSTRING", StringComparison.OrdinalIgnoreCase))
            return IECType.DefaultWString;
        if (typeName.StartsWith("STRING", StringComparison.OrdinalIgnoreCase))
            return IECType.DefaultString;

        return ctx.GetTypeDef(typeName) ?? IECType.DInt;
    }

    private static Function CreateFunctionDeclaration(
        TypeMapper typeMapper, FunctionSymbol funcSym)
    {
        var retType = typeMapper.Map(funcSym.ReturnType);
        var paramTypes = funcSym.Parameters
            .Select(p => p.Scope == VarScope.Input
                ? typeMapper.Map(p.Type)
                : IrType.Pointer)
            .ToArray();

        var funcType = new FunctionType(retType, paramTypes, false);
        var func = new Function(funcSym.Name, funcType, LinkageType.External);

        for (int i = 0; i < funcSym.Parameters.Count; i++) {
            if (i < func.Parameters.Count)
                func.Parameters[i].Name = funcSym.Parameters[i].Name;
        }

        return func;
    }

    private static IECType BuildFBStructType(CodeGenContext ctx, FunctionBlockNode fb)
    {
        var fields = new Dictionary<string, IECType>(StringComparer.OrdinalIgnoreCase);
        foreach (var vb in fb.VarBlocks) {
            // 实例存储 = VAR_INPUT（实例状态）+ VAR（Local/Temp）。
            // VAR_OUTPUT / VAR_IN_OUT 的存储是**调用方变量**，仍经指针形参直达，不进结构体。
            if (vb.Scope is not (VarScope.Input or VarScope.Local or VarScope.Temp))
                continue;
            foreach (var item in vb.Items) {
                var vType = FrameBuilder.ResolveDataTypeNode(ctx, item.TypeName) ?? IECType.DInt;
                foreach (var vn in item.Names)
                    fields[vn.Name] = vType;
            }
        }
        return IECType.MakeStruct(fb.Name, fields);
    }

    /// <summary>
    /// 计算 FB 的 VAR_INPUT 字段索引（供实例初始化与调用点直写）。
    /// 索引取自 <see cref="TypeMapper.GetFieldIndex"/>，与结构体实际布局一致。
    /// </summary>
    private static List<(int FieldIndex, IECType Type)> BuildFBInputFields(
        CodeGenContext ctx, FunctionBlockNode fb, IECType fbStructType, TypeMapper typeMapper)
    {
        var result = new List<(int, IECType)>();
        foreach (var vb in fb.VarBlocks) {
            if (vb.Scope != VarScope.Input)
                continue;
            foreach (var item in vb.Items) {
                var vType = FrameBuilder.ResolveDataTypeNode(ctx, item.TypeName) ?? IECType.DInt;
                foreach (var vn in item.Names)
                    result.Add((typeMapper.GetFieldIndex(fbStructType, vn.Name), vType));
            }
        }
        return result;
    }

    /// <summary>
    /// 收集 FB 成员的显式初值（F3）。
    /// </summary>
    /// <remarks>
    /// 只收**编译期常量**初值：FB 的 VAR 成员初值要在**实例创建处**（调用方）
    /// 写一次，而那里的作用域看不到 FB 自己的成员 —— 非常量表达式无法求值。
    /// </remarks>
    private static List<(int FieldIndex, IECType Type, long IntValue, double RealValue, bool IsReal)>
        BuildFBVarInitializers(
            CodeGenContext ctx,
            FunctionBlockNode fb,
            IECType fbStructType,
            TypeMapper typeMapper)
    {
        var result = new List<(int, IECType, long, double, bool)>();

        foreach (var vb in fb.VarBlocks)
        {
            if (vb.Scope is not (VarScope.Local or VarScope.Temp or VarScope.Input))
                continue;

            foreach (var item in vb.Items)
            {
                if (item.InitialValue is not ExpressionNode init)
                    continue;

                var vType = FrameBuilder.ResolveDataTypeNode(ctx, item.TypeName) ?? IECType.DInt;
                if (ConstantEvaluator.TryEvaluate(init, vType, out _) is not { } value)
                    continue;

                foreach (var vn in item.Names)
                {
                    result.Add((
                        typeMapper.GetFieldIndex(fbStructType, vn.Name),
                        vType,
                        value.IntValue,
                        value.RealValue,
                        value.IsReal));
                }
            }
        }

        return result;
    }

    private static List<VariableSymbol> BuildFBFormalParams(
        FunctionBlockNode fb, FunctionBlockSymbol fbSym)
    {
        var result = new List<VariableSymbol>();
        foreach (var vb in fb.VarBlocks) {
            if (vb.Scope is not (VarScope.Input or VarScope.Output or VarScope.InOut))
                continue;
            foreach (var item in vb.Items) {
                foreach (var vn in item.Names) {
                    if (fbSym.Members.TryGetValue(vn.Name, out var member))
                        result.Add(member);
                }
            }
        }
        return result;
    }

    /// <summary>
    /// FB 的 IR 形参类型：<c>__instance</c> + 各 <c>VAR_OUTPUT</c>/<c>VAR_IN_OUT</c> 指针。
    /// <b>不含</b> <c>VAR_INPUT</c> —— 输入由调用方直写实例字段，随实例存储保留（§6.3.10）。
    /// </summary>
    private static IrType[] BuildFBParamIrTypesExcludingInput(List<VariableSymbol> formalParams)
    {
        var irTypes = new List<IrType> { IrType.Pointer };
        foreach (var p in formalParams) {
            if (p.Scope == VarScope.Input)
                continue;
            irTypes.Add(IrType.Pointer);
        }
        return irTypes.ToArray();
    }

    /// <summary>
    /// 按声明序给 FB 形参命名，跳过不占形参槽的 <c>VAR_INPUT</c>。
    /// </summary>
    private static void NameFBFormalParams(Function func, List<VariableSymbol> formalParams)
    {
        int pi = 1;
        foreach (var p in formalParams) {
            if (p.Scope == VarScope.Input)
                continue;
            if (pi < func.Parameters.Count)
                func.Parameters[pi++].Name = p.Name;
        }
    }

    private static IrType[] BuildFBParamIrTypes(
        TypeMapper typeMapper, List<VariableSymbol> formalParams)
    {
        var irTypes = new List<IrType> { IrType.Pointer };
        foreach (var p in formalParams) {
            irTypes.Add(p.Scope == VarScope.Input
                ? typeMapper.Map(p.Type)
                : IrType.Pointer);
        }
        return irTypes.ToArray();
    }

    private void EmitFunctionBody(CodeGenContext ctx, FunctionNode fn)
    {
        var func = ctx.ResolveFunction(fn.Name);
        ctx.CurrentFunction = func;
        ctx.NameGen.ResetFunction();
        ctx.ClearAllocaMap();

        _debug.EnterFunction(func, fn.Name, fn.Span.FileName ?? "", fn.Span.Line);

        var builder = FrameBuilder.BuildPrologue(
            ctx, func, fn.VarBlocks, fn.ReturnType, fn.Name);
        ctx.Builder = builder;

        var exprEmitter = new ExpressionEmitter(ctx);
        var stmtEmitter = new StatementEmitter(ctx, exprEmitter);
        stmtEmitter.EmitAll(fn.Body);

        EnsureFunctionTerminator(ctx, func);
    }

    private void EmitProgramBody(CodeGenContext ctx, ProgramNode prog)
    {
        var func = ctx.ResolveFunction(prog.Name);
        ctx.CurrentFunction = func;
        ctx.NameGen.ResetFunction();
        ctx.ClearAllocaMap();

        _debug.EnterFunction(func, prog.Name, prog.Span.FileName ?? "", prog.Span.Line);

        var builder = FrameBuilder.BuildPrologue(ctx, func, prog.VarBlocks, null, null);
        ctx.Builder = builder;

        var exprEmitter = new ExpressionEmitter(ctx);
        var stmtEmitter = new StatementEmitter(ctx, exprEmitter);
        stmtEmitter.EmitAll(prog.Body);

        EnsureFunctionTerminator(ctx, func);
    }

    private void EmitFBBody(CodeGenContext ctx, FunctionBlockNode fb)
    {
        var func = ctx.ResolveFunction(fb.Name);
        ctx.CurrentFunction = func;
        ctx.NameGen.ResetFunction();
        ctx.ClearAllocaMap();

        _debug.EnterFunction(func, fb.Name, fb.Span.FileName ?? "", fb.Span.Line);

        var builder = FrameBuilder.BuildPrologue(
            ctx, func, fb.VarBlocks, null, fb.Name, isFunctionBlock: true);
        ctx.Builder = builder;

        var exprEmitter = new ExpressionEmitter(ctx);
        var stmtEmitter = new StatementEmitter(ctx, exprEmitter);

        foreach (var item in fb.BodyItems) {
            if (item is FBStatementItemNode stmtItem)
                stmtEmitter.Emit(stmtItem.Statement);
        }

        EnsureFunctionTerminator(ctx, func);

        foreach (var item in fb.BodyItems) {
            if (item is FBActionItemNode actionItem)
                EmitFBAction(ctx, fb, actionItem.Action);
        }
    }

    private void EmitFBAction(CodeGenContext ctx, FunctionBlockNode fb, ActionNode action)
    {
        var actName = $"{fb.Name}.{action.Name}";
        var func = ctx.ResolveFunction(actName);
        ctx.CurrentFunction = func;
        ctx.NameGen.ResetFunction();
        ctx.ClearAllocaMap();

        _debug.EnterFunction(func, actName, action.Span.FileName ?? "", action.Span.Line);

        var builder = FrameBuilder.BuildPrologue(
            ctx, func, fb.VarBlocks, null, fb.Name, isFunctionBlock: true);
        ctx.Builder = builder;

        var exprEmitter = new ExpressionEmitter(ctx);
        var stmtEmitter = new StatementEmitter(ctx, exprEmitter);
        stmtEmitter.EmitAll(action.Body);

        EnsureFunctionTerminator(ctx, func);
    }

    private static void EnsureFunctionTerminator(CodeGenContext ctx, Function func)
    {
        if (func.BasicBlocks.Count == 0)
            return;

        var lastBlock = func.BasicBlocks[^1];
        if (lastBlock.Terminator != null)
            return;

        ctx.Builder.InsertBlock = lastBlock;

        if (func.FunctionType.ReturnType == IrType.Void) {
            ctx.Builder.CreateRetVoid();
        } else if (ctx.TryGetAlloca(func.Name!, out var retAlloca)) {
            var retVal = ctx.Builder.CreateLoad(
                func.FunctionType.ReturnType, retAlloca, "ret.fallback");
            ctx.Builder.CreateRet(retVal);
        } else {
            var zero = CreateZeroConstant(func.FunctionType.ReturnType);
            ctx.Builder.CreateRet(zero);
        }
    }

    private static Constant CreateZeroConstant(IrType type)
    {
        if (type is IntegerType)
            return new ConstantInt(type, 0);
        if (type == IrType.Float || type == IrType.Double)
            return new ConstantFP(type, 0.0);
        if (type == IrType.Pointer)
            return new ConstantPointerNull();
        return new ConstantInt(IrType.Int32, 0);
    }
}