// CliProjectTests.cs — .stproj 项目文件系统（build / 项目模式 run）的 CLI 端到端测试。
//
// 被测：CliRouter → BuildCommand / RunCommand（真实 CLI 入口，含参数解析）
// 观测：捕获 Console.Out / Console.Error，断言退出码与输出，并**实际让生成物执行**。
//
// 关键前提：Include 路径相对**项目文件所在目录**解析，而非 CLI 当前工作目录；
// 本文件的临时项目都在 %TEMP% 下，而测试进程的 CWD 在测试输出目录，二者必然不同。
using IronST.CLI.Commands;

namespace IronST.CLI.Tests;

// Console.SetOut/SetError 是进程级全局状态 ⇒ 与其它 CLI 用例同集合串行。
[Collection("cli-console")]
public class CliProjectTests : IDisposable
{
    private readonly string _dir;

    public CliProjectTests()
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

    /// <summary>按 CLI 入口调用并捕获 stdout / stderr。</summary>
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

    private static string[] Lines(string output) =>
        output.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

    /// <summary>断言给定文本按顺序出现在输出行序列中（允许中间插入其它行）。</summary>
    private static void AssertLinesInOrder(string output, params string[] expected)
    {
        int idx = 0;
        foreach (var line in Lines(output))
        {
            if (idx < expected.Length && line == expected[idx])
                idx++;
        }
        Assert.Equal(expected.Length, idx);
    }

    // ── 源文件片段 ────────────────────────────────────────────

    private const string MathSt = """
        FUNCTION Add : DINT
        VAR_INPUT a : DINT; b : DINT; END_VAR
        Add := a + b;
        END_FUNCTION
        """;

    private const string TypesSt = """
        TYPE Point : STRUCT
        x : DINT;
        y : DINT;
        END_STRUCT;
        END_TYPE;
        """;

    private const string MainSt = """
        PROGRAM Main
        VAR
            p : Point;
            total : DINT;
            s : STRING;
        END_VAR
            p.x := 40;
            p.y := 2;
            Print(p.x);
            total := Add(40, 2);
            Print(total);
            s := CONCAT('proj', 'ok');
            Print(s);
        END_PROGRAM
        """;

    private static readonly string[] ProjectOutput = ["40", "42", "projok"];

    private const string AlphaSt = """
        PROGRAM Alpha
        VAR a : DINT; END_VAR
        a := 111; Print(a);
        END_PROGRAM
        """;

    private const string BetaSt = """
        PROGRAM Beta
        VAR b : DINT; END_VAR
        b := 222; Print(b);
        END_PROGRAM
        """;

    private const string SharedSt = """
        FUNCTION Shared : DINT
        Shared := 1;
        END_FUNCTION
        """;

    /// <summary>按任务给定的 .stproj 结构生成 XML。</summary>
    private static string ProjectXml(
        string? name,
        string? startup,
        string? outputPath,
        params string[] includes)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<STProject Version=\"1\">");
        sb.AppendLine("  <PropertyGroup>");
        if (name != null) sb.AppendLine($"    <Name>{name}</Name>");
        if (startup != null) sb.AppendLine($"    <StartupProgram>{startup}</StartupProgram>");
        if (outputPath != null) sb.AppendLine($"    <OutputPath>{outputPath}</OutputPath>");
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine("  <ItemGroup>");
        foreach (var i in includes)
            sb.AppendLine($"    <STCompile Include=\"{i}\" />");
        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine("</STProject>");
        return sb.ToString();
    }

    /// <summary>在临时目录里铺一个标准三文件项目，返回 .stproj 路径。</summary>
    private string WriteSampleProject(string projectFileName = "MyScript.stproj")
    {
        WriteIn("src/Main.st", MainSt);
        WriteIn("src/Math.st", MathSt);
        WriteIn("src/Types.st", TypesSt);
        return WriteIn(
            projectFileName,
            ProjectXml("MyScript", "Main", "dist/MyScript.isb",
                "src/Main.st", "src/Math.st", "src/Types.st"));
    }

    // ═══════════════════════════════════════════════════════════
    //  1. 合法项目 → build 生成 .isb（任务给定格式）
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Build_SampleProjectFormat_WritesConfiguredOutputPath()
    {
        // 与任务给出的 .stproj 示例逐字一致（仅缩进不同）。
        WriteIn("src/Main.st", MainSt);
        WriteIn("src/Math.st", MathSt);
        WriteIn("src/Types.st", TypesSt);
        var proj = WriteIn("MyScript.stproj", """
            <?xml version="1.0" encoding="utf-8"?>
            <STProject Version="1">
              <PropertyGroup>
                <Name>MyScript</Name>
                <StartupProgram>Main</StartupProgram>
                <OutputPath>dist/MyScript.isb</OutputPath>
              </PropertyGroup>

              <ItemGroup>
                <STCompile Include="src/Main.st" />
                <STCompile Include="src/Math.st" />
                <STCompile Include="src/Types.st" />
              </ItemGroup>
            </STProject>
            """);

        var (exit, @out, err) = Invoke("build", proj);

        Assert.Equal(0, exit);
        Assert.Equal("", err);
        Assert.True(File.Exists(Path.Combine(_dir, "dist", "MyScript.isb")));
        Assert.Contains("构建成功", @out);
    }

    // ═══════════════════════════════════════════════════════════
    //  2. -o 覆盖输出路径
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Build_OverrideOutputPath_WithDashO()
    {
        var proj = WriteSampleProject();
        var outFile = Path.Combine(_dir, "release.isb");

        var (exit, @out, err) = Invoke("build", proj, "-o", outFile);

        Assert.Equal(0, exit);
        Assert.Equal("", err);
        Assert.True(File.Exists(outFile));
        Assert.Contains("构建成功", @out);
    }

    // ═══════════════════════════════════════════════════════════
    //  3~4. 生成物可执行；项目模式 run 与字节码执行一致
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Run_GeneratedBytecode_ExecutesProjectProgram()
    {
        var proj = WriteSampleProject();
        var outFile = Path.Combine(_dir, "app.isb");

        var (bExit, _, bErr) = Invoke("build", proj, "-o", outFile);
        Assert.Equal(0, bExit);
        Assert.Equal("", bErr);

        var (rExit, rOut, rErr) = Invoke("run", outFile);

        Assert.Equal(0, rExit);
        Assert.Equal("", rErr);
        AssertLinesInOrder(rOut, ProjectOutput);
    }

    [Fact]
    public void Run_ProjectFile_MatchesBytecodeRun()
    {
        var proj = WriteSampleProject();
        var outFile = Path.Combine(_dir, "app.isb");

        var (bExit, _, _) = Invoke("build", proj, "-o", outFile);
        Assert.Equal(0, bExit);

        var (pExit, pOut, pErr) = Invoke("run", proj);
        var (bRunExit, bOut, _) = Invoke("run", outFile);

        Assert.Equal(0, pExit);
        Assert.Equal("", pErr);
        Assert.Equal(0, bRunExit);
        // 项目模式直接执行，不产生中间 .st 文件，行为与执行构建产物一致。
        AssertLinesInOrder(pOut, ProjectOutput);
        AssertLinesInOrder(bOut, ProjectOutput);
    }

    // ═══════════════════════════════════════════════════════════
    //  5. Include 相对项目文件解析（不依赖 CLI 当前工作目录）
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Build_ProjectInOtherDirectory_ResolvesIncludesRelativeToProjectFile()
    {
        // 前提：测试 CWD 与项目目录不同（否则本用例证明不了什么）。
        Assert.NotEqual(
            Path.GetFullPath(Directory.GetCurrentDirectory()).TrimEnd('\\'),
            Path.GetFullPath(_dir).TrimEnd('\\'));

        var proj = WriteSampleProject();
        var outFile = Path.Combine(_dir, "cwd-proof.isb");

        var (exit, _, err) = Invoke("build", proj, "-o", outFile);
        var (rExit, rOut, _) = Invoke("run", outFile);

        Assert.Equal(0, exit);
        Assert.Equal("", err);
        Assert.Equal(0, rExit);
        AssertLinesInOrder(rOut, ProjectOutput);
    }

    // ═══════════════════════════════════════════════════════════
    //  6~8. 入口选择语义
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Build_StartupProgram_SelectsNamedEntry()
    {
        WriteIn("src/alpha.st", AlphaSt);
        WriteIn("src/beta.st", BetaSt);
        var proj = WriteIn(
            "two.stproj",
            ProjectXml("Two", "Alpha", null, "src/alpha.st", "src/beta.st"));
        var outFile = Path.Combine(_dir, "alpha.isb");

        var (bExit, _, bErr) = Invoke("build", proj, "-o", outFile);
        Assert.Equal(0, bExit);
        Assert.Equal("", bErr);

        var (rExit, rOut, _) = Invoke("run", outFile);

        Assert.Equal(0, rExit);
        AssertLinesInOrder(rOut, "111");
        Assert.DoesNotContain("222", rOut);
    }

    [Fact]
    public void Build_MultipleProgramsWithoutStartup_FailsClearly()
    {
        WriteIn("src/alpha.st", AlphaSt);
        WriteIn("src/beta.st", BetaSt);
        var proj = WriteIn(
            "two.stproj",
            ProjectXml("Two", null, null, "src/alpha.st", "src/beta.st"));

        var (exit, _, err) = Invoke("build", proj);

        Assert.Equal(1, exit);
        Assert.Contains("Alpha", err);
        Assert.Contains("Beta", err);
    }

    [Fact]
    public void Build_StartupProgramNotFound_FailsClearly()
    {
        var proj = WriteSampleProject();
        WriteIn(
            "MyScript.stproj",
            ProjectXml("MyScript", "Ghost", null,
                "src/Main.st", "src/Math.st", "src/Types.st"));

        var (exit, _, err) = Invoke("build", proj);

        Assert.Equal(1, exit);
        Assert.Contains("Ghost", err);
    }

    // ═══════════════════════════════════════════════════════════
    //  9~13. 项目文件校验
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Build_InvalidXml_FailsClearly()
    {
        var proj = WriteIn("broken.stproj", "<STProject Version=\"1\"><PropertyGroup></STProject>");

        var (exit, _, err) = Invoke("build", proj);

        Assert.Equal(1, exit);
        Assert.Contains("XML", err);
    }

    [Fact]
    public void Build_MissingSourceFile_FailsClearly()
    {
        WriteIn("src/Main.st", MainSt);
        var proj = WriteIn(
            "missing.stproj",
            ProjectXml("P", "Main", null, "src/Main.st", "src/Gone.st"));

        var (exit, _, err) = Invoke("build", proj);

        Assert.Equal(1, exit);
        Assert.Contains("Gone.st", err);
    }

    [Fact]
    public void Build_DuplicateSourceFile_FailsClearly()
    {
        WriteIn("src/Main.st", MainSt);
        var proj = WriteIn(
            "dup.stproj",
            ProjectXml("P", "Main", null, "src/Main.st", "src/Main.st"));

        var (exit, _, err) = Invoke("build", proj);

        Assert.Equal(1, exit);
        Assert.Contains("重复", err);
    }

    [Fact]
    public void Build_NoSources_FailsClearly()
    {
        var proj = WriteIn("empty.stproj", """
            <?xml version="1.0" encoding="utf-8"?>
            <STProject Version="1">
              <PropertyGroup>
                <Name>Empty</Name>
              </PropertyGroup>
            </STProject>
            """);

        var (exit, _, err) = Invoke("build", proj);

        Assert.Equal(1, exit);
        Assert.Contains("源文件", err);
    }

    [Fact]
    public void Build_DuplicateDeclarationAcrossFiles_FailsClearly()
    {
        WriteIn("src/a.st", SharedSt);
        WriteIn("src/b.st", SharedSt);
        WriteIn("src/main.st", """
            PROGRAM Main
            VAR x : DINT; END_VAR
            x := Shared();
            END_PROGRAM
            """);
        var proj = WriteIn(
            "dupsym.stproj",
            ProjectXml("Dup", "Main", null, "src/a.st", "src/b.st", "src/main.st"));

        var (exit, _, err) = Invoke("build", proj);

        Assert.Equal(1, exit);
        Assert.Contains("Shared", err);
        Assert.Contains("重复声明", err);
    }

    [Fact]
    public void Build_ProjectFileMissing_FailsClearly()
    {
        var ghost = Path.Combine(_dir, "ghost.stproj");

        var (exit, _, err) = Invoke("build", ghost);

        Assert.Equal(1, exit);
        Assert.Contains("ghost.stproj", err);
    }

    [Fact]
    public void Build_NonProjectInput_FailsClearly()
    {
        var st = WriteIn("plain.st", MainSt);

        var (exit, _, err) = Invoke("build", st);

        Assert.Equal(1, exit);
        Assert.Contains(".stproj", err);
    }

    // ═══════════════════════════════════════════════════════════
    //  14. 项目文件不与其它输入混用
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Run_ProjectFileMixedWithOtherInputs_FailsClearly()
    {
        var proj = WriteSampleProject();
        var main = Path.Combine(_dir, "src", "Main.st");

        var (exit, _, err) = Invoke("run", proj, main);

        Assert.Equal(1, exit);
        Assert.Contains("混用", err);
    }
}
