// CliNullReferenceTests.cs — A2 的 CLI 端到端：空引用解引用带「文件:行」并以 1 退出
//
// 契约：`run` 遇到未绑定 REFERENCE 的解引用时，输出
//       「运行时错误: [NullReference] … （文件:行）」，退出码 1，而不是静默读到槽 0。
using IronST.CLI.Commands;

namespace IronST.CLI.Tests;

[Collection("cli-console")]
public class CliNullReferenceTests : IDisposable
{
    private const string NullSource = """
        PROGRAM Main
        VAR
            r : REFERENCE TO INT;
            x : INT;
            a : ARRAY[0..1] OF INT;
        END_VAR
            x := r^;
            a[x] := 1;
        END_PROGRAM
        """;

    private readonly string _dir;

    public CliNullReferenceTests()
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
    public void NullDereference_ReportsSourceLineAndExitsWithCode1()
    {
        var path = Path.Combine(_dir, "null.st");
        File.WriteAllText(path, NullSource);

        // 解引用语句在源码第 7 行（1 起）。
        var expectedLine = NullSource.Split('\n')
            .Select((text, index) => (text, line: index + 1))
            .First(t => t.text.Contains("x := r^;", StringComparison.Ordinal))
            .line;

        var (exit, _, err) = Invoke("run", path);

        Assert.Equal(1, exit);
        Assert.Contains("[NullReference]", err);
        Assert.Contains("空引用解引用", err);
        Assert.Contains($"null.st:{expectedLine}", err);
        Assert.DoesNotContain("Unhandled exception", err);
    }
}
