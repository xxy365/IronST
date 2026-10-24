// CliModTests.cs — `.stmod` 模块格式的 CLI 端到端测试（黑盒 + 字节级独立解析）。
//
// 设计意图：本文件**不引用** IronST 的模块读写 API，而是按《docs/stmod格式规范.md》
// 自行用 BinaryReader 解析产出的 .stmod。这样测试是格式规范的**第二实现**，
// 能独立发现读写器与规范不一致（而不是"读写器自测自"）。
//
// 覆盖：build 产物结构、必需区段、必需元数据、真实函数索引（FRAME/LEAVE/RET 边界）、
//       inspect 展示且不执行、.stmod 不可直接 run、非法输入拒绝、既有工作流回归。
using System.Text;
using IronST.CLI.Commands;

namespace IronST.CLI.Tests;

[Collection("cli-console")]
public class CliModTests : IDisposable
{
    private readonly string _dir;

    public CliModTests()
    {
        _dir = Path.Combine(
            Path.GetTempPath(), "ironst-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // 清理失败不影响用例结论
        }
    }

    // ── 基础设施 ──────────────────────────────────────────────

    private string WriteIn(string relativePath, string content)
    {
        var path = Path.Combine(_dir, relativePath);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, content);
        return path;
    }

    private static (int Exit, string Out, string Err) Invoke(params string[] args)
    {
        var outW = new StringWriter();
        var errW = new StringWriter();
        var prevOut = Console.Out;
        var prevErr = Console.Error;
        try
        {
            Console.SetOut(outW);
            Console.SetError(errW);
            int exit = CliRouter.Run(args);
            return (exit, outW.ToString(), errW.ToString());
        }
        finally
        {
            Console.SetOut(prevOut);
            Console.SetError(prevErr);
        }
    }

    // ── 源文件片段 ────────────────────────────────────────────

    private const string MathSt = """
        FUNCTION Add : DINT
        VAR_INPUT a : DINT; b : DINT; END_VAR
        Add := a + b;
        END_FUNCTION
        """;

    private const string HelperSt = """
        FUNCTION Mul : DINT
        VAR_INPUT a : DINT; b : DINT; END_VAR
        Mul := a * b;
        END_FUNCTION
        """;

    private const string MainSt = """
        PROGRAM Main
        VAR x : DINT; END_VAR
            x := Add(2, 3);
            x := Mul(x, 2);
            Print(x);
        END_PROGRAM
        """;

    private readonly record struct Section(
        uint Id, ushort Type, ushort Flags, ulong Offset, ulong Size, uint Alignment);

    private readonly record struct FuncEntry(
        uint FunctionId, uint Flags, uint CodeSectionId, uint ParamCount, uint LocalCount,
        ulong CodeOffset, ulong CodeLength, string Name, string Signature);

    /// <summary>
    /// 按规范独立解析 .stmod（测试自带的第二实现）。
    /// 只解析本任务断言所需的字段，故意不复用产品代码。
    /// </summary>
    private sealed class Probe
    {
        public required byte[] Bytes;
        public ushort Major, Minor;
        public uint Flags, HeaderSize, SectionCount, Reserved0, MetadataSectionId;
        public ulong SectionTableOffset;
        public required List<Section> Sections;
        public required Dictionary<string, (byte Type, byte[] Value)> Metadata;
        public required List<FuncEntry> Functions;
        public required byte[] Code;
        public uint ConstCount;
        public uint EntryPC;
        public bool HasEntryPC;

        public Section SectionOf(ushort type) =>
            Sections.Single(s => s.Type == type);

        public Section? MaybeSection(ushort type) =>
            Sections.Where(s => s.Type == type).Select(s => (Section?)s).FirstOrDefault();

        public string MetaString(string key) =>
            Encoding.UTF8.GetString(Metadata[key].Value);

        public static Probe Load(string path)
        {
            var bytes = File.ReadAllBytes(path);
            using var br = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8, leaveOpen: true);

            var magic = br.ReadBytes(4);
            if (Encoding.ASCII.GetString(magic) != "STMD")
                throw new InvalidDataException("bad magic");

            var p = new Probe
            {
                Bytes = bytes,
                Major = br.ReadUInt16(),
                Minor = br.ReadUInt16(),
                Flags = br.ReadUInt32(),
                HeaderSize = br.ReadUInt32(),
                SectionCount = br.ReadUInt32(),
                Reserved0 = br.ReadUInt32(),
                SectionTableOffset = br.ReadUInt64(),
                MetadataSectionId = br.ReadUInt32(),
                Sections = new(),
                Metadata = new(),
                Functions = new(),
                Code = Array.Empty<byte>(),
            };

            br.BaseStream.Position = (long)p.SectionTableOffset;
            for (int i = 0; i < p.SectionCount; i++)
            {
                uint id = br.ReadUInt32();
                ushort type = br.ReadUInt16();
                ushort flags = br.ReadUInt16();
                ulong offset = br.ReadUInt64();
                ulong size = br.ReadUInt64();
                uint align = br.ReadUInt32();
                uint reserved = br.ReadUInt32();
                if (reserved != 0)
                    throw new InvalidDataException("section reserved != 0");
                p.Sections.Add(new Section(id, type, flags, offset, size, align));
            }

            // ── Metadata ──
            var meta = p.SectionOf(1);
            br.BaseStream.Position = (long)meta.Offset;
            uint recCount = br.ReadUInt32();
            br.ReadUInt32(); br.ReadUInt32(); br.ReadUInt32();
            for (int i = 0; i < recCount; i++)
            {
                uint keyLen = br.ReadUInt32();
                byte vtype = br.ReadByte();
                byte rflags = br.ReadByte();
                ushort rres = br.ReadUInt16();
                uint valLen = br.ReadUInt32();
                if (rflags != 0 || rres != 0)
                    throw new InvalidDataException("metadata record reserved != 0");
                var key = Encoding.UTF8.GetString(br.ReadBytes((int)keyLen));
                var val = br.ReadBytes((int)valLen);
                p.Metadata.Add(key, (vtype, val));
            }

            // ── FunctionIndex ──
            var fi = p.SectionOf(2);
            br.BaseStream.Position = (long)fi.Offset;
            uint fCount = br.ReadUInt32();
            br.ReadUInt32(); br.ReadUInt32(); br.ReadUInt32();
            for (int i = 0; i < fCount; i++)
            {
                uint id = br.ReadUInt32();
                uint flags = br.ReadUInt32();
                uint codeSecId = br.ReadUInt32();
                uint nameLen = br.ReadUInt32();
                uint sigLen = br.ReadUInt32();
                uint paramCount = br.ReadUInt32();
                uint localCount = br.ReadUInt32();
                uint maxStack = br.ReadUInt32();
                ulong codeOffset = br.ReadUInt64();
                ulong codeLength = br.ReadUInt64();
                _ = maxStack;
                var name = Encoding.UTF8.GetString(br.ReadBytes((int)nameLen));
                var sig = Encoding.UTF8.GetString(br.ReadBytes((int)sigLen));
                p.Functions.Add(new FuncEntry(
                    id, flags, codeSecId, paramCount, localCount,
                    codeOffset, codeLength, name, sig));
            }

            // ── Code ──
            var codeSec = p.SectionOf(3);
            p.Code = bytes.AsSpan((int)codeSec.Offset, (int)codeSec.Size).ToArray();

            // ── ConstantPool ──
            var cpSec = p.SectionOf(4);
            br.BaseStream.Position = (long)cpSec.Offset;
            p.ConstCount = br.ReadUInt32();

            if (p.Metadata.TryGetValue("module.entryPC", out var ep) && ep.Type == 5)
                p.EntryPC = BitConverter.ToUInt32(ep.Value, 0);
            p.HasEntryPC = (p.Flags & 1u) != 0;

            return p;
        }
    }

    // ── 断言辅助 ──────────────────────────────────────────────

    private static void AssertNoOverlap(List<Section> sections)
    {
        var ordered = sections.OrderBy(s => s.Offset).ToList();
        for (int i = 1; i < ordered.Count; i++)
        {
            ulong prevEnd = ordered[i - 1].Offset + ordered[i - 1].Size;
            Assert.True(prevEnd <= ordered[i].Offset,
                $"区段重叠：0x{ordered[i - 1].Offset:X}..0x{prevEnd:X} 与 0x{ordered[i].Offset:X}");
        }
    }

    private static void AssertFunctionPointsAtRealCode(Probe p, FuncEntry f)
    {
        var codeSec = p.SectionOf(3);
        Assert.True(f.CodeLength > 0, $"{f.Name}: CodeLength 必须 > 0");
        Assert.True(f.CodeOffset + f.CodeLength <= codeSec.Size,
            $"{f.Name}: 0x{f.CodeOffset:X}+0x{f.CodeLength:X} 越过 Code 区段 0x{codeSec.Size:X}");

        int start = (int)f.CodeOffset;
        int end = start + (int)f.CodeLength;

        // 真实字节码布局：FRAME(0x24) 起始，LEAVE(0x25) + RET(0x80) 收尾。
        Assert.Equal(0x24, p.Code[start]);
        Assert.Equal(0x25, p.Code[end - 2]);
        Assert.Equal(0x80, p.Code[end - 1]);
    }

    // ═══════════════════════════════════════════════════════════
    //  1. build 产物：魔数 / 头字段 / 区段表
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ModBuild_ProducesFileWithStmodMagicAndHeader()
    {
        var main = WriteIn("Main.st", MainSt);
        var math = WriteIn("Math.st", MathSt);
        var helper = WriteIn("Helper.st", HelperSt);
        var outFile = Path.Combine(_dir, "app.stmod");

        var (exit, @out, err) = Invoke(
            "mod", "build", main, math, helper, "--entry", "Main", "-o", outFile);

        Assert.Equal(0, exit);
        Assert.Equal("", err);
        Assert.True(File.Exists(outFile));
        Assert.Contains(".stmod", @out);

        var p = Probe.Load(outFile);
        Assert.Equal(1, p.Major);
        Assert.Equal(48u, p.HeaderSize);
        Assert.Equal(p.Sections.Count, (int)p.SectionCount);
        Assert.True(p.SectionTableOffset >= p.HeaderSize);
        Assert.True(p.SectionTableOffset + (ulong)p.SectionCount * 32 <= (ulong)p.Bytes.Length);

        // 区段同时满足：8 字节对齐、不越界、互不重叠、不压住头部与区段表。
        foreach (var s in p.Sections)
        {
            Assert.True(s.Alignment >= 1 && (s.Alignment & (s.Alignment - 1)) == 0);
            Assert.Equal(0ul, s.Offset % s.Alignment);
            Assert.True(s.Offset >= p.HeaderSize);
            Assert.True(s.Offset + s.Size <= (ulong)p.Bytes.Length);
        }
        AssertNoOverlap(p.Sections);
    }

    [Fact]
    public void ModBuild_EmitsAllRequiredSections()
    {
        var main = WriteIn("Main.st", MainSt);
        var math = WriteIn("Math.st", MathSt);
        var helper = WriteIn("Helper.st", HelperSt);
        var outFile = Path.Combine(_dir, "sections.stmod");

        var (exit, _, err) = Invoke(
            "mod", "build", main, math, helper, "--entry", "Main", "-o", outFile);
        Assert.Equal(0, exit);
        Assert.Equal("", err);

        var p = Probe.Load(outFile);

        // Metadata=1, FunctionIndex=2, Code=3, ConstantPool=4 —— 必需区段各恰好一个。
        foreach (var type in new ushort[] { 1, 2, 3, 4 })
        {
            Assert.Single(p.Sections, s => s.Type == type);
        }

        // 头部指向的元数据区段必须与类型 1 的区段一致。
        var meta = p.SectionOf(1);
        Assert.Equal(meta.Id, p.MetadataSectionId);
    }

    // ═══════════════════════════════════════════════════════════
    //  2. 元数据
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ModBuild_EmitsRequiredMetadata()
    {
        var main = WriteIn("Main.st", MainSt);
        var math = WriteIn("Math.st", MathSt);
        var helper = WriteIn("Helper.st", HelperSt);
        var outFile = Path.Combine(_dir, "meta.stmod");

        var (exit, _, _) = Invoke(
            "mod", "build", main, math, helper, "--entry", "Main", "-o", outFile);
        Assert.Equal(0, exit);

        var p = Probe.Load(outFile);

        foreach (var key in new[] { "module.name", "module.version", "compiler.version", "compiler.target" })
        {
            Assert.True(p.Metadata.ContainsKey(key), $"缺少必需元数据 '{key}'");
        }

        // 值类型 1 = String。
        Assert.Equal(1, p.Metadata["module.name"].Type);
        Assert.Equal(1, p.Metadata["module.version"].Type);
        Assert.Equal(1, p.Metadata["compiler.version"].Type);
        Assert.False(string.IsNullOrWhiteSpace(p.MetaString("module.name")));

        // 入口信息：header Flags bit0 = 有入口，且 module.entryPC 指向 Code 内。
        Assert.True(p.HasEntryPC);
        Assert.True(p.EntryPC < p.SectionOf(3).Size);
    }

    [Fact]
    public void ModBuild_CustomMetadata_IsWrittenAndInspectable()
    {
        var main = WriteIn("Main.st", MainSt);
        var math = WriteIn("Math.st", MathSt);
        var helper = WriteIn("Helper.st", HelperSt);
        var outFile = Path.Combine(_dir, "custom-meta.stmod");

        var (exit, _, err) = Invoke(
            "mod", "build", main, math, helper, "--entry", "Main",
            "--meta", "mykey=myvalue", "--author", "Alice", "-o", outFile);

        Assert.Equal(0, exit);
        Assert.Equal("", err);

        var p = Probe.Load(outFile);
        Assert.Equal("myvalue", p.MetaString("mykey"));
        Assert.Equal("Alice", p.MetaString("module.author"));
        Assert.Equal(1, p.Metadata["mykey"].Type);
        Assert.Equal(1, p.Metadata["module.author"].Type);

        var (inspectExit, inspectOut, inspectErr) = Invoke("mod", "inspect", outFile);
        Assert.Equal(0, inspectExit);
        Assert.Equal("", inspectErr);
        Assert.Contains("mykey", inspectOut);
        Assert.Contains("myvalue", inspectOut);
        Assert.Contains("module.author", inspectOut);
        Assert.Contains("Alice", inspectOut);
    }

    [Fact]
    public void ModBuild_RejectsReservedMetadataKey()
    {
        var main = WriteIn("Main.st", MainSt);
        var outFile = Path.Combine(_dir, "reserved-meta.stmod");

        var (exit, _, err) = Invoke(
            "mod", "build", main, "--entry", "Main",
            "--meta", "module.name=override", "-o", outFile);

        Assert.Equal(1, exit);
        Assert.Contains("保留元数据", err);
        Assert.Contains("module.name", err);
    }

    // ═══════════════════════════════════════════════════════════
    //  3. 真实函数索引（核心验收点）
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ModBuild_FunctionIndexPointsAtRealBytecode()
    {
        var main = WriteIn("Main.st", MainSt);
        var math = WriteIn("Math.st", MathSt);
        var helper = WriteIn("Helper.st", HelperSt);
        var outFile = Path.Combine(_dir, "funcs.stmod");

        var (exit, _, err) = Invoke(
            "mod", "build", main, math, helper, "--entry", "Main", "-o", outFile);
        Assert.Equal(0, exit);
        Assert.Equal("", err);

        var p = Probe.Load(outFile);
        Assert.NotEmpty(p.Functions);

        foreach (var f in p.Functions)
        {
            AssertFunctionPointsAtRealCode(p, f);
            Assert.Equal(p.SectionOf(3).Id, f.CodeSectionId);
        }

        // 同一模块内的函数区间互不重叠。
        var ordered = p.Functions.OrderBy(f => f.CodeOffset).ToList();
        for (int i = 1; i < ordered.Count; i++)
        {
            Assert.True(ordered[i - 1].CodeOffset + ordered[i - 1].CodeLength <= ordered[i].CodeOffset,
                $"{ordered[i - 1].Name} 与 {ordered[i].Name} 的代码区间重叠");
        }

        // 名字唯一、functionId 唯一。
        Assert.Equal(p.Functions.Count, p.Functions.Select(f => f.Name).Distinct().Count());
        Assert.Equal(p.Functions.Count, p.Functions.Select(f => f.FunctionId).Distinct().Count());
    }

    [Fact]
    public void ModBuild_MultipleUserFunctions_AllLocated()
    {
        var main = WriteIn("Main.st", MainSt);
        var math = WriteIn("Math.st", MathSt);
        var helper = WriteIn("Helper.st", HelperSt);
        var outFile = Path.Combine(_dir, "multi.stmod");

        var (exit, _, _) = Invoke(
            "mod", "build", main, math, helper, "--entry", "Main", "-o", outFile);
        Assert.Equal(0, exit);

        var p = Probe.Load(outFile);

        // 用户函数必须都在索引里，且各自指向不同区间。
        foreach (var name in new[] { "Main", "Add", "Mul" })
        {
            var f = p.Functions.Single(x => x.Name == name);
            AssertFunctionPointsAtRealCode(p, f);
        }

        var starts = new[] { "Main", "Add", "Mul" }
            .Select(n => p.Functions.Single(x => x.Name == n).CodeOffset)
            .ToList();
        Assert.Equal(starts.Count, starts.Distinct().Count());

        // 参数/局部变量计数来自真实发射（Add/Mul 各 2 个参数）。
        Assert.Equal(2u, p.Functions.Single(x => x.Name == "Add").ParamCount);
        Assert.Equal(2u, p.Functions.Single(x => x.Name == "Mul").ParamCount);
    }

    // ═══════════════════════════════════════════════════════════
    //  4. inspect：展示且不执行
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ModInspect_ShowsMetadataSectionsAndFunctions()
    {
        var main = WriteIn("Main.st", MainSt);
        var math = WriteIn("Math.st", MathSt);
        var helper = WriteIn("Helper.st", HelperSt);
        var outFile = Path.Combine(_dir, "show.stmod");

        var (bExit, _, _) = Invoke(
            "mod", "build", main, math, helper, "--entry", "Main", "-o", outFile);
        Assert.Equal(0, bExit);

        var (exit, @out, err) = Invoke("mod", "inspect", outFile);

        Assert.Equal(0, exit);
        Assert.Equal("", err);
        Assert.Contains("module.name", @out);
        Assert.Contains("module.version", @out);
        Assert.Contains("FunctionIndex", @out);
        Assert.Contains("Code", @out);
        Assert.Contains("Main", @out);
        Assert.Contains("Add", @out);
        Assert.Contains("CodeOffset", @out);
        Assert.Contains("CodeLength", @out);
    }

    [Fact]
    public void ModInspect_DoesNotExecuteBytecode()
    {
        // 程序的核心可观测副作用是 Print(5*2) = 10。inspect 若执行了字节码，会看到 "10"。
        var main = WriteIn("Main.st", MainSt);
        var math = WriteIn("Math.st", MathSt);
        var helper = WriteIn("Helper.st", HelperSt);
        var outFile = Path.Combine(_dir, "noexec.stmod");

        var (bExit, _, _) = Invoke(
            "mod", "build", main, math, helper, "--entry", "Main", "-o", outFile);
        Assert.Equal(0, bExit);

        var (exit, @out, err) = Invoke("mod", "inspect", outFile);

        Assert.Equal(0, exit);
        Assert.Equal("", err);
        var lines = @out.Replace("\r\n", "\n").Split('\n');
        Assert.DoesNotContain("10", lines);
        Assert.DoesNotContain("[run] 执行完成", @out);
    }

    // ═══════════════════════════════════════════════════════════
    //  5. 加载策略：.stmod 自 P1 起可直接运行（链接协议 §7）
    //     —— 本用例是旧契约（"不可直接运行"）的反向断言，
    //        用真实的副作用 Print(10) 证明字节码确实被执行。
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Run_Stmod_IsExecutable_NotRejectedWithGuidance()
    {
        var main = WriteIn("Main.st", MainSt);
        var math = WriteIn("Math.st", MathSt);
        var helper = WriteIn("Helper.st", HelperSt);
        var outFile = Path.Combine(_dir, "runme.stmod");

        var (bExit, _, _) = Invoke(
            "mod", "build", main, math, helper, "--entry", "Main", "-o", outFile);
        Assert.Equal(0, bExit);

        var (exit, @out, err) = Invoke("run", outFile);

        Assert.Equal(0, exit);
        Assert.DoesNotContain("不能直接运行", err);

        // 必须真实执行：模块里的 Print(10) 结果必须出现在 stdout。
        var lines = @out.Replace("\r\n", "\n").Split('\n');
        Assert.Contains(lines, l => l.Trim() == "10");
    }

    [Fact]
    public void Run_StmodMixedWithSources_IsRejected()
    {
        var main = WriteIn("Main.st", MainSt);
        var math = WriteIn("Math.st", MathSt);
        var helper = WriteIn("Helper.st", HelperSt);
        var outFile = Path.Combine(_dir, "mixed.stmod");
        var (bExit, _, _) = Invoke("mod", "build", main, math, helper, "-o", outFile);
        Assert.Equal(0, bExit);

        var (exit, _, err) = Invoke("run", outFile, main);

        Assert.Equal(1, exit);
        Assert.Contains(".stmod", err);
    }

    // ═══════════════════════════════════════════════════════════
    //  6. 非法输入被拒绝
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ModInspect_InvalidMagic_FailsClearly()
    {
        var bad = Path.Combine(_dir, "bad.stmod");
        File.WriteAllBytes(bad, new byte[64]);

        var (exit, _, err) = Invoke("mod", "inspect", bad);

        Assert.Equal(1, exit);
        Assert.Contains("魔数", err);
    }

    [Fact]
    public void ModInspect_TruncatedFile_FailsClearly()
    {
        var bad = Path.Combine(_dir, "trunc.stmod");
        File.WriteAllBytes(bad, Encoding.ASCII.GetBytes("STMD"));

        var (exit, _, err) = Invoke("mod", "inspect", bad);

        Assert.Equal(1, exit);
        Assert.Contains(".stmod", err);
    }

    [Fact]
    public void ModInspect_MissingFile_FailsClearly()
    {
        var ghost = Path.Combine(_dir, "ghost.stmod");

        var (exit, _, err) = Invoke("mod", "inspect", ghost);

        Assert.Equal(1, exit);
        Assert.Contains("ghost.stmod", err);
    }

    [Fact]
    public void ModBuild_MissingInput_FailsClearly()
    {
        var (exit, _, err) = Invoke("mod", "build");

        Assert.Equal(1, exit);
        Assert.Contains("mod build", err);
    }

    [Fact]
    public void ModBuild_CompileError_FailsClearly()
    {
        var bad = WriteIn("Bad.st", """
            PROGRAM Main
            VAR x : DINT; END_VAR
            x := NoSuchFunction(1);
            END_PROGRAM
            """);
        var outFile = Path.Combine(_dir, "bad.stmod");

        var (exit, _, err) = Invoke("mod", "build", bad, "-o", outFile);

        Assert.Equal(1, exit);
        Assert.Contains("NoSuchFunction", err);
        Assert.False(File.Exists(outFile));
    }

    // ═══════════════════════════════════════════════════════════
    //  7. .stproj 集成与既有工作流回归
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ModBuild_FromStproj_Works()
    {
        WriteIn("src/Main.st", MainSt);
        WriteIn("src/Math.st", MathSt);
        WriteIn("src/Helper.st", HelperSt);
        var proj = WriteIn("P.stproj", """
            <?xml version="1.0" encoding="utf-8"?>
            <STProject Version="1">
              <PropertyGroup>
                <Name>P</Name>
                <StartupProgram>Main</StartupProgram>
              </PropertyGroup>
              <ItemGroup>
                <STCompile Include="src/Main.st" />
                <STCompile Include="src/Math.st" />
                <STCompile Include="src/Helper.st" />
              </ItemGroup>
            </STProject>
            """);

        var (exit, @out, err) = Invoke("mod", "build", proj);

        Assert.Equal(0, exit);
        Assert.Equal("", err);
        var produced = Path.Combine(_dir, "P.stmod");
        Assert.True(File.Exists(produced));
        Assert.Contains(".stmod", @out);

        var p = Probe.Load(produced);
        Assert.Equal("P", p.MetaString("module.name"));
        Assert.Contains(p.Functions, f => f.Name == "Main");
    }

    [Fact]
    public void ExistingWorkflows_StillWork_AfterModBuild()
    {
        WriteIn("src/Main.st", MainSt);
        WriteIn("src/Math.st", MathSt);
        WriteIn("src/Helper.st", HelperSt);
        var proj = WriteIn("P.stproj", """
            <?xml version="1.0" encoding="utf-8"?>
            <STProject Version="1">
              <PropertyGroup>
                <Name>P</Name>
                <StartupProgram>Main</StartupProgram>
                <OutputPath>dist/P.isb</OutputPath>
              </PropertyGroup>
              <ItemGroup>
                <STCompile Include="src/Main.st" />
                <STCompile Include="src/Math.st" />
                <STCompile Include="src/Helper.st" />
              </ItemGroup>
            </STProject>
            """);

        // 1) 模块构建
        var (mExit, _, mErr) = Invoke("mod", "build", proj);
        Assert.Equal(0, mExit);
        Assert.Equal("", mErr);

        // 2) 既有 .stproj → .isb 构建
        var (bExit, _, bErr) = Invoke("build", proj);
        Assert.Equal(0, bExit);
        Assert.Equal("", bErr);
        Assert.True(File.Exists(Path.Combine(_dir, "dist", "P.isb")));

        // 3) 既有 .isb 执行
        var (rExit, rOut, rErr) = Invoke("run", Path.Combine(_dir, "dist", "P.isb"));
        Assert.Equal(0, rExit);
        Assert.Equal("", rErr);
        Assert.Contains("10", rOut.Replace("\r\n", "\n").Split('\n'));

        // 4) 既有项目模式执行
        var (pExit, pOut, _) = Invoke("run", proj);
        Assert.Equal(0, pExit);
        Assert.Contains("10", pOut.Replace("\r\n", "\n").Split('\n'));
    }
}
