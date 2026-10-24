using IronST.BytecodeGen;
using IronST.Frontend;
using IronST.Frontend.Analysis;
using IronST.LLVMCodeGen.CodeGen;
using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.Integration.Tests;

/// <summary>一次宿主调用：名字、实参快照、返回值。</summary>
public sealed record HostCall(string Name, IReadOnlyList<BcValue> Arguments, BcValue Result);

/// <summary>
/// 宿主函数实参视图。前 4 个参数在 A0..A3，其余按调用约定在栈上。
/// </summary>
public readonly struct HostArgs
{
    private readonly VMContext _ctx;
    private readonly int _count;

    internal HostArgs(VMContext ctx, int count)
    {
        _ctx = ctx;
        _count = count;
    }

    public int Count => _count;

    public BcValue this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
                throw new ArgumentOutOfRangeException(nameof(index));
            // 第 5 个起由调用方 PUSH 到当前 SP 之上；栈下标不是从 0 开始。
            return index < 4
                ? _ctx.ReadReg(BytecodeBuilder.A0 + index)
                : _ctx.ReadStack(
                    (int)_ctx.ReadReg(BytecodeBuilder.SP).IntValue + (index - 4));
        }
    }

    public long Int(int index) => this[index].IntValue;
    public double Float(int index) => this[index].FloatValue;
    public string String(int index) => this[index].StringValue ?? "";

    /// <summary>调用发生时的栈指针（用于检查宿主调用是否回收了栈参数区）。</summary>
    public long StackPointer => _ctx.ReadReg(BytecodeBuilder.SP).IntValue;

    public BcValue[] ToArray()
    {
        var values = new BcValue[_count];
        for (int i = 0; i < _count; i++)
            values[i] = this[i];
        return values;
    }
}

/// <summary>
/// 注册 C# 宿主函数（ST 侧通过 HCALL 调用），并记录每次调用的实参与返回值。
/// </summary>
public sealed class StTestHost
{
    private readonly Dictionary<string, ushort> _ids = new(StringComparer.Ordinal);
    private readonly Dictionary<ushort, Func<VMContext, BcValue>> _handlers = new();
    private readonly List<ExternalFunction> _functions = [];
    private readonly List<HostCall> _calls = [];
    private ushort _nextId = 0x0100;

    public StTestHost()
    {
        // 字符串标准函数由运行时库提供实现，这里只登记名字→ID，供编译器发射 HCALL。
        foreach (var (name, id) in StringStandardLibrary.Functions)
            _ids[name] = id;
    }

    public IReadOnlyList<HostCall> Calls => _calls;
    public IReadOnlyDictionary<string, ushort> FunctionMap => _ids;

    public ExternalLibrary Library => new()
    {
        Name = "IronST Integration Test Host",
        Functions = _functions,
    };

    /// <summary>注册无返回值的宿主函数。</summary>
    public StTestHost RegisterAction(string name, int argCount, Action<HostArgs> handler) =>
        Add(name, argCount, args =>
        {
            handler(args);
            return BcValue.Void;
        }, returnType: null);

    /// <summary>注册有返回值的宿主函数。</summary>
    public StTestHost RegisterFunction(
        string name,
        int argCount,
        Func<HostArgs, BcValue> handler,
        string returnType = "DINT") =>
        Add(name, argCount, handler, returnType);

    private StTestHost Add(
        string name,
        int argCount,
        Func<HostArgs, BcValue> handler,
        string? returnType)
    {
        if (_ids.ContainsKey(name))
            throw new ArgumentException($"host function '{name}' is already registered", nameof(name));

        ushort id = _nextId++;
        _ids[name] = id;
        _functions.Add(new ExternalFunction
        {
            Name = name,
            ReturnTypeName = returnType,
            Parameters = Enumerable.Range(0, argCount)
                .Select(i => new ExternalParameter { Name = $"p{i}", TypeName = "DINT" })
                .ToList(),
        });
        _handlers[id] = ctx =>
        {
            var args = new HostArgs(ctx, argCount);
            var snapshot = args.ToArray();
            var result = handler(args);
            _calls.Add(new HostCall(name, snapshot, result));
            return result;
        };
        return this;
    }

    internal void Bind(HostBridge bridge)
    {
        foreach (var (id, handler) in _handlers)
            bridge.Register(id, handler);

        StringStandardLibrary.Register(bridge);
    }
}

/// <summary>一次 ST 编译 + 运行的结果。</summary>
public sealed class StRunResult
{
    public required VM VM { get; init; }
    public required StTestHost Host { get; init; }

    public VMState State => VM.State;
    public RuntimeErrorException? Error => VM.LastError;
    public IReadOnlyList<HostCall> Calls => Host.Calls;

    /// <summary>调用约定：返回值在 A0。</summary>
    public BcValue ReturnValue => VM.Context.ReadReg(BytecodeBuilder.A0);

    public BcValue Global(int index) => VM.Context.ReadGlobal(index);
}

/// <summary>
/// 端到端 harness：写 ST → 注入 C# 宿主函数 → 编译 → 在 VM 上运行。
/// </summary>
public static class StRunner
{
    /// <summary>运行带 PROGRAM 入口的 ST。</summary>
    public static StRunResult Run(string source, Action<StTestHost>? configureHost = null) =>
        Execute(source, configureHost, entryFunctionName: null, args: null);

    /// <summary>运行并捕获运行时错误（不向外抛），用于断言 <see cref="StRunResult.Error"/>。</summary>
    public static StRunResult RunExpectingError(
        string source,
        Action<StTestHost>? configureHost = null) =>
        Execute(source, configureHost, entryFunctionName: null, args: null, captureErrors: true);

    /// <summary>
    /// 直接调用 ST 中的 FUNCTION：入口桩会 CALL 该函数并 EXIT。
    /// 前 4 个标量参数写入 A0..A3，其余按调用约定压到当前 SP 之上；返回值从 A0 读取。
    /// 目前只支持标量实参（每个实参占一个槽）。
    /// </summary>
    public static StRunResult Call(
        string source,
        string functionName,
        Action<StTestHost>? configureHost = null,
        params BcValue[] args) =>
        Execute(source, configureHost, functionName, args);

    private static StRunResult Execute(
        string source,
        Action<StTestHost>? configureHost,
        string? entryFunctionName,
        BcValue[]? args,
        bool captureErrors = false)
    {
        var host = new StTestHost();
        configureHost?.Invoke(host);

        var analysis = FrontendPipeline.Analyze(
            source,
            "test.st",
            [HostBuiltins.Create(), IECStandardLibrary.CreateStringFunctions(), host.Library]);

        if (!analysis.Success) {
            var errors = analysis.Diagnostics
                .Where(d => d.Severity == Common.Diagnostics.DiagnosticSeverity.Error)
                .ToList();
            throw new InvalidOperationException(
                "frontend failed:\n" + string.Join("\n", errors.Select(e => $"  {e}")));
        }

        var module = new IRGenerator(hostInjectedBuiltins: host.FunctionMap.Keys)
            .Generate(analysis);

        var compiled = new ByteCodeGenerator(
            "test.st",
            hostFunctions: host.FunctionMap
        ).Generate(module, entryFunctionName: entryFunctionName);

        var bridge = new HostBridge();
        host.Bind(bridge);

        var vm = new VM(compiled.Module, new VMOptions
        {
            StackSize = 8192,
            HostBridge = bridge,
            StructTypes = compiled.StructRegistrations,
        });

        if (args is not null) {
            for (int i = 0; i < args.Length && i < 4; i++)
                vm.Context.WriteReg(BytecodeBuilder.A0 + i, args[i]);

            // 第 5 个起：模拟调用方在 CALL 之前的 PUSH（每次 SP 先减一）。
            if (args.Length > 4) {
                long sp = vm.Context.ReadReg(BytecodeBuilder.SP).IntValue;
                if (sp - (args.Length - 4) < 0)
                    throw new InvalidOperationException("too many direct-call arguments for the VM stack");

                for (int i = args.Length - 1; i >= 4; i--)
                    vm.Context.WriteStack((int)--sp, args[i]);

                vm.Context.WriteReg(BytecodeBuilder.SP, new BcValue(sp));
            }
        }

        if (captureErrors) {
            try { vm.Run(); }
            catch (RuntimeErrorException) { /* 已记录在 vm.LastError */ }
        } else {
            vm.Run();
        }

        return new StRunResult { VM = vm, Host = host };
    }
}
