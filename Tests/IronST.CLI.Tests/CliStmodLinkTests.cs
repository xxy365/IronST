// CliStmodLinkTests.cs — P3：多个 `.stmod` 的**真实静态链接**（`mod link`）。
//
// 关键证明点：
//   · 链接产物**脱离输入**可独立运行（删掉输入文件仍然能跑）；
//   · 跨模块调用、常量池、全局槽位、宿主 ID 都做了真实回填；
//   · 未解析导入 / 重复定义 / 签名不一致 / 入口缺失或不可执行 → 明确拒绝；
//   · 输出原子化，失败不留半成品。
using IronST.CLI.Commands;

namespace IronST.CLI.Tests;

[Collection("cli-console")]
public class CliStmodLinkTests : IDisposable
{
    private readonly string _dir;

    public CliStmodLinkTests()
    {
        _dir = Path.Combine(
            Path.GetTempPath(), "ironst-cli-stmod-link", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
    }

    // ── 夹具源码 ────────────────────────────────────────────────

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

    private const string LeafSt = """
        FUNCTION Leaf : DINT
        VAR_INPUT x : DINT; END_VAR
        Leaf := x * 2;
        END_FUNCTION
        """;

    private const string StepSt = """
        FUNCTION Step : DINT
        VAR_INPUT x : DINT; END_VAR
        Step := Leaf(x) + 1;
        END_FUNCTION
        """;

    private const string MainChainSt = """
        PROGRAM Main
        VAR r : DINT; END_VAR
            r := Step(5);
            Print(r);
        END_PROGRAM
        """;

    private const string GlobMathSt = """
        FUNCTION Bump : DINT
        VAR_GLOBAL
            g : DINT;
        END_VAR
        VAR_INPUT
            n : DINT;
        END_VAR
            g := g + n;
            Bump := g;
        END_FUNCTION
        """;

    private const string GlobMainSt = """
        PROGRAM Main
        VAR_GLOBAL
            seed : DINT;
        END_VAR
        VAR
            t : DINT;
        END_VAR
            seed := 100;
            t := Bump(3);
            Print(t);
            Print(seed);
        END_PROGRAM
        """;

    private const string AddRealSt = """
        FUNCTION Add : DINT
        VAR_INPUT a : REAL; b : REAL; END_VAR
        Add := 0;
        END_FUNCTION
        """;

    // ── 驱动 ────────────────────────────────────────────────────

    private string WriteSource(string name, string content)
    {
        var path = Path.Combine(_dir, name);
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

    private string BuildModule(string sourceName, string source, params string[] extra)
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

    // ── 基本链接 + 运行 ─────────────────────────────────────────

    [Fact]
    public void Link_TwoModules_ThenRun_ProducesRealResult()
    {
        var math = BuildModule("Math.st", MathSt);
        var main = BuildModule("Main.st", MainSt, "--ref", math, "--entry", "Main");

        var output = Path.Combine(_dir, "App.stmod");
        var (linkExit, _, linkErr) = Invoke("mod", "link", main, math, "-o", output);

        Assert.Equal(0, linkExit);
        Assert.Equal("", linkErr);
        Assert.True(File.Exists(output));

        var (runExit, runOut, _) = Invoke("run", output);
        Assert.Equal(0, runExit);
        Assert.Contains(runOut.Replace("\r\n", "\n").Split('\n'), l => l.Trim() == "10");
    }

    [Fact]
    public void Link_Output_IsIndependentOfInputFiles()
    {
        var math = BuildModule("Math.st", MathSt);
        var main = BuildModule("Main.st", MainSt, "--ref", math, "--entry", "Main");
        var output = Path.Combine(_dir, "Standalone.stmod");
        var (exit, _, _) = Invoke("mod", "link", main, math, "-o", output);
        Assert.Equal(0, exit);

        // 删掉两个输入 `.stmod`：产物必须仍能独立运行。
        File.Delete(main);
        File.Delete(math);
        Assert.False(File.Exists(main));
        Assert.False(File.Exists(math));

        var (runExit, runOut, _) = Invoke("run", output);
        Assert.Equal(0, runExit);
        Assert.Contains(runOut.Replace("\r\n", "\n").Split('\n'), l => l.Trim() == "10");
    }

    [Fact]
    public void Link_MultiHop_AcrossThreeModules()
    {
        // Leaf ← Step ← Main：三层跨模块调用。
        var leaf = BuildModule("Leaf.st", LeafSt);
        var mid = BuildModule("Step.st", StepSt, "--ref", leaf);
        var main = BuildModule("Main.st", MainChainSt, "--ref", mid, "--entry", "Main");

        var output = Path.Combine(_dir, "Chain.stmod");
        var (exit, _, err) = Invoke("mod", "link", main, mid, leaf, "-o", output);

        Assert.Equal(0, exit);
        Assert.Equal("", err);

        var (runExit, runOut, _) = Invoke("run", output);
        Assert.Equal(0, runExit);
        // Step(5) = Leaf(5) + 1 = 10 + 1 = 11
        Assert.Contains(runOut.Replace("\r\n", "\n").Split('\n'), l => l.Trim() == "11");
    }

    [Fact]
    public void Link_Inspect_ShowsLinkedNoImportsAndMergedExports()
    {
        var math = BuildModule("Math.st", MathSt);
        var main = BuildModule("Main.st", MainSt, "--ref", math, "--entry", "Main");
        var output = Path.Combine(_dir, "App.stmod");
        var (exit, _, _) = Invoke("mod", "link", main, math, "-o", output);
        Assert.Equal(0, exit);

        var (iExit, iOut, iErr) = Invoke("mod", "inspect", output);

        Assert.Equal(0, iExit);
        Assert.Equal("", iErr);
        Assert.Contains("module.linked = true", iOut);
        Assert.Contains("导入符号 (0)", iOut);
        Assert.Contains("链接状态: 已链接", iOut);
        Assert.Contains("Main", iOut);
        Assert.Contains("Add", iOut);
    }

    [Fact]
    public void Link_GlobalSlots_AreRemappedPerModule()
    {
        // Main 的 seed 在槽 0；Math 的 g 必须被重定基到槽 1。
        // 若重定基错误，Bump 会读写 Main 的 seed，结果就不再是 (3, 100)。
        var math = BuildModule("GlobMath.st", GlobMathSt);
        var main = BuildModule("GlobMain.st", GlobMainSt, "--ref", math, "--entry", "Main");

        var output = Path.Combine(_dir, "Glob.stmod");
        var (exit, _, err) = Invoke("mod", "link", main, math, "-o", output);
        Assert.Equal(0, exit);
        Assert.Equal("", err);

        var (runExit, runOut, _) = Invoke("run", output);
        Assert.Equal(0, runExit);
        var lines = runOut.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).ToList();
        Assert.Contains("3", lines);
        Assert.Contains("100", lines);
    }

    [Fact]
    public void Link_Relink_AlreadyLinkedModule_StaysCorrect()
    {
        var math = BuildModule("Math.st", MathSt);
        var main = BuildModule("Main.st", MainSt, "--ref", math, "--entry", "Main");
        var first = Path.Combine(_dir, "App.stmod");
        Assert.Equal(0, Invoke("mod", "link", main, math, "-o", first).Exit);

        var second = Path.Combine(_dir, "App2.stmod");
        var (exit, _, err) = Invoke("mod", "link", first, "-o", second);
        Assert.Equal(0, exit);
        Assert.Equal("", err);

        var (runExit, runOut, _) = Invoke("run", second);
        Assert.Equal(0, runExit);
        Assert.Contains(runOut.Replace("\r\n", "\n").Split('\n'), l => l.Trim() == "10");
    }

    [Fact]
    public void Link_IsDeterministic_ForSameInputs()
    {
        var math = BuildModule("Math.st", MathSt);
        var main = BuildModule("Main.st", MainSt, "--ref", math, "--entry", "Main");

        var a = Path.Combine(_dir, "A.stmod");
        var b = Path.Combine(_dir, "B.stmod");
        Assert.Equal(0, Invoke("mod", "link", main, math, "-o", a).Exit);
        Assert.Equal(0, Invoke("mod", "link", main, math, "-o", b).Exit);

        Assert.Equal(File.ReadAllBytes(a), File.ReadAllBytes(b));
    }

    // ── 拒绝路径 ────────────────────────────────────────────────

    [Fact]
    public void Link_UnresolvedImport_IsRejected_NoOutput()
    {
        var math = BuildModule("Math.st", MathSt);
        var main = BuildModule("Main.st", MainSt, "--ref", math, "--entry", "Main");
        var output = Path.Combine(_dir, "Bad.stmod");

        // 只给 Main，不给提供者 Math。
        var (exit, _, err) = Invoke("mod", "link", main, "-o", output);

        Assert.Equal(1, exit);
        Assert.Contains("未解析的外部引用", err);
        Assert.Contains("Add", err);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Link_DuplicateDefinition_IsRejected()
    {
        var a = BuildModule("MathA.st", MathSt);
        var b = BuildModule("MathB.st", MathSt);
        var output = Path.Combine(_dir, "Dup.stmod");

        var (exit, _, err) = Invoke("mod", "link", a, b, "-o", output);

        Assert.Equal(1, exit);
        Assert.Contains("重复定义", err);
        Assert.Contains("Add", err);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Link_SignatureMismatch_IsRejected()
    {
        var mathA = BuildModule("MathA.st", MathSt);
        var wrong = BuildModule("Wrong.st", AddRealSt);
        var main = BuildModule("Main.st", MainSt, "--ref", mathA, "--entry", "Main");
        var output = Path.Combine(_dir, "Mismatch.stmod");

        // Main 按 DINT 签名导入 Add，Wrong 提供的却是 REAL 签名。
        var (exit, _, err) = Invoke("mod", "link", main, wrong, "-o", output);

        Assert.Equal(1, exit);
        Assert.Contains("签名不一致", err);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Link_MissingEntry_ListsAvailable()
    {
        var math = BuildModule("Math.st", MathSt);
        var main = BuildModule("Main.st", MainSt, "--ref", math, "--entry", "Main");
        var output = Path.Combine(_dir, "NoEntry.stmod");

        var (exit, _, err) = Invoke("mod", "link", main, math, "--entry", "Nope", "-o", output);

        Assert.Equal(1, exit);
        Assert.Contains("找不到入口函数 'Nope'", err);
        Assert.Contains("可用入口", err);
        Assert.Contains("Main", err);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Link_DeclarationEntry_IsRejected()
    {
        var math = BuildModule("Math.st", MathSt);
        var main = BuildModule("Main.st", MainSt, "--ref", math, "--entry", "Main");
        var output = Path.Combine(_dir, "DeclEntry.stmod");

        // Print 是宿主声明，没有可执行实现。
        var (exit, _, err) = Invoke("mod", "link", main, math, "--entry", "Print", "-o", output);

        Assert.Equal(1, exit);
        Assert.Contains("Print", err);
        Assert.Contains("声明", err);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Link_ExplicitEntry_SelectsTheNamedFunction()
    {
        var math = BuildModule("Math.st", MathSt);
        var main = BuildModule("Main.st", MainSt, "--ref", math, "--entry", "Main");
        var output = Path.Combine(_dir, "EntryMain.stmod");

        var (exit, _, err) = Invoke("mod", "link", main, math, "--entry", "Main", "-o", output);
        Assert.Equal(0, exit);
        Assert.Equal("", err);

        var (iExit, iOut, _) = Invoke("mod", "inspect", output);
        Assert.Equal(0, iExit);
        Assert.Contains("EntryPoint", iOut);
    }

    [Fact]
    public void Link_RejectsNonStmodInput()
    {
        var bogus = WriteSource("nope.txt", "not a module");
        var (exit, _, err) = Invoke("mod", "link", bogus, "-o", Path.Combine(_dir, "x.stmod"));

        Assert.Equal(1, exit);
        Assert.Contains("需要 .stmod", err);
    }

    [Fact]
    public void Link_CorruptModule_IsRejected_NoOutput()
    {
        var corrupt = Path.Combine(_dir, "corrupt.stmod");
        File.WriteAllBytes(corrupt, new byte[80]);
        var output = Path.Combine(_dir, "Out.stmod");

        var (exit, _, err) = Invoke("mod", "link", corrupt, "-o", output);

        Assert.Equal(1, exit);
        Assert.Contains("无效", err);
        Assert.False(File.Exists(output));
    }
}
