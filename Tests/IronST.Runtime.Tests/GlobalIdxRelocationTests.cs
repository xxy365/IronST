// GlobalIdxRelocationTests.cs — 0B 步骤 3 · 切片 3：GlobalIdx 的记录与局部回填
using IronST.Runtime.Shared;

namespace IronST.Runtime.Tests;

/// <summary>
/// GETG/SETG = <c>[op][reg][LE16]</c>，立即数在操作码 +2 处、宽 16 位。
///
/// <para>
/// 符号身份是<b>声明名</b>（跨模块身份），<b>模块局部槽位索引</b>放在 Addend ——
/// 两者刻意分开，不把局部索引伪装成符号名。
/// </para>
/// </summary>
public class GlobalIdxRelocationTests
{
    // ═══════════════════════════════════════════════════════════════
    //  记录
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GetGlobal_RecordsDeclaredName_AndLocalSlot()
    {
        var asm = new BytecodeAssembler();
        asm.DeclareGlobal("counter");   // slot 0
        asm.DeclareGlobal("total");     // slot 1
        asm.GetGlobal(BytecodeAssembler.A0, "total");

        var r = Assert.Single(asm.Relocations.Records);

        Assert.Equal(RelocationKind.GlobalIdx, r.Kind);
        Assert.Equal(RelocationSection.Code, r.Section);
        Assert.Equal("total", r.SymbolName);     // 声明名，不是 "1"
        Assert.Equal(1, r.Addend);               // 局部槽位索引
        Assert.Equal(2, r.Offset);               // GETG 在 PC 0，立即数从 2 开始
        Assert.Equal(16, r.EncodingWidthBits);
    }

    [Fact]
    public void SetGlobal_RecordsDeclaredName_AndLocalSlot()
    {
        var asm = new BytecodeAssembler();
        asm.DeclareGlobal("counter");
        asm.SetGlobal(BytecodeAssembler.A0, "counter");

        var r = Assert.Single(asm.Relocations.Records);

        Assert.Equal(RelocationKind.GlobalIdx, r.Kind);
        Assert.Equal("counter", r.SymbolName);
        Assert.Equal(0, r.Addend);
        Assert.Equal(2, r.Offset);               // SETG 同样是 [op][reg][LE16]
    }

    [Fact]
    public void EachReference_GetsItsOwnRecord()
    {
        var asm = new BytecodeAssembler();
        asm.DeclareGlobal("g");
        asm.GetGlobal(BytecodeAssembler.A0, "g");    // PC 0..2
        asm.SetGlobal(BytecodeAssembler.A1, "g");    // PC 3..5
        asm.GetGlobal(BytecodeAssembler.A2, "g");    // PC 6..8

        Assert.Equal(3, asm.Relocations.Count);
        // GETG/SETG 各 4 字节（[op][reg][LE16]），立即数在操作码 +2
        Assert.Equal(new[] { 2, 6, 10 },
            asm.Relocations.Records.Select(r => r.Offset).ToArray());
    }

    // ═══════════════════════════════════════════════════════════════
    //  回填：单模块内由模块局部映射解析
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Build_WritesLocalSlot_AsLittleEndian16()
    {
        var asm = new BytecodeAssembler();
        asm.DeclareGlobal("a");   // 0
        asm.DeclareGlobal("b");   // 1
        asm.DeclareGlobal("c");   // 2
        asm.GetGlobal(BytecodeAssembler.A0, "c");

        var m = asm.Build();

        Assert.Equal(2, BitConverter.ToInt16(m.Code, 2));
    }

    [Fact]
    public void Slot0_IsLegal()
    {
        var asm = new BytecodeAssembler();
        asm.DeclareGlobal("first");
        asm.GetGlobal(BytecodeAssembler.A0, "first");

        var m = asm.Build();

        Assert.Equal(0, BitConverter.ToInt16(m.Code, 2));
    }

    [Fact]
    public void Slot32766_IsLegal()
    {
        var asm = new BytecodeAssembler();
        for (int i = 0; i < BytecodeAssembler.MaxGlobals; i++)
            asm.DeclareGlobal($"g{i}");

        string last = $"g{RelocationRules.MaxGlobalSlotIndex}";
        asm.SetGlobal(BytecodeAssembler.A0, last);

        var m = asm.Build();

        Assert.Equal(RelocationRules.MaxGlobalSlotIndex, BitConverter.ToInt16(m.Code, 2));
        Assert.Equal(RelocationRules.MaxGlobalSlotIndex, asm.Relocations.Records.Single().Addend);
    }

    [Fact]
    public void MaxGlobals_ComesFromRelocationRules()
    {
        // 单一事实来源：汇编器的上限常量就是重定位规则里的那个
        Assert.Equal(RelocationRules.MaxGlobalSlots, BytecodeAssembler.MaxGlobals);
        Assert.Equal(32767, RelocationRules.MaxGlobalSlots);
        Assert.Equal(32766, RelocationRules.MaxGlobalSlotIndex);
    }

    [Fact]
    public void Build_Twice_ProducesSameBytes()
    {
        var asm = new BytecodeAssembler();
        asm.DeclareGlobal("g");
        asm.GetGlobal(BytecodeAssembler.A0, "g");
        asm.Label("end");
        asm.Raw.RET();

        var first = asm.Build().Code;
        var second = asm.Build().Code;

        Assert.Equal(first, second);
    }

    [Fact]
    public void MixedAddr32AndGlobalIdx_BothBackfill()
    {
        var asm = new BytecodeAssembler();
        asm.DeclareGlobal("g0");                   // slot 0
        asm.DeclareGlobal("g1");                   // slot 1
        asm.Label("top");                          // PC 0
        asm.GetGlobal(BytecodeAssembler.A0, "g1"); // PC 0..3，立即数在 2
        asm.Jmp("end");                            // PC 4..8，操作数在 5
        asm.Label("end");                          // PC 9
        asm.Raw.RET();

        var m = asm.Build();

        Assert.Equal(1, BitConverter.ToInt16(m.Code, 2));      // GlobalIdx
        Assert.Equal(9u, BitConverter.ToUInt32(m.Code, 5));    // Addr32
        Assert.Equal(2, asm.Relocations.Count);                // 两类各一条
    }

    // ═══════════════════════════════════════════════════════════════
    //  边界校验
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Record_NegativeSlot_Rejected()
    {
        var table = new RelocationTable();

        var ex = Assert.Throws<InvalidOperationException>(() => table.Add(
            RelocationRecord.Create(RelocationKind.GlobalIdx, RelocationSection.Code, 2, "g", addend: -1)));

        Assert.Contains("out of range", ex.Message);
        Assert.Contains("0..32766", ex.Message);
        Assert.Equal(0, table.Count);
    }

    [Fact]
    public void Record_SlotAboveMax_Rejected()
    {
        var table = new RelocationTable();

        var ex = Assert.Throws<InvalidOperationException>(() => table.Add(
            RelocationRecord.Create(RelocationKind.GlobalIdx, RelocationSection.Code, 2, "g", addend: 32767)));

        Assert.Contains("out of range", ex.Message);
    }

    [Fact]
    public void ApplyGlobalIdx_UndeclaredName_ThrowsWithReferenceSite()
    {
        var asm = new BytecodeAssembler();
        asm.DeclareGlobal("g");
        asm.GetGlobal(BytecodeAssembler.A0, "g");
        asm.Relocations.Add(RelocationRecord.Create(
            RelocationKind.GlobalIdx, RelocationSection.Code, 8, "ghost", addend: 0));

        var ex = Assert.Throws<InvalidOperationException>(() => asm.Build());

        Assert.Contains("Undefined global variable: 'ghost'", ex.Message);
        Assert.Contains("code offset 8", ex.Message);   // DG-1：引用来源
    }

    [Fact]
    public void ApplyGlobalIdx_StaleSlot_Throws()
    {
        // 记录里的局部槽位必须与模块映射一致，否则记录已过期
        var asm = new BytecodeAssembler();
        asm.DeclareGlobal("g");   // slot 0
        asm.GetGlobal(BytecodeAssembler.A0, "g");
        asm.Relocations.Add(RelocationRecord.Create(
            RelocationKind.GlobalIdx, RelocationSection.Code, 8, "g", addend: 5));

        var ex = Assert.Throws<InvalidOperationException>(() => asm.Build());

        Assert.Contains("mismatch", ex.Message);
        Assert.Contains("slot 5", ex.Message);
        Assert.Contains("resolves to 0", ex.Message);
    }

    [Fact]
    public void UndeclaredName_StillThrowsKeyNotFound()
    {
        // 迁移前的既有行为：裸 KeyNotFoundException（后续步骤再升级为带引用点的诊断）
        var asm = new BytecodeAssembler();

        Assert.Throws<KeyNotFoundException>(() => asm.GetGlobal(BytecodeAssembler.A0, "nope"));
    }
}
