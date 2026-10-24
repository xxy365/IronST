// FormatCommand.cs — format <file.st ...> [--check]
//
// StFormatter 的 CLI 薄封装：读文件 → 格式化 → 写回。不做任何额外解析，
// 语法错误的文件保持原样（Changed == false）。支持 --check 只检查不写入。
//
// 必须保持可被 IronST.IDE 进程内通过 CliRouter.Run 调用（无子进程依赖）。
using IronST.Frontend.Formatting;

namespace IronST.CLI.Commands;

public static class FormatCommand
{
    public static int Run(string[] args)
    {
        bool check = args.Contains("--check");
        var files = args.Where(a => !a.StartsWith('-')).ToArray();

        if (files.Length == 0)
        {
            Console.Error.WriteLine(
                """
                用法: ironst format <file.st ...> [--check]
                      --check  只检查是否需要格式化，不写回
                退出码: 0=已是格式化状态, 1=有语法错误/文件不存在/--check 需要格式化
                """);
            return 1;
        }

        var formatted = 0;
        var needFormatting = 0;
        var errors = 0;

        foreach (var file in files)
        {
            if (!File.Exists(file))
            {
                Console.Error.WriteLine($"错误: 找不到文件 '{file}'");
                errors++;
                continue;
            }

            try
            {
                var source = File.ReadAllText(file);
                var result = StFormatter.Format(source);

                if (result.HasErrors)
                {
                    Console.Error.WriteLine($"✗ {file}: 源码有语法错误，保持原样（{result.Diagnostics.Count(d => d.Severity == IronST.Common.Diagnostics.DiagnosticSeverity.Error)} 个错误）");
                    errors++;
                    continue;
                }

                if (!result.Changed)
                {
                    Console.WriteLine($"✓ {file}: 已是格式化状态");
                    continue;
                }

                if (check)
                {
                    needFormatting++;
                    Console.WriteLine($"△ {file}: 需要格式化");
                }
                else
                {
                    File.WriteAllText(file, result.Text);
                    formatted++;
                    Console.WriteLine($"✎ {file}: 已格式化");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"✗ {file}: 格式化失败 — {ex.Message}");
                errors++;
            }
        }

        if (!check)
        {
            Console.WriteLine($"\n格式化 {formatted} 个文件。");
            return errors > 0 ? 1 : 0;
        }

        Console.WriteLine($"\n{files.Length - errors - needFormatting} 个文件已是格式化状态，{needFormatting} 个文件需要格式化。");
        return needFormatting > 0 || errors > 0 ? 1 : 0;
    }
}
