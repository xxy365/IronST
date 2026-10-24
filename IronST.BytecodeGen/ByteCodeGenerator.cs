using IronST.IRBuilder.Emitter;
using IronST.IRBuilder.IR;
using IronST.Runtime.Shared;

namespace IronST.BytecodeGen;

/// <summary>
/// IRBuilder Module -> BytecodeModule 单遍编译器。
/// 从 IRBuilder 对象模型中提取全局变量、函数定义，发射字节码。
/// </summary>
public sealed class ByteCodeGenerator
{
    private readonly string? _sourceFileName;
    private readonly IReadOnlyDictionary<string, ushort>? _hostFunctions;
    private readonly bool _allowExternalCalls;

    // 结构体类型注册表
    private readonly Dictionary<StructType, byte> _structTypeMap = new();
    private byte _nextStructTypeId = 1;
    private readonly List<(byte typeId, int fieldCount)> _structRegistrations = new();

    /// <param name="allowExternalCalls">
    /// <c>true</c> 时允许对"声明但非宿主"的外部函数发起调用，并把它保留为未解析的
    /// <c>Addr32</c> 引用（独立编译 + 后续链接）。默认 <c>false</c>：保持既有的
    /// "External function has no host handler" 诊断。
    /// </param>
    public ByteCodeGenerator(
        string? sourceFileName = null,
        IReadOnlyDictionary<string, ushort>? hostFunctions = null,
        bool allowExternalCalls = false)
    {
        _sourceFileName = sourceFileName;
        _hostFunctions = hostFunctions;
        _allowExternalCalls = allowExternalCalls;
    }

    private byte EnsureStructTypeId(StructType st)
    {
        if (_structTypeMap.TryGetValue(st, out byte id))
            return id;
        id = _nextStructTypeId++;
        _structTypeMap[st] = id;
        _structRegistrations.Add((id, st.ElementTypes.Count));
        return id;
    }

    /// <param name="module">IRBuilder Module（从 LLParser 或手动构建获得）</param>
    /// <param name="entryFunctionName">入口函数名，null 时自动查找 "main"/"Main" 或首个非声明函数</param>
    /// <param name="irSourceMap">可选的 IRSourceMapParser.Parse() 预计算结果</param>
    public CompileResult Generate(
        Module module,
        string? entryFunctionName = null,
        Dictionary<string, List<(string file, int line)?>>? irSourceMap = null)
    {
        // 重置类型注册表（每次编译独立）
        _structTypeMap.Clear();
        _structRegistrations.Clear();
        _nextStructTypeId = 1;

        var asm = new BytecodeAssembler();

        // 静态链接要求所有绝对 CALL 都可重定位（函数会随模块拼接移动）。
        asm.RecordResolvedCalls = true;
        asm.AllowUnresolvedAddr32 = _allowExternalCalls;
        var sourceMap = new Dictionary<int, (string file, int line)>();
        var debugModule = new DebugModule();

        // 全局变量
        // F6：槽位以 short 编码，且循环计数器 gIdx 自身也是 short —— 一旦超出上限，
        // gIdx++ 会从 32767 回绕成 -32768，`gIdx < Count` 依然成立，循环永不终止，
        // 同时 globalIndex 被写入负索引。必须在进入循环前拒绝。
        //
        // A8：聚合全局（结构体 / 数组）按**扁平槽**展开 —— 名字映射到起始槽号，
        // 连续的 FlatSlotCount 个槽归它所有。此前每个全局只占一槽，聚合字段的
        // 读写会落到别的全局或越界槽位。标量仍占 1 槽 ⇒ 既有程序槽位不变。
        var globalIndex = new Dictionary<string, short>(StringComparer.OrdinalIgnoreCase);
        int nextGlobalSlot = 0;
        foreach (var gv in module.GlobalVariables)
        {
            int slots = FunctionEmitter.FlatSlotCount(gv.ValueType);
            if (nextGlobalSlot + slots > BytecodeAssembler.MaxGlobals)
                throw new InvalidOperationException(
                    $"Too many global slots: '{gv.Name}' needs {slots} slot(s) at base {nextGlobalSlot}, "
                        + $"which exceeds the {BytecodeAssembler.MaxGlobals}-slot limit "
                        + $"(slot index range is 0..{BytecodeAssembler.MaxGlobals - 1})."
                );

            var name = gv.Name!;
            debugModule.GlobalNames.Add(name);
            asm.DeclareGlobal(name, slots);
            globalIndex[name] = (short)nextGlobalSlot;
            nextGlobalSlot += slots;
        }

        // F11：字符串常量全局初始化。字节码路径没有通用全局初始化器，IR 里
        // `@str.N = constant [L x i8] c"…\00"` 的内容此前从不落到全局槽 —— 全局槽一直是
        // 默认 Void，故字面量读到 nil。这里在入口 stub **之前**，为每个字符串常量全局发射
        // 「LDC 该串；SETG 槽」，把 BcValue(String) 落进全局槽（设计档 §3.2）。
        // 只有字符串字面量会产生 ConstantDataArray 全局（Module.AddGlobalString 的唯一
        // 调用者是 ExpressionEmitter.EmitStringConstant），故非字符串程序零改动。
        EmitStringGlobalInitializers(module, globalIndex, asm);
        EmitNumericGlobalInitializers(module, globalIndex, asm);

        var allFuncs = module.Functions.Where(f => !IsStub(f)).ToList();

        // P2：外部模块函数声明（声明但**非宿主**）不在本模块发射实现。
        // 这样对它的 CALL 会在 _funcTable 中找不到目标，从而保留为未解析 Addr32，
        // 由链接器解析到提供实现的模块（绝不生成"指向桩体的合法 0 地址调用"）。
        bool IsHostDecl(Function f) =>
            f.Name is not null && _hostFunctions is not null
                && _hostFunctions.ContainsKey(f.Name);
        bool IsExternalDecl(Function f) => f.IsDeclaration && !IsHostDecl(f);
        var emitFuncs = allFuncs.Where(f => !IsExternalDecl(f)).ToList();

        // 定位入口函数
        Function? program = null;
        if (entryFunctionName != null) {
            program = emitFuncs.FirstOrDefault(f => f.Name == entryFunctionName);
        } else {
            program = emitFuncs.FirstOrDefault(f => f.Name is "main" or "Main")
                   ?? emitFuncs.FirstOrDefault(f => !f.IsDeclaration);
        }

        if (entryFunctionName == null)
            entryFunctionName = program?.Name;

        // 入口 stub：call entry + exit
        if (entryFunctionName != null)
            asm.CallFunc(entryFunctionName);
        asm.Exit();

        // 入口函数排在最前面
        if (program != null) {
            var emitter = CreateEmitter(program, globalIndex, asm, sourceMap, irSourceMap);
            emitter.Emit();
            debugModule.Functions.Add(emitter.DebugFunction);
        }

        // 其余函数
        foreach (var f in emitFuncs) {
            if (f == program)
                continue;

            var emitter = CreateEmitter(f, globalIndex, asm, sourceMap, irSourceMap);
            emitter.Emit();
            debugModule.Functions.Add(emitter.DebugFunction);
        }

        var moduleOutput = asm.Build();

        // 函数索引的唯一事实来源：汇编器在发射完 LEAVE/RET 后登记的精确字节范围。
        var functionLayout = BuildFunctionLayout(
            asm.FunctionRanges, allFuncs, entryFunctionName);

        return new CompileResult(moduleOutput, debugModule, sourceMap, _structRegistrations,
            new LLEmitter().Emit(module), asm.Relocations, functionLayout, asm.UnresolvedSymbols);
    }

    /// <summary>
    /// 把汇编器的字节范围与 IR 函数元信息合并为对外函数布局。
    /// </summary>
    /// <remarks>
    /// 名字以<b>汇编器实际发射的名字</b>为准（<see cref="FunctionEmitter"/> 用
    /// <c>func.Name ?? "__func"</c>）；参数个数与签名取自 IR 函数类型，
    /// 因为汇编器的 <c>paramCount</c> 是"前 4 槽走 A 寄存器"的个数，不是语言层参数个数。
    /// </remarks>
    private static List<FunctionLayoutEntry> BuildFunctionLayout(
        IReadOnlyList<BytecodeAssembler.FunctionRange> ranges,
        IReadOnlyList<Function> allFuncs,
        string? entryFunctionName)
    {
        var byName = new Dictionary<string, Function>(StringComparer.Ordinal);
        foreach (var f in allFuncs)
            byName.TryAdd(f.Name ?? "__func", f);

        var layout = new List<FunctionLayoutEntry>(ranges.Count);
        uint nextId = 0;

        foreach (var range in ranges)
        {
            byName.TryGetValue(range.Name, out var ir);
            bool isDeclaration = ir?.IsDeclaration ?? false;

            layout.Add(new FunctionLayoutEntry
            {
                FunctionId = nextId++,
                Name = range.Name,
                Signature = ir?.FunctionType.ToIRString() ?? "",
                StartPC = range.StartPC,
                EndPC = range.EndPC,
                ParamCount = ir?.Parameters.Count ?? range.ParamCount,
                LocalCount = range.LocalCount,
                // 语言层的 POU 都是 External 链接；声明式函数（宿主/外部）不算导出。
                IsExported = ir is not null && !isDeclaration && ir.Linkage == LinkageType.External,
                IsDeclaration = isDeclaration,
                IsEntryPoint = !isDeclaration && entryFunctionName is not null
                    && range.Name == entryFunctionName,
            });
        }

        return layout;
    }

    public BytecodeModule GenerateModule(Module module)
        => Generate(module).Module;

    public DebugModule EmitDebug(Module module)
        => Generate(module).DebugModule;

    private FunctionEmitter CreateEmitter(
        Function func,
        Dictionary<string, short> globalIndex,
        BytecodeAssembler asm,
        Dictionary<int, (string file, int line)> sourceMap,
        Dictionary<string, List<(string file, int line)?>>? irSourceMap)
    {
        return new FunctionEmitter(
            func, asm, globalIndex, _sourceFileName, sourceMap, _hostFunctions, irSourceMap,
            EnsureStructTypeId, _allowExternalCalls);
    }

    /// <summary>
    /// 为每个字符串常量全局发射「LDC 字符串；SETG 槽」的初始化（F11，设计档 §3.2）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 判定：<see cref="GlobalVariable.IsConstant"/> 且其 <see cref="GlobalVariable.Initializer"/>
    /// 是 <see cref="ConstantDataArray"/> —— 本仓库该组合只由
    /// <see cref="Module.AddGlobalString(string, string, LinkageType)"/> 产生
    /// （字符串字面量的唯一来源），故非字符串程序不产生任何此类全局。
    /// </para>
    /// <para>
    /// 发射在模块顶层、入口 stub <b>之前</b>：VM 自 PC 0 起执行，初始化先于入口函数调用。
    /// 用汇编器临时寄存器（入口处无活跃寄存器），复用既有的统一常量池 + <c>LDC</c> / <c>SETG</c> 通道。
    /// </para>
    /// </remarks>
    private static void EmitStringGlobalInitializers(
        Module module,
        Dictionary<string, short> globalIndex,
        BytecodeAssembler asm)
    {
        foreach (var gv in module.GlobalVariables) {
            if (!gv.IsConstant || gv.Initializer is not ConstantDataArray cda)
                continue;
            if (!globalIndex.ContainsKey(gv.Name ?? ""))
                continue;

            byte tmp = asm.Load(DecodeConstantString(cda));
            asm.SetGlobal(tmp, gv.Name!);
            asm.FreeTemp(tmp);
        }
    }

    /// <summary>
    /// 为每个**带显式初始值**的数值型全局发射「LDC 常量；SETG 槽」的初始化（F3）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 判定：<see cref="GlobalVariable.HasExplicitInitializer"/> 为真且
    /// <see cref="GlobalVariable.Initializer"/> 是 <see cref="ConstantInt"/> 或
    /// <see cref="ConstantFP"/>。
    /// </para>
    /// <para>
    /// 没有显式初值的全局**不发射任何指令** —— 零初始化由 VM 的全局区天然承担，
    /// 此类程序字节码逐字节不变。
    /// </para>
    /// <para>
    /// 发射位置与字符串全局初值器一致：模块顶层、入口 stub <b>之前</b> ——
    /// VM 自 PC 0 起执行，初值先于入口函数调用就位。
    /// </para>
    /// </remarks>
    private static void EmitNumericGlobalInitializers(
        Module module,
        Dictionary<string, short> globalIndex,
        BytecodeAssembler asm)
    {
        foreach (var gv in module.GlobalVariables)
        {
            if (!gv.HasExplicitInitializer)
                continue;
            if (!globalIndex.ContainsKey(gv.Name ?? ""))
                continue;

            byte tmp;
            switch (gv.Initializer)
            {
                case ConstantInt ci:
                    tmp = asm.Load(ci.Value);
                    break;
                case ConstantFP cf:
                    tmp = asm.Load(cf.Value);
                    break;
                default:
                    continue;
            }

            asm.SetGlobal(tmp, gv.Name!);
            asm.FreeTemp(tmp);
        }
    }

    /// <summary>
    /// 把 <see cref="ConstantDataArray"/> 还原为字符串：UTF-8 字节去掉尾部 <c>NUL</c> 终止符。
    /// </summary>
    /// <remarks>
    /// 对齐 <see cref="ConstantDataArray.FromString"/>（UTF-8 + 尾 <c>\0</c>）。
    /// 本切片只对 STRING 取证；WSTRING 字面量未取证的边界见设计档 §6。
    /// </remarks>
    private static string DecodeConstantString(ConstantDataArray cda)
    {
        var bytes = cda.Data;
        int len = bytes.Length;
        if (len > 0 && bytes[len - 1] == 0)
            len--;
        return System.Text.Encoding.UTF8.GetString(bytes, 0, len);
    }

    /// <summary>
    /// stub 函数体仅含 alloca / store / br / ret / unreachable，无实质逻辑，不发射字节码。
    /// </summary>
    private static bool IsStub(Function f)
    {
        foreach (var bb in f.BasicBlocks)
            foreach (var inst in bb.Instructions)
                if (inst is not (AllocaInst or StoreInst or BranchInst
                    or ReturnInst or UnreachableInst))
                    return false;
        return f.BasicBlocks.Count > 0;
    }
}

public sealed class CompileResult
{
    public BytecodeModule Module { get; }
    public DebugModule DebugModule { get; }
    public IReadOnlyDictionary<int, (string file, int line)> SourceMap { get; }

    /// <summary>结构体类型注册列表。宿主需对每项调用 StructHeap.RegisterType(typeId, fieldCount)。</summary>
    public IReadOnlyList<(byte typeId, int fieldCount)> StructRegistrations { get; }

    /// <summary>LLVM IR 文本（LLEmitter 输出），供 --ir/--opt/--bc 使用。</summary>
    public string IrText { get; }

    /// <summary>
    /// 本次编译产出的重定位记录（内存元数据，<b>不写入</b> .isb）。
    /// </summary>
    /// <remarks>
    /// 记录与模块格式解耦：持久化表示由 .ist 模块格式设计统一决定，
    /// .isb 头部字节布局尚未冻结（§7.3），本切片不改任何序列化格式。
    /// </remarks>
    public RelocationTable Relocations { get; }

    /// <summary>
    /// 每个函数的真实字节范围与元信息（按发射顺序）。
    /// </summary>
    /// <remarks>
    /// 来源是 <see cref="BytecodeAssembler.FunctionRanges"/>，即 <c>DefFunc</c>
    /// 发射完 <c>LEAVE</c>/<c>RET</c> 后记录的 <c>PC</c> —— <b>不是</b>按源码行号
    /// 或函数排列顺序推测的。`.stmod` 的函数索引直接消费它。
    /// </remarks>
    public IReadOnlyList<FunctionLayoutEntry> FunctionLayout { get; }

    /// <summary>
    /// 装配期未解析的外部函数符号（去重，按首次出现顺序）。
    /// 非空 ⇒ 该产物是**未链接模块**，不得直接执行。
    /// </summary>
    public IReadOnlyList<string> UnresolvedSymbols { get; }

    public CompileResult(
        BytecodeModule module,
        DebugModule debugModule,
        Dictionary<int, (string file, int line)> sourceMap,
        IReadOnlyList<(byte typeId, int fieldCount)> structRegistrations,
        string irText,
        RelocationTable relocations,
        IReadOnlyList<FunctionLayoutEntry> functionLayout,
        IReadOnlyList<string>? unresolvedSymbols = null)
    {
        Module = module;
        DebugModule = debugModule;
        SourceMap = sourceMap;
        StructRegistrations = structRegistrations;
        IrText = irText;
        Relocations = relocations;
        FunctionLayout = functionLayout;
        UnresolvedSymbols = unresolvedSymbols ?? [];
    }
}

/// <summary>一个函数在一次编译产物中的真实布局。</summary>
public sealed class FunctionLayoutEntry
{
    public required uint FunctionId { get; init; }
    public required string Name { get; init; }

    /// <summary>签名文本（来自 IR 函数类型），例如 <c>DINT (DINT, DINT)</c>。</summary>
    public required string Signature { get; init; }

    /// <summary>函数起始 PC（含）。</summary>
    public required int StartPC { get; init; }

    /// <summary>函数结束 PC（不含，= 末尾 RET 之后）。</summary>
    public required int EndPC { get; init; }

    /// <summary>真实字节长度。</summary>
    public int CodeLength => EndPC - StartPC;

    public required int ParamCount { get; init; }

    /// <summary>帧内局部槽位数（FRAME 操作数）。</summary>
    public required int LocalCount { get; init; }

    public bool IsExported { get; init; }
    public bool IsDeclaration { get; init; }
    public bool IsEntryPoint { get; init; }
}