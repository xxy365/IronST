// ═══════════════════════════════════════════════════════════════
//  Disassembler.cs — 字节码反汇编器 (适配新版 Shared ISA)
// ═══════════════════════════════════════════════════════════════
using IronST.Runtime.Shared;

namespace IronST.Runtime.Shared;

public static class Disassembler
{
    public static string Disassemble(ReadOnlySpan<byte> code, uint pc)
    {
        if (pc >= (uint)code.Length)
            return "<eof>";

        OpCode op = (OpCode)code[(int)pc];

        if (!VMBase.InstrLength.TryGetValue(op, out var len))
            return $"??? 0x{(byte)op:X2}";

        int rem = code.Length - (int)pc;
        int i = (int)pc;

        return op switch
        {
            // ── 1 字节 [op] ────────────────────────────────────
            OpCode.NOP => "NOP",
            OpCode.HALT => "HALT",
            OpCode.BRK => "BRK",
            OpCode.LEAVE => "LEAVE",
            OpCode.RET => "RET",
            OpCode.VMRET => "VMRET",

            // ── 2 字节 [op][byte] ──────────────────────────────
            OpCode.SYSCALL => Safe(2, rem, $"SYSCALL 0x{code[i + 1]:X2}"),
            OpCode.LNIL => Safe(2, rem, $"LNIL R{code[i + 1]}"),
            OpCode.LTRUE => Safe(2, rem, $"LTRUE R{code[i + 1]}"),
            OpCode.LFALSE => Safe(2, rem, $"LFALSE R{code[i + 1]}"),
            OpCode.PUSH => Safe(2, rem, $"PUSH R{code[i + 1]}"),
            OpCode.POP => Safe(2, rem, $"POP R{code[i + 1]}"),
            OpCode.JMPR => Safe(2, rem, $"JMPR R{code[i + 1]}"),
            OpCode.CALLR => Safe(2, rem, $"CALLR R{code[i + 1]}"),
            OpCode.IINC => Safe(2, rem, $"IINC R{code[i + 1]}"),
            OpCode.IDEC => Safe(2, rem, $"IDEC R{code[i + 1]}"),
            OpCode.VMCALL => Safe(2, rem, $"VMCALL {code[i + 1]}"),

            // ── 3 字节 [op][rd][rs] — 双寄存器 ─────────────────
            OpCode.MOV => Safe(3, rem, $"MOV R{code[i + 1]}, R{code[i + 2]}"),
            OpCode.SWAP => Safe(3, rem, $"SWAP R{code[i + 1]}, R{code[i + 2]}"),
            OpCode.INEG => Safe(3, rem, $"INEG R{code[i + 1]}, R{code[i + 2]}"),
            OpCode.FNEG => Safe(3, rem, $"FNEG R{code[i + 1]}, R{code[i + 2]}"),
            OpCode.NOT => Safe(3, rem, $"NOT R{code[i + 1]}, R{code[i + 2]}"),
            OpCode.CLZ => Safe(3, rem, $"CLZ R{code[i + 1]}, R{code[i + 2]}"),
            OpCode.TYPE => Safe(3, rem, $"TYPE R{code[i + 1]}, R{code[i + 2]}"),
            OpCode.GETR => Safe(3, rem, $"GETR R{code[i + 1]}, R{code[i + 2]}"),
            OpCode.SETR => Safe(3, rem, $"SETR R{code[i + 1]}, R{code[i + 2]}"),
            OpCode.BOUNDS => Safe(3, rem, $"BOUNDS R{code[i + 1]}, R{code[i + 2]}"),
            OpCode.NULLCK => Safe(2, rem, $"NULLCK R{code[i + 1]}"),
            OpCode.GETGI => Safe(3, rem, $"GETGI R{code[i + 1]}, R{code[i + 2]}"),
            OpCode.SETGI => Safe(3, rem, $"SETGI R{code[i + 1]}, R{code[i + 2]}"),
            OpCode.TOINT => Safe(3, rem, $"TOINT R{code[i + 1]}, R{code[i + 2]}"),
            OpCode.TOFLT => Safe(3, rem, $"TOFLT R{code[i + 1]}, R{code[i + 2]}"),
            OpCode.TOSTR => Safe(3, rem, $"TOSTR R{code[i + 1]}, R{code[i + 2]}"),
            OpCode.TOBOOL => Safe(3, rem, $"TOBOOL R{code[i + 1]}, R{code[i + 2]}"),

            // ── 3 字节 [op][lo][hi] — 16 位操作数 ──────────────
            OpCode.PUSHN => Safe(3, rem, $"PUSHN {(short)(code[i + 1] | (code[i + 2] << 8))}"),
            OpCode.POPN => Safe(3, rem, $"POPN {(short)(code[i + 1] | (code[i + 2] << 8))}"),
            OpCode.FRAME => Safe(3, rem, $"FRAME {(short)(code[i + 1] | (code[i + 2] << 8))}"),
            OpCode.HCALL => Safe(3, rem, $"HCALL 0x{(ushort)(code[i + 1] | (code[i + 2] << 8)):X4}"),

            // ── 3 字节 [op][rs][off8] — 单条件分支 ─────────────
            OpCode.BZ => Safe(3, rem, $"BZ R{code[i + 1]}, {(sbyte)code[i + 2]:+#;-#;0}"),
            OpCode.BNZ => Safe(3, rem, $"BNZ R{code[i + 1]}, {(sbyte)code[i + 2]:+#;-#;0}"),

            // ── 4 字节 [op][rd][s1][s2] — 整数算术 ─────────────
            OpCode.IADD => Safe(4, rem, $"IADD R{code[i + 1]}, R{code[i + 2]}, R{code[i + 3]}"),
            OpCode.ISUB => Safe(4, rem, $"ISUB R{code[i + 1]}, R{code[i + 2]}, R{code[i + 3]}"),
            OpCode.IMUL => Safe(4, rem, $"IMUL R{code[i + 1]}, R{code[i + 2]}, R{code[i + 3]}"),
            OpCode.IDIV => Safe(4, rem, $"IDIV R{code[i + 1]}, R{code[i + 2]}, R{code[i + 3]}"),

            // ── 4 字节 [op][rd][s1][s2] — 浮点算术 ─────────────
            OpCode.FADD => Safe(4, rem, $"FADD R{code[i + 1]}, R{code[i + 2]}, R{code[i + 3]}"),
            OpCode.FSUB => Safe(4, rem, $"FSUB R{code[i + 1]}, R{code[i + 2]}, R{code[i + 3]}"),
            OpCode.FMUL => Safe(4, rem, $"FMUL R{code[i + 1]}, R{code[i + 2]}, R{code[i + 3]}"),
            OpCode.FDIV => Safe(4, rem, $"FDIV R{code[i + 1]}, R{code[i + 2]}, R{code[i + 3]}"),

            // ── 4 字节 [op][rd][s1][s2] — 位运算 ───────────────
            OpCode.AND => Safe(4, rem, $"AND R{code[i + 1]}, R{code[i + 2]}, R{code[i + 3]}"),
            OpCode.OR => Safe(4, rem, $"OR R{code[i + 1]}, R{code[i + 2]}, R{code[i + 3]}"),
            OpCode.XOR => Safe(4, rem, $"XOR R{code[i + 1]}, R{code[i + 2]}, R{code[i + 3]}"),
            OpCode.SHL => Safe(4, rem, $"SHL R{code[i + 1]}, R{code[i + 2]}, R{code[i + 3]}"),
            OpCode.SHR => Safe(4, rem, $"SHR R{code[i + 1]}, R{code[i + 2]}, R{code[i + 3]}"),
            OpCode.SAR => Safe(4, rem, $"SAR R{code[i + 1]}, R{code[i + 2]}, R{code[i + 3]}"),
            OpCode.ROL => Safe(4, rem, $"ROL R{code[i + 1]}, R{code[i + 2]}, R{code[i + 3]}"),
            OpCode.ROR => Safe(4, rem, $"ROR R{code[i + 1]}, R{code[i + 2]}, R{code[i + 3]}"),

            // ── 4 字节 [op][rd][rs1][rs2] — 比较 ───────────────
            OpCode.CMP => Safe(4, rem, $"CMP R{code[i + 1]}, R{code[i + 2]}, R{code[i + 3]}"),
            OpCode.IEQ => Safe(4, rem, $"IEQ R{code[i + 1]}, R{code[i + 2]}, R{code[i + 3]}"),
            OpCode.INE => Safe(4, rem, $"INE R{code[i + 1]}, R{code[i + 2]}, R{code[i + 3]}"),
            OpCode.FEQ => Safe(4, rem, $"FEQ R{code[i + 1]}, R{code[i + 2]}, R{code[i + 3]}"),

            // ── 4 字节 [op][rd][rs][imm8] — 立即数位运算 ───────
            OpCode.ANDI => Safe(4, rem, $"ANDI R{code[i + 1]}, R{code[i + 2]}, {code[i + 3]}"),
            OpCode.ORI => Safe(4, rem, $"ORI R{code[i + 1]}, R{code[i + 2]}, {code[i + 3]}"),
            OpCode.XORI => Safe(4, rem, $"XORI R{code[i + 1]}, R{code[i + 2]}, {code[i + 3]}"),
            OpCode.SHLI => Safe(4, rem, $"SHLI R{code[i + 1]}, R{code[i + 2]}, {code[i + 3]}"),
            OpCode.SHRI => Safe(4, rem, $"SHRI R{code[i + 1]}, R{code[i + 2]}, {code[i + 3]}"),

            // ── 4 字节 [op][rd][rs][tag8] — 类型检查 ───────────
            OpCode.ISTYPE => Safe(4, rem, $"ISTYPE R{code[i + 1]}, R{code[i + 2]}, {(ValueTag)code[i + 3]}"),

            // ── 4 字节 结构体 SGET/SSET ────────────────────────
            OpCode.SGET => Safe(4, rem, $"SGET R{code[i + 1]}, R{code[i + 2]}, f={code[i + 3]}"),
            OpCode.SSET => Safe(4, rem, $"SSET R{code[i + 1]}, R{code[i + 2]}, f={code[i + 3]}"),

            // ── 5 字节 结构体 SNEW [op][rd][typeId][count:2 LE] ─
            OpCode.SNEW => Safe(5, rem,
                $"SNEW R{code[i + 1]}, type={code[i + 2]}, {(ushort)(code[i + 3] | (code[i + 4] << 8))}"),

            // ── 4 字节 [op][r1][r2][off8] — 双条件分支 ─────────
            OpCode.BEQ => Safe(4, rem, $"BEQ R{code[i + 1]}, R{code[i + 2]}, {(sbyte)code[i + 3]:+#;-#;0}"),
            OpCode.BNE => Safe(4, rem, $"BNE R{code[i + 1]}, R{code[i + 2]}, {(sbyte)code[i + 3]:+#;-#;0}"),
            OpCode.BLT => Safe(4, rem, $"BLT R{code[i + 1]}, R{code[i + 2]}, {(sbyte)code[i + 3]:+#;-#;0}"),
            OpCode.BGE => Safe(4, rem, $"BGE R{code[i + 1]}, R{code[i + 2]}, {(sbyte)code[i + 3]:+#;-#;0}"),
            OpCode.BGT => Safe(4, rem, $"BGT R{code[i + 1]}, R{code[i + 2]}, {(sbyte)code[i + 3]:+#;-#;0}"),
            OpCode.BLE => Safe(4, rem, $"BLE R{code[i + 1]}, R{code[i + 2]}, {(sbyte)code[i + 3]:+#;-#;0}"),

            // ── 4 字节 [op][rd][s16] — 变量访问 ────────────────
            OpCode.GETL => Safe(4, rem, $"GETL R{code[i + 1]}, {(short)(code[i + 2] | (code[i + 3] << 8))}"),
            OpCode.SETL => Safe(4, rem, $"SETL R{code[i + 1]}, {(short)(code[i + 2] | (code[i + 3] << 8))}"),
            OpCode.GETG => Safe(4, rem, $"GETG R{code[i + 1]}, {(short)(code[i + 2] | (code[i + 3] << 8))}"),
            OpCode.SETG => Safe(4, rem, $"SETG R{code[i + 1]}, {(short)(code[i + 2] | (code[i + 3] << 8))}"),

            // ── 5 字节 [op][addr32] — 绝对跳转/调用 ────────────
            OpCode.JMP => Safe(5, rem, $"JMP 0x{BitConverter.ToUInt32(code[(i + 1)..]):X}"),
            OpCode.CALL => Safe(5, rem, $"CALL 0x{BitConverter.ToUInt32(code[(i + 1)..]):X}"),

            // ── 6 字节 [op][rd][idx32] — 统一常量池加载 ────────
            OpCode.LDC => Safe(6, rem, $"LDC R{code[i + 1]}, [{BitConverter.ToInt32(code[(i + 2)..])}]"),

            // ── fallback ───────────────────────────────────────
            _ => $"??? 0x{(byte)op:X2} [{len}B]",
        };
    }

    private static string Safe(int need, int rem, string s) => rem >= need ? s : "<trunc>";
}