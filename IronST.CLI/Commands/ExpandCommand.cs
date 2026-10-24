// ExpandCommand.cs — expand <file.stproj> [-o out.st]
//
// 附加功能：把项目展开为一个独立可编译的 .st 文件（检查 / 分发 / 单文件场景）。
// 明确不参与默认链路：build 与 run 都不经过 expand，也不存在
// "先生成合并 .st 再作为默认编译输入"的路径。
namespace IronST.CLI.Commands;

public static class ExpandCommand
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
            Console.Error.WriteLine("错误: 缺少项目文件 (expand <file.stproj> [-o out.st])");
            return 1;
        }

        if (inputs.Count > 1)
        {
            Console.Error.WriteLine("错误: expand 只接受一个 .stproj 项目文件");
            return 1;
        }

        var projectPath = inputs[0];
        if (!projectPath.EndsWith(".stproj", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                $"错误: expand 需要 .stproj 项目文件，实际为 '{projectPath}'");
            return 1;
        }

        STProjectFile project;
        string text;
        try
        {
            project = STProjectFile.Load(projectPath);
            text = ProjectBuilder.Expand(project);
        }
        catch (ProjectFileException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        var outPath = output ?? ProjectBuilder.DefaultExpandOutputPath(project);
        var outDir = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(outDir))
            Directory.CreateDirectory(outDir);

        try
        {
            File.WriteAllText(outPath, text);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: 无法写出源码 '{outPath}': {ex.Message}");
            return 1;
        }

        Console.WriteLine($"已写出源码: {outPath}");
        Console.WriteLine($"展开成功: {projectPath}");
        return 0;
    }
}
