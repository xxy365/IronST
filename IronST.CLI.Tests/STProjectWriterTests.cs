// STProjectWriterTests.cs — 新建/写出 `.stproj`（阶段 I）。
//
// 契约：
//   · 生成的 .stproj 必须能被 STProjectFile.Load 读通，且能被 `build` / `run` 直接用；
//   · 新建工程**不覆盖**任何已存在的文件（.stproj 或源文件）；
//   · 校验失败时**不留下任何半成品**（不产出 .stproj，也不产出源文件）。
using IronST.CLI.Commands;

namespace IronST.CLI.Tests;

[Collection("cli-console")]
public class STProjectWriterTests : IDisposable
{
    private readonly string _dir;

    public STProjectWriterTests()
    {
        _dir = Path.Combine(
            Path.GetTempPath(), "ironst-stproj-writer", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
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

    private static STProjectTemplate Template(
        string name,
        string? startup = null,
        IReadOnlyList<string>? sources = null,
        IReadOnlyList<string>? moduleRefs = null,
        string? outputPath = null) => new()
    {
        Name = name,
        StartupProgram = startup,
        Sources = sources ?? ["Main.st"],
        ModuleReferences = moduleRefs ?? [],
        OutputPath = outputPath,
    };

    // ═══════════════════════════════════════════════════════════
    //  1. 写入内容可被读取器读回
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void WriteXml_RoundTripsThroughLoader()
    {
        var projDir = Path.Combine(_dir, "RoundTrip");
        Directory.CreateDirectory(projDir);
        File.WriteAllText(Path.Combine(projDir, "Main.st"), "PROGRAM Main\nEND_PROGRAM\n");

        var template = Template(
            "RoundTrip", startup: "Main", outputPath: "dist/RoundTrip.isb");
        var xml = STProjectWriter.WriteXml(template);
        var projPath = Path.Combine(projDir, "RoundTrip.stproj");
        File.WriteAllText(projPath, xml);

        var project = STProjectFile.Load(projPath);

        Assert.Equal("RoundTrip", project.Name);
        Assert.Equal("Main", project.StartupProgram);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(projDir, "dist", "RoundTrip.isb")),
            project.OutputPath);
        Assert.Equal(
            [Path.GetFullPath(Path.Combine(projDir, "Main.st"))],
            project.SourceFiles);
        Assert.Empty(project.ModuleReferences);
    }

    [Fact]
    public void WriteXml_WritesModuleReferences()
    {
        var projDir = Path.Combine(_dir, "WithRefs");
        Directory.CreateDirectory(projDir);
        File.WriteAllText(Path.Combine(projDir, "Main.st"), "PROGRAM Main\nEND_PROGRAM\n");
        Directory.CreateDirectory(Path.Combine(projDir, "lib"));
        File.WriteAllBytes(Path.Combine(projDir, "lib", "Math.stmod"), [0x00]);

        var template = Template("WithRefs", startup: "Main", moduleRefs: ["lib/Math.stmod"]);
        var projPath = Path.Combine(projDir, "WithRefs.stproj");
        File.WriteAllText(projPath, STProjectWriter.WriteXml(template));

        var project = STProjectFile.Load(projPath);

        Assert.Equal(
            [Path.GetFullPath(Path.Combine(projDir, "lib", "Math.stmod"))],
            project.ModuleReferences);
    }

    // ═══════════════════════════════════════════════════════════
    //  2. 新建工程：产出真实可构建、可运行的项目
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void CreateProject_GeneratesBuildableRunnableProject()
    {
        var projDir = Path.Combine(_dir, "NewApp");

        var projPath = STProjectWriter.CreateProject(projDir, Template("NewApp"));

        Assert.True(File.Exists(projPath));
        Assert.True(File.Exists(Path.Combine(projDir, "Main.st")));

        // 产生的工程必须能被真实编译器构建……
        var isb = Path.Combine(projDir, "NewApp.isb");
        var (buildExit, _, buildErr) = Invoke("build", projPath, "-o", isb);
        Assert.Equal(0, buildExit);
        Assert.Equal("", buildErr);
        Assert.True(File.Exists(isb));

        // ……并真实执行出模板里的副作用。
        var (runExit, runOut, runErr) = Invoke("run", projPath);
        Assert.Equal(0, runExit);
        Assert.Equal("", runErr);
        Assert.Contains("42", runOut);
    }

    [Fact]
    public void CreateProject_CreatesMissingDirectory()
    {
        var projDir = Path.Combine(_dir, "a", "b", "Deep");

        var projPath = STProjectWriter.CreateProject(projDir, Template("Deep"));

        Assert.True(Directory.Exists(projDir));
        Assert.Equal("Deep", STProjectFile.Load(projPath).Name);
    }

    // ═══════════════════════════════════════════════════════════
    //  2b. 编码：必须是 UTF-8（声明与字节一致），且不带 BOM
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void CreateProject_WritesUtf8WithoutBom_AndDeclaresUtf8()
    {
        var projDir = Path.Combine(_dir, "Encoding");

        var projPath = STProjectWriter.CreateProject(projDir, Template("Encoding"));

        // 声明的编码必须与实际字节一致，否则严格 XML 解析器会读错。
        Assert.StartsWith(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
            File.ReadAllText(projPath).TrimStart());

        Assert.False(HasUtf8Bom(File.ReadAllBytes(projPath)), "工程文件不应带 UTF-8 BOM");
        Assert.False(
            HasUtf8Bom(File.ReadAllBytes(Path.Combine(projDir, "Main.st"))),
            "源文件不应带 UTF-8 BOM");
    }

    private static bool HasUtf8Bom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

    // ═══════════════════════════════════════════════════════════
    //  3. 安全：不覆盖已有文件、失败不留半成品
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void CreateProject_RefusesExistingProjectFile()
    {
        var projDir = Path.Combine(_dir, "Exists");
        Directory.CreateDirectory(projDir);
        var existing = Path.Combine(projDir, "Exists.stproj");
        File.WriteAllText(existing, "KEEP");

        Assert.Throws<ProjectFileException>(
            () => STProjectWriter.CreateProject(projDir, Template("Exists")));

        Assert.Equal("KEEP", File.ReadAllText(existing));
        Assert.False(File.Exists(Path.Combine(projDir, "Main.st")));
    }

    [Fact]
    public void CreateProject_RefusesExistingSourceFile_AndCreatesNothingElse()
    {
        var projDir = Path.Combine(_dir, "SrcExists");
        Directory.CreateDirectory(projDir);
        var existingSource = Path.Combine(projDir, "Main.st");
        File.WriteAllText(existingSource, "PROGRAM Mine\nEND_PROGRAM\n");

        Assert.Throws<ProjectFileException>(
            () => STProjectWriter.CreateProject(projDir, Template("SrcExists")));

        Assert.Equal("PROGRAM Mine\nEND_PROGRAM\n", File.ReadAllText(existingSource));
        Assert.False(File.Exists(Path.Combine(projDir, "SrcExists.stproj")));
    }

    [Fact]
    public void CreateProject_RejectsMissingModuleReference_AndCreatesNothing()
    {
        var projDir = Path.Combine(_dir, "BadRef");

        Assert.Throws<ProjectFileException>(() => STProjectWriter.CreateProject(
            projDir, Template("BadRef", moduleRefs: ["lib/Ghost.stmod"])));

        Assert.False(File.Exists(Path.Combine(projDir, "BadRef.stproj")));
        Assert.False(File.Exists(Path.Combine(projDir, "Main.st")));
    }

    [Fact]
    public void CreateProject_RejectsNonStmodModuleReference()
    {
        var projDir = Path.Combine(_dir, "BadRefExt");
        Directory.CreateDirectory(projDir);
        File.WriteAllText(Path.Combine(projDir, "Math.st"), "FUNCTION Math : DINT\nEND_FUNCTION\n");

        Assert.Throws<ProjectFileException>(() => STProjectWriter.CreateProject(
            projDir, Template("BadRefExt", moduleRefs: ["Math.st"])));

        Assert.False(File.Exists(Path.Combine(projDir, "BadRefExt.stproj")));
    }

    [Fact]
    public void CreateProject_RejectsEmptyOrInvalidName()
    {
        var projDir = Path.Combine(_dir, "BadName");

        Assert.Throws<ProjectFileException>(
            () => STProjectWriter.CreateProject(projDir, Template("")));
        Assert.Throws<ProjectFileException>(
            () => STProjectWriter.CreateProject(projDir, Template("  ")));
        Assert.Throws<ProjectFileException>(
            () => STProjectWriter.CreateProject(
                projDir, Template("bad/name" + Path.GetInvalidFileNameChars()[0])));
    }

    [Fact]
    public void CreateProject_RejectsSourceEscapingProjectDirectory()
    {
        var projDir = Path.Combine(_dir, "Escape");

        Assert.Throws<ProjectFileException>(() => STProjectWriter.CreateProject(
            projDir, Template("Escape", sources: ["../Outside.st"])));

        Assert.False(File.Exists(Path.Combine(projDir, "Escape.stproj")));
        Assert.False(File.Exists(Path.Combine(_dir, "Outside.st")));
    }

    [Fact]
    public void CreateProject_RejectsNonStSource()
    {
        var projDir = Path.Combine(_dir, "BadSrcExt");

        Assert.Throws<ProjectFileException>(() => STProjectWriter.CreateProject(
            projDir, Template("BadSrcExt", sources: ["Main.txt"])));

        Assert.False(File.Exists(Path.Combine(projDir, "BadSrcExt.stproj")));
    }
}
