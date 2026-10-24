// ═══════════════════════════════════════════════════════════════
//  BytecodeAssemblerTests.cs — BytecodeBuilder/BytecodeAssembler 单元测试
//
//  背景：模块 A 清理时删掉了针对旧 ISA 的 BytecodeAssemblerTests。
//  本文件按当前 ISA 重写直接单测：
//    • 逐条指令的精确字节编码（操作码 / 操作数顺序 / 小端）
//    • 全 81 条 OpCode 的“发射长度 == 指令长度表” + “可反汇编” round-trip
//    • 汇编器层语义：标签前向回填、DefFunc 的 FRAME…LEAVE RET 外壳、
//      未定义符号与越界分支的 fail-closed 行为
//  只测直接单元，不依赖端到端 ST→IR→字节码链路。
// ═══════════════════════════════════════════════════════════════
using IronST.Runtime.Shared;
using Xunit;

using OpCode = IronST.Runtime.Shared.OpCode;

namespace IronST.BytecodeGen.Tests;

public class BytecodeAssemblerTests
{
    // ══════════════════════════════════════════
    //  辅助
    // ══════════════════════════════════════════

    private static byte[] Emit(Action<BytecodeBuilder> emit)
    {
        var b = new BytecodeBuilder();
        emit(b);
        return b.GetRawCode();
    }

    private static void AssertBytes(byte[] expected, Action<BytecodeBuilder> emit)
        => Assert.Equal(expected, Emit(emit));

    /// <summary>每条 OpCode 的一条代表性发射配方（覆盖全部 81 条）。</summary>
    private static readonly Dictionary<OpCode, Action<BytecodeBuilder>> EmitTable = new()
    {
        [OpCode.NOP] = b => b.NOP(),
        [OpCode.HALT] = b => b.HALT(),
        [OpCode.BRK] = b => b.BRK(),
        [OpCode.SYSCALL] = b => b.SYSCALL(0x10),

        [OpCode.LNIL] = b => b.LNIL(8),
        [OpCode.LTRUE] = b => b.LTRUE(8),
        [OpCode.LFALSE] = b => b.LFALSE(8),
        [OpCode.LDC] = b => b.LDC(8, 0),
        [OpCode.MOV] = b => b.MOV(1, 2),
        [OpCode.SWAP] = b => b.SWAP(1, 2),

        [OpCode.PUSH] = b => b.PUSH(8),
        [OpCode.POP] = b => b.POP(8),
        [OpCode.PUSHN] = b => b.PUSHN(2),
        [OpCode.POPN] = b => b.POPN(2),
        [OpCode.FRAME] = b => b.FRAME(2),
        [OpCode.LEAVE] = b => b.LEAVE(),

        [OpCode.GETL] = b => b.GETL(8, 0),
        [OpCode.SETL] = b => b.SETL(8, 0),
        [OpCode.GETG] = b => b.GETG(8, 0),
        [OpCode.SETG] = b => b.SETG(8, 0),
        [OpCode.GETR] = b => b.GETR(1, 2),
        [OpCode.SETR] = b => b.SETR(1, 2),
        [OpCode.GETGI] = b => b.GETGI(1, 2),
        [OpCode.SETGI] = b => b.SETGI(1, 2),

        [OpCode.IADD] = b => b.IADD(1, 2, 3),
        [OpCode.ISUB] = b => b.ISUB(1, 2, 3),
        [OpCode.IMUL] = b => b.IMUL(1, 2, 3),
        [OpCode.IDIV] = b => b.IDIV(1, 2, 3),
        [OpCode.FADD] = b => b.FADD(1, 2, 3),
        [OpCode.FSUB] = b => b.FSUB(1, 2, 3),
        [OpCode.FMUL] = b => b.FMUL(1, 2, 3),
        [OpCode.FDIV] = b => b.FDIV(1, 2, 3),
        [OpCode.INEG] = b => b.INEG(1, 2),
        [OpCode.FNEG] = b => b.FNEG(1, 2),
        [OpCode.IINC] = b => b.IINC(8),
        [OpCode.IDEC] = b => b.IDEC(8),

        [OpCode.AND] = b => b.AND(1, 2, 3),
        [OpCode.OR] = b => b.OR(1, 2, 3),
        [OpCode.XOR] = b => b.XOR(1, 2, 3),
        [OpCode.SHL] = b => b.SHL(1, 2, 3),
        [OpCode.SHR] = b => b.SHR(1, 2, 3),
        [OpCode.SAR] = b => b.SAR(1, 2, 3),
        [OpCode.NOT] = b => b.NOT(1, 2),
        [OpCode.ROL] = b => b.ROL(1, 2, 3),
        [OpCode.ROR] = b => b.ROR(1, 2, 3),
        [OpCode.ANDI] = b => b.ANDI(1, 2, 3),
        [OpCode.ORI] = b => b.ORI(1, 2, 3),
        [OpCode.XORI] = b => b.XORI(1, 2, 3),
        [OpCode.SHLI] = b => b.SHLI(1, 2, 3),
        [OpCode.SHRI] = b => b.SHRI(1, 2, 3),
        [OpCode.CLZ] = b => b.CLZ(1, 2),

        [OpCode.CMP] = b => b.CMP(1, 2, 3),
        [OpCode.IEQ] = b => b.IEQ(1, 2, 3),
        [OpCode.INE] = b => b.INE(1, 2, 3),
        [OpCode.FEQ] = b => b.FEQ(1, 2, 3),
        [OpCode.TYPE] = b => b.TYPE(1, 2),
        [OpCode.ISTYPE] = b => b.ISTYPE(1, 2, ValueTag.Int64),

        [OpCode.RET] = b => b.RET(),
        [OpCode.JMPR] = b => b.JMPR(1),
        [OpCode.CALLR] = b => b.CALLR(1),
        [OpCode.JMP] = b => b.JMP(0),
        [OpCode.CALL] = b => b.CALL(0),
        [OpCode.BZ] = b => b.BZ(1, 0),
        [OpCode.BNZ] = b => b.BNZ(1, 0),
        [OpCode.BEQ] = b => b.BEQ(1, 2, 0),
        [OpCode.BNE] = b => b.BNE(1, 2, 0),
        [OpCode.BLT] = b => b.BLT(1, 2, 0),
        [OpCode.BGE] = b => b.BGE(1, 2, 0),
        [OpCode.BGT] = b => b.BGT(1, 2, 0),
        [OpCode.BLE] = b => b.BLE(1, 2, 0),

        [OpCode.SNEW] = b => b.SNEW(8, 1, 2),
        [OpCode.SGET] = b => b.SGET(1, 2, 3),
        [OpCode.SSET] = b => b.SSET(1, 2, 3),
        [OpCode.BOUNDS] = b => b.BOUNDS(1, 2),
        [OpCode.NULLCK] = b => b.NULLCK(3),

        [OpCode.TOINT] = b => b.TOINT(1, 2),
        [OpCode.TOFLT] = b => b.TOFLT(1, 2),
        [OpCode.TOSTR] = b => b.TOSTR(1, 2),
        [OpCode.TOBOOL] = b => b.TOBOOL(1, 2),

        [OpCode.HCALL] = b => b.HCALL(0x0033),
        [OpCode.VMCALL] = b => b.VMCALL(1),
        [OpCode.VMRET] = b => b.VMRET(),
    };

    // ══════════════════════════════════════════
    //  1. 精确字节编码
    // ══════════════════════════════════════════

    [Fact]
    public void Encode_OneByte()
    {
        AssertBytes(new byte[] { 0x00 }, b => b.NOP());
        AssertBytes(new byte[] { 0x01 }, b => b.HALT());
        AssertBytes(new byte[] { 0x25 }, b => b.LEAVE());
        AssertBytes(new byte[] { 0x80 }, b => b.RET());
    }

    [Fact]
    public void Encode_TwoByte_Register()
    {
        AssertBytes(new byte[] { 0x10, 0x08 }, b => b.LNIL(8));
        AssertBytes(new byte[] { 0x5B, 0x08 }, b => b.IINC(8));
    }

    [Fact]
    public void Encode_TwoRegisters()
    {
        AssertBytes(new byte[] { 0x18, 0x01, 0x02 }, b => b.MOV(1, 2));
    }

    [Fact]
    public void Encode_ThreeRegisters()
    {
        AssertBytes(new byte[] { 0x50, 0x01, 0x02, 0x03 }, b => b.IADD(1, 2, 3));
    }

    [Fact]
    public void Encode_Immediate16_LittleEndian()
    {
        // 300 = 0x012C → LE [0x2C, 0x01]
        AssertBytes(new byte[] { 0x24, 0x2C, 0x01 }, b => b.FRAME(300));
        // 2 → [0x02, 0x00]
        AssertBytes(new byte[] { 0x22, 0x02, 0x00 }, b => b.PUSHN(2));
    }

    [Fact]
    public void Encode_Hcall_LittleEndian()
    {
        AssertBytes(new byte[] { 0xB0, 0x33, 0x00 }, b => b.HCALL(0x0033));
        AssertBytes(new byte[] { 0xB0, 0x01, 0x02 }, b => b.HCALL(0x0201));
    }

    [Fact]
    public void Encode_LocalAccess_Signed16LittleEndian()
    {
        // -3 作为 short = 0xFFFD → LE [0xFD, 0xFF]
        AssertBytes(new byte[] { 0x30, 0x08, 0xFD, 0xFF }, b => b.GETL(8, -3));
    }

    [Fact]
    public void Encode_Branch_Relative8()
    {
        // -2 → (byte)(sbyte)-2 = 0xFE
        AssertBytes(new byte[] { 0x87, 0x08, 0xFE }, b => b.BZ(8, -2));
        AssertBytes(new byte[] { 0x89, 0x01, 0x02, 0xFD }, b => b.BEQ(1, 2, -3));
    }

    [Fact]
    public void Encode_Struct()
    {
        AssertBytes(new byte[] { 0x90, 0x0A, 0x04, 0x02, 0x00 }, b => b.SNEW(10, 4, 2));
        AssertBytes(new byte[] { 0x91, 0x0A, 0x08, 0x02 }, b => b.SGET(10, 8, 2));
        AssertBytes(new byte[] { 0x92, 0x0B, 0x0A, 0x01 }, b => b.SSET(11, 10, 1));
    }

    [Fact]
    public void Encode_Bounds()
    {
        AssertBytes(new byte[] { 0x94, 0x08, 0x09 }, b => b.BOUNDS(8, 9));
    }

    [Fact]
    public void Encode_Ldc_ConstantIndex()
    {
        // LDC R8, idx=5 → [0x14][0x08][LE32 5]
        AssertBytes(new byte[] { 0x14, 0x08, 0x05, 0x00, 0x00, 0x00 }, b => b.LDC(8, 5));
    }

    [Fact]
    public void Encode_JumpCall_Addr32()
    {
        AssertBytes(new byte[] { 0x85, 0x34, 0x12, 0x00, 0x00 }, b => b.JMP(0x1234));
        AssertBytes(new byte[] { 0x86, 0xCD, 0xAB, 0x00, 0x00 }, b => b.CALL(0xABCD));
    }

    [Fact]
    public void Encode_ImmediateBitOps()
    {
        AssertBytes(new byte[] { 0x69, 0x01, 0x02, 0xFF }, b => b.ANDI(1, 2, 0xFF));
    }

    [Fact]
    public void Encode_IsType_Tag()
    {
        // ValueTag.Int64 == 1
        AssertBytes(new byte[] { 0x7F, 0x01, 0x02, 0x01 },
            b => b.ISTYPE(1, 2, ValueTag.Int64));
    }

    // ══════════════════════════════════════════
    //  2. 全 ISA round-trip（长度表 + 反汇编）
    // ══════════════════════════════════════════

    [Fact]
    public void EveryOpcode_HasPositiveLength()
    {
        foreach (var op in Enum.GetValues<OpCode>()) {
            Assert.True(VMBase.GetInstrLength(op) > 0,
                $"{op} 缺少指令长度表条目");
        }
    }

    [Fact]
    public void EveryOpcode_EmitsExactlyTableLength()
    {
        foreach (var op in Enum.GetValues<OpCode>()) {
            Assert.True(EmitTable.ContainsKey(op), $"缺少 {op} 的发射配方");

            int emitted = Emit(EmitTable[op]).Length;
            byte expected = VMBase.GetInstrLength(op);

            Assert.True(emitted == expected,
                $"{op} 发射 {emitted} 字节，长度表却是 {expected}");
        }
    }

    [Fact]
    public void EveryOpcode_DisassemblesWithoutFallback()
    {
        foreach (var op in Enum.GetValues<OpCode>()) {
            byte[] code = Emit(EmitTable[op]);
            string text = Disassembler.Disassemble(code, 0);

            Assert.DoesNotContain("???", text);
        }
    }

    [Fact]
    public void EmittedCode_WalksCleanlyByLengthTable()
    {
        // 全部指令顺序拼在一起后，按长度表走一遍必须恰好到末尾（与 ValidateCode 同口径）。
        var b = new BytecodeBuilder();
        foreach (var op in Enum.GetValues<OpCode>())
            EmitTable[op](b);

        byte[] code = b.GetRawCode();
        int pc = 0;
        while (pc < code.Length) {
            byte len = VMBase.GetInstrLength((OpCode)code[pc]);
            Assert.True(len > 0, $"偏移 {pc} 处出现未知操作码 0x{code[pc]:X2}");
            pc += len;
        }
        Assert.Equal(code.Length, pc);
    }

    // ══════════════════════════════════════════
    //  3. 汇编器语义：标签 / 函数外壳 / fail-closed
    // ══════════════════════════════════════════

    [Fact]
    public void Label_ForwardBranch_PatchesRelativeOffset()
    {
        var asm = new BytecodeAssembler();
        asm.Bz(BytecodeBuilder.A0, "end"); // PC 0，3 字节，偏移字节在 PC 2
        asm.Raw.NOP();
        asm.Raw.NOP();
        asm.Label("end");                  // PC 5
        asm.Raw.RET();

        byte[] code = asm.Build().Code;

        Assert.Equal(6, code.Length);
        Assert.Equal(0x87, code[0]);            // BZ
        // rel = 5 - (2 + 1) = 2
        Assert.Equal(0x02, code[2]);
    }

    [Fact]
    public void DefFunc_EmitsFrameLeaveRetShell()
    {
        var asm = new BytecodeAssembler();
        asm.DefFunc("f", paramCount: 1, extraLocals: 1, () => asm.Raw.NOP());

        byte[] code = asm.Build().Code;

        Assert.Equal(0x24, code[0]);            // FRAME（3 字节）
        Assert.Equal(0x31, code[3]);            // SETL A0, L0（4 字节）
        Assert.Equal(0x00, code[7]);            // body NOP
        Assert.Equal(0x25, code[8]);            // LEAVE
        Assert.Equal(0x80, code[9]);            // RET
        Assert.Equal(10, code.Length);

        var range = Assert.Single(asm.FunctionRanges);
        Assert.Equal("f", range.Name);
        Assert.Equal(0, range.StartPC);
        Assert.Equal(10, range.EndPC);
        Assert.Equal(1, range.ParamCount);
        Assert.Equal(2, range.LocalCount);      // param + extra
    }

    [Fact]
    public void UndefinedLabel_FailsClosed()
    {
        var asm = new BytecodeAssembler();
        asm.Jmp("missing");

        Assert.Throws<InvalidOperationException>(() => asm.Build());
    }

    [Fact]
    public void BranchOffsetOutOfRange_FailsClosed()
    {
        var asm = new BytecodeAssembler();
        asm.Bz(BytecodeBuilder.A0, "far");
        for (int i = 0; i < 200; i++)
            asm.Raw.NOP();
        asm.Label("far");

        Assert.Throws<InvalidOperationException>(() => asm.Build());
    }
}
