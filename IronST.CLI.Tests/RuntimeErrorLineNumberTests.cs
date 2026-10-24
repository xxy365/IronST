// RuntimeErrorLineNumberTests.cs — CLI 运行期错误的源行号回归。
//
// 位置由 VM 单点回填（见 IronST.Runtime.Shared/RuntimeError.cs、SourceResolver.cs）：
// 宿主只要把 PC → (文件, 行) 表交给 VMOptions.SourceLocator，异常 Message 里就带
// 「（文件:行）」。CLI 侧一律打印 ex.Message，不再各自查表。
//
// 本文件锁两件事：
//   1) `run <file.st>` 端到端：数组越界报错带上真实的 文件:行号；
//   2) `run <file.isb>`：编译落盘的旁车 <name>.map.json 让字节码输入也能报行号。
using IronST.CLI.Commands;

namespace IronST.CLI.Tests;

[Collection("cli-console")]
public class RuntimeErrorLineNumberTests : IDisposable
{
    private readonly string _dir;

    public RuntimeErrorLineNumberTests()
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
            // 忽略清理失败（进程仍持有句柄等）。
        }
    }

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

    /// <summary>越界语句刻意落在第 6 行（VAR/END_VAR/空行各占一行）。</summary>
    private const string BoundsAtLine6 = """
        PROGRAM Main
        VAR
            g : ARRAY[0..99] OF DINT;
        END_VAR

            g[999] := 1;
        END_PROGRAM
        """;

    [Fact]
    public void Run_ArrayBounds_ReportsSourceLine()
    {
        var path = Write("probe.st", BoundsAtLine6);

        var (exit, _, err) = Invoke("run", path);

        Assert.Equal(1, exit);
        Assert.Contains("[ArrayBounds]", err);
        Assert.Contains($"{path}:6", err);
    }

    [Fact]
    public void Run_BytecodeWithSidecarMap_ReportsSourceLine()
    {
        var path = Write("probe.st", BoundsAtLine6);
        var isb = Path.Combine(_dir, "probe.isb");

        var (compileExit, _, compileErr) = Invoke("compile", path, "-o", isb);
        Assert.Equal(0, compileExit);
        Assert.True(File.Exists(isb), compileErr);

        // 旁车源映射与二进制同名、扩展名换成 .map.json。
        var mapPath = Path.ChangeExtension(isb, ".map.json");
        Assert.True(File.Exists(mapPath), $"缺少旁车源映射: {mapPath}");

        var (exit, _, err) = Invoke("run", isb);

        Assert.Equal(1, exit);
        Assert.Contains("[ArrayBounds]", err);
        Assert.Contains($"{path}:6", err);
    }
}
