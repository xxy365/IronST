using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace IronST.IRBuilder.OptRunner;

/// <summary>
/// 通过 stdin/stdout 管道调用 LLVM opt 工具，全程不写磁盘。
/// 自动查找集成在项目中的 opt 二进制文件。
/// </summary>
public class OptRunner
{
    /// <summary>
    /// opt 可执行文件路径。
    /// 默认为空，表示自动查找（按优先级：嵌入二进制 → 系统 PATH）。
    /// 设置此属性可手动指定路径。
    /// </summary>
    public string? OptPath { get; set; }

    /// <summary>超时时间（毫秒），默认 60 秒</summary>
    public int TimeoutMs { get; set; } = 60_000;

    // ================================================================
    // 公共方法
    // ================================================================

    /// <summary>
    /// 以文本模式运行 opt：.ll → opt → .ll 文本
    /// </summary>
    public string RunAsText(string irInput, string args = "-S")
    {
        if (!args.Contains("-S"))
            args = "-S " + args;

        var result = RunOpt(irInput, args);
        return result.stdout;
    }

    /// <summary>
    /// 以二进制模式运行 opt：.ll → opt → .bc 字节码
    /// </summary>
    public byte[] RunAsBitcode(string irInput, string args = "")
    {
        var result = RunOpt(irInput, args);
        return result.stdoutBytes;
    }

    // ================================================================
    // 查找 opt 二进制
    // ================================================================

    /// <summary>
    /// 获取 opt 可执行文件的完整路径。
    /// 查找顺序：
    /// 1. 用户显式指定的 OptPath
    /// 2. 可执行文件同目录下的 tools/opt/{rid}/opt(.exe)
    /// 3. IronST.IRBuilder.dll 同目录下的 tools/opt/{rid}/opt(.exe)
    /// 4. 系统 PATH 中的 "opt"
    /// </summary>
    private string ResolveOptPath()
    {
        // 1. 用户显式指定
        if (!string.IsNullOrEmpty(OptPath))
            return OptPath;

        var exeSuffix = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "";
        var rid = GetCurrentRid();

        // 2. 入口程序集所在目录
        var entryDir = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location);
        if (entryDir != null)
        {
            var bundled = Path.Combine(entryDir, "tools", "opt", rid, $"opt{exeSuffix}");
            if (File.Exists(bundled))
                return bundled;
        }

        // 3. 当前程序集（IronST.IRBuilder.dll）所在目录
        var libDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (libDir != null)
        {
            var bundled = Path.Combine(libDir, "tools", "opt", rid, $"opt{exeSuffix}");
            if (File.Exists(bundled))
                return bundled;
        }

        // 4. 当前工作目录（开发时常见）
        var cwdBundled = Path.Combine(
            Directory.GetCurrentDirectory(),
            "tools",
            "opt",
            rid,
            $"opt{exeSuffix}"
        );
        if (File.Exists(cwdBundled))
            return cwdBundled;

        // 5. 回退到系统 PATH
        return $"opt{exeSuffix}";
    }

    /// <summary>获取当前平台的 Runtime Identifier</summary>
    internal static string GetCurrentRid()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "win-x64";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "linux-arm64"
                : "linux-x64";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "osx-arm64"
                : "osx-x64";

        // 未知平台，返回一个合理的默认值
        return RuntimeInformation.RuntimeIdentifier;
    }

    // ================================================================
    // 执行 opt 进程
    // ================================================================
    private (string stdout, byte[] stdoutBytes, string stderr) RunOpt(string irInput, string args)
    {
        var optPath = ResolveOptPath();

        var psi = new ProcessStartInfo
        {
            FileName = optPath,
            Arguments = args,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };

        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException(
                $"无法启动 opt 工具。\n"
                    + $"  尝试的路径: {optPath}\n"
                    + $"  当前平台 RID: {GetCurrentRid()}\n"
                    + $"  请确认已运行 tools/download-opt 脚本下载 opt，\n"
                    + $"  或将 opt 添加到系统 PATH。\n"
                    + $"  原始错误: {ex.Message}",
                ex
            );
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"启动 opt 失败 (路径: {optPath}): {ex.Message}",
                ex
            );
        }

        process.StandardInput.Write(irInput);
        process.StandardInput.Close();

        var stdoutTask = ReadAllBytesAsync(process.StandardOutput.BaseStream);
        var stderrTask = process.StandardError.ReadToEndAsync();

        // 先等待流读取完成，避免进程因管道缓冲区满而阻塞导致死锁
        // stdoutTask 是 Task<byte[]>，stderrTask 是 Task<string>，泛型参数不同
        // 因此用 Task 类型数组承载
        if (!Task.WaitAll(new Task[] { stdoutTask, stderrTask }, TimeoutMs))
        {
            try
            {
                process.Kill(true);
            }
            catch { }
            throw new TimeoutException($"opt 工具在 {TimeoutMs}ms 内未完成，已终止。");
        }

        // 流读完后确保进程完全退出
        process.WaitForExit();

        var stdoutBytes = stdoutTask.Result;
        var stdoutText = System.Text.Encoding.UTF8.GetString(stdoutBytes);
        var stderr = stderrTask.Result;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"opt 执行失败（退出码 {process.ExitCode}）:\n"
                    + $"  路径: {optPath}\n"
                    + $"  参数: {args}\n"
                    + $"  stderr:\n{stderr}"
            );
        }

        return (stdoutText, stdoutBytes, stderr);
    }

    private static async Task<byte[]> ReadAllBytesAsync(System.IO.Stream stream)
    {
        using var ms = new System.IO.MemoryStream();
        await stream.CopyToAsync(ms);
        return ms.ToArray();
    }
}
