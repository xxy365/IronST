// BuildCommand.cs — build <file.stproj> [-o out.isb]
//
// 读取项目文件 → 调 ProjectBuilder（复用 CompileFiles + 默认宿主环境）
// → 把整个项目编译为一个 .isb。不做源码展开，不引入第二套编译实现。
using IronST.BytecodeGen;

namespace IronST.CLI.Commands;

public static class BuildCommand
{
    public static int Run(string[] args)
    {
        var inputs = new List<string>();
        string? output = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("错误: 缺少 '-o' 的参数");
                        return 1;
                    }
                    output = args[++i];
                    if (output.Length == 0)
                    {
                        Console.Error.WriteLine("错误: -o 不能为空");
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
            Console.Error.WriteLine("错误: 缺少项目文件 (build <file.stproj> [-o out.isb])");
            return 1;
        }

        if (inputs.Count > 1)
        {
            Console.Error.WriteLine("错误: build 只接受一个 .stproj 项目文件");
            return 1;
        }

        var projectPath = inputs[0];
        if (!projectPath.EndsWith(".stproj", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                $"错误: build 需要 .stproj 项目文件，实际为 '{projectPath}'");
            return 1;
        }

        STProjectFile project;
        CompileResult result;
        try
        {
            project = STProjectFile.Load(projectPath);
            result = ProjectBuilder.Compile(project);
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

        var outPath = output ?? ProjectBuilder.DefaultOutputPath(project);
        var outDir = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(outDir))
            Directory.CreateDirectory(outDir);

        try
        {
            IronRuntime.Save(result, outPath);
            // 旁车源映射：run <out.isb> 时据此把运行期错误还原成「文件:行」。
            IronRuntime.SaveSourceMap(result, outPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: 无法写出字节码 '{outPath}': {ex.Message}");
            return 1;
        }

        Console.WriteLine($"已写出字节码: {outPath}");
        Console.WriteLine($"构建成功: {projectPath}");
        return 0;
    }
}
