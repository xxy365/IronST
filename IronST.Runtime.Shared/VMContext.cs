// ═══════════════════════════════════════════════════════════════
//  VMContext.cs — 宿主与调试器共用的 VM 访问接口
// ═══════════════════════════════════════════════════════════════
namespace IronST.Runtime.Shared;

public sealed class VMContext
{
    private readonly VMBase _vm;

    internal VMContext(VMBase vm) => _vm = vm;

    // ── 寄存器 ──────────────────────────────────────────────
    public BcValue ReadReg(int index) => _vm._registers[index];
    public void WriteReg(int index, BcValue v) => _vm._registers[index] = v;

    // 下面的越界异常一律传 pc=0，表示"PC 未知"：由 VM 的指令边界
    // （RunLoop 的 catch → RuntimeErrorException.AdoptPC）补上当前指令的 PC。
    // 不要传 _vm._pc —— 它在指令执行途中是陈旧的，会指向错误的源码行。

    // ── 全局变量 ────────────────────────────────────────────
    public BcValue ReadGlobal(int index)
    {
        if ((uint)index >= (uint)_vm._globals.Length)
            throw new RuntimeErrorException(
                RuntimeErrorType.SlotOverflow,
                0,
                $"ReadGlobal: index {index} out of range {_vm._globals.Length}"
            );
        return _vm._globals[index];
    }

    public void WriteGlobal(int index, BcValue v)
    {
        if ((uint)index >= (uint)_vm._globals.Length)
            throw new RuntimeErrorException(
                RuntimeErrorType.SlotOverflow,
                0,
                $"WriteGlobal: index {index} out of range {_vm._globals.Length}"
            );
        _vm._globals[index] = v;
    }

    // ── 栈 ──────────────────────────────────────────────────
    public BcValue ReadStack(int index)
    {
        if ((uint)index >= (uint)_vm._stack.Length)
            throw new RuntimeErrorException(
                RuntimeErrorType.SlotOverflow,
                0,
                $"ReadStack: index {index} out of range {_vm._stack.Length}"
            );
        return _vm._stack[index];
    }

    public void WriteStack(int index, BcValue v)
    {
        if ((uint)index >= (uint)_vm._stack.Length)
            throw new RuntimeErrorException(
                RuntimeErrorType.SlotOverflow,
                0,
                $"WriteStack: index {index} out of range {_vm._stack.Length}"
            );
        _vm._stack[index] = v;
    }

    // ── 状态 ────────────────────────────────────────────────
    public uint PC => _vm._pc;
    public VMState State => _vm._state;
    public int StackSize => _vm.StackSize;

    // ── 代码/常量 (只读，调试/反汇编用) ─────────────────────
    public ReadOnlySpan<byte> Code => _vm.Code;
    public IReadOnlyList<BcValue> ConstantPool => _vm.ConstantPool;
}