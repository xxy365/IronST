// HostIdxRelocationTests.cs — 0B 步骤 3 · 切片 5：HostIdx 的记录、回填与重映射
//
// HCALL = [0xB0][id_lo][id_hi]：共 3 字节、**无 rd 字节**，立即数在操作码 +1 处、
// 宽 16 位、无符号编码（BytecodeBuilder.cs:213-219 手工逐字节写入，未用 LE16 助手）。
// 对照 GETG/SETG = [op][rd][LE16]，立即数在 +2 —— 两者的 Offset 相差 1 正源于此。
//
// 与 ConstIdx 的关键区别：宿主函数**有**跨模块稳定身份（函数名），故记录带 SymbolName、
// 模块局部 ID 放 Addend；重编号可按名解析。ConstIdx 因常量无身份而禁止符号名。
//
// 三项容量口径分列（docs/阶段0B设计.md §5 切片 5 侦查补正），不得合并：
//   ① 编码能力 0..65535（16 位无符号）；② 消费侧无硬上限（未命中只抛带诊断的 RuntimeError，
//      VMBase.RunLoop.cs:618-628，不崩溃）；③ 分配器策略上限 0..254
//      （HostFunctionRegistry.cs:167-168）。Validate 只按 ① 校验。
using IronST.Runtime.Shared;

namespace IronST.Runtime.Tests;

public class HostIdxRelocationTests
{
    // ═══════════════════════════════════════════════════════════════
    //  记录
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void RecordHostIdx_CarriesStableName_AndModuleLocalId()
    {
        var asm = new BytecodeAssembler();
        asm.RecordHostIdx("Square", 7, RelocationSourceLocation.Unknown);
        asm.Raw.HCALL(7);

        var r = Assert.Single(asm.Relocations.Records);

        Assert.Equal(RelocationKind.HostIdx, r.Kind);
        Assert.Equal(RelocationSection.Code, r.Section);
        Assert.Equal("Square", r.SymbolName);   // 跨模块身份是名字，不是那个 7
        Assert.Equal(7, r.Addend);              // 模块局部宿主索引
        Assert.Equal(16, r.EncodingWidthBits);
    }

    [Fact]
    public void ImmediateOffset_IsAtOpcodePlus1_NotPlus2()
    {
        // HCALL 无 rd 字节：立即数紧随操作码。GlobalIdx/GETG 因有 rd 才是 +2。
        var asm = new BytecodeAssembler();
        asm.Raw.RET();                          // PC 0
        asm.RecordHostIdx("Foo", 3, RelocationSourceLocation.Unknown);  // 记录时 PC=1 → Offset=2
        asm.Raw.HCALL(3);                       // PC 1..3

        var r = Assert.Single(asm.Relocations.Records);
        Assert.Equal(2, r.Offset);

        var m = asm.Build();
        Assert.Equal(0xB0, m.Code[1]);          // 操作码
        Assert.Equal(3, m.Code[2]);             // Offset 正指向立即数低字节
        Assert.Equal(0, m.Code[3]);
    }

    [Fact]
    public void Record_MustHappenBeforeEmit_ForOffsetToMatch()
    {
        // 记录用的是"记录当时"的 PC；先发射再记录会指向错误位置。
        var asm = new BytecodeAssembler();
        asm.Raw.HCALL(1);                       // PC 0..2，已发射
        asm.RecordHostIdx("Late", 1, RelocationSourceLocation.Unknown); // PC=3 → Offset=4

        var r = Assert.Single(asm.Relocations.Records);
        Assert.Equal(4, r.Offset);

        // 偏移落在代码之外（代码只有 3 字节），回填时必须拒绝而非静默写越界
        var ex = Assert.Throws<InvalidOperationException>(() => asm.Build());
        Assert.Contains("outside the code section", ex.Message);
    }

    [Fact]
    public void Record_PreservesSourceLocation()
    {
        var asm = new BytecodeAssembler();
        asm.RecordHostIdx(
            "Foo", 1, new RelocationSourceLocation("m.st", 42, -1));
        asm.Raw.HCALL(1);

        var r = Assert.Single(asm.Relocations.Records);
        Assert.Equal("m.st", r.Source.File);
        Assert.Equal(42, r.Source.Line);
        Assert.False(r.Source.IsUnknown);
    }

    [Fact]
    public void Record_UnknownSource_IsExplicitNotAFakeLine()
    {
        // 0 是合法行号，"没有源"必须显式记为 Unknown（-1），不得伪造
        var asm = new BytecodeAssembler();
        asm.RecordHostIdx("Foo", 1, RelocationSourceLocation.Unknown);
        asm.Raw.HCALL(1);

        var r = Assert.Single(asm.Relocations.Records);
        Assert.True(r.Source.IsUnknown);
        Assert.Equal(-1, r.Source.Line);
    }

    // ═══════════════════════════════════════════════════════════════
    //  回填（单模块幂等）
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Build_DoesNotChangeAnyByte_SingleModule()
    {
        var asm = new BytecodeAssembler();
        asm.RecordHostIdx("Foo", 5, RelocationSourceLocation.Unknown);
        asm.Raw.HCALL(5);
        asm.RecordHostIdx("Bar", 9, RelocationSourceLocation.Unknown);
        asm.Raw.HCALL(9);

        var emitted = asm.Raw.GetRawCode();
        var m = asm.Build();

        // 回填后与发射时逐字节一致 —— 单模块内恒等
        Assert.Equal(emitted, m.Code);
    }

    [Fact]
    public void Build_Twice_ProducesSameBytes()
    {
        var asm = new BytecodeAssembler();
        asm.RecordHostIdx("Foo", 5, RelocationSourceLocation.Unknown);
        asm.Raw.HCALL(5);

        var first = asm.Build().Code;
        var second = asm.Build().Code;

        Assert.Equal(first, second);
    }

    [Fact]
    public void Build_WritesLocalIdAsLittleEndian16()
    {
        var asm = new BytecodeAssembler();
        asm.RecordHostIdx("Foo", 0x1234, RelocationSourceLocation.Unknown);
        asm.Raw.HCALL(0x1234);

        var m = asm.Build();

        // HCALL 操作码 + 小端 id16
        Assert.Equal(0xB0, m.Code[0]);
        Assert.Equal(0x34, m.Code[1]);          // 低字节
        Assert.Equal(0x12, m.Code[2]);          // 高字节
        Assert.Equal(0x1234, BitConverter.ToUInt16(m.Code, 1));
    }

    [Fact]
    public void PatchUInt16_MatchesHcallEmission_ByteForByte()
    {
        // 回填写回的字节必须与发射时逐位相同，否则回填本身就是破坏
        var emitted = new BytecodeAssembler();
        emitted.Raw.HCALL(0xABCD);

        var patched = new BytecodeAssembler();
        patched.Raw.HCALL(0x0000);              // 先写成 0
        patched.Raw.PatchUInt16(1, 0xABCD);

        Assert.Equal(emitted.Raw.GetRawCode(), patched.Raw.GetRawCode());
    }

    [Fact]
    public void PatchUInt16_HighBitSet_DoesNotSignExtend()
    {
        var asm = new BytecodeAssembler();
        asm.Raw.HCALL(0);
        asm.Raw.PatchUInt16(1, 0xFFFE);

        Assert.Equal(0xFE, asm.Raw.GetRawCode()[1]);
        Assert.Equal(0xFF, asm.Raw.GetRawCode()[2]);
    }

    // ═══════════════════════════════════════════════════════════════
    //  重编号（步骤④：局部 ID 可经映射写成合并后 ID）
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void HostIdRemap_RewritesLocalId_ToMergedId()
    {
        var asm = new BytecodeAssembler();
        asm.HostIdRemap = (name, local) => (ushort)(local + 100);
        asm.RecordHostIdx("Foo", 3, RelocationSourceLocation.Unknown);
        asm.Raw.HCALL(3);

        var m = asm.Build();

        // 局部 3 → 合并后 103
        Assert.Equal(103, BitConverter.ToUInt16(m.Code, 1));
    }

    [Fact]
    public void HostIdRemap_ResolvesBySymbolName_EachRecordGetsItsOwnId()
    {
        // 按名解析：两条记录的局部 ID 相同，但名字不同 ⇒ 最终 ID 不同
        var asm = new BytecodeAssembler();
        asm.HostIdRemap = (name, local) => name switch
        {
            "Alpha" => 10,
            "Beta" => 20,
            _ => local,
        };

        asm.RecordHostIdx("Alpha", 0, RelocationSourceLocation.Unknown);
        asm.Raw.HCALL(0);
        asm.RecordHostIdx("Beta", 0, RelocationSourceLocation.Unknown);
        asm.Raw.HCALL(0);

        var m = asm.Build();

        // 第一条 HCALL 占 PC 0..2，第二条占 PC 3..5 ⇒ 操作码在 3、立即数在 4
        Assert.Equal(10, BitConverter.ToUInt16(m.Code, 1));
        Assert.Equal(20, BitConverter.ToUInt16(m.Code, 4));
    }

    [Fact]
    public void HostIdRemap_Null_MeansIdentity()
    {
        var asm = new BytecodeAssembler();
        Assert.Null(asm.HostIdRemap);           // 默认 null = 单模块恒等

        asm.RecordHostIdx("Foo", 42, RelocationSourceLocation.Unknown);
        asm.Raw.HCALL(42);

        var m = asm.Build();
        Assert.Equal(42, BitConverter.ToUInt16(m.Code, 1));
    }

    [Fact]
    public void HostIdRemap_CanCollapseTwoModulesOntoOneId()
    {
        // 两个模块各自的局部 0 其实是同一个宿主 ⇒ 合并后必然同 ID
        var asm = new BytecodeAssembler();
        asm.HostIdRemap = (name, _) => (ushort)(name == "Same" ? 77 : 0);

        asm.RecordHostIdx("Same", 0, RelocationSourceLocation.Unknown);
        asm.Raw.HCALL(0);
        asm.RecordHostIdx("Same", 0, RelocationSourceLocation.Unknown);
        asm.Raw.HCALL(0);

        var m = asm.Build();

        Assert.Equal(77, BitConverter.ToUInt16(m.Code, 1));
        Assert.Equal(77, BitConverter.ToUInt16(m.Code, 4));
    }

    [Fact]
    public void RemapResult_IsVisibleInRecord_OnlyThroughBytes()
    {
        // 记录本身不持有"已解析值" —— 重编号结果只体现在字节流
        var asm = new BytecodeAssembler();
        asm.HostIdRemap = (_, _) => (ushort)999;
        asm.RecordHostIdx("Foo", 1, RelocationSourceLocation.Unknown);
        asm.Raw.HCALL(1);

        Assert.Equal(1, asm.Relocations.Records[0].Addend);  // 记录仍是局部值

        var m = asm.Build();
        Assert.Equal(999, BitConverter.ToUInt16(m.Code, 1)); // 字节是最终值
    }

    // ═══════════════════════════════════════════════════════════════
    //  写侧拒绝
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Record_WithoutSymbolName_Rejected()
    {
        // HostIdx 的身份是宿主函数名；没有名字就无法跨模块解析、无法给未定义符号诊断
        Assert.Throws<InvalidOperationException>(() =>
            RelocationRecord.Create(
                RelocationKind.HostIdx, RelocationSection.Code, 0,
                symbolName: null, RelocationSourceLocation.Unknown, addend: 0));
    }

    [Fact]
    public void Record_NegativeId_Rejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            RelocationRecord.Create(
                RelocationKind.HostIdx, RelocationSection.Code, 0,
                "Foo", RelocationSourceLocation.Unknown, addend: -1));
    }

    [Fact]
    public void Record_IdAboveUShortMax_Rejected()
    {
        // 超出 16 位无符号编码范围必然被截断成另一个宿主 ID
        var ex = Assert.Throws<InvalidOperationException>(() =>
            RelocationRecord.Create(
                RelocationKind.HostIdx, RelocationSection.Code, 0,
                "Foo", RelocationSourceLocation.Unknown, addend: 65536));

        Assert.Contains("16-bit unsigned encoding range", ex.Message);
    }

    [Fact]
    public void HostIdRemap_CanProduceMaxUShort()
    {
        // ushort 返回类型天然不越界，故无需（也不应）再写一道越界检查：
        // 钩子能给出的最大值就是编码上限 65535，必须能正常回填。
        var asm = new BytecodeAssembler();
        asm.HostIdRemap = (_, _) => ushort.MaxValue;

        asm.RecordHostIdx("Foo", 1, RelocationSourceLocation.Unknown);
        asm.Raw.HCALL(1);

        var m = asm.Build();
        Assert.Equal(ushort.MaxValue, BitConverter.ToUInt16(m.Code, 1));
    }

    [Fact]
    public void ApplyHostIdx_OffsetOutsideCode_Throws()
    {
        var asm = new BytecodeAssembler();
        asm.RecordHostIdx("Foo", 1, RelocationSourceLocation.Unknown); // Offset=1
        asm.Raw.RET();                          // 只有 1 字节，Offset+2 越界

        var ex = Assert.Throws<InvalidOperationException>(() => asm.Build());
        Assert.Contains("outside the code section", ex.Message);
    }

    // ═══════════════════════════════════════════════════════════════
    //  容量口径分列（不合并为单一容量结论）
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void EncodingRange_AllowsAnyUShort_NotCappedAtAllocatorLimit()
    {
        // 口径①编码能力：0..65535 全域合法。Validate **不**按分配器策略上限拒绝。
        var r = RelocationRecord.Create(
            RelocationKind.HostIdx, RelocationSection.Code, 0,
            "Foo", RelocationSourceLocation.Unknown, addend: 65535);

        Assert.Equal(65535, r.Addend);
    }

    [Fact]
    public void AllocatorPolicyLimit_Is254_ButNotUsedByValidate()
    {
        // 口径③分配器策略：0..254（HostFunctionRegistry.cs:167-168 拒绝 _nextId==255）。
        // 它是分配侧事实，**不是**编码容量，故 Validate 不据此拒绝 —— 否则等于把
        // 一条未经消费侧核实的口径冻结成硬事实（§8.6-2）。
        Assert.Equal(254, RelocationRules.MaxAllocatedHostId);

        // 300 > 254：分配策略之外，但仍在编码范围内 ⇒ 记录层放行
        var r = RelocationRecord.Create(
            RelocationKind.HostIdx, RelocationSection.Code, 0,
            "Foo", RelocationSourceLocation.Unknown, addend: 300);
        Assert.Equal(300, r.Addend);
    }

    [Fact]
    public void RequiresSymbol_IsTrue_ForHostIdx()
    {
        // 与 ConstIdx（false）对照：宿主函数有稳定名字
        Assert.True(RelocationRules.RequiresSymbol(RelocationKind.HostIdx));
        Assert.False(RelocationRules.RequiresSymbol(RelocationKind.ConstIdx));
    }

    [Fact]
    public void EncodingWidth_Is16_ForHostIdx()
    {
        Assert.Equal(16, RelocationRules.EncodingWidthBits(RelocationKind.HostIdx));
    }

    // ═══════════════════════════════════════════════════════════════
    //  静态 ABI ID 不进重定位表（§5 HOST-2 裁定）
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void StaticHostCallIds_ProduceNoHostIdxRecord()
    {
        // 汇编器 Print 族直接写 HostCallIds.PRINT_STR/INT/CHAR（:482/:490/:498/:506/:516）。
        // 这些是 ABI 冻结常量、在发射点只有 ID 没有名字，裁定为**不进表、不参与重编号**。
        var asm = new BytecodeAssembler();
        asm.Print("hi");
        asm.PrintLn();
        asm.PrintInt(BytecodeAssembler.A0);

        // Print 内部 Load 字面量会产生 ConstIdx 记录，但绝不能有 HostIdx
        Assert.DoesNotContain(
            asm.Relocations.Records, r => r.Kind == RelocationKind.HostIdx);

        var m = asm.Build();
        // HCALL 指令本身照常存在，只是没有重定位记录
        Assert.Contains((byte)0xB0, m.Code);
    }

    [Fact]
    public void StaticAndDynamicHostCalls_Coexist_WithoutPollutingEachOther()
    {
        // 静态 ABI ID（不记）与动态宿主（记）可以在同一模块共存
        var asm = new BytecodeAssembler();
        asm.PrintInt(BytecodeAssembler.A0);          // 静态 PRINT_INT = 0x0002
        asm.RecordHostIdx("MyFunc", 5, RelocationSourceLocation.Unknown);
        asm.Raw.HCALL(5);                            // 动态

        var host = asm.Relocations.Records
            .Where(r => r.Kind == RelocationKind.HostIdx)
            .ToList();

        var r = Assert.Single(host);
        Assert.Equal("MyFunc", r.SymbolName);
        Assert.Equal(5, r.Addend);
        // PrintInt = MOV(3 字节, BytecodeBuilder.cs:207) + HCALL(3 字节) = 6 字节
        // ⇒ 记录时 PC=6，Offset = 6+1 = 7
        Assert.Equal(7, r.Offset);
    }

    // ═══════════════════════════════════════════════════════════════
    //  与既有类别的回归共存
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void MixedKinds_AllBackfill()
    {
        var asm = new BytecodeAssembler();
        asm.DeclareGlobal("g0");                // slot 0
        asm.HostIdRemap = (name, local) => (ushort)(local + 50);

        asm.Raw.RET();                          // PC 0

        asm.Label("top");                       // PC 1
        asm.Load(42L);                          // PC 1..6，ConstIdx 在 3
        asm.GetGlobal(BytecodeAssembler.A0, "g0");  // PC 7..10，GlobalIdx 在 9
        asm.RecordHostIdx("H", 2, RelocationSourceLocation.Unknown);
        asm.Raw.HCALL(2);                       // PC 11..13，HostIdx 在 12
        asm.Jmp("top");                         // PC 14..18，Addr32 在 15
        asm.Raw.RET();

        var m = asm.Build();

        Assert.Equal(4, asm.Relocations.Count);
        Assert.Equal(new[] {
            RelocationKind.ConstIdx,
            RelocationKind.GlobalIdx,
            RelocationKind.HostIdx,
            RelocationKind.Addr32,
        }, asm.Relocations.Records.Select(r => r.Kind).ToArray());

        Assert.Equal(0, BitConverter.ToInt32(m.Code, 3));    // 常量局部索引
        Assert.Equal(0, BitConverter.ToInt16(m.Code, 9));    // 全局槽位
        Assert.Equal(52, BitConverter.ToUInt16(m.Code, 12)); // 局部 2 → 重编号后 52
        Assert.Equal(1u, BitConverter.ToUInt32(m.Code, 15)); // 回到 "top" = PC 1
    }

    [Fact]
    public void UnmigratedKind_StillRejected()
    {
        // 逐类迁移：未迁移种类（StructTypeId，随 F9 一起做）仍被显式拒绝，不静默跳过。
        // 切片 5 之前本用例的载体是 HostIdx，现已迁移，故改用 StructTypeId。
        var asm = new BytecodeAssembler();
        asm.Raw.RET();
        asm.Relocations.Add(RelocationRecord.Create(
            RelocationKind.StructTypeId, RelocationSection.Code, 0, "ST0", addend: 0));

        var ex = Assert.Throws<InvalidOperationException>(() => asm.Build());

        Assert.Contains("StructTypeId", ex.Message);
        Assert.Contains("not handled by BytecodeAssembler yet", ex.Message);
        Assert.Contains(
            "only Addr32, GlobalIdx, ConstIdx and HostIdx are migrated", ex.Message);
    }

    [Fact]
    public void RecordHostIdx_DoesNotTouchTheCode_UntilBuild()
    {
        // 记录本身不改字节：写回只发生在回填阶段
        var asm = new BytecodeAssembler();
        asm.Raw.HCALL(3);
        var before = asm.Raw.GetRawCode();

        asm.RecordHostIdx("Late", 3, RelocationSourceLocation.Unknown);

        Assert.Equal(before, asm.Raw.GetRawCode());
        Assert.Single(asm.Relocations.Records);
    }
}
