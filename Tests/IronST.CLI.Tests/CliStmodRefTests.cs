// CliStmodRefTests.cs — P2：独立编译 + 保留未解析外部引用（`mod build --ref`）。
//
// 关键证明点：
//   · `Math.st` 与 `Main.st` **分开编译**，Main 只依赖 `Math.stmod`；
//   · Main.stmod 明确记录"未链接 + 导入 Add"，绝不把 Add 的实现内联进来；
//   · 未链接模块**不得直接运行**；
//   · 没有 `--ref` 时，未声明函数仍是编译错误（不放松普通错误检查）。
using IronST.CLI.Commands;

namespace IronST.CLI.Tests;

[Collection("cli-console")]
public class CliStmodRefTests : IDisposable
{
    private readonly string _dir;

    public CliStmodRefTests()
    {
        _dir = Path.Combine(
            Path.GetTempPath(), "ironst-cli-stmod-ref", Guid.NewGuid().ToString("N"));
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

    private string WriteIn(string rel, string content)
    {
        var path = Path.Combine(_dir, rel);
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

    private string BuildMathModule()
    {
        var math = WriteIn("Math.st", MathSt);
        var outFile = Path.Combine(_dir, "Math.stmod");
        var (exit, _, err) = Invoke("mod", "build", math, "-o", outFile);
        Assert.Equal(0, exit);
        Assert.Equal("", err);
        return outFile;
    }

    [Fact]
    public void Build_WithRef_PreservesUnresolvedImport_AndRunRejects()
    {
        var mathModule = BuildMathModule();
        var main = WriteIn("Main.st", MainSt);
        var outFile = Path.Combine(_dir, "Main.stmod");

        var (exit, _, err) = Invoke(
            "mod", "build", main, "--ref", mathModule, "--entry", "Main", "-o", outFile);

        Assert.Equal(0, exit);
        Assert.Equal("", err);

        var (iExit, iOut, iErr) = Invoke("mod", "inspect", outFile);
        Assert.Equal(0, iExit);
        Assert.Equal("", iErr);
        Assert.Contains("module.linked = false", iOut);
        Assert.Contains("module.imports = Add", iOut);
        Assert.Contains("导入符号 (1)", iOut);
        Assert.Contains("Add", iOut);
        Assert.Contains("未链接", iOut);

        // 未链接模块绝不可直接运行。
        var (rExit, rOut, rErr) = Invoke("run", outFile);
        Assert.Equal(1, rExit);
        Assert.Contains("未解析的外部引用", rErr);
        Assert.DoesNotContain("10", rOut.Replace("\r\n", "\n").Split('\n'));
    }

    [Fact]
    public void Build_WithRef_DoesNotNeedProviderSource()
    {
        // 证明"独立编译"：编译 Main 时提供者源码已不存在，只靠 Math.stmod。
        var mathSource = WriteIn("Math.st", MathSt);
        var mathModule = Path.Combine(_dir, "Math.stmod");
        var (mExit, _, _) = Invoke("mod", "build", mathSource, "-o", mathModule);
        Assert.Equal(0, mExit);

        File.Delete(mathSource);
        Assert.False(File.Exists(mathSource));

        var main = WriteIn("Main.st", MainSt);
        var outFile = Path.Combine(_dir, "Main.stmod");
        var (exit, _, err) = Invoke(
            "mod", "build", main, "--ref", mathModule, "--entry", "Main", "-o", outFile);

        Assert.Equal(0, exit);
        Assert.Equal("", err);
        Assert.True(File.Exists(outFile));
    }

    [Fact]
    public void Build_WithoutRef_UndeclaredFunction_IsStillAnError()
    {
        var main = WriteIn("Main.st", MainSt);
        var outFile = Path.Combine(_dir, "NoRef.stmod");

        var (exit, _, err) = Invoke(
            "mod", "build", main, "--entry", "Main", "-o", outFile);

        Assert.Equal(1, exit);
        Assert.Contains("Add", err);
        // 失败不得留下半成品。
        Assert.False(File.Exists(outFile));
    }

    [Fact]
    public void Build_WithCorruptReference_FailsClearly()
    {
        var bad = Path.Combine(_dir, "bad.stmod");
        File.WriteAllBytes(bad, new byte[64]);

        var main = WriteIn("Main.st", MainSt);
        var outFile = Path.Combine(_dir, "BadOutput.stmod");

        var (exit, _, err) = Invoke(
            "mod", "build", main, "--ref", bad, "--entry", "Main", "-o", outFile);

        Assert.Equal(1, exit);
        Assert.Contains(".stmod", err);
        Assert.False(File.Exists(outFile));
    }

    [Fact]
    public void Build_WithRef_ExportsOwnFunctions_ForFurtherLinking()
    {
        var mathModule = BuildMathModule();
        var main = WriteIn("Main.st", MainSt);
        var outFile = Path.Combine(_dir, "Main.stmod");
        var (bExit, _, _) = Invoke(
            "mod", "build", main, "--ref", mathModule, "--entry", "Main", "-o", outFile);
        Assert.Equal(0, bExit);

        var (exit, @out, err) = Invoke("mod", "inspect", outFile);

        Assert.Equal(0, exit);
        Assert.Equal("", err);
        // Main 自己仍是可被后续模块引用的导出符号。
        Assert.Contains("导出符号 (1)", @out);
        Assert.Contains("Main", @out);
    }
}
