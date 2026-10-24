// ═══════════════════════════════════════════════════════════════
//  HostArgs.cs — 宿主函数参数视图
//  按索引懒读，行为像列表，零分配
// ═══════════════════════════════════════════════════════════════

using IronST.BytecodeGen;
using IronST.Runtime.Shared;

namespace IronST.CLI;

/// <summary>
/// 宿主函数参数的只读视图。
/// <para>索引 0-3 读寄存器 A0-A3，索引 ≥ 4 自动走栈。</para>
/// <para>
/// <code>
/// // 推荐用法 — 直接索引读 BcValue
/// long  n = args[0].IntValue;
/// double f = args[1].FloatValue;
///
/// // 需要真正的列表时
/// List&lt;BcValue&gt; list = args.ToList(3);
/// </code>
/// </para>
/// </summary>
public readonly struct HostArgs
{
    private static readonly int[] RegMap =
    [
        BytecodeBuilder.A0,
        BytecodeBuilder.A1,
        BytecodeBuilder.A2,
        BytecodeBuilder.A3,
    ];

    private readonly VMContext _ctx;

    internal HostArgs(VMContext ctx) => _ctx = ctx;

    // ── 索引器：行为等同 IReadOnlyList<BcValue> ──────────────
    public BcValue this[int index] =>
        index switch
        {
            >= 0 and < 4 => _ctx.ReadReg(RegMap[index]),
            // 第 5 个起由调用方 PUSH 到当前 SP 之上；栈下标不是从 0 开始。
            >= 4 => _ctx.ReadStack(
                (int)_ctx.ReadReg(BytecodeBuilder.SP).IntValue + (index - 4)),
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };

    // ── 快捷读取 ─────────────────────────────────────────────
    /// <summary>读取第 <paramref name="i"/> 个参数的整数值</summary>
    public long Int(int i) => this[i].IntValue;

    /// <summary>读取第 <paramref name="i"/> 个参数的浮点值</summary>
    public double Float(int i) => this[i].FloatValue;

    /// <summary>读取第 <paramref name="i"/> 个参数的原始 BcValue</summary>
    public BcValue Ref(int i) => this[i];

    // ── 转换为真正的集合 ─────────────────────────────────────
    /// <summary>收集前 <paramref name="count"/> 个参数为 List</summary>
    public List<BcValue> ToList(int count)
    {
        var list = new List<BcValue>(count);
        for (int i = 0; i < count; i++)
            list.Add(this[i]);
        return list;
    }

    /// <summary>收集前 <paramref name="count"/> 个参数为数组</summary>
    public BcValue[] ToArray(int count)
    {
        var arr = new BcValue[count];
        for (int i = 0; i < count; i++)
            arr[i] = this[i];
        return arr;
    }
}

// ═══════════════════════════════════════════════════════════════
//  HostFunctionRegistry.cs — 宿主函数注册表
//  自动分配索引 | HostArgs 参数视图 | 链式 API
// ═══════════════════════════════════════════════════════════════
public sealed class HostFunctionRegistry
{
    // ── 内部条目 ──────────────────────────────────────────────
    private readonly record struct Entry(ushort Index, Func<VMContext, BcValue> Handler);

    private readonly Dictionary<string, Entry> _map = new(StringComparer.Ordinal);
    private ushort _nextId;

    // ════════════════════════════════════════════════════════════
    //  Register — 通过 HostArgs 取参，返回 BcValue
    // ════════════════════════════════════════════════════════════

    /// <summary>
    /// 注册有返回值的宿主函数。
    /// <code>
    /// reg.Register("Square", (args) =>
    ///     new BcValue(args[0].IntValue * args[0].IntValue));
    /// </code>
    /// </summary>
    public HostFunctionRegistry Register(string name, Func<HostArgs, BcValue> handler) =>
        Add(name, ctx => handler(new HostArgs(ctx)));

    // ════════════════════════════════════════════════════════════
    //  RegisterAction — 无返回值版本
    // ════════════════════════════════════════════════════════════

    /// <summary>
    /// 注册无返回值的宿主函数（内部返回 BcValue(0)）。
    /// <code>
    /// reg.RegisterAction("Print", (args) =>
    ///     Console.WriteLine(args[0].IntValue));
    /// </code>
    /// </summary>
    public HostFunctionRegistry RegisterAction(string name, Action<HostArgs> handler) =>
        Add(
            name,
            ctx =>
            {
                handler(new HostArgs(ctx));
                return new BcValue(0L);
            }
        );

    // ════════════════════════════════════════════════════════════
    //  RegisterRaw — 完全自定义（需要 VMContext 时用）
    // ════════════════════════════════════════════════════════════

    /// <summary>注册函数并直接拿到 VMContext，适合需要特殊逻辑的场景。</summary>
    public HostFunctionRegistry RegisterRaw(string name, Func<VMContext, BcValue> handler) =>
        Add(name, handler);

    // ════════════════════════════════════════════════════════════
    //  输出 / 绑定
    // ════════════════════════════════════════════════════════════

    /// <summary>生成 <c>IronRuntime.Compile(..., hostFunctions: ...)</c> 所需的字典</summary>
    public Dictionary<string, ushort> ToFunctionMap() =>
        _map.ToDictionary(kv => kv.Key, kv => kv.Value.Index, StringComparer.Ordinal);

    /// <summary>
    /// 将所有已注册函数一次性绑定到运行时。
    /// <code>
    /// IronRuntime.Run(compiled, bridge => registry.BindAll(bridge.Register));
    /// </code>
    /// </summary>
    public void BindAll(Action<ushort, Func<VMContext, BcValue>> register)
    {
        foreach (var kv in _map)
            register(kv.Value.Index, kv.Value.Handler);
    }

    // ════════════════════════════════════════════════════════════
    //  查询
    // ════════════════════════════════════════════════════════════

    public int Count => _map.Count;

    public bool Contains(string n) => _map.ContainsKey(n);

    // ════════════════════════════════════════════════════════════
    //  私有
    // ════════════════════════════════════════════════════════════

    private HostFunctionRegistry Add(string name, Func<VMContext, BcValue> handler)
    {
        if (_nextId == byte.MaxValue)
            throw new InvalidOperationException("宿主函数索引溢出 (最多 255 个)");

        if (_map.ContainsKey(name))
            throw new ArgumentException($"'{name}' 已注册 (index={_map[name].Index})");

        _map[name] = new Entry(_nextId++, handler);
        return this;
    }
}
