// STModLinkProtocolTests.cs — `.stmod` 链接协议（docs/stmod链接协议.md）的
// 独立字节级契约测试（阶段 P0）。
//
// 设计意图：本文件**不引用**产品的模块读写 API（除被断言的读取器本身），
// 而是按规范自行拼装 `.stmod` 字节。这样这些用例是规范的**第二实现**，
// 能独立发现"读取器对新增元数据 / 新增区段缺少校验"的缺口。
//
// 红灯含义：`ImportSymbols` / `ExportSymbols` 区段与 `module.linked` /
// `module.globalCount` 元数据在 v1.0 时被读取器**静默忽略**（未知可选区段 /
// 未知 Key）。本协议要求它们被解析与校验；因此下列用例在实现前
// 会因"预期异常未抛出"而失败 —— 这是真实的功能缺口，不是夹具错误。
using System.Text;
using IronST.Runtime.Shared.Modules;

namespace IronST.Runtime.Tests;

public class STModLinkProtocolTests
{
    // ═══════════════════════════════════════════════════════════
    //  独立字节级构造器（第二实现）
    // ═══════════════════════════════════════════════════════════

    private readonly record struct RawSection(
        uint Id, ushort Type, ushort Flags, byte[] Payload, uint Alignment = 8);

    private static byte[] BuildMetadata(IEnumerable<(string Key, byte Type, byte[] Value)> records)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        var list = records.ToList();
        bw.Write((uint)list.Count);
        bw.Write(0u);
        bw.Write(0u);
        bw.Write(0u);
        foreach (var (key, type, value) in list)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
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

    private static byte[] Str(string s) => Encoding.UTF8.GetBytes(s);

    private static byte[] U32(uint v) => BitConverter.GetBytes(v);

    private static List<(string Key, byte Type, byte[] Value)> RequiredMetadataRecords() =>
    [
        ("module.name", 1, Str("TestMod")),
        ("module.version", 1, Str("1.0.0")),
        ("compiler.version", 1, Str("0.0.0")),
    ];

    /// <summary>一条构成合法的单函数模块（FRAME 0; LEAVE; RET）。</summary>
    private static byte[] BuildFunctionIndex()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        var name = Str("Trivial");
        var sig = Str("VOID ()");
        bw.Write(1u);
        bw.Write(0u);
        bw.Write(0u);
        bw.Write(0u);
        bw.Write(0u);            // FunctionId
        bw.Write(0u);            // Flags
        bw.Write(3u);            // CodeSectionId
        bw.Write((uint)name.Length);
        bw.Write((uint)sig.Length);
        bw.Write(0u);            // ParamCount
        bw.Write(0u);            // LocalCount
        bw.Write(0u);            // MaxStack
        bw.Write(0ul);           // CodeOffset
        bw.Write(5ul);           // CodeLength
        bw.Write(name);
        bw.Write(sig);
        bw.Flush();
        return ms.ToArray();
    }

    private static byte[] BuildCode() => [0x24, 0x00, 0x00, 0x25, 0x80];

    private static byte[] BuildConstantPool()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(0u);
        bw.Write(0u);
        bw.Flush();
        return ms.ToArray();
    }

    private static byte[] BuildFile(
        IEnumerable<(string Key, byte Type, byte[] Value)> metadata,
        params RawSection[] extraSections)
    {
        var sections = new List<RawSection>
        {
            new(1, 1, 1, BuildMetadata(metadata)),
            new(2, 2, 1, BuildFunctionIndex()),
            new(3, 3, 1, BuildCode()),
            new(4, 4, 1, BuildConstantPool()),
        };
        sections.AddRange(extraSections);

        uint headerSize = 48;
        ulong tableOffset = headerSize;
        ulong tableBytes = (ulong)sections.Count * 32;

        var offsets = new ulong[sections.Count];
        ulong cursor = tableOffset + tableBytes;
        cursor = (cursor + 7) / 8 * 8;
        for (int i = 0; i < sections.Count; i++)
        {
            offsets[i] = cursor;
            cursor = (cursor + (ulong)sections[i].Payload.Length + 7) / 8 * 8;
        }

        using var outMs = new MemoryStream();
        using var bw = new BinaryWriter(outMs);
        bw.Write(Encoding.ASCII.GetBytes("STMD"));
        bw.Write((ushort)1);
        bw.Write((ushort)0);
        bw.Write(0u);                       // header flags
        bw.Write(headerSize);
        bw.Write((uint)sections.Count);
        bw.Write(0u);
        bw.Write(tableOffset);
        bw.Write(1u);                       // MetadataSectionId
        bw.Write(0u);
        bw.Write(0ul);
        for (int i = 0; i < sections.Count; i++)
        {
            bw.Write(sections[i].Id);
            bw.Write(sections[i].Type);
            bw.Write(sections[i].Flags);
            bw.Write(offsets[i]);
            bw.Write((ulong)sections[i].Payload.Length);
            bw.Write(sections[i].Alignment);
            bw.Write(0u);
        }
        for (int i = 0; i < sections.Count; i++)
        {
            while ((ulong)outMs.Position < offsets[i])
                outMs.WriteByte(0);
            bw.Write(sections[i].Payload);
        }
        bw.Flush();
        return outMs.ToArray();
    }

    // ── ImportSymbols / ExportSymbols 记录编码（规范 §4 / §5）─────────

    private static byte[] ImportPayload(params (string Name, string Sig, ushort Kind)[] records)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((uint)records.Length);
        bw.Write(0u);
        foreach (var (name, sig, kind) in records)
        {
            var n = Str(name);
            var s = Str(sig);
            bw.Write((uint)n.Length);
            bw.Write((uint)s.Length);
            bw.Write(kind);
            bw.Write((ushort)0);
            bw.Write(n);
            bw.Write(s);
        }
        bw.Flush();
        return ms.ToArray();
    }

    private static byte[] ExportPayload(params (uint FuncId, string Name, string Sig)[] records)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((uint)records.Length);
        bw.Write(0u);
        foreach (var (id, name, sig) in records)
        {
            var n = Str(name);
            var s = Str(sig);
            bw.Write(id);
            bw.Write((uint)n.Length);
            bw.Write((uint)s.Length);
            bw.Write((ushort)1);
            bw.Write((ushort)1);
            bw.Write(n);
            bw.Write(s);
        }
        bw.Flush();
        return ms.ToArray();
    }

    // ═══════════════════════════════════════════════════════════
    //  新元数据 Key 的类型校验
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Metadata_ModuleLinked_MustBeBoolean()
    {
        var meta = RequiredMetadataRecords();
        meta.Add(("module.linked", 3, U32(1)));   // Int32 —— 非法

        var ex = Assert.Throws<STModFormatException>(() => STModReader.Read(BuildFile(meta)));
        Assert.Contains("module.linked", ex.Message);
    }

    [Fact]
    public void Metadata_GlobalCount_MustBeUInt32()
    {
        var meta = RequiredMetadataRecords();
        meta.Add(("module.globalCount", 1, Str("4")));   // String —— 非法

        var ex = Assert.Throws<STModFormatException>(() => STModReader.Read(BuildFile(meta)));
        Assert.Contains("module.globalCount", ex.Message);
    }

    [Fact]
    public void Metadata_Boolean_OversizedValue_IsRejected()
    {
        var meta = RequiredMetadataRecords();
        meta.Add(("module.linked", 2, [0x00, 0x00]));   // Boolean 长度必须为 1

        var ex = Assert.Throws<STModFormatException>(() => STModReader.Read(BuildFile(meta)));
        Assert.Contains("module.linked", ex.Message);
    }

    // ═══════════════════════════════════════════════════════════
    //  ImportSymbols 区段校验（类型 5，可选）
    // ═══════════════════════════════════════════════════════════

    private static byte[] WithImport(byte[] payload) =>
        BuildFile(RequiredMetadataRecords(), new RawSection(5, 5, 0, payload));

    [Fact]
    public void ImportSymbols_TruncatedPayload_IsRejected()
    {
        // Count=1 但没有记录负载 → 必须拒绝（不得按声明长度分配内存）。
        var ex = Assert.Throws<STModFormatException>(() => STModReader.Read(WithImport(U32(1))));
        Assert.Contains(".stmod", ex.Message);
    }

    [Fact]
    public void ImportSymbols_DuplicateSymbolName_IsRejected()
    {
        var payload = ImportPayload(
            ("Add", "DINT (DINT, DINT)", 1),
            ("Add", "DINT (DINT, DINT)", 1));

        var ex = Assert.Throws<STModFormatException>(() => STModReader.Read(WithImport(payload)));
        Assert.Contains("Add", ex.Message);
    }

    [Fact]
    public void ImportSymbols_EmptyName_IsRejected()
    {
        var payload = ImportPayload(("", "DINT ()", 1));

        var ex = Assert.Throws<STModFormatException>(() => STModReader.Read(WithImport(payload)));
        Assert.Contains(".stmod", ex.Message);
    }

    [Fact]
    public void ImportSymbols_UnknownKind_IsRejected()
    {
        var payload = ImportPayload(("Weird", "DINT ()", 9));

        var ex = Assert.Throws<STModFormatException>(() => STModReader.Read(WithImport(payload)));
        Assert.Contains(".stmod", ex.Message);
    }

    // ═══════════════════════════════════════════════════════════
    //  ExportSymbols 区段校验（类型 6，可选）
    // ═══════════════════════════════════════════════════════════

    private static byte[] WithExport(byte[] payload) =>
        BuildFile(RequiredMetadataRecords(), new RawSection(6, 6, 0, payload));

    [Fact]
    public void ExportSymbols_TruncatedPayload_IsRejected()
    {
        var ex = Assert.Throws<STModFormatException>(() => STModReader.Read(WithExport(U32(1))));
        Assert.Contains(".stmod", ex.Message);
    }

    [Fact]
    public void ExportSymbols_UnknownFunctionId_IsRejected()
    {
        // FunctionId 99 不在 FunctionIndex（只有 id 0）中 → 必须拒绝。
        var payload = ExportPayload((99u, "Add", "DINT (DINT, DINT)"));

        var ex = Assert.Throws<STModFormatException>(() => STModReader.Read(WithExport(payload)));
        Assert.Contains(".stmod", ex.Message);
    }

    // ═══════════════════════════════════════════════════════════
    //  正常路径：合法的新区段必须被接受（并保留语义）
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void WellFormed_ImportAndExport_AreAccepted()
    {
        var file = BuildFile(
            RequiredMetadataRecords(),
            new RawSection(5, 5, 0, ImportPayload(("Add", "DINT (DINT, DINT)", 1))),
            new RawSection(6, 6, 0, ExportPayload((0u, "Trivial", "VOID ()"))));

        var module = STModReader.Read(file);

        Assert.NotNull(module);
        Assert.Equal(1, module.FormatVersionMajor);
        Assert.Single(module.Functions);
    }

    [Fact]
    public void Metadata_NewKeys_RoundTripThroughReader()
    {
        var meta = RequiredMetadataRecords();
        meta.Add(("module.linked", 2, [0x00]));
        meta.Add(("module.globalCount", 5, U32(7)));

        var module = STModReader.Read(BuildFile(meta));

        Assert.False(module.Metadata["module.linked"].AsBoolean());
        Assert.Equal(7u, module.Metadata["module.globalCount"].AsUInt32());
    }
}
