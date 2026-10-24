// CliInstructionBudgetTests.cs — CLI 的 --max-instructions 回归测试（C1）
//
// 契约：--max-instructions N 时 VM 执行 N 条指令后以「运行时错误: 指令预算耗尽」退出，
//       退出码 1（不是取消的 130，也不是成功的 0）；正常程序带预算不受影响；
//       非法参数返回 1。
using System.Diagnostics;
using IronST.CLI.Commands;

namespace IronST.CLI.Tests;

[Collection("cli-console")]
public class CliInstructionBudgetTests : IDisposable
{
    private const string InfiniteLoopSource = """
        PROGRAM Main
        VAR
            i : DINT;
        END_VAR
        WHILE TRUE DO
            i := i + 1;
        END_WHILE
        END_PROGRAM
        """;

    private const string HaltingSource = """
        PROGRAM Main
        VAR
            i : DINT;
        END_VAR
        i := 1 + 2;
        END_PROGRAM
        """;

    private readonly string _dir;

    public CliInstructionBudgetTests()
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
            // 清理失败不影响用例结果
        }
    }

    private string Write(string name, string content)
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

    [Fact]
    public void Budget_StopsInfiniteLoop_WithRuntimeErrorAndExitCode1()
    {
        var st = Write("hang.st", InfiniteLoopSource);

        var sw = Stopwatch.StartNew();
        var (exit, _, err) = Invoke("run", st, "--max-instructions", "500");
        sw.Stop();

        Assert.Equal(1, exit);
        Assert.Contains("指令预算耗尽", err);
        Assert.Contains("InstructionBudgetExceeded", err);
        Assert.Contains("500", err);
        Assert.DoesNotContain("已取消", err);
        Assert.True(
            sw.ElapsedMilliseconds < 10_000,
            $"预算未生效：CLI 耗时 {sw.ElapsedMilliseconds} ms（死循环仍未被叫停）");
    }

    [Fact]
    public void Budget_DoesNotAffectProgramThatHalts()
    {
        var st = Write("ok.st", HaltingSource);

        var (exit, outText, err) = Invoke("run", st, "--max-instructions", "60000");

        Assert.Equal(0, exit);
        Assert.Contains("执行完成", outText);
        Assert.Empty(err);
    }

    [Fact]
    public void BudgetIsDeterministic_NotWallClock()
    {
        // 同一预算跑两次：都在同一处停下（错误信息里的 PC 相同），
        // 说明是确定性计数而不是墙钟超时。
        var st = Write("hang2.st", InfiniteLoopSource);

        var (_, _, err1) = Invoke("run", st, "--max-instructions", "777");
        var (_, _, err2) = Invoke("run", st, "--max-instructions", "777");

        Assert.Equal(err1, err2);
        Assert.Contains("at 0x", err1);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("abc")]
    public void InvalidBudget_IsRejected(string value)
    {
        var st = Write("ok2.st", HaltingSource);

        var (exit, _, err) = Invoke("run", st, "--max-instructions", value);

        Assert.Equal(1, exit);
        Assert.Contains("--max-instructions", err);
    }

    [Fact]
    public void MissingBudgetValue_IsRejected()
    {
        var st = Write("ok3.st", HaltingSource);

        var (exit, _, err) = Invoke("run", st, "--max-instructions");

        Assert.Equal(1, exit);
        Assert.Contains("--max-instructions", err);
    }
}
