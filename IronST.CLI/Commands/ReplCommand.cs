// ReplCommand.cs — repl：交互式执行 ST 代码
using System.Text;
using IronST.Frontend;
using IronST.Frontend.Analysis;
using IronST.Runtime.Shared;

namespace IronST.CLI.Commands;

public static class ReplCommand
{
    private static readonly IReadOnlyList<ExternalLibrary> Libraries =
        [IECStandardLibrary.Create()];

    public static int Run(string[] args)
    {
        Console.WriteLine("IronST REPL — 输入 IEC 61131-3 ST 代码，空行执行，exit 退出");
        Console.WriteLine("示例: PROGRAM P VAR x : DINT; END_VAR x := 1 + 2; END_PROGRAM");

        var buffer = new StringBuilder();
        while (true)
        {
            Console.Write("st> ");
            var line = Console.ReadLine();
            if (line == null)
                break;

            var trimmed = line.Trim();
            if (trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("quit", StringComparison.OrdinalIgnoreCase))
                break;

            if (trimmed.Length == 0)
            {
                if (buffer.Length > 0)
                {
                    RunBlock(buffer.ToString());
                    buffer.Clear();
                }
                continue;
            }

            buffer.AppendLine(line);
        }

        return 0;
    }

    private static void RunBlock(string source)
    {
        try
        {
            var result = IronRuntime.Compile(source, "repl.st", libraries: Libraries);
            IronRuntime.Run(result);
            Console.WriteLine("✓ 执行完成");
        }
        catch (CompilationException ex)
        {
            Console.Error.WriteLine(ex.Message);
        }
        catch (RuntimeErrorException ex)
        {
            // 源位置已由 VM 按 SourceLocator 回填进异常 Message。
            Console.Error.WriteLine($"运行时错误: {ex.Message}");
        }
    }
}
