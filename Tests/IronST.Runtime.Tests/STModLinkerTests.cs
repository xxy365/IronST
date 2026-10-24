// STModLinkerTests.cs — P4：链接器的**负路径与合并语义**（不经过 CLI，直接驱动 STModLinker）。
//
// 覆盖：
//   · 未注册宿主函数 → 拒绝（绝不造假实现）；
//   · 不支持的重定位类型（StructTypeId）→ 拒绝；
//   · 无任何目标可解析的 Addr32 → 拒绝；
//   · 导入/签名不一致 → 拒绝；
//   · 重复定义 → 拒绝并列出模块；入口歧义 → 明确报"歧义"；
//   · 全局槽位 / 常量池的真实重定基（检查回填后的字节）。
using System.Buffers.Binary;
using IronST.Runtime.Shared;
using IronST.Runtime.Shared.Modules;

namespace IronST.Runtime.Tests;

public class STModLinkerTests
{
    private static STModFunctionEntry Fn(
        uint id,
        string name,
        string signature = "void ()",
        ulong offset = 0,
        ulong length = 8,
        STModFunctionFlags flags = STModFunctionFlags.None) => new()
    {
        FunctionId = id,
        Name = name,
        Signature = signature,
        Flags = flags,
        CodeOffset = offset,
        CodeLength = length,
    };

    private static STModModule Module(
        string name,
        IReadOnlyList<STModFunctionEntry> functions,
        byte[]? code = null,
        RelocationTable? relocations = null,
        IReadOnlyList<STModSymbol>? imports = null,
        IReadOnlyList<BcValue>? pool = null,
        int globalCount = 0) => new()
    {
        Metadata = new Dictionary<string, STModMetadataEntry>(StringComparer.Ordinal)
        {
            [STModMetadataKeys.ModuleName] =
                STModMetadataEntry.FromString(STModMetadataKeys.ModuleName, name),
            [STModMetadataKeys.ModuleVersion] =
                STModMetadataEntry.FromString(STModMetadataKeys.ModuleVersion, "1.0.0"),
            [STModMetadataKeys.CompilerVersion] =
                STModMetadataEntry.FromString(STModMetadataKeys.CompilerVersion, "1.0.0.0"),
        },
        Functions = functions,
        Code = code ?? new byte[16],
        ConstantPool = pool ?? [],
        Sections = [],
        HasEntryPoint = false,
        EntryPC = 0,
        Relocations = relocations,
        Imports = imports ?? [],
        Exports = [],
        IsLinked = true,
        GlobalCount = globalCount,
        FormatVersionMajor = STModFormat.FormatVersionMajor,
        FormatVersionMinor = STModFormat.FormatVersionMinor,
    };

    // ── 拒绝路径 ────────────────────────────────────────────────

    [Fact]
    public void Link_UnknownHostFunction_IsRejected()
    {
        var table = new RelocationTable();
        table.Add(RelocationRecord.Create(
            RelocationKind.HostIdx, RelocationSection.Code, 0, "Ghost", addend: 0));

        var module = Module("A", [Fn(0, "Main", length: 16)], relocations: table);

        var ex = Assert.Throws<STModLinkException>(() => STModLinker.Link(
            [module],
            new STModLinkOptions
            {
                HostFunctions = new Dictionary<string, ushort>(StringComparer.Ordinal),
            }));

        Assert.Contains("Ghost", ex.Message);
    }

    [Fact]
    public void Link_UnsupportedRelocationKind_IsRejected()
    {
        var table = new RelocationTable();
        table.Add(RelocationRecord.Create(
            RelocationKind.StructTypeId, RelocationSection.Code, 0, "SomeStruct", addend: 1));

        var module = Module("A", [Fn(0, "Main", length: 16)], relocations: table);

        var ex = Assert.Throws<STModLinkException>(() => STModLinker.Link([module]));

        Assert.Contains("不支持的重定位", ex.Message);
    }

    [Fact]
    public void Link_Add32WithNoResolvableTarget_IsRejected()
    {
        var table = new RelocationTable();
        table.Add(RelocationRecord.Create(
            RelocationKind.Addr32, RelocationSection.Code, 0, "Nowhere", addend: 0));

        var module = Module("A", [Fn(0, "Main", length: 16)], relocations: table);

        var ex = Assert.Throws<STModLinkException>(() => STModLinker.Link([module]));

        Assert.Contains("Nowhere", ex.Message);
    }

    [Fact]
    public void Link_ImportWithoutProvider_IsRejected()
    {
        var consumer = Module(
            "C",
            [Fn(0, "Main")],
            imports: [new STModSymbol { Name = "Add", Signature = "i32 (i32, i32)" }]);

        var ex = Assert.Throws<STModLinkException>(() => STModLinker.Link([consumer]));

        Assert.Contains("未解析的外部引用", ex.Message);
        Assert.Contains("Add", ex.Message);
    }

    [Fact]
    public void Link_SignatureMismatch_IsRejected()
    {
        var provider = Module("P", [Fn(0, "Add", signature: "i32 (i32, i32)")]);
        var consumer = Module(
            "C",
            [Fn(0, "Main")],
            imports: [new STModSymbol { Name = "Add", Signature = "i32 (float, float)" }]);

        var ex = Assert.Throws<STModLinkException>(() => STModLinker.Link([consumer, provider]));

        Assert.Contains("签名不一致", ex.Message);
    }

    [Fact]
    public void Link_DuplicateDefinition_IsRejected_ListingModules()
    {
        var a = Module("ModA", [Fn(0, "Dup")]);
        var b = Module("ModB", [Fn(0, "Dup")]);

        var ex = Assert.Throws<STModLinkException>(() => STModLinker.Link([a, b]));

        Assert.Contains("重复定义", ex.Message);
        Assert.Contains("Dup", ex.Message);
        Assert.Contains("ModA", ex.Message);
        Assert.Contains("ModB", ex.Message);
    }

    [Fact]
    public void Link_AmbiguousExplicitEntry_IsReportedAsAmbiguity_WithCandidates()
    {
        var a = Module("ModA", [Fn(0, "Dup")]);
        var b = Module("ModB", [Fn(0, "Dup")]);

        var ex = Assert.Throws<STModLinkException>(() => STModLinker.Link(
            [a, b], new STModLinkOptions { EntryName = "Dup" }));

        Assert.Contains("歧义", ex.Message);
        Assert.Contains("ModA:Dup", ex.Message);
        Assert.Contains("ModB:Dup", ex.Message);
    }

    [Fact]
    public void Link_EntryThatIsOnlyDeclaration_IsRejected()
    {
        var hostOnly = Module(
            "H",
            [Fn(0, "Main"), Fn(1, "Print", offset: 8, flags: STModFunctionFlags.Declaration)]);

        var ex = Assert.Throws<STModLinkException>(() => STModLinker.Link(
            [hostOnly], new STModLinkOptions { EntryName = "Print" }));

        Assert.Contains("Print", ex.Message);
        Assert.Contains("声明", ex.Message);
    }

    // ── 合并语义（检查回填字节）────────────────────────────────

    [Fact]
    public void Link_GlobalSlots_AreRebasedPerModule()
    {
        var a = Module("A", [Fn(0, "Fa", length: 4)], code: new byte[4], globalCount: 1);

        var tableB = new RelocationTable();
        tableB.Add(RelocationRecord.Create(
            RelocationKind.GlobalIdx, RelocationSection.Code, 0, "y", addend: 0));
        var b = Module(
            "B", [Fn(0, "Fb", length: 4)], code: new byte[4],
            relocations: tableB, globalCount: 2);

        var linked = STModLinker.Link([a, b]);

        Assert.Equal(3, linked.GlobalCount);
        // B 的代码基址为 8（A 占 4，按 8 对齐 → 8）。
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(linked.Code.AsSpan(8, 2)));
    }

    [Fact]
    public void Link_GlobalSlots_OverLimit_IsRejected()
    {
        var a = Module("A", [Fn(0, "Fa")], globalCount: 40000);
        var b = Module("B", [Fn(0, "Fb")], globalCount: 40000);

        var ex = Assert.Throws<STModLinkException>(() => STModLinker.Link([a, b]));

        Assert.Contains("全局槽位", ex.Message);
    }

    [Fact]
    public void Link_ConstantPool_IsConcatenatedAndRebased()
    {
        var a = Module(
            "A", [Fn(0, "Fa", length: 8)], code: new byte[8],
            pool: [new BcValue(10L)]);

        var tableB = new RelocationTable();
        tableB.Add(RelocationRecord.Create(
            RelocationKind.ConstIdx, RelocationSection.Code, 0, null, addend: 1));
        var b = Module(
            "B", [Fn(0, "Fb", length: 8)], code: new byte[8],
            relocations: tableB, pool: [new BcValue(20L), new BcValue(30L)]);

        var linked = STModLinker.Link([a, b]);

        Assert.Equal(3, linked.ConstantPool.Count);
        // B 的局部常量 1 → 合并后 1 + 1 = 2。
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(linked.Code.AsSpan(8, 4)));
    }

    [Fact]
    public void Link_IsLinkedAndHasNoImports()
    {
        var provider = Module("P", [Fn(0, "Add", signature: "i32 (i32, i32)")]);
        var consumer = Module(
            "C",
            [Fn(0, "Main")],
            imports: [new STModSymbol { Name = "Add", Signature = "i32 (i32, i32)" }]);

        var linked = STModLinker.Link([consumer, provider]);

        Assert.True(linked.IsLinked);
        Assert.Empty(linked.Imports);
        Assert.True(linked.HasEntryPoint == false || linked.EntryPC < (uint)linked.Code.Length);
    }
}
