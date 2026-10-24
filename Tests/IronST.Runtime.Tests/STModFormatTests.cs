// STModFormatTests.cs — `.stmod` 格式规范的对抗性验证。
//
// 与 CliModTests 的分工：
//   · CliModTests    —— 黑盒 CLI + 自带第二套字节解析，验证产物符合规范。
//   · 本文件         —— 直接对 STModWriter/STModReader 施压：正常路径往返、
//                       以及**构造非法文件**验证读取器拒绝。
//
// 非法文件不是靠"改几个字节碰运气"，而是用本文件自带的 RawFile 重新序列化器
// （规范的第二实现）精确构造：重复 Key、错位区段、重叠区段、越界、长度溢出、
// 缺必需区段、未知必需区段、未解析导入、函数区间越界等。
using System.Text;
using IronST.Runtime.Shared;
using IronST.Runtime.Shared.Modules;

namespace IronST.Runtime.Tests;

public class STModFormatTests
{
    // ══════════════════════════════════════════════════════════════
    //  基线模块
    // ══════════════════════════════════════════════════════════════

    private static readonly byte[] BaseCode =
        Enumerable.Range(0, 16).Select(i => (byte)(0x10 + i)).ToArray();

    private static STModMetadataEntry S(string k, string v) =>
        STModMetadataEntry.FromString(k, v);

    private static STModMetadataEntry U32(string k, uint v) =>
        STModMetadataEntry.FromUInt32(k, v);

    private static STModMetadataEntry B(string k, bool v) =>
        STModMetadataEntry.FromBoolean(k, v);

    private static STModFunctionEntry Fn(
        string name, uint id, ulong offset, ulong length, uint parms = 0, uint locals = 2) =>
        new()
        {
            FunctionId = id,
            Name = name,
            Signature = "void ()",
            CodeOffset = offset,
            CodeLength = length,
            ParamCount = parms,
            LocalCount = locals,
        };

    private static Dictionary<string, STModMetadataEntry> BaseMetadata() =>
        new(StringComparer.Ordinal)
        {
            ["module.name"] = S("module.name", "T"),
            ["module.version"] = S("module.version", "1.0.0"),
            ["compiler.version"] = S("compiler.version", "test"),
            ["module.entryPC"] = U32("module.entryPC", 0),
            ["module.requiredFeatures"] = S("module.requiredFeatures", "bytecode,constant-pool"),
        };

    private static STModModule Baseline(
        IReadOnlyDictionary<string, STModMetadataEntry>? metadata = null,
        IReadOnlyList<STModFunctionEntry>? functions = null,
        byte[]? code = null,
        IReadOnlyList<BcValue>? pool = null,
        RelocationTable? relocations = null,
        bool hasEntryPoint = true) =>
        new()
        {
            Metadata = metadata ?? BaseMetadata(),
            Functions = functions ?? [Fn("f", 0, 0, (ulong)BaseCode.Length)],
            Code = code ?? BaseCode,
            ConstantPool = pool ?? [new BcValue(7L)],
            Sections = [],
            HasEntryPoint = hasEntryPoint,
            EntryPC = 0,
            Relocations = relocations,
        };

    private static byte[] ValidBytes() => STModWriter.Write(Baseline());

    private static RawFile ValidRaw() => RawFile.Parse(ValidBytes());

    private static void AssertRejected(byte[] bytes, string expectedFragment)
    {
        var ex = Assert.Throws<STModFormatException>(() => { _ = STModReader.Read(bytes); });
        Assert.Contains(expectedFragment, ex.Message);
    }

    // ══════════════════════════════════════════════════════════════
    //  1. 正常路径：元数据多种类型 / 自定义 Key / 空值
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void RoundTrip_AllMetadataValueTypes()
    {
        var metadata = BaseMetadata();
        metadata["custom.string"] = S("custom.string", "你好, world");
        metadata["custom.bool"] = B("custom.bool", true);
        metadata["custom.i32"] = STModMetadataEntry.FromInt32("custom.i32", -123);
        metadata["custom.i64"] = STModMetadataEntry.FromInt64("custom.i64", long.MinValue);
        metadata["custom.u32"] = U32("custom.u32", uint.MaxValue);
        metadata["custom.u64"] = STModMetadataEntry.FromUInt64("custom.u64", ulong.MaxValue);
        metadata["custom.bytes"] = STModMetadataEntry.FromBytes(
            "custom.bytes", [1, 2, 3, 4, 0xFF]);

        var read = STModReader.Read(STModWriter.Write(Baseline(metadata: metadata)));

        Assert.Equal(STModValueType.String, read.Metadata["custom.string"].ValueType);
        Assert.Equal("你好, world", read.Metadata["custom.string"].AsString());

        Assert.Equal(STModValueType.Boolean, read.Metadata["custom.bool"].ValueType);
        Assert.True(read.Metadata["custom.bool"].AsBoolean());

        Assert.Equal(STModValueType.Int32, read.Metadata["custom.i32"].ValueType);
        Assert.Equal(-123, read.Metadata["custom.i32"].AsInt32());

        Assert.Equal(STModValueType.Int64, read.Metadata["custom.i64"].ValueType);
        Assert.Equal(long.MinValue, read.Metadata["custom.i64"].AsInt64());

        Assert.Equal(STModValueType.UInt32, read.Metadata["custom.u32"].ValueType);
        Assert.Equal(uint.MaxValue, read.Metadata["custom.u32"].AsUInt32());

        Assert.Equal(STModValueType.UInt64, read.Metadata["custom.u64"].ValueType);
        Assert.Equal(ulong.MaxValue, read.Metadata["custom.u64"].AsUInt64());

        Assert.Equal(STModValueType.ByteArray, read.Metadata["custom.bytes"].ValueType);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 0xFF }, read.Metadata["custom.bytes"].Value);
    }

    [Fact]
    public void RoundTrip_CustomKeyNeedsNoHeaderChange()
    {
        var metadata = BaseMetadata();
        metadata["acme.vendor.flag"] = S("acme.vendor.flag", "on");

        var bytes = STModWriter.Write(Baseline(metadata: metadata));

        // 文件头长度不变 —— 新增 Key 不需要动固定布局。
        Assert.Equal(STModFormat.HeaderSize, BitConverter.ToUInt32(bytes, 12));

        var read = STModReader.Read(bytes);
        Assert.Equal("on", read.GetString("acme.vendor.flag"));
    }

    [Fact]
    public void RoundTrip_EmptyStringAndEmptyByteArray_AreAllowed()
    {
        var metadata = BaseMetadata();
        metadata["custom.empty"] = S("custom.empty", "");
        metadata["custom.zero"] = STModMetadataEntry.FromBytes("custom.zero", []);

        var read = STModReader.Read(STModWriter.Write(Baseline(metadata: metadata)));

        Assert.Equal("", read.Metadata["custom.empty"].AsString());
        Assert.Empty(read.Metadata["custom.zero"].Value);
    }

    [Fact]
    public void RoundTrip_UnknownOptionalKey_IsIgnoredNotRejected()
    {
        var metadata = BaseMetadata();
        metadata["future.unknown.thing"] = S("future.unknown.thing", "42");

        var read = STModReader.Read(STModWriter.Write(Baseline(metadata: metadata)));

        Assert.Equal(6, read.Metadata.Count);
        Assert.True(read.Metadata.ContainsKey("future.unknown.thing"));
    }

    [Fact]
    public void RoundTrip_ConstantPoolPreserved()
    {
        BcValue[] pool =
        [
            new BcValue(1234567890123L),
            new BcValue(3.5),
            new BcValue("字符串常量"),
        ];

        var read = STModReader.Read(STModWriter.Write(Baseline(pool: pool)));

        Assert.Equal(3, read.ConstantPool.Count);
        Assert.Equal(1234567890123L, read.ConstantPool[0].IntValue);
        Assert.Equal(3.5, read.ConstantPool[1].FloatValue);
        Assert.Equal("字符串常量", read.ConstantPool[2].StringValue);
    }

    [Fact]
    public void RoundTrip_RelocationsPreserved()
    {
        var table = new RelocationTable();
        table.Add(RelocationRecord.Create(
            RelocationKind.HostIdx, RelocationSection.Code, 3, "Print", addend: 4));

        var read = STModReader.Read(STModWriter.Write(Baseline(relocations: table)));

        Assert.NotNull(read.Relocations);
        var record = Assert.Single(read.Relocations!.Records);
        Assert.Equal(RelocationKind.HostIdx, record.Kind);
        Assert.Equal("Print", record.SymbolName);
        Assert.Equal(4, record.Addend);
    }

    [Fact]
    public void RoundTrip_FunctionIndexFieldsPreserved()
    {
        var read = STModReader.Read(ValidBytes());

        var f = Assert.Single(read.Functions);
        Assert.Equal("f", f.Name);
        Assert.Equal("void ()", f.Signature);
        Assert.Equal(0ul, f.CodeOffset);
        Assert.Equal((ulong)BaseCode.Length, f.CodeLength);
        Assert.Equal(STModWriter.CodeSectionId, f.CodeSectionId);
    }

    [Fact]
    public void RoundTrip_NoEntryPoint_IsAccepted()
    {
        var read = STModReader.Read(
            STModWriter.Write(Baseline(hasEntryPoint: false)));

        Assert.False(read.HasEntryPoint);
    }

    [Fact]
    public void RoundTrip_HigherMinorVersion_IsAccepted()
    {
        // 次版本更大 = 只增可选内容，必须接受（向前兼容）。
        var raw = ValidRaw();
        raw.Minor = 1;

        var read = STModReader.Read(raw.ToBytes());

        Assert.Equal(1, read.FormatVersionMajor);
        Assert.Equal(1, read.FormatVersionMinor);
    }

    // ══════════════════════════════════════════════════════════════
    //  2. 文件头
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Reject_InvalidMagic()
    {
        var bytes = ValidBytes();
        bytes[0] = (byte)'X';

        AssertRejected(bytes, "魔数");
    }

    [Fact]
    public void Reject_IsbFile_AsStmod()
    {
        // .isb 魔数 0x49525354 必须被 .stmod 读取器拒绝（两者不得混淆）。
        var isb = new byte[64];
        BitConverter.GetBytes(0x49525354u).CopyTo(isb, 0);

        AssertRejected(isb, "魔数");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(20)]
    [InlineData(47)]
    public void Reject_TruncatedFile(int length)
    {
        var bytes = ValidBytes().AsSpan(0, length).ToArray();

        AssertRejected(bytes, "截断");
    }

    [Fact]
    public void Reject_UnsupportedMajorVersion()
    {
        var raw = ValidRaw();
        raw.Major = 2;

        AssertRejected(raw.ToBytes(), "格式版本");
    }

    [Fact]
    public void Reject_HeaderSizeMismatch()
    {
        var raw = ValidRaw();
        raw.HeaderSize = 64;

        AssertRejected(raw.ToBytes(), "固定头长度");
    }

    [Fact]
    public void Reject_NonZeroHeaderReserved()
    {
        var raw = ValidRaw();
        raw.Reserved0 = 1;

        AssertRejected(raw.ToBytes(), "保留字段");
    }

    // ══════════════════════════════════════════════════════════════
    //  3. 区段表
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Reject_SectionOffsetBeyondFile()
    {
        var raw = ValidRaw();
        var bytes = raw.ToBytes();
        raw.Section(STModSectionType.Code).DeclaredOffset = (ulong)bytes.Length + 8;

        AssertRejected(raw.ToBytes(honorDeclared: true), "越界");
    }

    [Fact]
    public void Reject_SectionSizeOverflow()
    {
        var raw = ValidRaw();
        raw.Section(STModSectionType.Code).DeclaredSize = ulong.MaxValue;

        AssertRejected(raw.ToBytes(honorDeclared: true), "越界");
    }

    [Fact]
    public void Reject_SectionOffsetInsideHeader()
    {
        var raw = ValidRaw();
        raw.Section(STModSectionType.Code).DeclaredOffset = 8;

        AssertRejected(raw.ToBytes(honorDeclared: true), "固定头");
    }

    [Fact]
    public void Reject_MisalignedSectionOffset()
    {
        var raw = ValidRaw();
        raw.Section(STModSectionType.Code).DeclaredOffset =
            raw.Section(STModSectionType.Code).DeclaredOffset + 1;

        AssertRejected(raw.ToBytes(honorDeclared: true), "对齐");
    }

    [Fact]
    public void Reject_NonPowerOfTwoAlignment()
    {
        var raw = ValidRaw();
        raw.Section(STModSectionType.Code).DeclaredAlignment = 3;

        AssertRejected(raw.ToBytes(honorDeclared: true), "2 的幂");
    }

    [Fact]
    public void Reject_OverlappingSections()
    {
        var raw = ValidRaw();
        raw.Section(STModSectionType.ConstantPool).DeclaredOffset =
            raw.Section(STModSectionType.Code).DeclaredOffset;

        AssertRejected(raw.ToBytes(honorDeclared: true), "重叠");
    }

    [Fact]
    public void Reject_SectionStraddlesAnotherSection()
    {
        var raw = ValidRaw();
        // 把 Metadata 的声明长度拉长到越过 FunctionIndex 的起点。
        raw.Section(STModSectionType.Metadata).DeclaredSize =
            raw.Section(STModSectionType.FunctionIndex).DeclaredOffset
            - raw.Section(STModSectionType.Metadata).DeclaredOffset
            + 16;

        AssertRejected(raw.ToBytes(honorDeclared: true), "重叠");
    }

    [Fact]
    public void Reject_MissingRequiredSection()
    {
        var raw = ValidRaw();
        raw.Sections.RemoveAll(s => s.Type == (ushort)STModSectionType.ConstantPool);

        AssertRejected(raw.ToBytes(), "缺少必需区段");
    }

    [Fact]
    public void Reject_DuplicateRequiredSection()
    {
        var raw = ValidRaw();
        raw.Sections.Add(new RawSection { Id = 90, Type = (ushort)STModSectionType.Code });

        AssertRejected(raw.ToBytes(), "重复");
    }

    [Fact]
    public void Reject_UnknownRequiredSectionType()
    {
        var raw = ValidRaw();
        raw.Sections.Add(new RawSection
        {
            Id = 91,
            Type = 0x9000,
            Flags = (ushort)STModSectionFlags.Required,
        });

        AssertRejected(raw.ToBytes(), "未知区段类型");
    }

    [Fact]
    public void Accept_UnknownOptionalSectionType()
    {
        var raw = ValidRaw();
        raw.Sections.Add(new RawSection { Id = 92, Type = 0x9001, Payload = [1, 2, 3] });

        var read = STModReader.Read(raw.ToBytes());

        Assert.Contains(read.Sections, s => (ushort)s.Type == 0x9001);
    }

    [Fact]
    public void Reject_ImportSymbols_MarkedRequired()
    {
        // v1 不实现模块间链接：未解析的外部符号必须明确拒绝，
        // 而不是产出一个"格式合法但运行必错"的模块。
        var raw = ValidRaw();
        raw.Sections.Add(new RawSection
        {
            Id = 93,
            Type = (ushort)STModSectionType.ImportSymbols,
            Flags = (ushort)STModSectionFlags.Required,
            Payload = [0, 0, 0, 0],
        });

        AssertRejected(raw.ToBytes(), "导入");
    }

    [Fact]
    public void Reject_SectionReservedNotZero()
    {
        var raw = ValidRaw();
        raw.Section(STModSectionType.Code).Reserved = 1;

        AssertRejected(raw.ToBytes(honorDeclared: true), "保留字段");
    }

    // ══════════════════════════════════════════════════════════════
    //  4. 元数据：重复 / 非法 Key / 长度 / 类型
    // ══════════════════════════════════════════════════════════════

    private static byte[] WithMeta(params (string Key, byte Type, byte[] Value)[] records)
    {
        var raw = ValidRaw();
        raw.Section(STModSectionType.Metadata).Payload = MetaPayload(records);
        return raw.ToBytes();
    }

    private static (string Key, byte Type, byte[] Value)[] BaseRecords() =>
    [
        ("module.name", 1, Encoding.UTF8.GetBytes("T")),
        ("module.version", 1, Encoding.UTF8.GetBytes("1.0.0")),
        ("compiler.version", 1, Encoding.UTF8.GetBytes("test")),
        ("module.entryPC", 5, new byte[4]),
    ];

    [Fact]
    public void Reject_DuplicateMetadataKey()
    {
        var bytes = WithMeta(
            [.. BaseRecords(),
                ("module.name", 1, Encoding.UTF8.GetBytes("T")),
                ("acme.dup", 1, Encoding.UTF8.GetBytes("a")),
                ("acme.dup", 1, Encoding.UTF8.GetBytes("b"))]);

        AssertRejected(bytes, "重复");
    }

    [Fact]
    public void Reject_EmptyMetadataKey()
    {
        var bytes = WithMeta([.. BaseRecords(), ("", 1, [])]);

        AssertRejected(bytes, "KeyLength");
    }

    [Fact]
    public void Reject_ControlCharacterInKey()
    {
        var bytes = WithMeta([.. BaseRecords(), ("a\u0001b", 1, [])]);

        AssertRejected(bytes, "控制");
    }

    [Fact]
    public void Reject_MetadataKeyTooLong()
    {
        var bytes = WithMeta([.. BaseRecords(), (new string('k', 1025), 1, [])]);

        AssertRejected(bytes, "KeyLength");
    }

    [Fact]
    public void Reject_BooleanValueWithWrongLength()
    {
        var bytes = WithMeta([.. BaseRecords(), ("acme.b", 2, [1, 0])]);

        AssertRejected(bytes, "Boolean");
    }

    [Fact]
    public void Reject_Int32ValueWithWrongLength()
    {
        var bytes = WithMeta([.. BaseRecords(), ("acme.i", 3, [1, 2, 3])]);

        AssertRejected(bytes, "Int32");
    }

    [Fact]
    public void Reject_UnknownMetadataValueType()
    {
        var bytes = WithMeta([.. BaseRecords(), ("acme.x", 99, [1])]);

        AssertRejected(bytes, "ValueType");
    }

    [Fact]
    public void Reject_MissingRequiredMetadataKey()
    {
        var bytes = WithMeta(
            ("module.version", 1, Encoding.UTF8.GetBytes("1.0.0")),
            ("compiler.version", 1, Encoding.UTF8.GetBytes("test")),
            ("module.entryPC", 5, new byte[4]));

        AssertRejected(bytes, "module.name");
    }

    [Fact]
    public void Reject_RequiredMetadataKeyWithWrongType()
    {
        var bytes = WithMeta(
            ("module.name", 3, new byte[4]),
            ("module.version", 1, Encoding.UTF8.GetBytes("1.0.0")),
            ("compiler.version", 1, Encoding.UTF8.GetBytes("test")),
            ("module.entryPC", 5, new byte[4]));

        AssertRejected(bytes, "module.name");
    }

    [Fact]
    public void Reject_RequiredFeatureNotSupported()
    {
        var metadata = BaseMetadata();
        metadata["module.requiredFeatures"] = S(
            "module.requiredFeatures", "bytecode,teleportation");

        AssertRejected(STModWriter.Write(Baseline(metadata: metadata)), "teleportation");
    }

    [Fact]
    public void Reject_ValueLengthBeyondMetadataSection()
    {
        // 声明 1 MiB 但区段内没有那么多字节 —— 必须拒绝而不是按声明长度分配。
        var raw = ValidRaw();
        var payload = MetaPayload([.. BaseRecords()]);
        // 区段头 16 字节 + 记录头 12 字节后即第一条记录的 ValueLength（偏移 24）。
        BitConverter.GetBytes(1 << 20).CopyTo(payload, 24);
        raw.Section(STModSectionType.Metadata).Payload = payload;

        AssertRejected(raw.ToBytes(), "截断");
    }

    // ══════════════════════════════════════════════════════════════
    //  5. 函数索引：越界 / 重复 / 空长度 / 重叠
    // ══════════════════════════════════════════════════════════════

    private static byte[] WithFunctions(
        params (uint Id, string Name, ulong Off, ulong Len)[] entries)
    {
        var raw = ValidRaw();
        raw.Section(STModSectionType.FunctionIndex).Payload = FuncPayload(entries);
        return raw.ToBytes();
    }

    [Fact]
    public void Reject_FunctionRangeBeyondCode()
    {
        var bytes = WithFunctions((0, "f", (ulong)BaseCode.Length - 1, 8));

        AssertRejected(bytes, "越过 Code 区段");
    }

    [Fact]
    public void Reject_FunctionZeroCodeLength()
    {
        var bytes = WithFunctions((0, "f", 0, 0));

        AssertRejected(bytes, "CodeLength");
    }

    [Fact]
    public void Reject_FunctionEmptyName()
    {
        var bytes = WithFunctions((0, "", 0, 4));

        AssertRejected(bytes, "name");
    }

    [Fact]
    public void Reject_DuplicateFunctionId()
    {
        var bytes = WithFunctions((7, "a", 0, 4), (7, "b", 4, 4));

        AssertRejected(bytes, "functionId");
    }

    [Fact]
    public void Reject_DuplicateFunctionName()
    {
        var bytes = WithFunctions((0, "dup", 0, 4), (1, "dup", 4, 4));

        AssertRejected(bytes, "重复的函数名");
    }

    [Fact]
    public void Reject_OverlappingFunctionRanges()
    {
        var bytes = WithFunctions((0, "a", 0, 8), (1, "b", 4, 8));

        AssertRejected(bytes, "重叠");
    }

    [Fact]
    public void Reject_FunctionCodeSectionIdNotCodeSection()
    {
        var raw = ValidRaw();
        raw.Section(STModSectionType.FunctionIndex).Payload =
            FuncPayload([(0, "f", 0, 4)], codeSectionId: 4);

        AssertRejected(raw.ToBytes(), "CodeSectionId");
    }

    // ══════════════════════════════════════════════════════════════
    //  6. 入口点
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Reject_EntryPcBeyondCode()
    {
        var metadata = BaseMetadata();
        metadata["module.entryPC"] = U32("module.entryPC", 9999);

        AssertRejected(STModWriter.Write(Baseline(metadata: metadata)), "入口 PC");
    }

    [Fact]
    public void Reject_EntryPointDeclaredWithoutEntryPcMetadata()
    {
        var metadata = BaseMetadata();
        metadata.Remove("module.entryPC");

        AssertRejected(STModWriter.Write(Baseline(metadata: metadata)), "entryPC");
    }

    // ══════════════════════════════════════════════════════════════
    //  7. 写侧：非法模块不得产出文件
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Write_RejectsMissingRequiredMetadata()
    {
        var metadata = BaseMetadata();
        metadata.Remove("module.name");

        Assert.Throws<STModFormatException>(
            () => { _ = STModWriter.Write(Baseline(metadata: metadata)); });
    }

    [Fact]
    public void Write_RejectsFunctionRangeBeyondCode()
    {
        Assert.Throws<STModFormatException>(
            () => { _ = STModWriter.Write(Baseline(functions: [Fn("bad", 0, 4, 999)])); });
    }

    [Fact]
    public void Write_RejectsDuplicateFunctionId()
    {
        Assert.Throws<STModFormatException>(() =>
        {
            _ = STModWriter.Write(Baseline(
                functions: [Fn("a", 1, 0, 4), Fn("b", 1, 4, 4)]));
        });
    }

    [Fact]
    public void Write_RejectsEmptyFunctionName()
    {
        Assert.Throws<STModFormatException>(
            () => { _ = STModWriter.Write(Baseline(functions: [Fn("", 0, 0, 4)])); });
    }

    // ══════════════════════════════════════════════════════════════
    //  8. 宿主兼容性（不执行字节码）
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void HostCompatibility_MissingHostFunction_IsReported()
    {
        var table = new RelocationTable();
        table.Add(RelocationRecord.Create(
            RelocationKind.HostIdx, RelocationSection.Code, 3, "Print", addend: 4));

        var module = STModReader.Read(STModWriter.Write(Baseline(relocations: table)));

        var problems = STModHostCompatibility.Check(
            module, new Dictionary<string, ushort>(StringComparer.Ordinal));

        Assert.Contains(problems, p => p.Contains("Print"));
    }

    [Fact]
    public void HostCompatibility_IdMismatch_IsReported()
    {
        var table = new RelocationTable();
        table.Add(RelocationRecord.Create(
            RelocationKind.HostIdx, RelocationSection.Code, 3, "Print", addend: 4));

        var module = STModReader.Read(STModWriter.Write(Baseline(relocations: table)));

        var problems = STModHostCompatibility.Check(
            module, new Dictionary<string, ushort>(StringComparer.Ordinal) { ["Print"] = 9 });

        Assert.Contains(problems, p => p.Contains("Print") && p.Contains("不兼容"));
    }

    [Fact]
    public void HostCompatibility_MatchingEnvironment_IsClean()
    {
        var table = new RelocationTable();
        table.Add(RelocationRecord.Create(
            RelocationKind.HostIdx, RelocationSection.Code, 3, "Print", addend: 4));

        var module = STModReader.Read(STModWriter.Write(Baseline(relocations: table)));

        var problems = STModHostCompatibility.Check(
            module, new Dictionary<string, ushort>(StringComparer.Ordinal) { ["Print"] = 4 });

        Assert.Empty(problems);
    }

    // ══════════════════════════════════════════════════════════════
    //  测试自带第二实现：头 + 区段表 + 负载的可变副本
    // ══════════════════════════════════════════════════════════════

    private sealed class RawSection
    {
        public uint Id;
        public ushort Type;
        public ushort Flags;
        public uint Reserved;
        public ulong DeclaredOffset;
        public ulong? DeclaredSize;
        public uint DeclaredAlignment;
        public byte[] Payload = [];
    }

    private sealed class RawFile
    {
        public ushort Major = 1;
        public ushort Minor;
        public uint Flags = 1;
        public uint HeaderSize = 48;
        public uint Reserved0;
        public uint Reserved1;
        public ulong Reserved2;
        public ulong TableOffset = 48;
        public uint MetadataSectionId = 1;
        public List<RawSection> Sections { get; } = [];

        public RawSection Section(STModSectionType type) =>
            Sections.First(s => s.Type == (ushort)type);

        public static RawFile Parse(byte[] data)
        {
            using var br = new BinaryReader(
                new MemoryStream(data, writable: false), Encoding.UTF8, leaveOpen: true);

            var f = new RawFile();
            br.ReadBytes(4);
            f.Major = br.ReadUInt16();
            f.Minor = br.ReadUInt16();
            f.Flags = br.ReadUInt32();
            f.HeaderSize = br.ReadUInt32();
            uint count = br.ReadUInt32();
            f.Reserved0 = br.ReadUInt32();
            f.TableOffset = br.ReadUInt64();
            f.MetadataSectionId = br.ReadUInt32();
            f.Reserved1 = br.ReadUInt32();
            f.Reserved2 = br.ReadUInt64();

            br.BaseStream.Position = (long)f.TableOffset;
            for (uint i = 0; i < count; i++)
            {
                var s = new RawSection
                {
                    Id = br.ReadUInt32(),
                    Type = br.ReadUInt16(),
                    Flags = br.ReadUInt16(),
                };
                ulong offset = br.ReadUInt64();
                ulong size = br.ReadUInt64();
                s.DeclaredOffset = offset;
                s.DeclaredAlignment = br.ReadUInt32();
                s.Reserved = br.ReadUInt32();
                s.Payload = data.AsSpan((int)offset, (int)size).ToArray();
                f.Sections.Add(s);
            }

            return f;
        }

        /// <summary>
        /// 重新序列化。负载一律按紧凑布局写出（保持文件其它部分可读），
        /// 仅当 <paramref name="honorDeclared"/> 时用声明值填写区段表 ——
        /// 这样才能构造"声明的偏移/长度非法，但文件本身自洽"的样本。
        /// </summary>
        public byte[] ToBytes(bool honorDeclared = false)
        {
            var actual = new ulong[Sections.Count];
            ulong cursor = AlignUp(TableOffset + (ulong)Sections.Count * 32);
            for (int i = 0; i < Sections.Count; i++)
            {
                actual[i] = cursor;
                cursor = AlignUp(cursor + (ulong)Sections[i].Payload.Length);
            }

            ulong total = Math.Max(cursor, HeaderSize);
            using var ms = new MemoryStream((int)total);
            using var bw = new BinaryWriter(ms);

            bw.Write("STMD"u8);
            bw.Write(Major);
            bw.Write(Minor);
            bw.Write(Flags);
            bw.Write(HeaderSize);
            bw.Write((uint)Sections.Count);
            bw.Write(Reserved0);
            bw.Write(TableOffset);
            bw.Write(MetadataSectionId);
            bw.Write(Reserved1);
            bw.Write(Reserved2);

            for (int i = 0; i < Sections.Count; i++)
            {
                var s = Sections[i];
                ulong offset = honorDeclared ? s.DeclaredOffset : actual[i];
                ulong size = honorDeclared
                    ? s.DeclaredSize ?? (ulong)s.Payload.Length
                    : (ulong)s.Payload.Length;
                uint align = honorDeclared && s.DeclaredAlignment != 0 ? s.DeclaredAlignment : 8;

                bw.Write(s.Id);
                bw.Write(s.Type);
                bw.Write(s.Flags);
                bw.Write(offset);
                bw.Write(size);
                bw.Write(align);
                bw.Write(s.Reserved);
            }

            for (int i = 0; i < Sections.Count; i++)
            {
                PadTo(bw, (long)actual[i]);
                bw.Write(Sections[i].Payload);
            }
            PadTo(bw, (long)total);

            return ms.ToArray();
        }

        private static ulong AlignUp(ulong value)
        {
            ulong rem = value % 8;
            return rem == 0 ? value : value + (8 - rem);
        }

        private static void PadTo(BinaryWriter bw, long target)
        {
            while (bw.BaseStream.Position < target)
                bw.Write((byte)0);
        }
    }

    private static byte[] MetaPayload(
        params (string Key, byte Type, byte[] Value)[] records)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((uint)records.Length);
        bw.Write(0u);
        bw.Write(0u);
        bw.Write(0u);

        foreach (var (key, type, value) in records)
        {
            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            bw.Write((uint)keyBytes.Length);
            bw.Write(type);
            bw.Write((byte)0);
            bw.Write((ushort)0);
            bw.Write((uint)value.Length);
            bw.Write(keyBytes);
            bw.Write(value);
        }

        bw.Flush();
        return ms.ToArray();
    }

    private static byte[] FuncPayload(
        (uint Id, string Name, ulong Off, ulong Len)[] entries, uint codeSectionId = 3)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((uint)entries.Length);
        bw.Write(0u);
        bw.Write(0u);
        bw.Write(0u);

        foreach (var (id, name, off, len) in entries)
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(name);
            byte[] sigBytes = Encoding.UTF8.GetBytes("void ()");
            bw.Write(id);
            bw.Write(0u);                       // flags
            bw.Write(codeSectionId);
            bw.Write((uint)nameBytes.Length);
            bw.Write((uint)sigBytes.Length);
            bw.Write(0u);                       // paramCount
            bw.Write(1u);                       // localCount
            bw.Write(0u);                       // maxStack
            bw.Write(off);
            bw.Write(len);
            bw.Write(nameBytes);
            bw.Write(sigBytes);
        }

        bw.Flush();
        return ms.ToArray();
    }
}
