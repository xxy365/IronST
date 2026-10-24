// RelocationTableTests.cs — 0B 步骤 3 · 切片 1：重定位记录模型
using System.Text;
using IronST.Runtime.Shared;

namespace IronST.Runtime.Tests;

/// <summary>
/// 覆盖设计 §3.1（七字段记录模型）、§3.3（六类编码宽度）、§2.5（DG-1 引用来源）
/// 以及切片 1 的两条约束：记录不存最终值、不依赖即时回填。
/// </summary>
public class RelocationTableTests
{
    private static RelocationTable SampleTable()
    {
        var table = new RelocationTable();

        table.Add(RelocationRecord.Create(
            RelocationKind.Addr32,
            RelocationSection.Code,
            offset: 12,
            symbolName: "Main",
            source: new RelocationSourceLocation("a.st", 3, 5)));

        table.Add(RelocationRecord.Create(
            RelocationKind.GlobalIdx,
            RelocationSection.Code,
            offset: 24,
            symbolName: "counter",
            source: new RelocationSourceLocation("a.st", 7, 9)));

        table.Add(RelocationRecord.Create(
            RelocationKind.ConstIdx,
            RelocationSection.Code,
            offset: 36,
            symbolName: null,
            source: RelocationSourceLocation.Unknown,
            addend: 5));

        return table;
    }

    // ═══════════════════════════════════════════════════════════════
    //  编码宽度与符号要求 —— 对照 §3.3 逐条
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(RelocationKind.Addr32, 32)]
    [InlineData(RelocationKind.Off8, 8)]
    [InlineData(RelocationKind.GlobalIdx, 16)]
    [InlineData(RelocationKind.ConstIdx, 32)]
    [InlineData(RelocationKind.StructTypeId, 8)]
    [InlineData(RelocationKind.HostIdx, 16)]
    public void EncodingWidths_MatchFrozenSpec(RelocationKind kind, int expectedBits)
    {
        Assert.Equal(expectedBits, RelocationRules.EncodingWidthBits(kind));

        // 符号名按 RequiresSymbol 给：ConstIdx 不得携带符号名（常量无跨模块身份），
        // 其余种类必须携带 —— 两条规则分别由各自的 Theory 覆盖。
        Assert.Equal(expectedBits, RelocationRecord.Create(
            kind, RelocationSection.Code, 0,
            symbolName: RelocationRules.RequiresSymbol(kind) ? "sym" : null
        ).EncodingWidthBits);
    }

    [Theory]
    [InlineData(RelocationKind.Addr32, true)]
    [InlineData(RelocationKind.Off8, true)]
    [InlineData(RelocationKind.GlobalIdx, true)]
    [InlineData(RelocationKind.ConstIdx, false)]
    [InlineData(RelocationKind.StructTypeId, true)]
    [InlineData(RelocationKind.HostIdx, true)]
    public void RequiresSymbol_OnlyConstIdxIsExempt(RelocationKind kind, bool expected)
    {
        Assert.Equal(expected, RelocationRules.RequiresSymbol(kind));
    }

    [Fact]
    public void Create_FillsWidthFromKind_AndAddsToTable()
    {
        var table = new RelocationTable();

        table.Add(RelocationRecord.Create(
            RelocationKind.HostIdx, RelocationSection.Code, 3, "Print"));

        Assert.Equal(1, table.Count);
        Assert.Equal(16, table.Records[0].EncodingWidthBits);
        Assert.Equal("Print", table.Records[0].SymbolName);
    }

    // ═══════════════════════════════════════════════════════════════
    //  边界校验
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Add_RejectsNegativeOffset()
    {
        var table = new RelocationTable();

        var ex = Assert.Throws<InvalidOperationException>(() => table.Add(
            RelocationRecord.Create(RelocationKind.Addr32, RelocationSection.Code, -1, "Main")));

        Assert.Contains("non-negative", ex.Message);
    }

    [Fact]
    public void Add_RejectsWidthMismatch()
    {
        var table = new RelocationTable();

        // Addr32 必须 32 位 —— 存 16 位会在写入时静默截断
        var bad = new RelocationRecord(
            RelocationKind.Addr32, RelocationSection.Code, 0, "Main",
            RelocationSourceLocation.Unknown, EncodingWidthBits: 16, Addend: 0);

        var ex = Assert.Throws<InvalidOperationException>(() => table.Add(bad));

        Assert.Contains("truncate", ex.Message);
        Assert.Equal(0, table.Count);   // 校验失败不得留下半条记录
    }

    [Theory]
    [InlineData(RelocationKind.Addr32)]
    [InlineData(RelocationKind.Off8)]
    [InlineData(RelocationKind.GlobalIdx)]
    [InlineData(RelocationKind.StructTypeId)]
    [InlineData(RelocationKind.HostIdx)]
    public void Add_RejectsMissingSymbol_ForSymbolKinds(RelocationKind kind)
    {
        var table = new RelocationTable();

        var ex = Assert.Throws<InvalidOperationException>(() => table.Add(
            RelocationRecord.Create(kind, RelocationSection.Code, 0, symbolName: null)));

        Assert.Contains("symbol", ex.Message);
        Assert.Equal(0, table.Count);
    }

    [Fact]
    public void Add_AllowsMissingSymbol_ForConstIdx()
    {
        var table = new RelocationTable();

        // ConstIdx 的索引来源由 Addend 承载，不要求符号名
        table.Add(RelocationRecord.Create(
            RelocationKind.ConstIdx, RelocationSection.Code, 8, symbolName: null, addend: 42));

        Assert.Equal(1, table.Count);
        Assert.Equal(42, table.Records[0].Addend);
    }

    [Fact]
    public void Add_RejectsUnknownKind()
    {
        var table = new RelocationTable();

        Assert.Throws<InvalidOperationException>(() => table.Add(
            RelocationRecord.Create((RelocationKind)99, RelocationSection.Code, 0, "x")));
    }

    [Fact]
    public void Add_RejectsNegativeSourceLine()
    {
        var table = new RelocationTable();

        var ex = Assert.Throws<InvalidOperationException>(() => table.Add(
            RelocationRecord.Create(
                RelocationKind.Addr32, RelocationSection.Code, 0, "Main",
                new RelocationSourceLocation("a.st", -2, -1))));

        Assert.Contains("line/column", ex.Message);
    }

    // ═══════════════════════════════════════════════════════════════
    //  序列化往返
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Serialize_Load_RoundTrips_AllFields()
    {
        var original = new RelocationTable();
        original.Add(RelocationRecord.Create(
            RelocationKind.Off8,
            RelocationSection.Code,
            offset: 123,
            symbolName: "LoopStart",
            source: new RelocationSourceLocation("module b.st", 42, 7),
            addend: -128));

        var loaded = RelocationTable.Load(original.Serialize());

        Assert.Equal(1, loaded.Count);
        var r = loaded.Records[0];
        Assert.Equal(RelocationKind.Off8, r.Kind);
        Assert.Equal(RelocationSection.Code, r.Section);
        Assert.Equal(123, r.Offset);
        Assert.Equal("LoopStart", r.SymbolName);
        Assert.Equal("module b.st", r.Source.File);
        Assert.Equal(42, r.Source.Line);
        Assert.Equal(7, r.Source.Column);
        Assert.Equal(8, r.EncodingWidthBits);
        Assert.Equal(-128, r.Addend);
    }

    [Fact]
    public void Serialize_Load_RoundTrips_MultipleRecords_InOrder()
    {
        var original = SampleTable();

        var loaded = RelocationTable.Load(original.Serialize());

        Assert.Equal(original.Count, loaded.Count);
        for (int i = 0; i < original.Count; i++)
            Assert.Equal(original.Records[i], loaded.Records[i]);
    }

    [Fact]
    public void UnresolvedSymbol_And_SourcePosition_SurviveRoundTrip()
    {
        // 核心验收：记录必须能保留"未解析的符号"与"引用源位置"，
        // 否则链接期做不出有上下文的未定义符号诊断（§2.5 硬性条件）。
        var table = new RelocationTable();
        table.Add(RelocationRecord.Create(
            RelocationKind.Addr32, RelocationSection.Code, 0, "NotYetDefined",
            new RelocationSourceLocation("lib.st", 11, 3)));

        var loaded = RelocationTable.Load(table.Serialize());
        var r = loaded.Records[0];

        Assert.Equal("NotYetDefined", r.SymbolName);          // 仍未解析 —— 表不替它求值
        Assert.False(r.Source.IsUnknown);
        Assert.Equal(("lib.st", 11, 3), (r.Source.File, r.Source.Line, r.Source.Column));
    }

    [Fact]
    public void UnknownSourceLocation_RoundTripsAsUnknown()
    {
        var table = new RelocationTable();
        table.Add(RelocationRecord.Create(
            RelocationKind.GlobalIdx, RelocationSection.Code, 4, "g0"));

        var r = RelocationTable.Load(table.Serialize()).Records[0];

        Assert.True(r.Source.IsUnknown);
        Assert.Equal(-1, r.Source.Line);
        Assert.Equal(-1, r.Source.Column);
    }

    [Fact]
    public void Records_CarryNoResolvedValue()
    {
        // 约束 1 的结构性保证：模型里根本没有"最终数值"字段。
        // 记录只描述"按哪个符号改写哪里"，解析结果由链接期写进字节流。
        var type = typeof(RelocationRecord);

        Assert.DoesNotContain(
            type.GetProperties(),
            p => p.Name.Contains("Value", StringComparison.OrdinalIgnoreCase)
                 || p.Name.Contains("Resolved", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Table_IsIndependentOfEagerBackfill()
    {
        // 约束 2：本表完全不依赖 BytecodeAssembler.Build()/SaveTo()/BuildModule()
        // 的即时回填（:520/:526/:532）—— 全程不碰任何汇编器或字节码构建器。
        var table = new RelocationTable();
        table.Add(RelocationRecord.Create(
            RelocationKind.Addr32, RelocationSection.Code, 0, "Anywhere",
            new RelocationSourceLocation("x.st", 1, 1)));

        var bytes = table.Serialize();
        var restored = RelocationTable.Load(bytes);

        Assert.Equal(1, restored.Count);
        Assert.Equal("Anywhere", restored.Records[0].SymbolName);
        Assert.Equal(0, restored.Records[0].Offset);
    }

    [Fact]
    public void FindBySymbol_UsesOrdinal_AndPreservesOriginalName()
    {
        // 本表用 Ordinal（原始名）；跨模块合并的大小写规则由链接器施加（§9 推迟裁定）。
        var table = SampleTable();

        Assert.Single(table.FindBySymbol("counter"));
        Assert.Empty(table.FindBySymbol("COUNTER"));   // 不在本层做大小写折叠
        Assert.Empty(table.FindBySymbol("nope"));

        // 原始大小写原样保留（SYM-1.a）
        Assert.Equal("counter", table.FindBySymbol("counter").Single().SymbolName);
    }

    // ═══════════════════════════════════════════════════════════════
    //  损坏数据必须被拒绝
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Load_RejectsBadMagic()
    {
        var bytes = SampleTable().Serialize();
        bytes[0] = (byte)'X';

        Assert.Throws<InvalidDataException>(() => RelocationTable.Load(bytes));
    }

    [Fact]
    public void Load_RejectsBadVersion()
    {
        var bytes = SampleTable().Serialize();
        bytes[4] = 99;   // 版本字节

        var ex = Assert.Throws<InvalidDataException>(() => RelocationTable.Load(bytes));
        Assert.Contains("version", ex.Message);
    }

    [Fact]
    public void Load_RejectsTruncatedHeader()
    {
        var bytes = SampleTable().Serialize();

        Assert.Throws<InvalidDataException>(
            () => RelocationTable.Load(bytes[..8]));   // HeaderSize = 12
    }

    [Fact]
    public void Load_RejectsTruncatedRecord()
    {
        var bytes = SampleTable().Serialize();

        Assert.Throws<InvalidDataException>(
            () => RelocationTable.Load(bytes[..^5]));
    }

    [Fact]
    public void Load_RejectsNegativeCount()
    {
        var bytes = new byte[12];
        Encoding.ASCII.GetBytes("IRLR").CopyTo(bytes, 0);
        bytes[4] = 1;                       // 版本
        BitConverter.GetBytes(-1).CopyTo(bytes, 8);   // 记录数

        var ex = Assert.Throws<InvalidDataException>(() => RelocationTable.Load(bytes));
        Assert.Contains("count", ex.Message);
    }

    [Fact]
    public void Load_RejectsTrailingBytes()
    {
        var bytes = SampleTable().Serialize();
        var padded = bytes.Concat(new byte[] { 0xAB }).ToArray();

        var ex = Assert.Throws<InvalidDataException>(() => RelocationTable.Load(padded));
        Assert.Contains("trailing", ex.Message);
    }

    [Fact]
    public void Load_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => RelocationTable.Load(null!));
    }
}
