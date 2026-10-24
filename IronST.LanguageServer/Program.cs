// Program.cs — IronST Language Server 入口
//
// 用法:
//   ironst-ls [--stdio] [--log <path>]
//
// 通过 stdio 与客户端通信（VS Code / Neovim / opencode ...）。
// stdout 严格保留给 LSP 协议，任何日志都写 stderr 或 --log 指定的文件。
using IronST.LanguageServer.Server;
using System.Text;

var log = OpenLog(args);

try
{
    // 协议层走原始流，绕开 Console 的编码与缓冲行为
    var input = Console.OpenStandardInput();
    var output = Console.OpenStandardOutput();

    var server = new LanguageServer(input, output, log);

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    log?.WriteLine("[ironst-ls] 启动，等待 initialize...");
    return await server.RunAsync(cts.Token);
}
catch (Exception ex)
{
    log?.WriteLine($"[ironst-ls] 致命错误: {ex}");
    Console.Error.WriteLine($"ironst-ls fatal: {ex.Message}");
    return 1;
}
finally
{
    log?.Dispose();
}

static TextWriter? OpenLog(string[] args)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == "--log")
        {
            try
            {
                return new StreamWriter(args[i + 1], append: true, Encoding.UTF8)
                {
                    AutoFlush = true,
                };
            }
            catch
            {
                return null;
            }
        }
    }

    // 没给 --log 就写 stderr，不干扰协议
    return Console.Error;
}
