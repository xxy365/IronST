// CliStringStandardLibraryTests.cs — CLI 默认宿主环境是否接入 IEC 字符串标准库。
//
// 背景：字符串标准函数（LEN/LEFT/RIGHT/MID/CONCAT/INSERT/DELETE/REPLACE/FIND）
// 的实现在 IronST.Runtime.Shared/StringStandardLibrary.cs，但 CLI 的默认宿主环境
// 此前只接线了 Print —— 于是 `ironst run script.st` 只要用到字符串函数就以
// 「未声明的函数」失败，实现形同虚设。
//
// 本文件从真实 CLI 入口（CliRouter）驱动，断言三件事：
//   1. 九个字符串函数全部可用（编译期声明 / 名→HCALL ID / 运行期注册三源一致）；
//   2. 代表性返回值、参数边界与真实 stdout；
//   3. 默认环境**没有**顺带注入数学声明（F13 未解，不得把
//      「未声明的函数 'SQRT'」换成后端「has no host handler」）。
//
// 临时文件落在 %TEMP%/ironst-cli-tests/<guid>/，用例结束删除；不引用固定绝对路径。
using IronST.CLI.Commands;

namespace IronST.CLI.Tests;

// Console.SetOut/SetError 是进程级全局状态 ⇒ 与 CliMultiFileTests 同集合串行。
[Collection("cli-console")]
public class CliStringStandardLibraryTests : IDisposable
{
    private readonly string _dir;

    public CliStringStandardLibraryTests()
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

    /// <summary>把捕获的 stdout 归一化为逐行数组（去掉平台换行差异与结尾空行）。</summary>
    private static string[] Lines(string output) =>
        output.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

    // ── 1. 九个字符串函数全部可用（代表值 + 真实 stdout）─────────

    [Fact]
    public void Run_AllNineStringFunctions_PrintsExpectedStdout()
    {
        var src = Write("nine.st", """
            PROGRAM Main
            VAR
                s : STRING;
                n : DINT;
            END_VAR
                s := CONCAT('hello', 'world');
                Print(s);
                n := LEN(s);
                Print(n);
                s := LEFT('hello', 3);
                Print(s);
                s := RIGHT('hello', 3);
                Print(s);
                s := MID('hello', 2, 2);
                Print(s);
                s := INSERT('hello', 'XY', 3);
                Print(s);
                s := DELETE('hello', 2, 2);
                Print(s);
                s := REPLACE('hello', 'XY', 2, 2);
                Print(s);
                n := FIND('hello', 'll');
                Print(n);
            END_PROGRAM
            """);

        var (exit, @out, err) = Invoke("run", src);

        Assert.Equal(0, exit);
        Assert.Equal("", err);
        Assert.Equal(
            [
                "helloworld", // CONCAT
                "10",         // LEN
                "hel",        // LEFT
                "llo",        // RIGHT
                "el",         // MID(length=2, start=2)
                "heXYllo",    // INSERT
                "hlo",        // DELETE(length=2, start=2)
                "hXYlo",      // REPLACE(length=2, start=2)
                "3",          // FIND
                "[run] 执行完成",
            ],
            Lines(@out));
    }

    // ── 2. 参数边界：越界钳制而非溢出/异常 ───────────────────────

    [Fact]
    public void Run_StringFunctionBoundaries_AreClamped()
    {
        var src = Write("bounds.st", """
            PROGRAM Main
            VAR
                t : STRING;
                n : DINT;
            END_VAR
                t := LEFT('hello', 99);
                Print(t);
                t := RIGHT('hello', 0);
                n := LEN(t);
                Print(n);
                t := MID('hello', 5, 99);
                n := LEN(t);
                Print(n);
                n := FIND('hello', 'zz');
                Print(n);
                t := DELETE('hello', 99, 2);
                Print(t);
            END_PROGRAM
            """);

        var (exit, @out, err) = Invoke("run", src);

        Assert.Equal(0, exit);
        Assert.Equal("", err);
        Assert.Equal(
            [
                "hello", // LEFT：n >= 长度 → 原串
                "0",     // RIGHT：n = 0 → 空串
                "0",     // MID：start 越界 → 空串
                "0",     // FIND：未找到 → 0
                "h",     // DELETE：n 超长 → 删到串尾
                "[run] 执行完成",
            ],
            Lines(@out));
    }

    [Fact]
    public void Run_StringFunctionWrongArity_FailsWithArityDiagnostic()
    {
        var src = Write("arity.st", """
            PROGRAM Main
            VAR t : STRING; END_VAR
                t := CONCAT('only-one');
                Print(t);
            END_PROGRAM
            """);

        var (exit, _, err) = Invoke("run", src);

        Assert.Equal(1, exit);
        Assert.Contains("CONCAT", err);
        Assert.Contains("2 个实参", err);
    }

    // ── 3. 不得顺带注入数学声明（F13 未解）───────────────────────

    [Fact]
    public void Run_MathFunction_StillReportsUndeclared_NoHostHandlerLeak()
    {
        var src = Write("math.st", """
            PROGRAM Main
            VAR r : LREAL; END_VAR
                r := SQRT(4.0);
                Print(r);
            END_PROGRAM
            """);

        var (exit, @out, err) = Invoke("run", src);

        Assert.Equal(1, exit);
        Assert.Contains("未声明的函数 'SQRT'", err);
        // 后端「has no host handler」只会在数学声明被注入、却无宿主实现时出现。
        Assert.DoesNotContain("host handler", err);
        Assert.DoesNotContain("执行完成", @out);
    }

    // ── 4. compile 路径与字节码路径同源 ──────────────────────────

    [Fact]
    public void Compile_ProgramUsingStringFunctions_Succeeds()
    {
        var src = Write("compile-me.st", """
            PROGRAM Main
            VAR s : STRING; n : DINT; END_VAR
            s := CONCAT('ab', 'cd');
            n := LEN(s);
            Print(s);
            Print(n);
            END_PROGRAM
            """);

        var (exit, @out, err) = Invoke("compile", src);

        Assert.Equal(0, exit);
        Assert.Equal("", err);
        Assert.True(File.Exists(Path.ChangeExtension(src, ".ll")));
        Assert.Contains("已写出 IR", @out);
    }

    [Fact]
    public void Run_CompiledBytecode_RegistersStringHostFunctions()
    {
        var src = Write("roundtrip.st", """
            PROGRAM Main
            VAR s : STRING; n : DINT; END_VAR
            s := CONCAT('a', 'b');
            Print(s);
            n := LEN(s);
            Print(n);
            END_PROGRAM
            """);
        var isb = Path.Combine(_dir, "roundtrip.isb");

        var (cExit, _, cErr) = Invoke("compile", src, "-o", isb);
        Assert.Equal(0, cExit);
        Assert.Equal("", cErr);
        Assert.True(File.Exists(isb));

        var (rExit, rOut, rErr) = Invoke("run", isb);

        Assert.Equal(0, rExit);
        Assert.Equal("", rErr);
        Assert.Contains("ab", rOut);
        Assert.Contains("2", rOut);
    }
}
