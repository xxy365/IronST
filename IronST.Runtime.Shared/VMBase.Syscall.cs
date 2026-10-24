// ═══════════════════════════════════════════════════════════════
//  VMBase.Syscall.cs — SYSCALL 分发 (ISA §9.1)
//  包含: HandleSyscall
// ═══════════════════════════════════════════════════════════════
namespace IronST.Runtime.Shared;

public abstract partial class VMBase
{
    // ══════════════════════════════════════════════════════════
    //  SYSCALL 分发 (ISA §9.1)
    // ══════════════════════════════════════════════════════════
    protected void HandleSyscall(byte func, RegisterFile R, uint pc)
    {
        switch ((SyscallFunc)func) {
            case SyscallFunc.EXIT:
                _state = VMState.Halted;
                break;

            case SyscallFunc.ABORT:
                _state = VMState.Halted;
                throw RuntimeError(
                    RuntimeErrorType.IllegalOpcode,
                    pc,
                    $"ABORT code {R[4].IntValue}"
                );

            default:
                throw RuntimeError(
                    RuntimeErrorType.IllegalOpcode,
                    pc,
                    $"Unknown SYSCALL 0x{func:X2}"
                );
        }
    }
}