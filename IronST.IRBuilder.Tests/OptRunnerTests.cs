using IronST.IRBuilder.OptRunner;
using System.Runtime.InteropServices;
using Xunit;

// 用别名解决命名空间和类同名
using OptRunnerClass = IronST.IRBuilder.OptRunner.OptRunner;

namespace IronST.IRBuilder.Tests;

/// <summary>OptRunner 测试</summary>
public class OptRunnerTests
{
    private const string SimpleIR = @"
define i32 @add(i32 %a, i32 %b) {
entry:
  %sum = add i32 %a, %b
  ret i32 %sum
}
";

    /// <summary>检查 opt 是否可用</summary>
    private static bool IsOptAvailable()
    {
        try
        {
            var rid = OptRunnerClass.GetCurrentRid();
            var exeSuffix = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "";

            var searchPaths = new string[]
            {
                Path.Combine(AppContext.BaseDirectory, "tools", "opt", rid, $"opt{exeSuffix}"),
                Path.Combine(Directory.GetCurrentDirectory(), "tools", "opt", rid, $"opt{exeSuffix}"),
            };

            if (searchPaths.Any(File.Exists))
                return true;

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "opt",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            p?.WaitForExit(3000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    [Fact]
    public void OptRunner_自动查找opt_不存在时给出清晰错误()
    {
        var runner = new OptRunnerClass
        {
            OptPath = "nonexistent_opt_binary_12345"
        };

        var ex = Assert.Throws<InvalidOperationException>(() => runner.RunAsText(SimpleIR));
        Assert.Contains("opt", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OptRunner_文本输出S模式()
    {
        if (!IsOptAvailable()) return;

        var runner = new OptRunnerClass();
        var result = runner.RunAsText(SimpleIR, "-S");
        Assert.Contains("define i32 @add", result);
    }

    [Fact]
    public void OptRunner_二进制输出BC模式()
    {
        if (!IsOptAvailable()) return;

        var runner = new OptRunnerClass();
        var result = runner.RunAsBitcode(SimpleIR);
        Assert.NotEmpty(result);
        Assert.True(result.Length >= 4);
    }

    [Fact]
    public void OptRunner_指定优化级别()
    {
        if (!IsOptAvailable()) return;

        var runner = new OptRunnerClass();
        var result = runner.RunAsText(SimpleIR, "-S -O2");
        Assert.Contains("define", result);
    }

    [Fact]
    public void OptRunner_RID检测不崩溃()
    {
        var rid = OptRunnerClass.GetCurrentRid();
        Assert.NotNull(rid);
        Assert.NotEmpty(rid);
    }
}