// CliStmodEntryTests.cs — P4：链接模块的**入口选择**与执行。
//
// 覆盖：
//   · 链接后按"首个声明入口的模块"取默认入口，能真实执行；
//   · 入口位于**第二个**模块时用 `--entry` 精确选择；
//   · `run <linked.stmod> --entry NAME` 覆盖；
//   · 显式入口歧义 → 报错并列出模块限定候选。
using IronST.CLI.Commands;

namespace IronST.CLI.Tests;

[Collection("cli-console")]
public class CliStmodEntryTests : IDisposable
{
    private readonly string _dir;

    public CliStmodEntryTests()
    {
        _dir = Path.Combine(
            Path.GetTempPath(), "ironst-cli-stmod-entry", Guid.NewGuid().ToString("N"));
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

    private string WriteSource(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string Build(string sourceName, string source, params string[] extra)
    {
        var src = WriteSource(sourceName, source);
        var outFile = Path.Combine(_dir, Path.GetFileNameWithoutExtension(sourceName) + ".stmod");
        var args = new List<string> { "mod", "build", src };
        args.AddRange(extra);
        args.AddRange(["-o", outFile]);
        var (exit, _, err) = Invoke(args.ToArray());
        Assert.Equal(0, exit);
        Assert.Equal("", err);
        return outFile;
    }

    private static bool HasLine(string text, string target) =>
        text.Replace("\r\n", "\n").Split('\n').Any(l => l.Trim() == target);

    [Fact]
    public void Run_LinkedModule_DefaultEntryFromFirstModule_Executes()
    {
        var math = Build("Math.st", MathSt);
        var main = Build("Main.st", MainSt, "--ref", math, "--entry", "Main");

        var app = Path.Combine(_dir, "App.stmod");
        // 主程序模块在**最前** → 默认入口取自它。
        var (linkExit, _, linkErr) = Invoke("mod", "link", main, math, "-o", app);
        Assert.Equal(0, linkExit);
        Assert.Equal("", linkErr);

        var (exit, @out, _) = Invoke("run", app);
        Assert.Equal(0, exit);
        Assert.True(HasLine(@out, "10"), $"期望 10，实际：\n{@out}");
    }

    [Fact]
    public void Run_LinkedModule_EntryInSecondModule_Explicit_Executes()
    {
        var math = Build("Math.st", MathSt);
        var main = Build("Main.st", MainSt, "--ref", math, "--entry", "Main");

        var app = Path.Combine(_dir, "App.stmod");
        // Math 在前、Main 在后；用 --entry 精确选择第二个模块里的入口。
        var (linkExit, _, linkErr) = Invoke(
            "mod", "link", math, main, "--entry", "Main", "-o", app);
        Assert.Equal(0, linkExit);
        Assert.Equal("", linkErr);

        var (exit, @out, _) = Invoke("run", app);
        Assert.Equal(0, exit);
        Assert.True(HasLine(@out, "10"), $"期望 10，实际：\n{@out}");
    }

    [Fact]
    public void Run_LinkedModule_ExplicitEntryByName_Executes()
    {
        var math = Build("Math.st", MathSt);
        var main = Build("Main.st", MainSt, "--ref", math, "--entry", "Main");
        var app = Path.Combine(_dir, "App.stmod");
        Assert.Equal(0, Invoke("mod", "link", main, math, "-o", app).Exit);

        var (exit, @out, err) = Invoke("run", app, "--entry", "Main");

        Assert.Equal(0, exit);
        Assert.Equal("", err);
        Assert.True(HasLine(@out, "10"), $"期望 10，实际：\n{@out}");
    }

    [Fact]
    public void Run_LinkedModule_MissingEntry_ListsAvailable()
    {
        var math = Build("Math.st", MathSt);
        var main = Build("Main.st", MainSt, "--ref", math, "--entry", "Main");
        var app = Path.Combine(_dir, "App.stmod");
        Assert.Equal(0, Invoke("mod", "link", main, math, "-o", app).Exit);

        var (exit, _, err) = Invoke("run", app, "--entry", "NoSuchEntry");

        Assert.Equal(1, exit);
        Assert.Contains("NoSuchEntry", err);
        Assert.Contains("Main", err);
    }

    [Fact]
    public void Link_AmbiguousEntryName_ListsQualifiedCandidates()
    {
        var a = Build("MathA.st", MathSt);   // 两个模块都实现 Add
        var b = Build("MathB.st", MathSt);
        var app = Path.Combine(_dir, "Amb.stmod");

        var (exit, _, err) = Invoke("mod", "link", a, b, "--entry", "Add", "-o", app);

        Assert.Equal(1, exit);
        Assert.Contains("歧义", err);
        Assert.Contains("MathA:Add", err);
        Assert.Contains("MathB:Add", err);
        Assert.False(File.Exists(app));
    }
}
