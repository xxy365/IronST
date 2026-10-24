// ═══════════════════════════════════════════════════════════════
//  HostBridge.cs — 宿主互操作层 (ISA §9)
//  包含: HostBridge
//
//  放在 IronST.Runtime.Shared，发布包和开发包共享
// ═══════════════════════════════════════════════════════════════
namespace IronST.Runtime.Shared;

// ── HostBridge: HCALL 方法注册表 ─────────────────────────

public sealed class HostBridge
{
    private readonly Dictionary<ushort, Func<VMContext, BcValue>> _handlers = new();

    /// <summary>注册宿主方法 (HCALL)</summary>
    public void Register(ushort id, Func<VMContext, BcValue> handler)
        => _handlers[id] = handler ?? throw new ArgumentNullException(nameof(handler));

    /// <summary>
    /// 注册宿主方法；若该 ID 已有实现则保留原实现。
    /// </summary>
    /// <remarks>
    /// VM 用它在不覆盖宿主显式注册的前提下安装语言内建 HCALL
    /// （例如整数取余）。自定义宿主可在构造 VM 前注册同 ID 来覆盖默认实现。
    /// </remarks>
    public void RegisterIfAbsent(ushort id, Func<VMContext, BcValue> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handlers.TryAdd(id, handler);
    }

    internal bool TryInvoke(ushort id, VMContext ctx, out BcValue result)
    {
        if (_handlers.TryGetValue(id, out var fn)) { result = fn(ctx); return true; }
        result = default;
        return false;
    }
}