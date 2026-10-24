// RunCommand.cs — run <file.st ...> | <file.isb | file.irst> [--entry NAME] [--stack N]
//
// 多文件：`run A.st B.st` 复用 IronRuntime.CompileFiles（前端合并为单一编译单元），
// 入口规则完全来自 CompileFiles，本命令只做参数转发与错误展示。
using IronST.Runtime.Shared;
using IronST.Runtime.Shared.Modules;

namespace IronST.CLI.Commands;

public static class RunCommand
{
    public static int Run(string[] args)
    {
        var inputs = new List<string>();
        string? entry = null;
        int stackSize = 8192;
        int timeoutMs = 0;
        long maxInstructions = 0;   // 0 = 不限（默认与既有行为一致）

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--stack":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("错误: 缺少 '--stack' 的参数");
                        return 1;
                    }
                    if (!int.TryParse(args[++i], out stackSize) || stackSize <= 0)
                    {
                        Console.Error.WriteLine("错误: --stack 必须是正整数");
                        return 1;
                    }
                    break;

                case "--entry":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("错误: 缺少 '--entry' 的参数");
                        return 1;
                    }
                    entry = args[++i];
                    if (entry.Length == 0)
                    {
                        Console.Error.WriteLine("错误: --entry 不能为空");
                        return 1;
                    }
                    break;

                case "--timeout":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("错误: 缺少 '--timeout' 的参数");
                        return 1;
                    }
                    if (!int.TryParse(args[++i], out timeoutMs) || timeoutMs <= 0)
                    {
                        Console.Error.WriteLine("错误: --timeout 必须是正整数（毫秒）");
                        return 1;
                    }
                    break;

                case "--max-instructions":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("错误: 缺少 '--max-instructions' 的参数");
                        return 1;
                    }
                    if (!long.TryParse(args[++i], out maxInstructions) || maxInstructions <= 0)
                    {
                        Console.Error.WriteLine("错误: --max-instructions 必须是正整数（指令条数）");
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
            Console.Error.WriteLine("错误: 缺少输入文件 (run <file.st> [更多 .st ...])");
            return 1;
        }

        foreach (var f in inputs)
        {
            if (!File.Exists(f))
            {
                Console.Error.WriteLine($"错误: 找不到文件 '{f}'");
                return 1;
            }
        }

        // ── 模块文件：.stmod 是可执行的模块输入（链接协议 §7）──
        bool IsModule(string f) =>
            f.EndsWith(".stmod", StringComparison.OrdinalIgnoreCase);

        if (inputs.Any(IsModule))
        {
            if (inputs.Count > 1)
            {
                Console.Error.WriteLine("错误: .stmod 模块输入不支持与其它输入混用");
                return 1;
            }

            var modulePath = inputs[0];
            STModModule stmod;
            try
            {
                stmod = STModReader.Load(modulePath);
            }
            catch (STModFormatException ex)
            {
                Console.Error.WriteLine($"错误: {ex.Message}");
                return 1;
            }

            // 宿主 ABI：按名重新核对动态宿主函数 ID（不执行字节码即校验）。
            var problems = STModHostCompatibility.Check(stmod, DefaultHostEnvironment.FunctionMap);
            if (problems.Count > 0)
            {
                Console.Error.WriteLine("错误: 模块与当前宿主环境不兼容：");
                foreach (var problem in problems)
                    Console.Error.WriteLine($"  - {problem}");
                return 1;
            }

            // 未解析的外部引用：绝不执行"运行必错"的模块。
            if (!stmod.IsLinked)
            {
                Console.Error.WriteLine(
                    "错误: .stmod 存在未解析的外部引用，不能直接运行。"
                    + "请先用 `ironst mod link` 链接为完整模块。");
                return 1;
            }

            try
            {
                var loaded = entry is null
                    ? STModLoader.Load(stmod)
                    : STModLoader.Load(stmod, entry);
                Console.WriteLine($"[run] 加载模块: {modulePath}");
                // 旁车源映射（若有）：与 .isb 同源，令错误带「文件:行」。
                RunVm(token => IronRuntime.RunModule(
                    loaded.Module,
                    DefaultHostEnvironment.Register,
                    stackSize,
                    structTypes: null,
                    entryReturnPC: loaded.EntryReturnPC,
                    sourceMap: IronRuntime.LoadSourceMap(modulePath),
                    cancellation: token,
                    maxInstructions: maxInstructions), timeoutMs);
            }
            catch (STModEntryException ex)
            {
                Console.Error.WriteLine($"错误: {ex.Message}");
                return 1;
            }
            catch (RuntimeErrorException ex)
            {
                Console.Error.WriteLine($"运行时错误: {ex.Message}");
                return 1;
            }

            Console.WriteLine("[run] 执行完成");
            return 0;
        }

        // ── 项目文件：按项目配置构建并执行（与 build 共用 ProjectBuilder，无第二套管线）──
        bool IsProject(string f) => f.EndsWith(".stproj", StringComparison.OrdinalIgnoreCase);

        if (inputs.Any(IsProject))
        {
            if (inputs.Count > 1)
            {
                Console.Error.WriteLine("错误: .stproj 项目文件不能与其它输入混用");
                return 1;
            }

            try
            {
                var project = STProjectFile.Load(inputs[0]);
                var projectResult = ProjectBuilder.Compile(project, entry);
                // 与 build / run <isb> 同源的宿主注册，避免"编译成功、运行期 HCALL unregistered"。
                RunVm(token => IronRuntime.Run(
                    projectResult, DefaultHostEnvironment.Register, stackSize, token,
                    maxInstructions), timeoutMs);
            }
            catch (ProjectFileException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
            catch (CompilationException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
            catch (RuntimeErrorException ex)
            {
                // 源位置已由 VM 按 SourceLocator 回填进异常 Message。
                Console.Error.WriteLine($"运行时错误: {ex.Message}");
                return 1;
            }

            Console.WriteLine("[run] 执行完成");
            return 0;
        }

        // ── 字节码输入：单文件直接执行；多文件 / 显式入口不适用 ──
        bool IsBytecode(string f) =>
            f.EndsWith(".isb", StringComparison.OrdinalIgnoreCase)
            || f.EndsWith(".irst", StringComparison.OrdinalIgnoreCase);

        if (inputs.Any(IsBytecode))
        {
            if (inputs.Count > 1)
            {
                Console.Error.WriteLine("错误: 字节码输入不支持多文件");
                return 1;
            }
            if (entry != null)
            {
                Console.Error.WriteLine("错误: --entry 仅适用于 ST 源码，字节码已固化入口");
                return 1;
            }

            try
            {
                var module = IronRuntime.LoadModule(inputs[0]);
                Console.WriteLine($"[run] 加载字节码: {inputs[0]}");
                // 与 compile 侧同源的默认宿主环境：compile 已为 Print 发出 HCALL，
                // 这里必须注册同一实现，否则运行期 "HCALL unregistered"。
                // 旁车源映射（若有）：令错误带「文件:行」。
                RunVm(token => IronRuntime.RunModule(
                    module,
                    DefaultHostEnvironment.Register,
                    stackSize,
                    sourceMap: IronRuntime.LoadSourceMap(inputs[0]),
                    cancellation: token,
                    maxInstructions: maxInstructions), timeoutMs);
            }
            catch (RuntimeErrorException ex)
            {
                Console.Error.WriteLine($"运行时错误: {ex.Message}");
                return 1;
            }

            Console.WriteLine("[run] 执行完成");
            return 0;
        }

        // ── ST 源码：统一走合并编译入口（单文件亦同），入口规则来自 CompileFiles ──
        var files = new List<(string Source, string FileName)>(inputs.Count);
        foreach (var f in inputs)
        {
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

        try
        {
            var result = IronRuntime.CompileFiles(
                files,
                hostFunctions: DefaultHostEnvironment.FunctionMap,
                libraries: DefaultHostEnvironment.Libraries,
                entryFunctionName: entry);

            // 宿主注册与编译期声明必须成对，否则运行期 HCALL 未注册。
            RunVm(token => IronRuntime.Run(
                result, DefaultHostEnvironment.Register, stackSize, token,
                maxInstructions), timeoutMs);
        }
        catch (CompilationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (RuntimeErrorException ex)
        {
            // 源位置已由 VM 按 SourceLocator 回填进异常 Message。
            Console.Error.WriteLine($"运行时错误: {ex.Message}");
            return 1;
        }

        Console.WriteLine("[run] 执行完成");
        return 0;
    }

    /// <summary>
    /// 在取消令牌下运行 VM。Ctrl+C 与 <c>--timeout</c> 都走协作式取消：
    /// VM 在指令边界退出并抛 <see cref="OperationCanceledException"/>，
    /// 由 <c>CliRouter.ReportFatal</c> 统一映射为退出码 130。
    /// </summary>
    private static void RunVm(Action<CancellationToken> run, int timeoutMs)
    {
        using var cts = new CancellationTokenSource();
        if (timeoutMs > 0)
            cts.CancelAfter(timeoutMs);

        ConsoleCancelEventHandler handler = (_, e) =>
        {
            e.Cancel = true;   // 不让 CLR 直接杀进程，交给 VM 收尾
            cts.Cancel();
        };
        Console.CancelKeyPress += handler;
        try
        {
            run(cts.Token);
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }
}
