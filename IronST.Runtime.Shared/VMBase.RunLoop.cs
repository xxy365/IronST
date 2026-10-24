// ═══════════════════════════════════════════════════════════════
//  VMBase.RunLoop.cs — 主执行循环
//
//  ◆ _hooks == null 时 (发布模式):
//    JIT 对 null 分支做 branch elimination，
//    预测器对非 null 分支 miss ≈ 0，
//    实际开销 ≈ 1 ns / 指令 (可忽略)
// ═══════════════════════════════════════════════════════════════
using System.Numerics;
using System.Runtime.CompilerServices;

namespace IronST.Runtime.Shared;

public abstract partial class VMBase
{
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected void RunLoop()
    {
        var code = Code;
        var R = _registers;
        var stack = _stack;
        var globals = _globals;
        var pc = _pc;
        var host = _hostBridge;
        var hooks = _hooks;
        var heap = _structHeap;
        // 指令预算：-1 = 不限。放在局部变量里避免每步读字段；
        // 不限时只剩一次 long 比较（分支预测稳定），实测开销可忽略。
        var remaining = _maxInstructions > 0 ? _maxInstructions : -1;

        try {
            while (_state == VMState.Running) {
                // ── 调试钩子 (发布模式: hooks==null, JIT 消除此分支) ──
                if (hooks is not null && !hooks.PreStep(this, pc)) {
                    _pc = pc;
                    _state = VMState.Paused;
                    return;
                }

                // ── 指令预算（C1）──
                // 先判后扣：“上限 N”恰好允许执行 N 条，第 N+1 条边界报错。
                // 放在调试钩子之后：断点/单步先暂停，暂停本身不消耗预算、也不误判超限。
                if (remaining >= 0 && remaining-- == 0) {
                    // PC 由下面的 catch 用局部变量 pc 回填；这里不写 _pc。
                    throw new RuntimeErrorException(
                        RuntimeErrorType.InstructionBudgetExceeded,
                        pc,
                        $"指令预算耗尽：单次运行最多 {_maxInstructions} 条指令（--max-instructions）"
                    );
                }
    
                OpCode op = (OpCode)code[pc];
    
                switch (op) {
                    // ── 系统控制 ──
                    case OpCode.NOP:
                        pc++;
                        break;
                    case OpCode.HALT:
                        _state = VMState.Halted;
                        _pc = pc;
                        return;
                    case OpCode.BRK:
                        _pc = pc;
                        _state = VMState.Paused;
                        hooks?.OnBreak(this, pc);
                        return;
                    case OpCode.SYSCALL:
                        HandleSyscall(code[pc + 1], R, pc);
                        pc += 2;
                        break;
    
                    // ── 常量加载 ──
                    case OpCode.LNIL:
                        R[code[pc + 1]] = BcValue.Void;
                        pc += 2;
                        break;
                    case OpCode.LTRUE:
                        R[code[pc + 1]] = new BcValue(1L);
                        pc += 2;
                        break;
                    case OpCode.LFALSE:
                        R[code[pc + 1]] = new BcValue(0L);
                        pc += 2;
                        break;
                    case OpCode.LDC:
                        R[code[pc + 1]] = ConstantPool[BitConverter.ToInt32(code, (int)pc + 2)];
                        pc += 6;
                        break;
                    case OpCode.MOV:
                        R[code[pc + 1]] = R[code[pc + 2]];
                        pc += 3;
                        break;
                    case OpCode.SWAP: {
                            int d = code[pc + 1],
                                s = code[pc + 2];
                            var t = R[d];
                            R[d] = R[s];
                            R[s] = t;
                            pc += 3;
                            break;
                        }
    
                    // ── 栈操作 ──
                    case OpCode.PUSH: {
                            long sp = --R.SP;
                            if (sp < 0)
                                throw RuntimeError(RuntimeErrorType.StackOverflow, pc);
                            stack[(int)sp] = R[code[pc + 1]];
                            pc += 2;
                            break;
                        }
                    case OpCode.POP: {
                            long sp = R.SP;
                            if (sp >= StackSize)
                                throw RuntimeError(RuntimeErrorType.StackUnderflow, pc);
                            R[code[pc + 1]] = stack[(int)sp];
                            R.SP = sp + 1;
                            pc += 2;
                            break;
                        }
                    case OpCode.PUSHN: {
                            short n = (short)(code[pc + 1] | (code[pc + 2] << 8));
                            R.SP -= n;
                            if (R.SP < 0)
                                throw RuntimeError(RuntimeErrorType.StackOverflow, pc);
                            pc += 3;
                            break;
                        }
                    case OpCode.POPN: {
                            short n = (short)(code[pc + 1] | (code[pc + 2] << 8));
                            R.SP += n;
                            if (R.SP > StackSize)
                                throw RuntimeError(RuntimeErrorType.StackUnderflow, pc);
                            pc += 3;
                            break;
                        }
                    case OpCode.FRAME: {
                            short n = (short)(code[pc + 1] | (code[pc + 2] << 8));
                            long sp = --R.SP;
                            if (sp < 0)
                                throw RuntimeError(RuntimeErrorType.StackOverflow, pc);
                            stack[(int)sp] = R[3]; // push LR
                            sp = --R.SP;
                            if (sp < 0)
                                throw RuntimeError(RuntimeErrorType.StackOverflow, pc);
                            stack[(int)sp] = R[2]; // push FP
                            R.FP = sp;
                            R.SP = sp - n;
                            if (R.SP < 0)
                                throw RuntimeError(RuntimeErrorType.StackOverflow, pc);
                            pc += 3;
                            break;
                        }
                    case OpCode.LEAVE: {
                            R.SP = R.FP;
                            long sp = R.SP;
                            R[2] = stack[(int)sp];     // pop FP
                            R[3] = stack[(int)sp + 1]; // pop LR
                            R.SP = sp + 2;
                            pc++;
                            break;
                        }
    
                    // ── 变量访问 ──
                    case OpCode.GETL: {
                            int slot = (short)(code[pc + 2] | (code[pc + 3] << 8));
                            int addr = (int)R.FP - 1 - slot;
                            if ((uint)addr >= (uint)StackSize)
                                throw RuntimeError(RuntimeErrorType.SlotOverflow, pc);
                            R[code[pc + 1]] = stack[addr];
                            pc += 4;
                            break;
                        }
                    case OpCode.SETL: {
                            int slot = (short)(code[pc + 2] | (code[pc + 3] << 8));
                            int addr = (int)R.FP - 1 - slot;
                            if ((uint)addr >= (uint)StackSize)
                                throw RuntimeError(RuntimeErrorType.SlotOverflow, pc);
                            stack[addr] = R[code[pc + 1]];
                            pc += 4;
                            break;
                        }
                    case OpCode.GETG: {
                            int slot = (short)(code[pc + 2] | (code[pc + 3] << 8));
                            if ((uint)slot >= (uint)globals.Length)
                                throw RuntimeError(RuntimeErrorType.SlotOverflow, pc);
                            R[code[pc + 1]] = globals[slot];
                            pc += 4;
                            break;
                        }
                    case OpCode.SETG: {
                            int slot = (short)(code[pc + 2] | (code[pc + 3] << 8));
                            if ((uint)slot >= (uint)globals.Length)
                                throw RuntimeError(RuntimeErrorType.SlotOverflow, pc);
                            globals[slot] = R[code[pc + 1]];
                            pc += 4;
                            break;
                        }
                    case OpCode.GETR: {
                            int addr = (int)R.FP - 1 - (int)R[code[pc + 2]].IntValue;
                            if ((uint)addr >= (uint)StackSize)
                                throw RuntimeError(RuntimeErrorType.SlotOverflow, pc);
                            R[code[pc + 1]] = stack[addr];
                            pc += 3;
                            break;
                        }
                    case OpCode.SETR: {
                            int addr = (int)R.FP - 1 - (int)R[code[pc + 2]].IntValue;
                            if ((uint)addr >= (uint)StackSize)
                                throw RuntimeError(RuntimeErrorType.SlotOverflow, pc);
                            stack[addr] = R[code[pc + 1]];
                            pc += 3;
                            break;
                        }
                    case OpCode.GETGI: {
                            int idx = (int)R[code[pc + 2]].IntValue;
                            if ((uint)idx >= (uint)globals.Length)
                                throw RuntimeError(RuntimeErrorType.SlotOverflow, pc);
                            R[code[pc + 1]] = globals[idx];
                            pc += 3;
                            break;
                        }
                    case OpCode.SETGI: {
                            int idx = (int)R[code[pc + 2]].IntValue;
                            if ((uint)idx >= (uint)globals.Length)
                                throw RuntimeError(RuntimeErrorType.SlotOverflow, pc);
                            globals[idx] = R[code[pc + 1]];
                            pc += 3;
                            break;
                        }
    
                    // ── 整数算术 ──
                    case OpCode.IADD: {
                            int d = code[pc + 1],
                                s1 = code[pc + 2],
                                s2 = code[pc + 3];
                            R[d] = new BcValue(R[s1].IntValue + R[s2].IntValue);
                            pc += 4;
                            break;
                        }
                    case OpCode.ISUB: {
                            int d = code[pc + 1],
                                s1 = code[pc + 2],
                                s2 = code[pc + 3];
                            R[d] = new BcValue(R[s1].IntValue - R[s2].IntValue);
                            pc += 4;
                            break;
                        }
                    case OpCode.IMUL: {
                            int d = code[pc + 1],
                                s1 = code[pc + 2],
                                s2 = code[pc + 3];
                            R[d] = new BcValue(R[s1].IntValue * R[s2].IntValue);
                            pc += 4;
                            break;
                        }
                    case OpCode.IDIV: {
                            int d = code[pc + 1],
                                s1 = code[pc + 2],
                                s2 = code[pc + 3];
                            if (R[s2].IntValue == 0)
                                ThrowDivZero(pc);
                            R[d] = new BcValue(R[s1].IntValue / R[s2].IntValue);
                            pc += 4;
                            break;
                        }
    
                    // ── 浮点算术 ──
                    case OpCode.FADD: {
                            int d = code[pc + 1],
                                s1 = code[pc + 2],
                                s2 = code[pc + 3];
                            R[d] = new BcValue(R[s1].FloatValue + R[s2].FloatValue);
                            pc += 4;
                            break;
                        }
                    case OpCode.FSUB: {
                            int d = code[pc + 1],
                                s1 = code[pc + 2],
                                s2 = code[pc + 3];
                            R[d] = new BcValue(R[s1].FloatValue - R[s2].FloatValue);
                            pc += 4;
                            break;
                        }
                    case OpCode.FMUL: {
                            int d = code[pc + 1],
                                s1 = code[pc + 2],
                                s2 = code[pc + 3];
                            R[d] = new BcValue(R[s1].FloatValue * R[s2].FloatValue);
                            pc += 4;
                            break;
                        }
                    case OpCode.FDIV: {
                            int d = code[pc + 1],
                                s1 = code[pc + 2],
                                s2 = code[pc + 3];
                            if (R[s2].FloatValue == 0.0)
                                ThrowDivZero(pc);
                            R[d] = new BcValue(R[s1].FloatValue / R[s2].FloatValue);
                            pc += 4;
                            break;
                        }
                    case OpCode.INEG: {
                            int d = code[pc + 1],
                                s = code[pc + 2];
                            R[d] = new BcValue(-R[s].IntValue);
                            pc += 3;
                            break;
                        }
                    case OpCode.FNEG: {
                            int d = code[pc + 1],
                                s = code[pc + 2];
                            R[d] = new BcValue(-R[s].FloatValue);
                            pc += 3;
                            break;
                        }
                    case OpCode.IINC: {
                            int s = code[pc + 1];
                            if (R[s].Tag != ValueTag.Int64)
                                throw RuntimeError(RuntimeErrorType.TypeError, pc, "IINC: not Int64");
                            R.IncInt(s);
                            pc += 2;
                            break;
                        }
                    case OpCode.IDEC: {
                            int s = code[pc + 1];
                            if (R[s].Tag != ValueTag.Int64)
                                throw RuntimeError(RuntimeErrorType.TypeError, pc, "IDEC: not Int64");
                            R.DecInt(s);
                            pc += 2;
                            break;
                        }
    
                    // ── 位运算 ──
                    case OpCode.AND: {
                            int d = code[pc + 1],
                                s1 = code[pc + 2],
                                s2 = code[pc + 3];
                            R[d] = new BcValue(R[s1].IntValue & R[s2].IntValue);
                            pc += 4;
                            break;
                        }
                    case OpCode.OR: {
                            int d = code[pc + 1],
                                s1 = code[pc + 2],
                                s2 = code[pc + 3];
                            R[d] = new BcValue(R[s1].IntValue | R[s2].IntValue);
                            pc += 4;
                            break;
                        }
                    case OpCode.XOR: {
                            int d = code[pc + 1],
                                s1 = code[pc + 2],
                                s2 = code[pc + 3];
                            R[d] = new BcValue(R[s1].IntValue ^ R[s2].IntValue);
                            pc += 4;
                            break;
                        }
                    case OpCode.SHL: {
                            int d = code[pc + 1],
                                s1 = code[pc + 2],
                                s2 = code[pc + 3];
                            R[d] = new BcValue(R[s1].IntValue << (int)(R[s2].IntValue & 0x3F));
                            pc += 4;
                            break;
                        }
                    case OpCode.SHR: {
                            int d = code[pc + 1],
                                s1 = code[pc + 2],
                                s2 = code[pc + 3];
                            R[d] = new BcValue(
                                (long)((ulong)R[s1].IntValue >> (int)(R[s2].IntValue & 0x3F))
                            );
                            pc += 4;
                            break;
                        }
                    case OpCode.SAR: {
                            int d = code[pc + 1],
                                s1 = code[pc + 2],
                                s2 = code[pc + 3];
                            R[d] = new BcValue(R[s1].IntValue >> (int)(R[s2].IntValue & 0x3F));
                            pc += 4;
                            break;
                        }
                    case OpCode.NOT: {
                            int d = code[pc + 1],
                                s = code[pc + 2];
                            R[d] = new BcValue(~R[s].IntValue);
                            pc += 3;
                            break;
                        }
                    case OpCode.ROL: {
                            int d = code[pc + 1],
                                s1 = code[pc + 2],
                                s2 = code[pc + 3];
                            int sh = (int)(R[s2].IntValue & 0x3F);
                            R[d] = new BcValue(
                                (long)((ulong)R[s1].IntValue << sh | (ulong)R[s1].IntValue >> (64 - sh))
                            );
                            pc += 4;
                            break;
                        }
                    case OpCode.ROR: {
                            int d = code[pc + 1],
                                s1 = code[pc + 2],
                                s2 = code[pc + 3];
                            int sh = (int)(R[s2].IntValue & 0x3F);
                            R[d] = new BcValue(
                                (long)((ulong)R[s1].IntValue >> sh | (ulong)R[s1].IntValue << (64 - sh))
                            );
                            pc += 4;
                            break;
                        }
                    case OpCode.ANDI: {
                            int d = code[pc + 1],
                                s = code[pc + 2];
                            R[d] = new BcValue(R[s].IntValue & (long)code[pc + 3]);
                            pc += 4;
                            break;
                        }
                    case OpCode.ORI: {
                            int d = code[pc + 1],
                                s = code[pc + 2];
                            R[d] = new BcValue(R[s].IntValue | (long)code[pc + 3]);
                            pc += 4;
                            break;
                        }
                    case OpCode.XORI: {
                            int d = code[pc + 1],
                                s = code[pc + 2];
                            R[d] = new BcValue(R[s].IntValue ^ (long)code[pc + 3]);
                            pc += 4;
                            break;
                        }
                    case OpCode.SHLI: {
                            int d = code[pc + 1],
                                s = code[pc + 2];
                            R[d] = new BcValue(R[s].IntValue << (code[pc + 3] & 0x3F));
                            pc += 4;
                            break;
                        }
                    case OpCode.SHRI: {
                            int d = code[pc + 1],
                                s = code[pc + 2];
                            R[d] = new BcValue((long)((ulong)R[s].IntValue >> (code[pc + 3] & 0x3F)));
                            pc += 4;
                            break;
                        }
                    case OpCode.CLZ: {
                            int d = code[pc + 1],
                                s = code[pc + 2];
                            R[d] = new BcValue((long)BitOperations.LeadingZeroCount((ulong)R[s].IntValue));
                            pc += 3;
                            break;
                        }
    
                    // ── 比较 ──
                    case OpCode.CMP: {
                            int d = code[pc + 1],
                                s1 = code[pc + 2],
                                s2 = code[pc + 3];
                            R[d] = new BcValue((long)BcValue.Compare(R[s1], R[s2]));
                            pc += 4;
                            break;
                        }
                    case OpCode.IEQ: {
                            int d = code[pc + 1],
                                s1 = code[pc + 2],
                                s2 = code[pc + 3];
                            R[d] = new BcValue(R[s1].IntValue == R[s2].IntValue ? 1L : 0L);
                            pc += 4;
                            break;
                        }
                    case OpCode.INE: {
                            int d = code[pc + 1],
                                s1 = code[pc + 2],
                                s2 = code[pc + 3];
                            R[d] = new BcValue(R[s1].IntValue != R[s2].IntValue ? 1L : 0L);
                            pc += 4;
                            break;
                        }
                    case OpCode.FEQ: {
                            int d = code[pc + 1],
                                s1 = code[pc + 2],
                                s2 = code[pc + 3];
                            R[d] = new BcValue(R[s1].FloatValue == R[s2].FloatValue ? 1L : 0L);
                            pc += 4;
                            break;
                        }
                    case OpCode.TYPE:
                        R[code[pc + 1]] = new BcValue((long)R[code[pc + 2]].Tag);
                        pc += 3;
                        break;
                    case OpCode.ISTYPE:
                        R[code[pc + 1]] = new BcValue(
                            R[code[pc + 2]].Tag == (ValueTag)code[pc + 3] ? 1L : 0L
                        );
                        pc += 4;
                        break;
    
                    // ── 控制流 ──
                    case OpCode.RET:
                        pc = (uint)R[3].IntValue;
                        break;
                    case OpCode.JMPR:
                        pc = (uint)R[code[pc + 1]].IntValue;
                        break;
                    case OpCode.CALLR: {
                            int s = code[pc + 1];
                            R[3] = new BcValue((long)(pc + 2));
                            pc = (uint)R[s].IntValue;
                            break;
                        }
                    case OpCode.JMP:
                        pc = BitConverter.ToUInt32(code, (int)pc + 1);
                        break;
                    case OpCode.CALL:
                        R[3] = new BcValue((long)(pc + 5));
                        pc = BitConverter.ToUInt32(code, (int)pc + 1);
                        break;
                    case OpCode.BZ:
                        if (R[code[pc + 1]].IsFalsy)
                            pc = (uint)((int)(pc + 3) + (sbyte)code[pc + 2]);
                        else
                            pc += 3;
                        break;
                    case OpCode.BNZ:
                        if (R[code[pc + 1]].IsTruthy)
                            pc = (uint)((int)(pc + 3) + (sbyte)code[pc + 2]);
                        else
                            pc += 3;
                        break;
                    case OpCode.BEQ:
                    case OpCode.BNE: {
                            bool eq = BcValue.Eq(R[code[pc + 1]], R[code[pc + 2]]);
                            bool take = op == OpCode.BEQ ? eq : !eq;
                            pc = take ? (uint)((int)(pc + 4) + (sbyte)code[pc + 3]) : pc + 4;
                            break;
                        }
                    case OpCode.BLT:
                    case OpCode.BGE:
                    case OpCode.BGT:
                    case OpCode.BLE: {
                            int cmp = BcValue.Compare(R[code[pc + 1]], R[code[pc + 2]]);
                            bool take = op switch
                            {
                                OpCode.BLT => cmp < 0,
                                OpCode.BGE => cmp >= 0,
                                OpCode.BGT => cmp > 0,
                                _ => cmp <= 0,
                            };
                            pc = take ? (uint)((int)(pc + 4) + (sbyte)code[pc + 3]) : pc + 4;
                            break;
                        }
    
                    // ── 结构体 ──
                    case OpCode.SNEW: {
                            int d = code[pc + 1];
                            byte typeId = code[pc + 2];
                            ushort count = (ushort)(code[pc + 3] | (code[pc + 4] << 8));
                            int slot = heap.Alloc(typeId);
                            R[d] = BcValue.MakeStruct(typeId, slot);
                            pc += 5;
                            break;
                        }
                    case OpCode.SGET: {
                            int d = code[pc + 1],
                                obj = code[pc + 2];
                            byte field = code[pc + 3];
                            if (R[obj].Tag != ValueTag.Struct)
                                throw RuntimeError(RuntimeErrorType.TypeError, pc, "SGET: not Struct");
                            R[d] = heap.GetField((int)R[obj].IntValue, field);
                            pc += 4;
                            break;
                        }
                    case OpCode.SSET: {
                            int src = code[pc + 1],
                                obj = code[pc + 2];
                            byte field = code[pc + 3];
                            if (R[obj].Tag != ValueTag.Struct)
                                throw RuntimeError(RuntimeErrorType.TypeError, pc, "SSET: not Struct");
                            heap.SetField((int)R[obj].IntValue, field, R[src]);
                            pc += 4;
                            break;
                        }
    
                    // ── 数组边界 ──
                    case OpCode.NULLCK: {
                            // 引用 = 槽号，槽 0 也是合法槽，所以不能按数值判 0；
                            // 未绑定 / 失效的引用是 Void（零初始化），合法指针是 Int64。
                            if (R[code[pc + 1]].Tag == ValueTag.Void)
                                throw RuntimeError(
                                    RuntimeErrorType.NullReference,
                                    pc,
                                    "空引用解引用：REFERENCE 未绑定或为 NULL"
                                );
                            pc += 2;
                            break;
                        }
                    case OpCode.BOUNDS: {
                            int idx = code[pc + 1],
                                max = code[pc + 2];
                            long i = R[idx].IntValue;
                            long n = R[max].IntValue;
                            if (i < 0 || i >= n)
                                throw RuntimeError(
                                    RuntimeErrorType.ArrayBounds,
                                    pc,
                                    $"BOUNDS: index {i} out of range [0, {n})");
                            pc += 3;
                            break;
                        }
    
                    // ── 类型转换 ──
                    case OpCode.TOINT: {
                            int d = code[pc + 1],
                                s = code[pc + 2];
                            R[d] = new BcValue(R[s].AsInt64());
                            pc += 3;
                            break;
                        }
                    case OpCode.TOFLT: {
                            int d = code[pc + 1],
                                s = code[pc + 2];
                            R[d] = new BcValue(R[s].AsFloat64());
                            pc += 3;
                            break;
                        }
                    case OpCode.TOSTR: {
                            int d = code[pc + 1],
                                s = code[pc + 2];
                            R[d] = new BcValue(R[s].ToString() ?? "");
                            pc += 3;
                            break;
                        }
                    case OpCode.TOBOOL: {
                            int d = code[pc + 1],
                                s = code[pc + 2];
                            R[d] = new BcValue(R[s].IsTruthy ? 1L : 0L);
                            pc += 3;
                            break;
                        }
    
                    // ── 宿主互操作 ──
                    case OpCode.HCALL: {
                            ushort id = (ushort)(code[pc + 1] | (code[pc + 2] << 8));
                            if (!host.TryInvoke(id, Context, out var ret))
                                throw RuntimeError(
                                    RuntimeErrorType.IllegalOpcode,
                                    pc,
                                    $"HCALL {id}: unregistered"
                                );
                            R[4] = ret;
                            pc += 3;
                            break;
                        }
    
                    // ── VM 互操作 ──
                    case OpCode.VMCALL: {
                            _pc = pc + 2;
                            _state = VMState.Paused;
                            return;
                        }
                    case OpCode.VMRET: {
                            _pc = pc + 1;
                            _state = VMState.Paused;
                            return;
                        }
    
                    // ── 未定义 ──
                    default:
                        throw RuntimeError(
                            RuntimeErrorType.IllegalOpcode,
                            pc,
                            $"Undefined opcode 0x{(byte)op:X2} at 0x{pc:X}"
                        );
                }
            }
        } catch (RuntimeErrorException ex) {
            // 唯一还持有"当前指令 PC"的边界：pc 是这里的局部变量；
            // _pc 字段在指令执行途中是陈旧的（只在暂停/停机同步），不能当"当前 PC"用。
            // 组件（StructHeap / BcValue）不持有 VM，抛的异常 PC 是 0：在这里补齐。
            ex.AdoptPC(pc);
            _pc = pc;
            throw;
        } catch (IndexOutOfRangeException) {
            // 交给 Run() 的 IllegalOpcode 兜底；先对齐 _pc 让它拿到真实 PC。
            _pc = pc;
            throw;
        }

        _pc = pc;
    }
}
