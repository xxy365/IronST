// ═══════════════════════════════════════════════════════════════
//  BreakpointManager.cs — 高级断点管理
// ═══════════════════════════════════════════════════════════════
using IronST.Runtime.Shared;

namespace IronST.Runtime.Debug;

/// <summary>断点类型。</summary>
public enum BreakpointKind
{
    /// <summary>地址断点 — 命中指定 PC 时暂停。</summary>
    Address,
    /// <summary>条件断点 — 表达式为 true 时暂停。</summary>
    Conditional,
    /// <summary>命中计数断点 — 第 N 次命中时暂停。</summary>
    HitCount,
}

/// <summary>断点描述。</summary>
public sealed class Breakpoint
{
    public int Id { get; init; }
    public uint Address { get; init; }
    public BreakpointKind Kind { get; init; }
    public bool Enabled { get; set; } = true;

    /// <summary>条件断点: 每次命中时评估，返回 true 则暂停。</summary>
    public Func<VMContext, bool>? Condition { get; init; }

    /// <summary>命中计数: 达到此数时暂停。0 = 每次命中都暂停。</summary>
    public int HitCountThreshold { get; init; }

    internal int CurrentHits { get; set; }
}

/// <summary>高级断点管理器。</summary>
public sealed class BreakpointManager
{
    private readonly Dictionary<int, Breakpoint> _bps = new();
    private int _nextId = 1;

    public Breakpoint AddAddressBreakpoint(uint address)
    {
        var bp = new Breakpoint { Id = _nextId++, Address = address, Kind = BreakpointKind.Address };
        _bps[bp.Id] = bp;
        return bp;
    }

    public Breakpoint AddConditionalBreakpoint(uint address, Func<VMContext, bool> condition)
    {
        var bp = new Breakpoint
        {
            Id = _nextId++,
            Address = address,
            Kind = BreakpointKind.Conditional,
            Condition = condition
        };
        _bps[bp.Id] = bp;
        return bp;
    }

    public Breakpoint AddHitCountBreakpoint(uint address, int hitCount)
    {
        var bp = new Breakpoint
        {
            Id = _nextId++,
            Address = address,
            Kind = BreakpointKind.HitCount,
            HitCountThreshold = hitCount
        };
        _bps[bp.Id] = bp;
        return bp;
    }

    public void Remove(int id) => _bps.Remove(id);
    public void Clear() => _bps.Clear();
    public IReadOnlyDictionary<int, Breakpoint> All => _bps;

    /// <summary>检查给定 PC 是否应该暂停。返回命中断点的 Id，未命中返回 null。</summary>
    public int? ShouldBreak(uint pc, VMContext context)
    {
        foreach (var bp in _bps.Values) {
            if (!bp.Enabled || bp.Address != pc) continue;

            bp.CurrentHits++;

            bool shouldBreak = bp.Kind switch
            {
                BreakpointKind.Address => true,
                BreakpointKind.Conditional => bp.Condition?.Invoke(context) ?? true,
                BreakpointKind.HitCount => bp.CurrentHits >= bp.HitCountThreshold,
                _ => true,
            };

            if (shouldBreak)
                return bp.Id;
        }
        return null;
    }
}