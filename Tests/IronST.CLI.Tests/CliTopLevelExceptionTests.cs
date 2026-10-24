// CliTopLevelExceptionTests.cs — CLI 顶层异常边界回归测试
//
// 契约（CliRouter.ReportFatal）：
//   · 编译/运行期正常失败 → 诊断 + 退出码 1，不打印 .NET 堆栈
//   · 非预期内部异常 → 标注为工具缺陷 + 退出码 1，同样不打印堆栈
//   · 只有 IRONST_DEBUG=1 才允许输出堆栈
//
// 背景：F5（结构体 `=`）与 A9（数组元素聚合搬运）修复后，原先用来触发的
// 聚合 load 限制已经不再报错。这里改用仍未修复的 C8 寄存器压力
// （函数内临时寄存器只增不减）作为稳定触发源，继续守住
// “内部异常也被顶层边界接住”的契约：退出码 1 + 工具缺陷提示 + 不打印 .NET 堆栈。
// C8 修复后需要再换一个触发源，但本文件守护的是边界本身，不是该缺陷。
using IronST.CLI.Commands;

namespace IronST.CLI.Tests;

[Collection("cli-console")]
public class CliTopLevelExceptionTests : IDisposable
{
    /// <summary>
    /// C8 触发源：40 条数组元素赋值 + 40 条打印，函数内寄存器不回收，
    /// 在代码生成阶段抛 “Out of registers”。
    /// </summary>
    private static readonly string CodeGenFailureSource = BuildRegisterPressureSource();

    private static string BuildRegisterPressureSource()
    {
        var lines = new List<string>
        {
            "PROGRAM Main",
            "VAR",
            "    a : ARRAY[0..39] OF DINT;",
            "END_VAR",
        };
        for (int i = 0; i < 40; i++)
            lines.Add($"a[{i}] := {i};");
        for (int i = 0; i < 40; i++)
            lines.Add($"Print(a[{i}]);");
        lines.Add("END_PROGRAM");
        return string.Join("\n", lines);
    }

    private readonly string _dir;

    public CliTopLevelExceptionTests()
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
    public void Run_InternalCodeGenException_ExitsOne_WithoutStackTrace()
    {
        var st = Write("structcmp.st", CodeGenFailureSource);

        var (exit, _, err) = Invoke("run", st);

        Assert.Equal(1, exit);
        Assert.Contains("内部错误", err);
        Assert.Contains("Out of registers", err);
        Assert.DoesNotContain("Unhandled exception", err);
        Assert.DoesNotContain("   at ", err);
    }

    [Fact]
    public void Compile_InternalCodeGenException_ExitsOne_WithoutStackTrace()
    {
        var st = Write("structcmp2.st", CodeGenFailureSource);
        var isb = Path.Combine(_dir, "structcmp2.isb");

        var (exit, _, err) = Invoke("compile", st, "-o", isb);

        Assert.Equal(1, exit);
        Assert.Contains("内部错误", err);
        Assert.Contains("Out of registers", err);
        Assert.DoesNotContain("Unhandled exception", err);
        Assert.DoesNotContain("   at ", err);
    }

    [Fact]
    public void Run_SyntaxError_StillExitsOne_WithDiagnostic()
    {
        var st = Write("bad.st", """
            PROGRAM Main
            VAR x : INT; END_VAR
            x := 1
            END_PROGRAM
            """);

        var (exit, _, err) = Invoke("run", st);

        Assert.Equal(1, exit);
        Assert.Contains("E1001", err);
        Assert.DoesNotContain("Unhandled exception", err);
    }

    [Fact]
    public void UnknownCommand_StillExitsOne()
    {
        var (exit, _, err) = Invoke("does-not-exist");

        Assert.Equal(1, exit);
        Assert.Contains("未知命令", err);
    }
}
