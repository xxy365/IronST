using IronST.IRBuilder.IR;
using IronST.Common.Types;
using IronST.Common.Symbol;
using IronST.LLVMCodeGen.Debug;
using IronST.LLVMCodeGen.Exceptions;
using IronST.LLVMCodeGen.TypeMapping;
using IRBuild = IronST.IRBuilder.IR.IRBuilder;

namespace IronST.LLVMCodeGen.CodeGen;

public sealed class CodeGenContext
{
    public Module Module { get; }
    public TypeMapper TypeMapper { get; }
    public NameGenerator NameGen { get; }
    public IDebugEmitter Debug { get; }

    public Function CurrentFunction { get; set; } = null!;
    private IRBuild? _builder;

    /// <summary>
    /// 当前 IR 构建器（可写）。
    ///
    /// 赋值时顺手挂上调试位置回调：此后凡是经它创建的指令都自动带上
    /// "当前语句的源码行"。这样源码映射才是连续的 —— 否则只有显式调过
    /// AttachLocation 的那几条（store、条件分支）有行号，中间的表达式、
    /// FOR 初始化、循环自增全是空洞，取行号时会被算到上一条语句去。
    /// </summary>
    public IRBuild Builder
    {
        get => _builder!;
        set
        {
            _builder = value;
            value.OnInsert = inst =>
            {
                if (Debug.IsEnabled && !IsInPrologue)
                    Debug.AttachLocation(inst);
            };
        }
    }

    public bool IsInPrologue { get; set; }
    public Stack<BasicBlock> LoopEndBlocks { get; } = new();

    /// <summary>
    /// 由宿主注入实现的内建函数名（如字符串标准库）。非空时，这些名字<b>不再</b>由
    /// <see cref="BuiltinFunctions"/> 内联展开，而是发射对 IR 声明的普通调用 ——
    /// 由后端把调用转成 HCALL 交宿主实现（<see cref="IRGenerator"/> 构造时传入）。
    /// </summary>
    public IReadOnlySet<string>? HostInjectedBuiltins { get; set; }

    // ── 变量 alloca 映射 ──

    private readonly Dictionary<string, Value> _allocaMap = [];

    public void BindAlloca(string varName, Value alloca)
        => _allocaMap[varName] = alloca;

    public Value GetAlloca(string varName)
        => _allocaMap.TryGetValue(varName, out var v)
            ? v
            : throw new CodeGenException($"未绑定的变量: {varName}");

    public bool TryGetAlloca(string varName, out Value alloca)
        => _allocaMap.TryGetValue(varName, out alloca!);

    public void ClearAllocaMap() => _allocaMap.Clear();

    // ── 变量类型映射 ──

    private readonly Dictionary<string, IECType> _varTypeMap = [];

    public void BindVarType(string varName, IECType type)
        => _varTypeMap[varName] = type;

    public IECType GetVarType(string varName)
        => _varTypeMap.TryGetValue(varName, out var t)
            ? t
            : throw new CodeGenException($"未绑定的变量类型: {varName}");

    public bool TryGetVarType(string varName, out IECType type)
        => _varTypeMap.TryGetValue(varName, out type!);

    // ── 用户自定义类型注册 ──

    private readonly Dictionary<string, IECType> _typeDefMap = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterTypeDef(string name, IECType type)
        => _typeDefMap[name] = type;

    public IECType? GetTypeDef(string name)
        => _typeDefMap.GetValueOrDefault(name);

    // ── 函数注册表 ──

    private readonly Dictionary<string, Function> _functionRegistry = [];

    public void RegisterFunction(string name, Function function)
        => _functionRegistry[name] = function;

    public Function ResolveFunction(string name)
        => _functionRegistry.TryGetValue(name, out var f)
            ? f
            : throw new CodeGenException($"未定义的函数: {name}");

    public bool TryResolveFunction(string name, out Function function)
        => _functionRegistry.TryGetValue(name, out function!);

    // ── 结构体比较辅助函数（A1/F5）──
    //
    // 缓存放在上下文而不是 ExpressionEmitter：后者按函数体实例化，
    // 同一结构体在同一模块里应只生成一份比较辅助函数。

    private readonly Dictionary<string, Function> _structCompareHelpers = new(StringComparer.Ordinal);
    private int _structCompareHelperCounter;

    public bool TryGetStructCompareHelper(string key, out Function helper)
        => _structCompareHelpers.TryGetValue(key, out helper!);

    public void RegisterStructCompareHelper(string key, Function helper)
        => _structCompareHelpers[key] = helper;

    public string NextStructCompareHelperName() => $"__st_cmp_{++_structCompareHelperCounter}";

    // ── 函数形参注册 ──

    private readonly Dictionary<string, List<VariableSymbol>> _formalParams = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterFormalParams(string funcName, List<VariableSymbol> parameters)
        => _formalParams[funcName] = parameters;

    public List<VariableSymbol> GetFormalParams(string funcName)
        => _formalParams.TryGetValue(funcName, out var p) ? p : [];

    // ── FB 实例类型注册 ──

    private readonly Dictionary<string, IECType> _fbInstanceTypes = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterFBInstanceType(string fbName, IECType instanceType)
        => _fbInstanceTypes[fbName] = instanceType;

    public IECType? GetFBInstanceType(string fbName)
        => _fbInstanceTypes.GetValueOrDefault(fbName);

    // ── FB 的 VAR_INPUT 字段索引（供实例 alloca 处初始化 + 调用点直写）──

    private readonly Dictionary<string, List<(int FieldIndex, IECType Type)>> _fbInputFields
        = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterFBInputFields(string fbName, List<(int FieldIndex, IECType Type)> fields)
        => _fbInputFields[fbName] = fields;

    public List<(int FieldIndex, IECType Type)>? GetFBInputFields(string fbName)
        => _fbInputFields.GetValueOrDefault(fbName);

    // ── FB 成员的显式初值（F3）──
    //
    // FB 的 VAR / VAR_TEMP 是**实例状态**：初值在**实例创建处**写一次，
    // 不是每次调用都写（否则会把跨调用的状态冲掉）。
    // 这里只登记**编译期常量**初值 —— 非常量表达式引用不到 FB 自己的成员，
    // 留在实例创建处无法求值。

    private readonly Dictionary<string, List<(int FieldIndex, IECType Type, long IntValue, double RealValue, bool IsReal)>>
        _fbVarInitializers = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterFBVarInitializers(
        string fbName,
        List<(int FieldIndex, IECType Type, long IntValue, double RealValue, bool IsReal)> inits)
        => _fbVarInitializers[fbName] = inits;

    public List<(int FieldIndex, IECType Type, long IntValue, double RealValue, bool IsReal)>?
        GetFBVarInitializers(string fbName)
        => _fbVarInitializers.GetValueOrDefault(fbName);

    // ── 全局字符串映射 ──

    private readonly Dictionary<string, GlobalVariable> _globalStrings = [];

    public void RegisterGlobalString(string name, GlobalVariable gv)
        => _globalStrings[name] = gv;

    public GlobalVariable ResolveGlobalString(string name)
        => _globalStrings.TryGetValue(name, out var gv)
            ? gv
            : throw new CodeGenException($"未注册的全局字符串: {name}");

    // ══════════════════════════════════════════════
    //  新增：全局变量注册表（VAR_GLOBAL / VAR_EXTERNAL）
    // ══════════════════════════════════════════════

    private readonly Dictionary<string, GlobalVariable> _globalVars = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterGlobalVariable(string name, GlobalVariable gv)
        => _globalVars[name] = gv;

    public GlobalVariable GetGlobalVariable(string name)
        => _globalVars.TryGetValue(name, out var gv)
            ? gv
            : throw new CodeGenException($"未注册的全局变量: {name}");

    public bool TryGetGlobalVariable(string name, out GlobalVariable gv)
        => _globalVars.TryGetValue(name, out gv!);

    // ── 默认对齐（字节）──

    internal static int DefaultAlign(IrType type) => type switch
    {
        _ when type == IrType.Int1 || type == IrType.Int8 => 1,
        IntegerType { BitWidth: <= 16 } => 2,
        _ when type == IrType.Int32 || type == IrType.Float => 4,
        _ when type == IrType.Int64 || type == IrType.Double => 8,
        _ when type == IrType.Pointer => 8,
        _ => 4
    };

    // ── 构造 ──

    public CodeGenContext(Module module, TypeMapper typeMapper, IDebugEmitter debug)
    {
        Module = module;
        TypeMapper = typeMapper;
        Debug = debug;
        NameGen = new NameGenerator();
    }
}