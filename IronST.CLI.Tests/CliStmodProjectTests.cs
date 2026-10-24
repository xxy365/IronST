// CliStmodProjectTests.cs — P5：`.stproj` 驱动的静态链接（链接协议 §11）。
//
// 覆盖：
//   · 项目声明 <STModuleReference> → `mod build <proj.stproj>` 产出**已链接**、
//     自足的 `.stmod`（删掉所有输入后仍能运行）；
//   · 项目**未**声明模块引用时保持 P2 语义（只用 --ref 注入声明、保留外部引用）；
//   · 引用模块不存在 / 非 .stmod / 与 --ref 重复 的校验。
using IronST.CLI.Commands;

namespace IronST.CLI.Tests;

[Collection("cli-console")]
public class CliStmodProjectTests : IDisposable
{
    private readonly string _dir;

    public CliStmodProjectTests()
    {
        _dir = Path.Combine(
            Path.GetTempPath(), "ironst-cli-stproj-link", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
    }

    private const string MathSt = """
        FUNCTION Add : DINT
        VAR_INPUT a : DINT; b : DINT; END_VAR
        Add := a + b;
        END_FUNCTION
        """;

    private const string MainSt = """
        PROGRAM Main
        VAR x : DINT; END_VAR
            x := Add(2, 3);
            x := x * 2;
            Print(x);
        END_PROGRAM
        """;

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

    private string PathOf(string name) => Path.Combine(_dir, name);

    private string WriteFile(string name, string content)
    {
        var path = PathOf(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>写一个最小项目文件；<paramref name="moduleRefs"/> 为模块引用 Include 列表。</summary>
    private string WriteProject(string projName, IEnumerable<string> moduleRefs)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<STProject Version=\"1\">");
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine("    <Name>App</Name>");
        sb.AppendLine("    <StartupProgram>Main</StartupProgram>");
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine("  <ItemGroup>");
        sb.AppendLine("    <STCompile Include=\"Main.st\" />");
        sb.AppendLine("  </ItemGroup>");
        var refs = moduleRefs.ToList();
        if (refs.Count > 0)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var r in refs)
                sb.AppendLine($"    <STModuleReference Include=\"{r}\" />");
            sb.AppendLine("  </ItemGroup>");
        }
        sb.AppendLine("</STProject>");
        return WriteFile(projName, sb.ToString());
    }

    /// <summary>准备 Math.st + Math.stmod + Main.st，返回 Math.stmod 的路径。</summary>
    private string PrepareMath()
    {
        var mathSrc = WriteFile("Math.st", MathSt);
        WriteFile("Main.st", MainSt);
        var mathMod = PathOf("Math.stmod");
        var (exit, _, err) = Invoke("mod", "build", mathSrc, "-o", mathMod);
        Assert.Equal(0, exit);
        Assert.Equal("", err);
        return mathMod;
    }

    private static bool HasLine(string text, string target) =>
        text.Replace("\r\n", "\n").Split('\n').Any(l => l.Trim() == target);

    [Fact]
    public void Build_ProjectWithModuleReference_ProducesStandaloneLinkedModule()
    {
        PrepareMath();
        var proj = WriteProject("App.stproj", ["Math.stmod"]);
        var app = PathOf("App.stmod");

        var (exit, _, err) = Invoke("mod", "build", proj, "-o", app);

        Assert.Equal(0, exit);
        Assert.Equal("", err);

        // 产物必须是**已链接**、无导入的自足模块。
        var (insExit, insOut, _) = Invoke("mod", "inspect", app);
        Assert.Equal(0, insExit);
        Assert.Contains("链接状态: 已链接", insOut);
        Assert.Contains("导入符号 (0)", insOut);

        var (runExit, runOut, _) = Invoke("run", app);
        Assert.Equal(0, runExit);
        Assert.True(HasLine(runOut, "10"), $"期望 10，实际：\n{runOut}");

        // 脱离输入：删掉全部输入源与引用模块后仍可运行。
        File.Delete(PathOf("Math.stmod"));
        File.Delete(PathOf("Math.st"));
        File.Delete(PathOf("Main.st"));

        var (runExit2, runOut2, runErr2) = Invoke("run", app);
        Assert.Equal(0, runExit2);
        Assert.Equal("", runErr2);
        Assert.True(HasLine(runOut2, "10"), $"期望脱离输入后仍为 10，实际：\n{runOut2}");
    }

    [Fact]
    public void Build_ProjectWithModuleReferenceAndCliRef_DeduplicatesAndLinks()
    {
        var mathMod = PrepareMath();
        var proj = WriteProject("App.stproj", ["Math.stmod"]);
        var app = PathOf("App.stmod");

        // 同一模块既写在项目里、又通过 --ref 传入 → 去重后只链接一次。
        var (exit, _, err) = Invoke("mod", "build", proj, "--ref", mathMod, "-o", app);

        Assert.Equal(0, exit);
        Assert.Equal("", err);

        var (runExit, runOut, _) = Invoke("run", app);
        Assert.Equal(0, runExit);
        Assert.True(HasLine(runOut, "10"), $"期望 10，实际：\n{runOut}");
    }

    [Fact]
    public void Build_ProjectWithoutModuleReference_KeepsExternalReferences()
    {
        var mathMod = PrepareMath();
        var proj = WriteProject("App.stproj", []);
        var app = PathOf("App.stmod");

        var (exit, _, err) = Invoke("mod", "build", proj, "--ref", mathMod, "-o", app);

        Assert.Equal(0, exit);
        Assert.Equal("", err);

        // 只声明、不链接：保留未解析外部引用（与 `.st --ref` 语义一致）。
        var (insExit, insOut, _) = Invoke("mod", "inspect", app);
        Assert.Equal(0, insExit);
        Assert.Contains("未链接", insOut);
        Assert.Contains("导入符号 (1)", insOut);

        var (runExit, _, runErr) = Invoke("run", app);
        Assert.Equal(1, runExit);
        Assert.Contains("未解析", runErr);
    }

    [Fact]
    public void Build_ProjectWithMissingModuleReference_IsRejected_NoOutput()
    {
        WriteFile("Main.st", MainSt);
        var proj = WriteProject("App.stproj", ["Nope.stmod"]);
        var app = PathOf("App.stmod");

        var (exit, _, err) = Invoke("mod", "build", proj, "-o", app);

        Assert.Equal(1, exit);
        Assert.Contains("不存在", err);
        Assert.Contains("Nope.stmod", err);
        Assert.False(File.Exists(app));
    }

    [Fact]
    public void Build_ProjectWithNonStmodReference_IsRejected()
    {
        WriteFile("Main.st", MainSt);
        var proj = WriteProject("App.stproj", ["Main.st"]);

        var (exit, _, err) = Invoke("mod", "build", proj, "-o", PathOf("App.stmod"));

        Assert.Equal(1, exit);
        Assert.Contains(".stmod", err);
    }
}
