// ConstIdxRelocationTests.cs — 0B 步骤 3 · 切片 4：ConstIdx 的记录、回填与重映射
//
// LDC = [op][rd][LE32]：立即数在操作码 +2 处、宽 32 位、有符号编码（BytecodeBuilder.cs:311）。
// 常量池只追加不去重（AddConst/AddString），索引一经分配终身不变。
//
// 与 GlobalIdx 的关键区别：常量没有跨模块稳定身份，故记录不带符号名，
// 模块局部索引放在 Addend；链接期重编号必须走 ConstIndexRemap 显式通道。
using IronST.Runtime.Shared;

namespace IronST.Runtime.Tests;

public class ConstIdxRelocationTests
{
    // ═══════════════════════════════════════════════════════════════
    //  记录
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void LoadLong_RecordsConstIdx_WithLocalIndexAndNoSymbol()
    {
        var asm = new BytecodeAssembler();
        asm.Load(42L);                          // 局部索引 0
        asm.Load(99L);                          // 局部索引 1

        Assert.Equal(2, asm.Relocations.Count);

        var first = asm.Relocations.Records[0];
        Assert.Equal(RelocationKind.ConstIdx, first.Kind);
        Assert.Equal(RelocationSection.Code, first.Section);
        Assert.Equal(2, first.Offset);          // LDC 在 PC 0，立即数从 2 开始
        Assert.Equal(0, first.Addend);          // 模块局部常量序号
        Assert.Null(first.SymbolName);          // 常量无符号身份
        Assert.Equal(32, first.EncodingWidthBits);
    }

    [Fact]
    public void LoadString_And_LoadDouble_AlsoRecord()
    {
        var asm = new BytecodeAssembler();
        asm.Load("hello");                      // 局部索引 0
        asm.Load(3.14);                         // 局部索引 1

        var records = asm.Relocations.Records.ToList();
        Assert.Equal(2, records.Count);
        Assert.All(records, r => Assert.Equal(RelocationKind.ConstIdx, r.Kind));
        Assert.Equal(new[] { 0, 1 }, records.Select(r => r.Addend).ToArray());
    }

    [Fact]
    public void ImmediateOffset_IsAtOpcodePlus2_AndStepsBy6()
    {
        var asm = new BytecodeAssembler();
        asm.Load(1L);                           // PC 0..5
        asm.Load(2L);                           // PC 6..11
        asm.Load(3L);                           // PC 12..17

        // LDC 每条 6 字节（op 1 + rd 1 + LE32 4），立即数在各自操作码 +2
        Assert.Equal(new[] { 2, 8, 14 },
            asm.Relocations.Records.Select(r => r.Offset).ToArray());
    }

    // ═══════════════════════════════════════════════════════════════
    //  回填：单模块恒等，字节不变
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Build_DoesNotChangeAnyByte_SingleModule()
    {
        var asm = new BytecodeAssembler();
        asm.Load(42L);
        asm.Load(3.14);
        asm.Load("text");

        // 发射时写入的立即数
        var emitted = asm.Raw.GetRawCode();

        var m = asm.Build();

        // 回填后与发射时逐字节一致 —— 单模块内恒等
        Assert.Equal(emitted, m.Code);
    }

    [Fact]
    public void Build_WritesLocalIndexAsLittleEndian32()
    {
        var asm = new BytecodeAssembler();
        asm.Load(1L);                           // 0
        asm.Load(2L);                           // 1
        asm.Load(3L);                           // 2

        var m = asm.Build();

        Assert.Equal(0, BitConverter.ToInt32(m.Code, 2));
        Assert.Equal(1, BitConverter.ToInt32(m.Code, 8));
        Assert.Equal(2, BitConverter.ToInt32(m.Code, 14));
    }

    [Fact]
    public void Build_Twice_ProducesSameBytes()
    {
        var asm = new BytecodeAssembler();
        asm.Load(7L);
        asm.Load("dup");
        asm.Load(7L);                           // 池不去重，仍是新索引 2

        var first = asm.Build().Code;
        var second = asm.Build().Code;

        Assert.Equal(first, second);
    }

    [Fact]
    public void RepeatedLiteral_GetsNewIndex_NotDeduplicated()
    {
        // §3.3 REL-4 已冻结：首版追加合并、不去重
        var asm = new BytecodeAssembler();
        asm.Load(7L);
        asm.Load(7L);

        Assert.Equal(new[] { 0, 1 },
            asm.Relocations.Records.Select(r => r.Addend).ToArray());
    }

    // ═══════════════════════════════════════════════════════════════
    //  重映射：链接期合并常量池的入口
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ConstIndexRemap_RewritesLocalIndex_ToMergedIndex()
    {
        // 模拟链接：本模块 2 条常量被追加到合并池的第 5、6 位
        var asm = new BytecodeAssembler();
        asm.Load(10L);                          // 局部 0
        asm.Load(20L);                          // 局部 1
        asm.Raw.RET();

        asm.ConstIndexRemap = local => local + 5;

        var m = asm.Build();

        Assert.Equal(5, BitConverter.ToInt32(m.Code, 2));
        Assert.Equal(6, BitConverter.ToInt32(m.Code, 8));
    }

    [Fact]
    public void ConstIndexRemap_CanCollapseDuplicates()
    {
        // 证明映射是任意的，不只仿射：局部 0 与局部 2 可指向同一条合并后常量
        var asm = new BytecodeAssembler();
        asm.Load(1L);                           // 局部 0
        asm.Load(2L);                           // 局部 1
        asm.Load(3L);                           // 局部 2
        asm.Raw.RET();

        asm.ConstIndexRemap = local => local switch
        {
            0 => 9,
            1 => 10,
            2 => 9,
            _ => local,
        };

        var m = asm.Build();

        Assert.Equal(9, BitConverter.ToInt32(m.Code, 2));
        Assert.Equal(10, BitConverter.ToInt32(m.Code, 8));
        Assert.Equal(9, BitConverter.ToInt32(m.Code, 14));
    }

    [Fact]
    public void ConstIndexRemap_DoesNotTouchTheModulesOwnPool()
    {
        // 重映射只改代码里的立即数；本模块常量池条目数不受影响。
        var asm = new BytecodeAssembler();
        asm.Load(1L);
        asm.Load(2L);
        asm.Raw.RET();

        asm.ConstIndexRemap = local => local + 100;

        var m = asm.Build();

        Assert.Equal(2, m.ConstantPool.Length);
        Assert.Equal(1L, m.ConstantPool[0].AsInt64());
        Assert.Equal(2L, m.ConstantPool[1].AsInt64());
        // 代码指向合并池，本模块池保持局部形态 —— 由链接器负责拼接
        Assert.Equal(100, BitConverter.ToInt32(m.Code, 2));
    }

    [Fact]
    public void ConstIndexRemap_Null_MeansIdentity()
    {
        var asm = new BytecodeAssembler();
        asm.Load(5L);

        Assert.Null(asm.ConstIndexRemap);

        var m = asm.Build();
        Assert.Equal(0, BitConverter.ToInt32(m.Code, 2));
    }

    // ═══════════════════════════════════════════════════════════════
    //  容量边界（写入前校验：索引 / 计数 / 偏移）
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Record_WithSymbolName_Rejected()
    {
        // 语义边界：局部索引不得伪装成符号身份
        var table = new RelocationTable();

        var ex = Assert.Throws<InvalidOperationException>(() => table.Add(
            RelocationRecord.Create(
                RelocationKind.ConstIdx, RelocationSection.Code, 2, "someConst", addend: 0)));

        Assert.Contains("must not carry a symbol name", ex.Message);
        Assert.Equal(0, table.Count);
    }

    [Fact]
    public void Record_NegativeIndex_Rejected()
    {
        var table = new RelocationTable();

        var ex = Assert.Throws<InvalidOperationException>(() => table.Add(
            RelocationRecord.Create(
                RelocationKind.ConstIdx, RelocationSection.Code, 2, symbolName: null, addend: -1)));

        Assert.Contains("out of range", ex.Message);
        Assert.Contains("0..", ex.Message);
    }

    [Fact]
    public void Record_IndexAboveMax_Rejected()
    {
        var table = new RelocationTable();

        var ex = Assert.Throws<InvalidOperationException>(() => table.Add(
            RelocationRecord.Create(
                RelocationKind.ConstIdx, RelocationSection.Code, 2, symbolName: null,
                addend: RelocationRules.MaxConstIndex + 1)));

        Assert.Contains("out of range", ex.Message);
    }

    [Fact]
    public void MaxConstIndex_ComesFromClrArrayLimit_NotFieldType()
    {
        // 上限依据运行时消费逻辑（CLR 数组下标），而非"32 位能表示什么"
        Assert.Equal(Array.MaxLength - 1, RelocationRules.MaxConstIndex);
        Assert.True(RelocationRules.MaxConstIndex > 0);
    }

    [Fact]
    public void ApplyConstIdx_IndexOutsidePool_Throws()
    {
        var asm = new BytecodeAssembler();
        asm.Load(1L);                           // 池内只有 1 条（索引 0）
        asm.Raw.RET();

        // 伪造一条索引 5 的记录 —— 记录与模块状态不一致
        asm.Relocations.Add(RelocationRecord.Create(
            RelocationKind.ConstIdx, RelocationSection.Code, 2, symbolName: null, addend: 5));

        var ex = Assert.Throws<InvalidOperationException>(() => asm.Build());

        Assert.Contains("outside this module's constant pool", ex.Message);
        Assert.Contains("count 1", ex.Message);
        Assert.Contains("code offset 2", ex.Message);   // DG-1：引用来源
    }

    [Fact]
    public void ApplyConstIdx_OffsetOutsideCode_Throws()
    {
        var asm = new BytecodeAssembler();
        asm.Load(1L);                           // 代码 6 字节
        asm.Raw.RET();                          // +1 = 7 字节
        asm.Relocations.Add(RelocationRecord.Create(
            RelocationKind.ConstIdx, RelocationSection.Code, 100, symbolName: null, addend: 0));

        var ex = Assert.Throws<InvalidOperationException>(() => asm.Build());

        Assert.Contains("outside the code section", ex.Message);
        Assert.Contains("code length", ex.Message);
    }

    [Fact]
    public void ApplyConstIdx_RemappedNegative_Throws()
    {
        var asm = new BytecodeAssembler();
        asm.Load(1L);
        asm.Raw.RET();

        asm.ConstIndexRemap = _ => -1;

        var ex = Assert.Throws<InvalidOperationException>(() => asm.Build());

        Assert.Contains("remapped index -1", ex.Message);
        Assert.Contains("out of range", ex.Message);
    }

    [Fact]
    public void ApplyConstIdx_RemappedAboveMax_Throws()
    {
        var asm = new BytecodeAssembler();
        asm.Load(1L);
        asm.Raw.RET();

        asm.ConstIndexRemap = _ => RelocationRules.MaxConstIndex + 1;

        var ex = Assert.Throws<InvalidOperationException>(() => asm.Build());

        Assert.Contains("out of range", ex.Message);
    }

    [Fact]
    public void ApplyConstIdx_RemappedIndexBeyondOwnPool_IsAllowed()
    {
        // 合并池比本模块池大是预期情形 —— 重映射后不按本模块池计数复检上界
        var asm = new BytecodeAssembler();
        asm.Load(1L);                           // 局部 0，本模块池 1 条
        asm.Raw.RET();

        asm.ConstIndexRemap = _ => 1000;        // 远超本模块池

        var m = asm.Build();

        Assert.Equal(1000, BitConverter.ToInt32(m.Code, 2));
    }

    // ═══════════════════════════════════════════════════════════════
    //  与其他类别并存
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void MixedKinds_AllBackfill()
    {
        var asm = new BytecodeAssembler();
        asm.DeclareGlobal("g0");                // slot 0
        asm.Raw.RET();

        asm.Label("top");                       // PC 1
        asm.Load(42L);                          // PC 1..6，ConstIdx 在 3
        asm.GetGlobal(BytecodeAssembler.A0, "g0");  // PC 7..10，GlobalIdx 在 9
        asm.Jmp("top");                         // PC 11..15，Addr32 在 12
        asm.Raw.RET();

        var m = asm.Build();

        Assert.Equal(3, asm.Relocations.Count);
        Assert.Equal(new[] {
            RelocationKind.ConstIdx,
            RelocationKind.GlobalIdx,
            RelocationKind.Addr32,
        }, asm.Relocations.Records.Select(r => r.Kind).ToArray());

        Assert.Equal(0, BitConverter.ToInt32(m.Code, 3));   // 常量局部索引
        Assert.Equal(0, BitConverter.ToInt16(m.Code, 9));   // 全局槽位
        Assert.Equal(1u, BitConverter.ToUInt32(m.Code, 12)); // 回到 "top" = PC 1
    }

    // ═══════════════════════════════════════════════════════════════
    //  与既有类别的回归共存
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void UnmigratedKind_StillRejected()
    {
        // 逐类迁移：未迁移种类仍被显式拒绝，不静默跳过。
        // 切片 5 之前本用例的载体是 HostIdx；HostIdx 已迁移，故改用 StructTypeId
        // （随 §6.2 生产路径恢复与 F9 一起做，见 0B 设计 §3.5 迁移顺序）。
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
    public void EmitLdc_WritesSameBytes_AsRawLdcWould()
    {
        // 收口改造不得改变发射结果
        var withRecord = new BytecodeAssembler();
        withRecord.EmitLdc(8, 0, RelocationSourceLocation.Unknown);

        var raw = new BytecodeAssembler();
        raw.Raw.LDC(8, 0);

        Assert.Equal(raw.Raw.GetRawCode(), withRecord.Raw.GetRawCode());
    }
}
