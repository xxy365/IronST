// CliRouter.cs — 子命令路由（模块 D）
//
// 顶层异常边界：任何命令抛出的异常都在这里收敛。
// 契约：正常失败（编译/运行/模块/IO）→ 诊断 + 退出码 1，绝不打印堆栈；
//       非预期异常 → 明确标注为工具缺陷 + 退出码 1；
//       需要排障时设置 IRONST_DEBUG=1 才会输出堆栈。
using IronST.LLVMCodeGen.Exceptions;
using IronST.Runtime.Shared;
using IronST.Runtime.Shared.Modules;

namespace IronST.CLI.Commands;

public static class CliRouter
{
    public static int Run(string[] args)
    {
        try
        {
            return Dispatch(args);
        }
        catch (Exception ex)
        {
            return ReportFatal(ex);
        }
    }

    private static int Dispatch(string[] args)
    {
        if (args.Length == 0)
            return PrintUsage(null);

        var cmd = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();

        return cmd switch
        {
            "compile" or "c" => CompileCommand.Run(rest),
            "build" or "b" => BuildCommand.Run(rest),
            "expand" or "e" => ExpandCommand.Run(rest),
            "mod" or "m" => ModCommand.Run(rest),
            "run" or "r" => RunCommand.Run(rest),
            "disasm" or "d" => DisasmCommand.Run(rest),
            "verify" or "v" => VerifyCommand.Run(rest),
            "format" => FormatCommand.Run(rest),
            "analyze" or "a" => AnalyzeCommand.Run(rest),
            "repl" => ReplCommand.Run(rest),
            "demo" => DemoCommand.Run(rest),
            "help" or "-h" or "--help" => PrintUsage(null),
            _ => PrintUsage($"未知命令: '{args[0]}'"),
        };
    }

    /// <summary>
    /// 把穿透到顶层的异常转成稳定的诊断 + 退出码。
    /// 不得在此处再抛异常，也不得输出 .NET 堆栈（除非 IRONST_DEBUG=1）。
    /// </summary>
    private static int ReportFatal(Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            Console.Error.WriteLine("已取消。");
            return 130;
        }

        // 预期内的失败：用户能看懂，且属于正常使用路径。
        if (ex is CompilationException
            or CodeGenException           // 含 TypeMappingException
            or RuntimeErrorException
            or ProjectFileException
            or STModFormatException
            or STModLinkException
            or STModEntryException
            or IOException
            or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");

            // 预期失败默认不输出堆栈；排障时用 IRONST_DEBUG=1 显式打开。
            if (IsDebugEnabled)
                Console.Error.WriteLine(ex.ToString());

            return 1;
        }

        // 非预期异常：明确告知这是工具的问题，避免用户误以为自己写错了 ST。
        Console.Error.WriteLine("错误: IronST 内部错误（这属于工具缺陷，不是你的程序的问题）");
        Console.Error.WriteLine($"  {ex.GetType().FullName}: {ex.Message}");

        if (IsDebugEnabled)
            Console.Error.WriteLine(ex.ToString());

        return 1;
    }

    private static bool IsDebugEnabled =>
        Environment.GetEnvironmentVariable("IRONST_DEBUG") == "1";

    private static int PrintUsage(string? error)
    {
        if (error != null)
            Console.Error.WriteLine($"错误: {error}\n");

        Console.WriteLine(
            """
            IronST — IEC 61131-3 Structured Text 编译器

            用法: ironst <命令> [参数]

            命令:
              compile <file.st ...>        编译（可多个源文件，合并为一个编译单元）
                    [-o out.isb]            字节码输出路径
                    [--ir out.ll]           LLVM IR 文本输出
                    [--bc out.bc]           LLVM Bitcode 输出（需 opt 工具）
                    [--opt 0|1|2]           优化级别（0=不优化, 1=O1, 2=O2）
              build <file.stproj>          按项目文件构建为单个 .isb
                    [-o out.isb]            覆盖项目 OutputPath
              expand <file.stproj>         把项目源码展开为一个 .st 文件
                    [-o out.st]             展开结果输出路径
              mod build <file.st|.stproj>  编译为 .stmod 模块
                    [--entry NAME]          显式入口 PROGRAM 名
                    [--ref m.stmod]         引用已编译模块的导出（可重复）
                    [--meta KEY=VALUE]      追加自定义模块元数据（可重复）
                    [--author NAME]         写入 module.author
                    [-o out.stmod]          模块输出路径
                    （.stproj 声明 <STModuleReference> 时产出已链接自足模块）
              mod link <a.stmod ...>       静态链接为单个完整模块
                    [--entry NAME]          选择入口函数（须唯一匹配）
                    [--name NAME]           覆盖输出模块名
                    [-o out.stmod]          输出路径（默认 linked.stmod）
              mod inspect <file.stmod>     查看元数据/区段/函数索引/链接状态（不执行字节码）
              run <file.st ...>             编译并执行（可多个源文件）
                    [--entry NAME]          显式入口 PROGRAM 名
                    [--stack N]             栈大小（默认 8192）
                    [--timeout MS]          运行超时（毫秒，默认不限）
                    [--max-instructions N]  指令预算（单次运行上限，默认不限）
              run <file.stmod>              执行 .stmod 模块（未链接模块会被拒绝）
                    [--entry NAME]          按函数名选择入口（须唯一匹配）
                    [--stack N]             栈大小（默认 8192）
                    [--timeout MS]          运行超时（毫秒，默认不限）
                    [--max-instructions N]  指令预算（单次运行上限，默认不限）
              run <file.stproj>             按项目文件构建并执行
                    [--entry NAME]          覆盖项目 StartupProgram
                    [--stack N]             栈大小（默认 8192）
                    [--timeout MS]          运行超时（毫秒，默认不限）
                    [--max-instructions N]  指令预算（单次运行上限，默认不限）
              run <file.isb | file.irst>    直接执行字节码（单文件）
                    [--timeout MS]          运行超时（毫秒，默认不限）
                    [--max-instructions N]  指令预算（单次运行上限，默认不限）
              disasm <file.isb>             反汇编字节码
              verify <file.st>              只做前端分析，报告错误
              format <file.st ...>          格式化源文件并写回（语法错误时保持原样）
                    [--check]               只检查是否需要格式化，不写回
              analyze <file.st>             前端分析并打印符号/调用/引用表
              repl                          交互式 REPL
              demo                          运行内置演示程序
              help                          显示本帮助

            退出码: 0=成功, 1=编译/运行错误, 2=工具缺失(opt), 130=取消
            """);
        return error == null ? 0 : 1;
    }
}
