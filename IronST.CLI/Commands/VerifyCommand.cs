// VerifyCommand.cs — verify <file.st>
using IronST.Common.Diagnostics;
using IronST.Frontend;

namespace IronST.CLI.Commands;

public static class VerifyCommand
{
    public static int Run(string[] args)
    {
        var input = args.FirstOrDefault();
        if (input == null || !File.Exists(input))
        {
            Console.Error.WriteLine($"错误: 找不到文件 '{input}'");
            return 1;
        }

        var result = FrontendPipeline.Analyze(File.ReadAllText(input), input);

        int errors = 0;
        foreach (var d in result.Diagnostics)
        {
            Console.WriteLine($"[{d.Severity,-7}] {d.Span} {d.Code}: {d.Message}");
            if (d.Severity == DiagnosticSeverity.Error)
                errors++;
        }

        if (errors == 0)
            Console.WriteLine($"✓ 验证通过: {input}（{result.Diagnostics.Count} 条诊断，无错误）");
        else
            Console.WriteLine($"✗ 验证失败: {errors} 个错误");

        return errors > 0 ? 1 : 0;
    }
}
