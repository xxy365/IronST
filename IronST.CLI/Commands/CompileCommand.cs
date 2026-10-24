// CompileCommand.cs — compile <file.st> [-o out.isb] [--ir out.ll] [--bc out.bc] [--opt 0|1|2]
using IronST.BytecodeGen;
using IronST.IRBuilder.OptRunner;

namespace IronST.CLI.Commands;

public static class CompileCommand
{
    public static int Run(string[] args)
    {
        var inputs = new List<string>();
        string? output = null, irPath = null, bcPath = null;
        int opt = 0;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o":
                    if (!TryNext(args, ref i, "-o", out output))
                        return 1;
                    break;
                case "--ir":
                    if (!TryNext(args, ref i, "--ir", out irPath))
                        return 1;
                    break;
                case "--bc":
                    if (!TryNext(args, ref i, "--bc", out bcPath))
                        return 1;
                    break;
                case "--opt":
                    if (!TryNext(args, ref i, "--opt", out var optStr))
                        return 1;
                    if (!int.TryParse(optStr, out opt) || opt is < 0 or > 2)
                    {
                        Console.Error.WriteLine("错误: --opt 必须是 0、1 或 2");
                        return 1;
                    }
                    break;
                default:
                    if (args[i].StartsWith('-'))
                    {
                        Console.Error.WriteLine($"错误: 未知选项 '{args[i]}'");
                        return 1;
                    }
                    inputs.Add(args[i]);
                    break;
            }
        }

        if (inputs.Count == 0)
        {
            Console.Error.WriteLine("错误: 缺少输入文件 (compile <file.st> [更多 .st ...])");
            return 1;
        }

        // 逐个读取源文件：存在性与可读性分开报告。
        var files = new List<(string Source, string FileName)>(inputs.Count);
        foreach (var f in inputs)
        {
            if (!File.Exists(f))
            {
                Console.Error.WriteLine($"错误: 找不到文件 '{f}'");
                return 1;
            }
            try
            {
                files.Add((File.ReadAllText(f), f));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"错误: 无法读取文件 '{f}': {ex.Message}");
                return 1;
            }
        }

        // 未指定任何输出 → 默认输出 IR 文本（与**首个**源文件同名 .ll，对齐 README）
        if (output == null && irPath == null && bcPath == null)
            irPath = Path.ChangeExtension(inputs[0], ".ll");

        CompileResult result;
        try
        {
            // 与 run 共用默认宿主环境（Print）：编译期声明与运行期注册同源，
            // 否则 `compile Main.st Math.st` 会因「未声明的函数 'Print'」失败。
            // 单文件沿用既有 Compile（保留原有入口回退行为）；多文件走合并编译入口
            // CompileFiles —— 不另写一套前端/IR/字节码管线。
            result = files.Count == 1
                ? IronRuntime.Compile(
                    files[0].Source,
                    files[0].FileName,
                    hostFunctions: DefaultHostEnvironment.FunctionMap,
                    libraries: DefaultHostEnvironment.Libraries)
                : IronRuntime.CompileFiles(
                    files,
                    hostFunctions: DefaultHostEnvironment.FunctionMap,
                    libraries: DefaultHostEnvironment.Libraries);
        }
        catch (CompilationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        var runner = new OptRunner();
        try
        {
            if (irPath != null)
            {
                var text = result.IrText;
                if (opt > 0)
                    text = runner.RunAsText(text, OptFlag(opt));
                File.WriteAllText(irPath, text);
                Console.WriteLine($"已写出 IR: {irPath} (opt={opt})");
            }
            if (bcPath != null)
            {
                var bc = runner.RunAsBitcode(result.IrText, OptFlag(opt));
                File.WriteAllBytes(bcPath, bc);
                Console.WriteLine($"已写出 Bitcode: {bcPath} (opt={opt})");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[opt] {ex.Message}");
            return 2;
        }

        if (output != null)
        {
            IronRuntime.Save(result, output);
            // 旁车源映射：run <out.isb> 时据此把运行期错误还原成「文件:行」。
            IronRuntime.SaveSourceMap(result, output);
            Console.WriteLine($"已写出字节码: {output}");
        }

        return 0;
    }

    private static string OptFlag(int opt) =>
        opt switch
        {
            1 => "-O1",
            2 => "-O2",
            _ => "",
        };

    private static bool TryNext(string[] args, ref int i, string flag, out string? value)
    {
        if (i + 1 >= args.Length)
        {
            Console.Error.WriteLine($"错误: 缺少 '{flag}' 的参数");
            value = null;
            return false;
        }
        value = args[++i];
        return true;
    }
}
