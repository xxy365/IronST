// ProjectBuilder.cs — 项目构建的共享入口（build 与项目模式 run 共用）。
//
// 为什么存在：`.stproj` 的 build 与 run 必须走**同一**编译与宿主环境，否则会出现
// "build 能过、run 不行"这类双路径漂移。这里只做三件事：读源文件、调既有
// IronRuntime.CompileFiles、算默认输出路径 —— 不另建前端/IR/字节码管线。
using IronST.BytecodeGen;
using IronST.Frontend.Analysis;

namespace IronST.CLI;

internal static class ProjectBuilder
{
    /// <summary>
    /// 按项目配置编译为一个编译单元（复用既有 <see cref="IronRuntime.CompileFiles"/>）。
    /// 宿主环境与 CLI 的 compile / run 同源（<see cref="DefaultHostEnvironment"/>）。
    /// </summary>
    /// <param name="entryOverride">
    /// 显式入口覆盖（如 <c>run proj.stproj --entry X</c>）；为 <c>null</c> 时用
    /// 项目的 StartupProgram，仍为 <c>null</c> 则由 <c>CompileFiles</c> 按既有规则选择。
    /// </param>
    /// <param name="extraLibraries">
    /// 追加的外部库声明（如 <c>mod build</c> 由 <c>.stmod</c> 导出构造的声明）；
    /// 与 <see cref="DefaultHostEnvironment.Libraries"/> 合并。为 <c>null</c> 时行为不变。
    /// </param>
    /// <param name="allowExternalCalls">
    /// <c>true</c> 时允许对"声明但非宿主"的外部函数发起调用并保留为未解析引用
    /// （独立编译 + 后续链接）；默认 <c>false</c>，保持既有错误检查。
    /// </param>
    public static CompileResult Compile(
        STProjectFile project,
        string? entryOverride = null,
        IReadOnlyList<ExternalLibrary>? extraLibraries = null,
        bool allowExternalCalls = false)
    {
        var files = new List<(string Source, string FileName)>(project.SourceFiles.Count);
        foreach (var path in project.SourceFiles)
        {
            try
            {
                files.Add((File.ReadAllText(path), path));
            }
            catch (Exception ex)
            {
                throw new ProjectFileException($"无法读取源文件 '{path}': {ex.Message}");
            }
        }

        var libraries = DefaultHostEnvironment.Libraries;
        if (extraLibraries is { Count: > 0 })
            libraries = libraries.Concat(extraLibraries).ToList();

        return IronRuntime.CompileFiles(
            files,
            hostFunctions: DefaultHostEnvironment.FunctionMap,
            libraries: libraries,
            entryFunctionName: entryOverride ?? project.StartupProgram,
            allowExternalCalls: allowExternalCalls);
    }

    /// <summary>项目默认输出路径：优先项目 <c>OutputPath</c>，否则 &lt;项目目录&gt;/&lt;Name|项目名&gt;.isb。</summary>
    public static string DefaultOutputPath(STProjectFile project)
    {
        if (project.OutputPath is not null)
            return project.OutputPath;

        return Path.Combine(project.ProjectDirectory, ProjectBaseName(project) + ".isb");
    }

    /// <summary>展开（<c>expand</c>）的默认输出路径：&lt;项目目录&gt;/&lt;Name|项目名&gt;.st。</summary>
    public static string DefaultExpandOutputPath(STProjectFile project) =>
        Path.Combine(project.ProjectDirectory, ProjectBaseName(project) + ".st");

    /// <summary>
    /// 把项目源文件按项目声明顺序展开为一个独立可编译的 .st 文本。
    ///
    /// <para>
    /// 实现依据：多文件编译（<c>FrontendPipeline.AnalyzeFiles</c>）本身要求**每个文件都是
    /// 完整编译单元**，随后在符号层面合并；因此"按序拼接各文件文本"与"合并后的语义"一致 ——
    /// 但这一点不由假设保证：调用方（测试）必须实际编译并执行展开产物来验证。
    /// </para>
    /// <para>
    /// 两个必要的文本处理：行尾统一为 \\n；每个文件内容去掉尾部换行后补一个空行分隔，
    /// 以免"文件以行注释结尾且无换行"时把下一个文件的首行注释掉。
    /// </para>
    /// </summary>
    public static string Expand(STProjectFile project)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("// 本文件由 `ironst expand` 从 .stproj 项目展开生成，请勿手工编辑。\n");
        sb.Append($"// 项目: {project.ProjectPath}\n");
        if (project.StartupProgram is not null)
            sb.Append($"// 启动 PROGRAM: {project.StartupProgram}\n");
        sb.Append('\n');

        foreach (var path in project.SourceFiles)
        {
            string content;
            try
            {
                content = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                throw new ProjectFileException($"无法读取源文件 '{path}': {ex.Message}");
            }

            var relative = Path.GetRelativePath(project.ProjectDirectory, path);
            sb.Append($"// ── {relative} ──\n");
            sb.Append(content.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n'));
            sb.Append("\n\n");
        }

        return sb.ToString();
    }

    private static string ProjectBaseName(STProjectFile project) =>
        string.IsNullOrWhiteSpace(project.Name)
            ? Path.GetFileNameWithoutExtension(project.ProjectPath)
            : project.Name;
}
