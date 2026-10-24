// ═══════════════════════════════════════════════════════════════
//  VMDebugSession.cs — 调试系统 (ISA §11)
//
//  ◆ 断点: 地址断点 + 条件断点 + 命中计数断点 (委托 BreakpointManager)
//  ◆ 单步: step into / step over
//  ◆ 监视: 全局变量/局部变量槽位监视
//  ◆ 错误断点: TypeError / DivideByZero / SlotOverflow
//  ◆ Trace: 指令跟踪日志 (含反汇编)
//  ◆ 源码映射: PC → 文件/行/列
// ═══════════════════════════════════════════════════════════════
using IronST.Runtime.Shared;

namespace IronST.Runtime.Debug;

// ── 源码映射 (ISA §12) ────────────────────────────────────────
public sealed class SourceMap
{
    public int Version { get; set; } = 1;
    public List<string> SourceFiles { get; } = new();
    public List<SourceMapping> Mappings { get; } = new();

    public record struct SourceMapping(uint PC, int FileIndex, int Line, int Column);

    /// <summary>二分查找: PC ≤ target 的最大条目</summary>
    public SourceMapping? FindByPC(uint pc)
    {
        int lo = 0,
            hi = Mappings.Count - 1;
        SourceMapping? result = null;
        while (lo <= hi) {
            int mid = (lo + hi) / 2;
            if (Mappings[mid].PC <= pc) {
                result = Mappings[mid];
                lo = mid + 1;
            } else
                hi = mid - 1;
        }
        return result;
    }
}

// ── 调试事件 ──────────────────────────────────────────────────
public abstract class DebugEvent
{
    public uint PC { get; }

    protected DebugEvent(uint pc) => PC = pc;
}

public sealed class BreakpointHitEvent : DebugEvent
{
    public uint BreakpointAddr { get; }
    public int BreakpointId { get; }

    public BreakpointHitEvent(uint pc, uint bpAddr, int breakpointId)
        : base(pc)
    {
        BreakpointAddr = bpAddr;
        BreakpointId = breakpointId;
    }
}

public sealed class StepCompleteEvent : DebugEvent
{
    public StepCompleteEvent(uint pc)
        : base(pc) { }
}

public sealed class RuntimeErrorEvent : DebugEvent
{
    public RuntimeErrorException Error { get; }

    public RuntimeErrorEvent(RuntimeErrorException err)
        : base(err.PC) => Error = err;
}

// ── Trace 条目 ────────────────────────────────────────────────
public sealed class DebugTraceEntry
{
    public uint PC;
    public byte Opcode;
    public string Disassembly = "";
    public string RegSnapshot = "";
}

// ══════════════════════════════════════════════════════════════
//  VMDebugSession — 调试会话
// ══════════════════════════════════════════════════════════════
public sealed class VMDebugSession : IVMDebugHooks
{
    // ── 断点 (委托给 BreakpointManager) ────────────────────────
    private readonly BreakpointManager _bpManager = new();

    /// <summary>底层断点管理器 (只读，用于高级查询)。</summary>
    public BreakpointManager Breakpoints => _bpManager;

    /// <summary>添加地址断点。返回断点对象 (含 Id，用于 RemoveBreakpoint)。</summary>
    public Breakpoint AddAddressBreakpoint(uint address) =>
        _bpManager.AddAddressBreakpoint(address);

    /// <summary>添加条件断点。返回断点对象。</summary>
    public Breakpoint AddConditionalBreakpoint(uint address, Func<VMContext, bool> condition) =>
        _bpManager.AddConditionalBreakpoint(address, condition);

    /// <summary>添加命中计数断点。返回断点对象。</summary>
    public Breakpoint AddHitCountBreakpoint(uint address, int hitCount) =>
        _bpManager.AddHitCountBreakpoint(address, hitCount);

    /// <summary>按 ID 移除断点。</summary>
    public void RemoveBreakpoint(int id) => _bpManager.Remove(id);

    /// <summary>清空所有断点。</summary>
    public void ClearBreakpoints() => _bpManager.Clear();

    // ── 监视 ──────────────────────────────────────────────────
    public HashSet<int> WatchedGlobalSlots { get; } = new();
    public HashSet<int> WatchedLocalSlots { get; } = new();

    // ── 错误断点 ──────────────────────────────────────────────
    public bool BreakOnTypeError { get; set; }
    public bool BreakOnDivideByZero { get; set; }
    public bool BreakOnSlotOverflow { get; set; }

    // ── Trace ─────────────────────────────────────────────────
    public bool TraceEnabled { get; set; }
    public int MaxTraceEntries { get; set; } = 50_000;
    private readonly Queue<DebugTraceEntry> _traceQueue = new();

    /// <summary>
    /// 已记录的跟踪条目 (只读视图)。
    /// 内部使用 Queue 存储，每次访问返回快照数组。
    /// </summary>
    public IReadOnlyList<DebugTraceEntry> TraceLog => _traceQueue.ToArray();

    // ── 源码映射 ──────────────────────────────────────────────
    public SourceMap? SourceMap { get; set; }

    // ── 事件 ──────────────────────────────────────────────────
    public event Action<DebugEvent>? OnDebugEvent;

    // ── 单步控制 ──────────────────────────────────────────────
    internal bool _singleStep;

    /// <summary>
    /// 上次暂停所在的 PC。
    ///
    /// VMBase.RunLoop 每次都从 _pc 重新起跑，并在指令执行**之前**调用
    /// PreStep —— 也就是说，暂停发生时那条指令还没执行过。Resume 之后的
    /// 第一次 PreStep 因此仍落在同一个 PC 上，必须放它过去；否则单步和
    /// 单步跳过会永远卡在原地（PC 纹丝不动，输出面板每条都一样）。
    /// </summary>
    private uint? _resumePc;

    /// <summary>
    /// 源码行级单步的出发行。
    ///
    /// 单步不能是"一条指令一停"：一行赋值会编译成取值、运算、存值好几条
    /// 指令，用户得按三四次才能往下走一行。这里记下出发时的源码行，一路
    /// 跑到行号变了才停，对齐主流编辑器 F10 的手感。
    /// </summary>
    private int? _stepLine;

    /// <summary>单步最多放行多少条指令。纯粹是保险丝：防止同一行上是死循环时永远按不动。</summary>
    private const int StepInstructionBudget = 5_000_000;

    private int _stepBudget;

    // ── Step Over 计数器 ──────────────────────────────────────
    internal bool _stepOver;
    internal int _callDepth;

    // ── 绑定的 VM (DebugVM 通过 Attach 设置) ──────────────────
    private VMBase? _vm;

    internal void Attach(VMBase vm) => _vm = vm;

    // ══════════════════════════════════════════════════════════
    //  IVMDebugHooks.PreStep — 每条指令执行前调用
    //  返回 false = 暂停执行
    // ══════════════════════════════════════════════════════════
    public bool PreStep(VMBase vm, uint pc)
    {
        if (TraceEnabled)
            LogTrace(vm, pc);

        // Resume 之后的第一次进来，PC 必然还停在原地（RunLoop 从 _pc 起跑），
        // 而那条指令尚未执行过。这一发必须放行，否则单步/单步跳过永远原地
        // 打转；从断点按"继续"也会立刻又命中同一个断点。
        bool justResumed = _resumePc == pc;
        if (justResumed)
            _resumePc = null;

        // ── Step Over ──
        if (_stepOver) {
            if (justResumed)
                return true;

            OpCode op = (OpCode)vm.Code[pc];

            if (op is OpCode.CALL or OpCode.CALLR)
                _callDepth++;

            if (op is OpCode.RET && _callDepth > 0) {
                _callDepth--;
                if (_callDepth == 0)
                    return true;
            }

            if (_callDepth > 0)
                return true;

            if (!StepLineChanged(pc))
                return true;    // 还没跨出出发那一行

            _stepOver = false;
            _stepLine = null;
            PauseAt(vm, pc, new StepCompleteEvent(pc));
            return false;
        }

        // ── Single Step ──
        if (_singleStep) {
            if (justResumed)
                return true;

            if (!StepLineChanged(pc))
                return true;    // 还没跨出出发那一行

            _singleStep = false;
            _stepLine = null;
            PauseAt(vm, pc, new StepCompleteEvent(pc));
            return false;
        }

        // ── 断点 (统一走 BreakpointManager) ──
        if (!justResumed && _bpManager.ShouldBreak(pc, vm.Context) is int bpId) {
            var bp = _bpManager.All[bpId];
            PauseAt(vm, pc, new BreakpointHitEvent(pc, bp.Address, bpId));
            return false;
        }

        return true;
    }

    /// <summary>
    /// 统一的暂停出口。先记下停在哪个 PC，Resume 之后好放行那条还没执行过
    /// 的指令（见 PreStep 开头的 justResumed）。
    /// </summary>
    private void PauseAt(VMBase vm, uint pc, DebugEvent ev)
    {
        _resumePc = pc;
        vm.Pause();
        OnDebugEvent?.Invoke(ev);
    }

    /// <summary>
    /// 单步是否已经跨到别的源码行。
    /// </summary>
    /// <returns>
    /// true 表示该停了（跨到新行 / 没有行号信息 / 触发指令预算）。
    /// 拿不到源码映射时一律返回 true，退化成指令级单步 —— 否则会无限往下跑。
    /// </returns>
    private bool StepLineChanged(uint pc)
    {
        if (SourceMap is null)
            return true;

        // 保险丝：同一行上如果跑的是死循环，行号永远不会变，这一停就再也
        // 按不出来了。给个很宽的预算，真撞上了就先停下来让用户看到。
        if (--_stepBudget <= 0)
            return true;

        // FindByPC 返回的是「PC ≤ target 的最近一条」，所以即便这条指令本身
        // 没有行号，拿到的也是它所属代码块的上一个行号，不会是 null。
        int? line = SourceMap.FindByPC(pc)?.Line;
        if (line is not int now || now <= 0)
            return true;

        return now != _stepLine;
    }

    // ══════════════════════════════════════════════════════════
    //  IVMDebugHooks.OnError — 运行时错误回调
    // ══════════════════════════════════════════════════════════
    public void OnError(RuntimeErrorException err)
    {
        bool shouldBreak = err.ErrorType switch
        {
            RuntimeErrorType.TypeError => BreakOnTypeError,
            RuntimeErrorType.DivideByZero => BreakOnDivideByZero,
            RuntimeErrorType.SlotOverflow => BreakOnSlotOverflow,
            _ => true,
        };
        if (shouldBreak)
            OnDebugEvent?.Invoke(new RuntimeErrorEvent(err));
    }

    // ══════════════════════════════════════════════════════════
    //  IVMDebugHooks.OnBreak — BRK 硬断点回调
    // ══════════════════════════════════════════════════════════
    public void OnBreak(VMBase vm, uint pc) =>
        OnDebugEvent?.Invoke(new BreakpointHitEvent(pc, pc, -1));

    // ══════════════════════════════════════════════════════════
    //  宿主 API
    // ══════════════════════════════════════════════════════════
    public void SetSingleStep()
    {
        _singleStep = true;
        BeginStep();
    }

    public void SetStepOver()
    {
        _stepOver = true;
        _callDepth = 0;
        BeginStep();
    }

    /// <summary>
    /// 单步出发前的准备：记下当前源码行，作为「跑到了哪一行」的对照基准。
    /// </summary>
    private void BeginStep()
    {
        _stepLine = SourceMap?.FindByPC(_vm?.Context.PC ?? 0)?.Line;
        _stepBudget = StepInstructionBudget;
    }

    // ══════════════════════════════════════════════════════════
    //  Trace 日志
    // ══════════════════════════════════════════════════════════
    private void LogTrace(VMBase vm, uint pc)
    {
        if (_traceQueue.Count >= MaxTraceEntries)
            _traceQueue.Dequeue();

        _traceQueue.Enqueue(
            new DebugTraceEntry
            {
                PC = pc,
                Opcode = vm.Code[pc],
                Disassembly = Disassemble(vm, pc),
                RegSnapshot =
                    $"SP={vm.Context.ReadReg(1)} FP={vm.Context.ReadReg(2)} R4={vm.Context.ReadReg(4)}",
            }
        );
    }

    // ══════════════════════════════════════════════════════════
    //  反汇编器 — 使用 OpCode 枚举
    // ══════════════════════════════════════════════════════════
    private static string Disassemble(VMBase vm, uint pc) => Disassembler.Disassemble(vm.Code, pc);
}