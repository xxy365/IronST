// CliMultiFileTests.cs — CLI 多文件编译 / 运行的端到端与回归测试。
//
// 被测：CliRouter → CompileCommand / RunCommand（真实 CLI 入口，含参数解析）
// 观测：捕获 Console.Out / Console.Error，断言退出码与输出文本。
//
// 为什么能"断言最终输出"：CLI 的 run 提供默认宿主环境（Print → stdout），
// 跨文件调用后的结果由 Print 落到 stdout。
//
// 临时文件：全部落在 %TEMP%/ironst-cli-tests/<guid>/，用例结束删除该目录；
// 不引用开发者机器上的固定路径。
using IronST.CLI.Commands;

namespace IronST.CLI.Tests;

// Console.SetOut/SetError 是进程级全局状态 ⇒ 该类不与其它集合并行。
[CollectionDefinition("cli-console", DisableParallelization = true)]
public sealed class CliConsoleCollection;

[Collection("cli-console")]
public class CliMultiFileTests : IDisposable
{
    private readonly string _dir;

    public CliMultiFileTests()
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

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
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

    // ── 源文件片段 ────────────────────────────────────────────

    private const string AddFunction = """
        FUNCTION Add : DINT
        VAR_INPUT a : DINT; b : DINT; END_VAR
        Add := a + b;
        END_FUNCTION
        """;

    private const string MainCallsAddAndPrints = """
        PROGRAM Main
        VAR x : DINT; END_VAR
        x := Add(40, 2);
        Print(x);
        END_PROGRAM
        """;

    private const string MainCallsAddNoPrint = """
        PROGRAM Main
        VAR x : DINT; END_VAR
        x := Add(1, 2);
        END_PROGRAM
        """;

    private const string PointType = """
        TYPE Point : STRUCT x : DINT; y : DINT; END_STRUCT; END_TYPE;
        """;

    private const string MainUsesPoint = """
        PROGRAM Main
        VAR p : Point; END_VAR
        p.x := 3; p.y := 4;
        Print(p.x);
        END_PROGRAM
        """;

    private const string ProgramAlpha = """
        PROGRAM Alpha
        VAR a : DINT; END_VAR
        a := 1; Print(a);
        END_PROGRAM
        """;

    private const string ProgramBeta = """
        PROGRAM Beta
        VAR b : DINT; END_VAR
        b := 2; Print(b);
        END_PROGRAM
        """;

    // compile 不注入宿主库 ⇒ 编译用程序不带 Print。
    private const string SingleNoPrint = """
        PROGRAM Main
        VAR x : DINT; END_VAR
        x := 5;
        END_PROGRAM
        """;

    private const string AlphaNoPrint = """
        PROGRAM Alpha
        VAR a : DINT; END_VAR
        a := 1;
        END_PROGRAM
        """;

    private const string BetaNoPrint = """
        PROGRAM Beta
        VAR b : DINT; END_VAR
        b := 2;
        END_PROGRAM
        """;

    // ═══════════════════════════════════════════════════════════
    //  1. 单文件 compile 兼容性
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Compile_SingleFile_StillSucceedsAndWritesIr()
    {
        var one = Write("one.st", SingleNoPrint);
        var (exit, @out, err) = Invoke("compile", one);

        Assert.Equal(0, exit);
        Assert.Equal("", err);
        Assert.True(File.Exists(Path.ChangeExtension(one, ".ll")));
        Assert.Contains("已写出 IR", @out);
    }

    [Fact]
    public void Compile_SingleFile_MultiplePrograms_KeepsLegacySelection()
    {
        // 单文件保持既有行为：compile 不因多 PROGRAM 报错（run 才需要唯一入口）。
        var both = Write("both.st", AlphaNoPrint + "\n" + BetaNoPrint);
        var (exit, _, err) = Invoke("compile", both);

        Assert.Equal(0, exit);
        Assert.Equal("", err);
    }

    // ═══════════════════════════════════════════════════════════
    //  2. 多文件 compile
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Compile_MultipleFiles_Succeeds()
    {
        var add = Write("add.st", AddFunction);
        var main = Write("main.st", MainCallsAddNoPrint);

        var (exit, @out, err) = Invoke("compile", add, main);

        Assert.Equal(0, exit);
        Assert.Equal("", err);
        Assert.True(File.Exists(Path.ChangeExtension(add, ".ll")));
        Assert.Contains("已写出 IR", @out);
    }

    [Fact]
    public void Compile_MultipleFiles_WritesBytecodeOutput()
    {
        var add = Write("add.st", AddFunction);
        var main = Write("main.st", MainCallsAddNoPrint);
        var outFile = Path.Combine(_dir, "app.isb");

        var (exit, @out, err) = Invoke("compile", add, main, "-o", outFile);

        Assert.Equal(0, exit);
        Assert.Equal("", err);
        Assert.True(File.Exists(outFile));
        Assert.Contains("已写出字节码", @out);
    }

    // compile 必须与 run 一致地认识宿主函数（Print）——
    // 否则用户给出的目标命令 `compile Main.st Math.st Types.st` 会因
    // "未声明的函数 'Print'" 直接失败。
    [Fact]
    public void Compile_MultipleFiles_ProgramUsingHostFunction_Succeeds()
    {
        var math = Write("math.st", AddFunction);
        var types = Write("types.st", PointType);
        var main = Write("main.st", MainCallsAddAndPrints);

        var (exit, @out, err) = Invoke("compile", main, math, types);

        Assert.Equal(0, exit);
        Assert.Equal("", err);
        Assert.True(File.Exists(Path.ChangeExtension(main, ".ll")));
        Assert.Contains("已写出 IR", @out);
    }

    [Fact]
    public void Compile_SingleFile_ProgramUsingHostFunction_Succeeds()
    {
        var one = Write("one.st", """
            PROGRAM Main
            VAR x : DINT; END_VAR
            x := 7; Print(x);
            END_PROGRAM
            """);

        var (exit, _, err) = Invoke("compile", one);

        Assert.Equal(0, exit);
        Assert.Equal("", err);
    }

    // ═══════════════════════════════════════════════════════════
    //  3~5. 多文件 run：跨文件函数调用 + 实际输出（真实端到端）
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Run_MultipleFiles_CrossFileFunctionCall_PrintsResult()
    {
        var add = Write("add.st", AddFunction);
        var main = Write("main.st", MainCallsAddAndPrints);

        var (exit, @out, err) = Invoke("run", add, main);

        Assert.Equal(0, exit);
        Assert.Equal("", err);
        Assert.Contains("42", @out);
        Assert.Contains("执行完成", @out);
    }

    [Fact]
    public void Run_MultipleFiles_CrossFileTypeReference_PrintsField()
    {
        var types = Write("types.st", PointType);
        var main = Write("main.st", MainUsesPoint);

        var (exit, @out, _) = Invoke("run", types, main);

        Assert.Equal(0, exit);
        Assert.Contains("3", @out);
    }

    [Fact]
    public void Run_SingleFile_PrintsViaHost()
    {
        var one = Write("one.st", """
            PROGRAM Main
            VAR x : DINT; END_VAR
            x := 7; Print(x);
            END_PROGRAM
            """);

        var (exit, @out, _) = Invoke("run", one);

        Assert.Equal(0, exit);
        Assert.Contains("7", @out);
    }

    // ═══════════════════════════════════════════════════════════
    //  6~8. 入口选择
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Run_ExplicitEntry_SelectsNamedProgram()
    {
        var alpha = Write("alpha.st", ProgramAlpha);
        var beta = Write("beta.st", ProgramBeta);

        var (exit, @out, err) = Invoke("run", alpha, beta, "--entry", "Beta");

        Assert.Equal(0, exit);
        Assert.Equal("", err);
        Assert.Contains("2", @out);
    }

    [Fact]
    public void Run_MultiplePrograms_NoEntry_FailsDeterministically()
    {
        var alpha = Write("alpha.st", ProgramAlpha);
        var beta = Write("beta.st", ProgramBeta);

        var (exit, _, err) = Invoke("run", alpha, beta);

        Assert.Equal(1, exit);
        Assert.Contains("Alpha", err);
        Assert.Contains("Beta", err);
        Assert.Contains("入口", err);
    }

    [Fact]
    public void Run_ExplicitEntryNotFound_FailsClearly()
    {
        var alpha = Write("alpha.st", ProgramAlpha);
        var beta = Write("beta.st", ProgramBeta);

        var (exit, _, err) = Invoke("run", alpha, beta, "--entry", "Nope");

        Assert.Equal(1, exit);
        Assert.Contains("Nope", err);
        // 不得静默回退到 Alpha/Beta
        Assert.DoesNotContain("执行完成", err);
    }

    [Fact]
    public void Run_ExplicitEntryNotFound_SingleFile_FailsClearly()
    {
        var one = Write("one.st", SingleNoPrint);
        var (exit, _, err) = Invoke("run", one, "--entry", "Ghost");

        Assert.Equal(1, exit);
        Assert.Contains("Ghost", err);
    }

    // ═══════════════════════════════════════════════════════════
    //  9~10. 输入错误 / 语法语义错误
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Run_MissingFile_FailsClearly()
    {
        var missing = Path.Combine(_dir, "not-there.st");
        var (exit, _, err) = Invoke("run", missing);

        Assert.Equal(1, exit);
        Assert.Contains("not-there.st", err);
    }

    [Fact]
    public void Compile_MissingFile_FailsClearly()
    {
        var missing = Path.Combine(_dir, "gone.st");
        var (exit, _, err) = Invoke("compile", missing);

        Assert.Equal(1, exit);
        Assert.Contains("gone.st", err);
    }

    [Fact]
    public void Run_MultipleFiles_WithSemanticError_ReportsError()
    {
        var add = Write("add.st", AddFunction);
        var broken = Write("broken.st", """
            PROGRAM Main
            VAR x : DINT; END_VAR
            x := undeclaredVar + 1;
            END_PROGRAM
            """);

        var (exit, _, err) = Invoke("run", add, broken);

        Assert.Equal(1, exit);
        Assert.NotEqual("", err);
        // 必须是"源码有问题"，而不是参数解析问题
        Assert.DoesNotContain("多余", err);
    }

    [Fact]
    public void Compile_MultipleFiles_WithSemanticError_ReportsError()
    {
        var add = Write("add.st", AddFunction);
        var broken = Write("broken.st", """
            PROGRAM Main
            VAR x : DINT; END_VAR
            x := undeclaredVar + 1;
            END_PROGRAM
            """);

        var (exit, _, err) = Invoke("compile", add, broken);

        Assert.Equal(1, exit);
        Assert.NotEqual("", err);
        Assert.DoesNotContain("多余", err);
    }

    [Fact]
    public void Run_MultipleFiles_WithSyntaxError_ReportsError()
    {
        var add = Write("add.st", AddFunction);
        var broken = Write("broken.st", """
            PROGRAM Main
            VAR x : DINT; END_VAR
            x := ;
            END_PROGRAM
            """);

        var (exit, _, err) = Invoke("run", add, broken);

        Assert.Equal(1, exit);
        Assert.NotEqual("", err);
        Assert.DoesNotContain("多余", err);
    }

    // ═══════════════════════════════════════════════════════════
    //  11. 文件顺序不影响入口选择
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Run_FileOrderDoesNotChangeEntrySelection()
    {
        var add = Write("add.st", AddFunction);
        var main = Write("main.st", MainCallsAddAndPrints);

        var (exit1, out1, _) = Invoke("run", add, main);
        var (exit2, out2, _) = Invoke("run", main, add);

        Assert.Equal(0, exit1);
        Assert.Equal(0, exit2);
        Assert.Contains("42", out1);
        Assert.Contains("42", out2);
    }

    // ═══════════════════════════════════════════════════════════
    //  回归：字节码路径
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Run_CompiledBytecode_StillRuns()
    {
        var src = Write("prog.st", SingleNoPrint);
        var isb = Path.Combine(_dir, "prog.isb");

        var (cExit, _, cErr) = Invoke("compile", src, "-o", isb);
        Assert.Equal(0, cExit);
        Assert.Equal("", cErr);
        Assert.True(File.Exists(isb));

        var (rExit, rOut, _) = Invoke("run", isb);
        Assert.Equal(0, rExit);
        Assert.Contains("执行完成", rOut);
    }

    [Fact]
    public void Run_CompiledBytecode_WithHostFunction_RegistersHost()
    {
        // compile 会为 Print 发出 HCALL；run <isb> 必须注册同一宿主实现，
        // 否则出现"编译成功但 VM 缺少必要注册信息"（HCALL unregistered）。
        var src = Write("prints.st", """
            PROGRAM Main
            VAR x : DINT; END_VAR
            x := 42; Print(x);
            END_PROGRAM
            """);
        var isb = Path.Combine(_dir, "prints.isb");

        var (cExit, _, _) = Invoke("compile", src, "-o", isb);
        Assert.Equal(0, cExit);
        Assert.True(File.Exists(isb));

        var (rExit, rOut, rErr) = Invoke("run", isb);
        Assert.Equal(0, rExit);
        Assert.Equal("", rErr);
        Assert.Contains("42", rOut);
    }

    [Fact]
    public void Run_BytecodeInputWithMultipleFiles_FailsClearly()
    {
        var isb = Path.Combine(_dir, "x.isb");
        File.WriteAllBytes(isb, [0x54, 0x53, 0x52, 0x49]);
        var src = Write("a.st", SingleNoPrint);

        var (exit, _, err) = Invoke("run", src, isb);

        Assert.Equal(1, exit);
        Assert.Contains("字节码", err);
    }
}
