// CliStmodRunTests.cs — `.stmod` 直接运行（阶段 P1）的端到端测试。
//
// 观测点：程序唯一副作用是 Print(Add(2,3) * 2) = 10。若 `.stmod` 真被执行，
// stdout 必然出现 "10"；若只是"表面接受"，则要么拒绝、要么无输出。
using System.Text;
using IronST.CLI.Commands;

namespace IronST.CLI.Tests;

[Collection("cli-console")]
public class CliStmodRunTests : IDisposable
{
    private readonly string _dir;

    public CliStmodRunTests()
    {
        _dir = Path.Combine(
            Path.GetTempPath(), "ironst-cli-stmod-run", Guid.NewGuid().ToString("N"));
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

    private string WriteIn(string rel, string content)
    {
        var path = Path.Combine(_dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
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

    private string BuildApp(out string outFile)
    {
        var main = WriteIn("Main.st", MainSt);
        var math = WriteIn("Math.st", MathSt);
        var helper = WriteIn("Helper.st", HelperSt);
        outFile = Path.Combine(_dir, "app.stmod");
        var (exit, _, err) = Invoke(
            "mod", "build", main, math, helper, "--entry", "Main", "-o", outFile);
        Assert.Equal(0, exit);
        Assert.Equal("", err);
        return outFile;
    }

    private static bool HasLine(string text, string target) =>
        text.Replace("\r\n", "\n").Split('\n').Any(l => l.Trim() == target);

    [Fact]
    public void Run_Stmod_DefaultEntry_ExecutesRealCode()
    {
        var outFile = BuildApp(out _);
        var before = File.ReadAllBytes(outFile);

        var (exit, @out, err) = Invoke("run", outFile);

        Assert.Equal(0, exit);
        Assert.Equal("", err);
        Assert.True(HasLine(@out, "10"), $"期望真实执行输出 10，实际 stdout：\n{@out}");
        Assert.Contains("[run] 执行完成", @out);

        // 执行只改内存副本：文件字节不得变化。
        Assert.Equal(before, File.ReadAllBytes(outFile));
    }

    [Fact]
    public void Run_Stmod_ExplicitEntryByName_ExecutesRealCode()
    {
        var outFile = BuildApp(out _);

        var (exit, @out, err) = Invoke("run", outFile, "--entry", "Main");

        Assert.Equal(0, exit);
        Assert.Equal("", err);
        Assert.True(HasLine(@out, "10"), $"期望 --entry Main 真实执行输出 10，实际：\n{@out}");
    }

    [Fact]
    public void Run_Stmod_MissingEntry_ListsAvailableEntries()
    {
        var outFile = BuildApp(out _);

        var (exit, _, err) = Invoke("run", outFile, "--entry", "NoSuchEntry");

        Assert.Equal(1, exit);
        Assert.Contains("NoSuchEntry", err);
        Assert.Contains("Main", err);   // 可用入口提示
    }

    [Fact]
    public void Run_Stmod_DeclarationEntry_IsRejectedAsNonExecutable()
    {
        var outFile = BuildApp(out _);

        // Print 是宿主函数声明（Declaration），不得作为入口。
        var (exit, @out, err) = Invoke("run", outFile, "--entry", "Print");

        Assert.Equal(1, exit);
        Assert.Contains("Print", err);
        Assert.DoesNotContain("[run] 执行完成", @out);
    }

    [Fact]
    public void Run_Stmod_CorruptFile_FailsClearly()
    {
        var bad = Path.Combine(_dir, "bad.stmod");
        File.WriteAllBytes(bad, new byte[64]);

        var (exit, _, err) = Invoke("run", bad);

        Assert.Equal(1, exit);
        Assert.Contains(".stmod", err);
    }

    [Fact]
    public void Inspect_ShowsLinkedState()
    {
        var outFile = BuildApp(out _);

        var (exit, @out, err) = Invoke("mod", "inspect", outFile);

        Assert.Equal(0, exit);
        Assert.Equal("", err);
        Assert.Contains("已链接", @out);
    }
}
