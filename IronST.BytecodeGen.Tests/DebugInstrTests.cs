// ═══════════════════════════════════════════════════════════════
//  DebugInstrTests.cs — DebugInstr 工厂方法 + Format() 单元测试
//
//  背景：模块 A 清理时删掉了针对旧 ISA 的 DebugInstrTests /
//  DebugInstrFormatTests。本文件按当前 ISA（OpCode 全集）重写直接单测，
//  不依赖端到端链路。
// ═══════════════════════════════════════════════════════════════
using IronST.Runtime.Shared;
using Xunit;

using OpCode = IronST.Runtime.Shared.OpCode;

namespace IronST.BytecodeGen.Tests;

public class DebugInstrTests
{
    // ══════════════════════════════════════════
    //  1. 工厂方法设置字段
    // ══════════════════════════════════════════

    [Fact]
    public void Op_SetsOnlyOpcode()
    {
        var i = DebugInstr.Op(OpCode.RET);

        Assert.Equal(OpCode.RET, i.Opcode);
        Assert.Equal(0, i.Dst);
        Assert.Equal(0, i.Src1);
        Assert.Equal(0, i.Src2);
    }

    [Fact]
    public void R1_SetsDst()
    {
        var i = DebugInstr.R1(OpCode.LNIL, 8);

        Assert.Equal(OpCode.LNIL, i.Opcode);
        Assert.Equal(8, i.Dst);
    }

    [Fact]
    public void R2_SetsDstAndSrc1()
    {
        var i = DebugInstr.R2(OpCode.MOV, 1, 2);

        Assert.Equal(1, i.Dst);
        Assert.Equal(2, i.Src1);
    }

    [Fact]
    public void R3_SetsThreeRegisters()
    {
        var i = DebugInstr.R3(OpCode.IADD, 1, 2, 3);

        Assert.Equal(OpCode.IADD, i.Opcode);
        Assert.Equal(1, i.Dst);
        Assert.Equal(2, i.Src1);
        Assert.Equal(3, i.Src2);
    }

    [Fact]
    public void I8_SetsOff8()
    {
        var i = DebugInstr.I8(OpCode.ANDI, 1, 5);

        Assert.Equal(OpCode.ANDI, i.Opcode);
        Assert.Equal(1, i.Dst);
        Assert.Equal(5, i.Off8);
    }

    [Fact]
    public void Slot_SetsImm16()
    {
        var i = DebugInstr.Slot(OpCode.GETL, 10, -3);

        Assert.Equal(OpCode.GETL, i.Opcode);
        Assert.Equal(10, i.Dst);
        Assert.Equal(-3, i.Imm16);
    }

    [Fact]
    public void Frame_SetsImm16()
    {
        var i = DebugInstr.Frame(300);

        Assert.Equal(OpCode.FRAME, i.Opcode);
        Assert.Equal(300, i.Imm16);
    }

    [Fact]
    public void I32_SetsImm32()
    {
        var i = DebugInstr.I32(OpCode.CALL, 0, 123456);

        Assert.Equal(0, i.Dst);
        Assert.Equal(123456, i.Imm32);
    }

    [Fact]
    public void Ldc_SetsFloat()
    {
        var i = DebugInstr.Ldc(8, 3.14);

        Assert.Equal(OpCode.LDC, i.Opcode);
        Assert.Equal(8, i.Dst);
        Assert.Equal(3.14, i.Float);
        Assert.Equal(0, i.Imm32); // 非整数标记
        Assert.Null(i.Str);
    }

    [Fact]
    public void LdcInt_SetsIntValAndMarker()
    {
        var i = DebugInstr.LdcInt(8, 42);

        Assert.Equal(OpCode.LDC, i.Opcode);
        Assert.Equal(8, i.Dst);
        Assert.Equal(42, i.IntVal);
        Assert.Equal(1, i.Imm32); // Imm32==1 表示走整数分支
    }

    [Fact]
    public void LdcStr_SetsStr()
    {
        var i = DebugInstr.LdcStr(9, "hello");

        Assert.Equal(OpCode.LDC, i.Opcode);
        Assert.Equal(9, i.Dst);
        Assert.Equal("hello", i.Str);
        Assert.Equal(0, i.Imm32);
    }

    [Fact]
    public void Jmp_SetsLabel()
    {
        var i = DebugInstr.Jmp("loop");

        Assert.Equal(OpCode.JMP, i.Opcode);
        Assert.Equal("loop", i.Label);
    }

    [Fact]
    public void Bz_Bnz_SetCondAndLabel()
    {
        var bz = DebugInstr.Bz(8, "end");
        var bnz = DebugInstr.Bnz(9, "top");

        Assert.Equal(OpCode.BZ, bz.Opcode);
        Assert.Equal(8, bz.Dst);
        Assert.Equal("end", bz.Label);

        Assert.Equal(OpCode.BNZ, bnz.Opcode);
        Assert.Equal(9, bnz.Dst);
        Assert.Equal("top", bnz.Label);
    }

    [Fact]
    public void Branch_SetsRegsAndLabel()
    {
        var i = DebugInstr.Branch(OpCode.BLT, 1, 2, "less");

        Assert.Equal(OpCode.BLT, i.Opcode);
        Assert.Equal(1, i.Src1);
        Assert.Equal(2, i.Src2);
        Assert.Equal("less", i.Label);
    }

    [Fact]
    public void Call_SetsCallTarget()
    {
        var i = DebugInstr.Call("factorial");

        Assert.Equal(OpCode.CALL, i.Opcode);
        Assert.Equal("factorial", i.CallTarget);
    }

    [Fact]
    public void Hcall_StoresIndexAsImm16()
    {
        var i = DebugInstr.Hcall(0x0102);

        Assert.Equal(OpCode.HCALL, i.Opcode);
        Assert.Equal(unchecked((short)0x0102), i.Imm16);
    }

    [Fact]
    public void Snew_Sget_Sset_SetOperands()
    {
        var n = DebugInstr.Snew(10, 4, 2);
        Assert.Equal(OpCode.SNEW, n.Opcode);
        Assert.Equal(10, n.Dst);
        Assert.Equal(4, n.Src1);
        Assert.Equal(2, unchecked((ushort)n.Imm16));

        var g = DebugInstr.Sget(10, 8, 2);
        Assert.Equal(OpCode.SGET, g.Opcode);
        Assert.Equal(10, g.Dst);
        Assert.Equal(8, g.Src1);
        Assert.Equal(2, g.Off8);

        var s = DebugInstr.Sset(11, 10, 1);
        Assert.Equal(OpCode.SSET, s.Opcode);
        Assert.Equal(11, s.Src1);
        Assert.Equal(10, s.Src2);
        Assert.Equal(1, s.Off8);
    }

    [Fact]
    public void Sget_FieldAboveSbyte_EncodesAsSbyte()
    {
        // 字段号以 sbyte 承载：>=128 时按补码存，Format 再按 byte 还原。
        var g = DebugInstr.Sget(10, 8, 200);

        Assert.Equal(unchecked((sbyte)200), g.Off8);
        Assert.Contains("200", g.Format());
    }

    [Fact]
    public void Pushn_Popn_SetImm16()
    {
        Assert.Equal(2, DebugInstr.Pushn(2).Imm16);
        Assert.Equal(OpCode.POPN, DebugInstr.Popn(3).Opcode);
    }

    // ══════════════════════════════════════════
    //  2. Format() 文本
    // ══════════════════════════════════════════

    [Fact]
    public void Format_OneByte()
    {
        Assert.Equal("NOP", DebugInstr.Op(OpCode.NOP).Format());
        Assert.Equal("HALT", DebugInstr.Op(OpCode.HALT).Format());
        Assert.Equal("RET", DebugInstr.Op(OpCode.RET).Format());
        Assert.Equal("LEAVE", DebugInstr.Op(OpCode.LEAVE).Format());
    }

    [Fact]
    public void Format_TwoByte()
    {
        Assert.Equal("LNIL R8", DebugInstr.R1(OpCode.LNIL, 8).Format());
        Assert.Equal("SYSCALL 0x10", DebugInstr.R1(OpCode.SYSCALL, 0x10).Format());
        Assert.Equal("HCALL 0x0033", DebugInstr.Hcall(0x0033).Format());
    }

    [Fact]
    public void Format_TwoRegisters()
    {
        Assert.Equal("MOV R1, R2", DebugInstr.R2(OpCode.MOV, 1, 2).Format());
        Assert.Equal("TOINT R1, R2", DebugInstr.R2(OpCode.TOINT, 1, 2).Format());
    }

    [Fact]
    public void Format_ThreeRegisters()
    {
        Assert.Equal("IADD R1, R2, R3", DebugInstr.R3(OpCode.IADD, 1, 2, 3).Format());
        Assert.Equal("FADD R4, R5, R6", DebugInstr.R3(OpCode.FADD, 4, 5, 6).Format());
    }

    [Fact]
    public void Format_Frame_And_Immediate16()
    {
        Assert.Equal("FRAME 300", DebugInstr.Frame(300).Format());
        Assert.Equal("PUSHN 2", DebugInstr.Pushn(2).Format());
        Assert.Equal("POPN 3", DebugInstr.Popn(3).Format());
    }

    [Fact]
    public void Format_LocalAccess()
    {
        Assert.Equal("GETL R10, L5", DebugInstr.Slot(OpCode.GETL, 10, 5).Format());
        Assert.Equal("SETL R10, L5", DebugInstr.Slot(OpCode.SETL, 10, 5).Format());
    }

    [Fact]
    public void Format_Ldc_ThreeFlavors()
    {
        Assert.Equal("LDC R8, -7", DebugInstr.LdcInt(8, -7).Format());
        Assert.Equal("LDC R8, 3.14", DebugInstr.Ldc(8, 3.14).Format());
        Assert.Equal("LDC R9, \"hi\"", DebugInstr.LdcStr(9, "hi").Format());
    }

    [Fact]
    public void Format_SymbolicJump()
    {
        Assert.Equal("JMP loop", DebugInstr.Jmp("loop").Format());
        Assert.Equal("CALL factorial", DebugInstr.Call("factorial").Format());
    }

    [Fact]
    public void Format_OffsetJump_WhenNoLabel()
    {
        // 无标签时退化为 8 位相对偏移 / 32 位绝对地址。
        var bz = new DebugInstr { Opcode = OpCode.BZ, Dst = 8, Off8 = -3 };
        Assert.Equal("BZ R8, -3", bz.Format());

        var jmp = new DebugInstr { Opcode = OpCode.JMP, Imm32 = 0x20 };
        Assert.Equal("JMP 0x20", jmp.Format());

        var call = new DebugInstr { Opcode = OpCode.CALL, Imm32 = 0x1F };
        Assert.Equal("CALL 0x1F", call.Format());
    }

    [Fact]
    public void Format_ConditionalBranches()
    {
        Assert.Equal("BEQ R1, R2, done", DebugInstr.Branch(OpCode.BEQ, 1, 2, "done").Format());
        Assert.Equal("BLT R1, R2, less", DebugInstr.Branch(OpCode.BLT, 1, 2, "less").Format());
    }

    [Fact]
    public void Format_StructOps()
    {
        Assert.Equal("SNEW R10, type=4, 2", DebugInstr.Snew(10, 4, 2).Format());
        Assert.Equal("SGET R10, R8, 2", DebugInstr.Sget(10, 8, 2).Format());
        Assert.Equal("SSET R11, R10, 1", DebugInstr.Sset(11, 10, 1).Format());
    }

    [Fact]
    public void Format_IsType()
    {
        var i = new DebugInstr { Opcode = OpCode.ISTYPE, Dst = 1, Src1 = 2, Off8 = 1 };

        Assert.Equal("ISTYPE R1, R2, 1", i.Format());
    }

    // ══════════════════════════════════════════
    //  3. 全 ISA 覆盖：每个 OpCode 都能格式化
    // ══════════════════════════════════════════

    [Fact]
    public void Format_EveryOpcode_IsNonEmptyAndNamed()
    {
        foreach (var op in Enum.GetValues<OpCode>()) {
            string text = new DebugInstr { Opcode = op }.Format();

            Assert.False(string.IsNullOrWhiteSpace(text), $"{op} formatted to empty");
            Assert.StartsWith(op.ToString(), text, StringComparison.Ordinal);
        }
    }
}
