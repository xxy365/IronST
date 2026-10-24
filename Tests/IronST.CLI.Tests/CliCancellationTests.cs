// CliCancellationTests.cs — CLI 的 --timeout / 协作式取消回归测试
//
// 契约：--timeout 到期后 VM 在指令边界退出，输出「已取消。」并返回 130；
//       正常程序带 timeout 不受影响；非法参数返回 1。
using System.Diagnostics;
using IronST.CLI.Commands;

namespace IronST.CLI.Tests;

[Collection("cli-console")]
public class CliCancellationTests : IDisposable
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

    public CliCancellationTests()
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
    public void Timeout_StopsInfiniteLoop_WithExitCode130()
    {
        var st = Write("hang.st", InfiniteLoopSource);

        var sw = Stopwatch.StartNew();
        var (exit, _, err) = Invoke("run", st, "--timeout", "200");
        sw.Stop();

        Assert.Equal(130, exit);
        Assert.Contains("已取消", err);
        Assert.DoesNotContain("Unhandled exception", err);
        Assert.True(
            sw.ElapsedMilliseconds < 10_000,
            $"--timeout 未生效：CLI 耗时 {sw.ElapsedMilliseconds} ms（死循环仍未被叫停）");
    }

    [Fact]
    public void Timeout_DoesNotAffectProgramThatHalts()
    {
        var st = Write("ok.st", HaltingSource);

        var (exit, outText, err) = Invoke("run", st, "--timeout", "60000");

        Assert.Equal(0, exit);
        Assert.Contains("执行完成", outText);
        Assert.DoesNotContain("已取消", err);
    }

    [Fact]
    public void InvalidTimeout_IsRejected()
    {
        var st = Write("ok2.st", HaltingSource);

        var (exit, _, err) = Invoke("run", st, "--timeout", "0");

        Assert.Equal(1, exit);
        Assert.Contains("--timeout", err);
    }
}
