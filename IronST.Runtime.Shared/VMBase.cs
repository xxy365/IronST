// ═══════════════════════════════════════════════════════════════
//  VMBase.cs — IronST 虚拟机基类 (核心：字段 / 生命周期 / 辅助方法)
//
//  ◆ 发布模式: 子类 VM 不设置 _hooks → null → JIT 消除全部调试分支
//  ◆ 开发模式: 子类 DebugVM 注入 IVMDebugHooks → 完整调试能力
//
//  ◆ C# 交互:
//    HCALL    — 宿主方法表直通
//    SYSCALL  — 内建 EXIT / ABORT
//
//  ◆ 文件拆分 (partial class):
//    VMBase.cs              — 本文件: 字段、构造、生命周期、辅助方法
//    VMBase.Instructions.cs — 指令长度表
//    VMBase.Syscall.cs      — SYSCALL 分发
//    VMBase.RunLoop.cs      — 主执行循环
// ═══════════════════════════════════════════════════════════════
using System.Runtime.CompilerServices;

namespace IronST.Runtime.Shared;

// ── 配置 (基类级别，子类可扩展) ─────────────────────────────
public class VMOptions
{
    /// <summary>
    /// 宿主方法表。VM 构造时会补充语言内建 HCALL；宿主已显式注册的同 ID
    /// 实现不会被覆盖。
    /// </summary>
    public HostBridge? HostBridge { get; set; }
    public int StackSize { get; set; } = 65536;
    public int StructHeapCapacity { get; set; } = 100_000;

    /// <summary>
    /// 结构体类型注册表 (typeId, fieldCount)。
    /// 由 ByteCodeGenerator 的 CompileResult.StructRegistrations 提供，
    /// VM 构造时写入 StructHeap；不注册则 SNEW 抛 "unregistered typeId"。
    /// </summary>
    public IReadOnlyList<(byte TypeId, int FieldCount)>? StructTypes { get; set; }

    /// <summary>
    /// 入口函数的返回地址（LR）。<c>null</c>（默认）= 保持既有行为：PC 从
    /// <see cref="BytecodeModule.EntryPC"/> 开始执行，入口桩自己调用真正的入口函数。
    /// </summary>
    /// <remarks>
    /// 当直接从某个函数（例如 <c>ironst run x.stmod --entry Add</c>）开始执行时，
    /// 该函数结尾的 <c>RET</c> 需要一个合法的返回地址；调用方把 LR 指向代码尾部追加的
    /// <c>SYSCALL EXIT</c> 桩，使程序在函数返回后正常停机。<b>只影响内存副本，不改文件</b>。
    /// </remarks>
    public uint? EntryReturnPC { get; set; }

    /// <summary>
    /// PC → (源文件, 行号) 的解析器，由宿主注入（见 <see cref="SourceResolver.FromMap"/>）。
    /// </summary>
    /// <remarks>
    /// <c>null</c>（默认）= 不回填源位置，运行期错误的 <see cref="RuntimeErrorException.Message"/>
    /// 只带 PC，与历史格式逐字一致。非 <c>null</c> 时，VM 在错误边界
    /// （<see cref="VMBase.Run"/> 的 catch）按 PC 回填一次
    /// <see cref="RuntimeErrorException.SourceLocation"/>，于是**所有**打印该异常的地方
    /// （CLI / IDE / 示例宿主）都自动带上「（文件:行）」。
    /// </remarks>
    public Func<uint, (string file, int line)?>? SourceLocator { get; set; }

    /// <summary>
    /// 协作式取消令牌。取消时回调把 VM 状态从 <see cref="VMState.Running"/> 翻走，
    /// 主循环在下一个字节码边界退出，随后 <see cref="VMBase.Run"/> 抛
    /// <see cref="OperationCanceledException"/>。
    /// </summary>
    /// <remarks>
    /// <c>default</c>（默认）= 不可取消，与既有行为完全一致。
    /// 取消**不是**抢占式的：若 VM 正阻塞在某个宿主 HCALL 里，要等该调用返回才生效。
    /// </remarks>
    public CancellationToken Cancellation { get; set; }

    /// <summary>
    /// 指令预算：单次 <see cref="VMBase.Run"/> 最多执行多少条字节码指令。
    /// 超过时在指令边界抛 <see cref="RuntimeErrorException"/>（
    /// <see cref="RuntimeErrorType.InstructionBudgetExceeded"/>），PC / 源位置照常回填。
    /// </summary>
    /// <remarks>
    /// <c>0</c>（默认）= 不限制，与既有行为完全一致。
    /// 与 <see cref="Cancellation"/> 的分工：取消是外部叫停（Ctrl+C / 墙钟超时，退出码 130），
    /// 预算是对“跑了多久”的确定性上限（不依赖墙钟，可复现），两者互不替代。
    /// 真实 PLC 程序的扫描循环本就长期运行，所以预算默认不开启，由宿主显式给出。
    /// </remarks>
    public long MaxInstructions { get; set; }
}

// ══════════════════════════════════════════════════════════════
//  VMBase (partial)
// ══════════════════════════════════════════════════════════════
public abstract partial class VMBase : IDisposable
{
    // ── 模块数据 ─────────────────────────────────────────────
    public byte[] Code;
    public BcValue[] ConstantPool;

    // ── 运行时状态 ───────────────────────────────────────────
    internal RegisterFile _registers;
    internal BcValue[] _stack;
    internal BcValue[] _globals;
    internal uint _pc;
    internal volatile VMState _state;

    // ── 协作式取消 ───────────────────────────────────────────
    // 回调可能从取消方线程写 _state，故 _state 声明为 volatile。
    private readonly CancellationToken _cancellation;

    // ── 结构体堆 ─────────────────────────────────────────────
    internal StructHeap _structHeap;

    // ── 宿主互操作 ───────────────────────────────────────────
    internal readonly HostBridge _hostBridge;

    // ── 调试钩子 (null = 发布模式，JIT 消除全部分支) ─────────
    protected readonly IVMDebugHooks? _hooks;

    // ── 错误源位置解析 (null = 只报 PC) ──────────────────────
    private readonly Func<uint, (string file, int line)?>? _sourceLocator;

    // ── 指令预算 (0 = 不限，热路径只多一次比较) ───────────────
    private readonly long _maxInstructions;

    // ── 错误 ─────────────────────────────────────────────────
    internal RuntimeErrorException? _lastError;

    // ── 公开 API ─────────────────────────────────────────────
    public VMContext Context { get; }
    public VMState State => _state;
    public RuntimeErrorException? LastError => _lastError;
    public int StackSize { get; }

    // ══════════════════════════════════════════════════════════
    //  构造 (子类调用)
    // ══════════════════════════════════════════════════════════
    protected VMBase(BytecodeModule module, VMOptions options, IVMDebugHooks? hooks)
    {
        Code = module.Code;
        ConstantPool = module.ConstantPool;
        StackSize = options.StackSize;
        _pc = module.EntryPC;
        _registers = new RegisterFile(StackSize);
        _stack = new BcValue[StackSize];
        _globals = new BcValue[module.GlobalCount];
        _structHeap = new StructHeap(options.StructHeapCapacity);
        _hostBridge = options.HostBridge ?? new HostBridge();

        // 语言内建 HCALL 不依赖宿主环境；安装到每个 VM，避免同一份字节码在
        // CLI / IDE / STModInvoker / 测试宿主之间出现不同的可执行性。
        MathStandardLibrary.Register(_hostBridge);
        _hooks = hooks;
        _sourceLocator = options.SourceLocator;
        _cancellation = options.Cancellation;
        _maxInstructions = options.MaxInstructions;
        _state = VMState.Init;
        Context = new VMContext(this);

        // 直接从函数入口开始执行时，把 LR 指向调用方插入的 EXIT 桩（默认 null = 不改）。
        if (options.EntryReturnPC is uint returnPC)
            _registers[3] = new BcValue((long)returnPC);

        // 结构体类型注册（来自 CompileResult.StructRegistrations）
        if (options.StructTypes != null)
            foreach (var (typeId, fieldCount) in options.StructTypes)
                _structHeap.RegisterType(typeId, fieldCount);
    }

    // ══════════════════════════════════════════════════════════
    //  生命周期
    // ══════════════════════════════════════════════════════════
    public void Run()
    {
        if (_state == VMState.Halted)
            throw new InvalidOperationException("VM is halted");

        _state = VMState.Running;

        // 取消回调只做一件事：把 _state 从 Running 翻走。
        // RunLoop 的 while 条件本来每执行一条指令就读一次 _state —— 热路径零新增开销。
        using var registration = _cancellation.CanBeCanceled
            ? _cancellation.Register(static state => ((VMBase)state!).RequestCancellation(), this)
            : default;

        try {
            RunLoop();
        } catch (OperationCanceledException) {
            // 宿主 HCALL 内直接抛取消：统一收口为 Halted，避免留下 Running 状态。
            _state = VMState.Halted;
            throw;
        } catch (RuntimeErrorException ex) {
            StampSource(ex);
            _lastError = ex;
            if (_hooks is not null) {
                _state = VMState.Paused;
                _hooks.OnError(ex);
                return;
            }
            _state = VMState.Halted;
            throw;
        } catch (IndexOutOfRangeException ex) {
            var err = new RuntimeErrorException(
                RuntimeErrorType.IllegalOpcode,
                _pc,
                $"PC out of range: {ex.Message}"
            );
            StampSource(err);
            _lastError = err;
            if (_hooks is not null) {
                _state = VMState.Paused;
                _hooks.OnError(err);
                return;
            }
            _state = VMState.Halted;
            throw;
        }

        // RunLoop 正常返回（HALT / BRK / 取消）后，区分「完成」与「被取消」。
        // 若程序恰好在此刻正常停机（Halted），取消不覆盖「已完成」。
        if (_state != VMState.Halted && _cancellation.IsCancellationRequested) {
            _state = VMState.Halted;
            throw new OperationCanceledException(_cancellation);
        }
    }

    /// <summary>
    /// 取消回调（可能在取消方线程执行）。只翻转状态，真正的收口在 <see cref="Run"/>。
    /// </summary>
    private void RequestCancellation()
    {
        if (_state == VMState.Running)
            _state = VMState.Paused;
    }

    public void Pause() => _state = VMState.Paused;

    public void Resume()
    {
        if (_state != VMState.Paused)
            throw new InvalidOperationException("VM is not paused");
        if (_pc < Code.Length && Code[_pc] == (byte)OpCode.BRK)
            _pc++;
        Run();
    }

    public void Reset(BytecodeModule module)
    {
        Code = module.Code;
        ConstantPool = module.ConstantPool;
        _pc = module.EntryPC;
        _state = VMState.Init;
        _lastError = null;
        _registers = new RegisterFile(StackSize);
        _stack = new BcValue[StackSize];
        _globals = new BcValue[module.GlobalCount];
        _structHeap.Reset();
    }

    // ══════════════════════════════════════════════════════════
    //  IDisposable
    // ══════════════════════════════════════════════════════════
    public void Dispose()
    {
    }

    // ══════════════════════════════════════════════════════════
    //  辅助方法
    // ══════════════════════════════════════════════════════════

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static RuntimeErrorException RuntimeError(
        RuntimeErrorType t,
        uint pc,
        string? msg = null
    ) => new(t, pc, msg ?? t.ToString());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static void ThrowDivZero(uint pc) =>
        throw new RuntimeErrorException(RuntimeErrorType.DivideByZero, pc, "Divide by zero");

    /// <summary>
    /// 源位置回填的<b>唯一</b>落点：按 <see cref="RuntimeErrorException.PC"/> 查一次，
    /// 写进异常的 <see cref="RuntimeErrorException.SourceLocation"/>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 为什么在 VM 的这个边界统一做，而不是让每个调用方自己查：调用方有 CLI/IDE/示例宿主/测试
    /// 七八个，漏一个就少一处行号（历史上就是这么漏的）。
    /// </para>
    /// <para>
    /// 走到这里时 <c>ex.PC</c> 一定是<b>真实</b>的：组件（<c>StructHeap</c>/<c>BcValue</c>）
    /// 抛的异常 PC 为 0，已由 <c>RunLoop</c> 的 catch 用当前指令 PC 补齐
    /// （<see cref="RuntimeErrorException.AdoptPC"/>）—— 那才是唯一还在现场的边界，
    /// <c>_pc</c> 字段在指令执行途中是陈旧的。
    /// </para>
    /// <para>
    /// 已回填过就不覆盖：宿主若在异常逃出 VM 前自行设过位置，以宿主的为准。
    /// </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void StampSource(RuntimeErrorException ex)
    {
        if (_sourceLocator is null || ex.SourceLocation is not null)
            return;

        ex.SourceLocation = _sourceLocator(ex.PC);
    }
}