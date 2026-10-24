// Services/ProjectWorkspace.cs
//
// IDE 侧的“当前工程”工作区：不重复实现 .stproj 格式，直接复用 IronST.CLI 的
// STProjectFile（读）与 STProjectWriter（新建），只负责：
//   · 记住当前打开的工程；
//   · 把工程内容摊平成解决方案树节点（源文件可双击打开，模块引用不可）;
//   · 打开失败时保持原工程不变（不做半开状态）。
using System;
using System.IO;
using IronST.CLI;
using IronST.IDE.Models;

namespace IronST.IDE.Services;

/// <summary>在 IDE 进程内跑一次 CLI 子命令的结果。</summary>
/// <param name="ExitCode">CLI 退出码（0 = 成功）。</param>
/// <param name="Output">捕获到的标准输出（含宿主 <c>Print</c> 的输出）。</param>
/// <param name="Error">捕获到的标准错误（编译/运行诊断）。</param>
public sealed record ProjectCommandResult(int ExitCode, string Output, string Error)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>当前打开的 IronST 工程（<c>.stproj</c>）。</summary>
public sealed class ProjectWorkspace
{
    private const string ModuleSuffix = "（模块）";

    /// <summary>当前工程；未打开任何工程时为 <c>null</c>。</summary>
    public STProjectFile? Project { get; private set; }

    public bool HasProject => Project is not null;

    /// <summary>当前工程的 <c>.stproj</c> 绝对路径。</summary>
    public string? ProjectPath => Project?.ProjectPath;

    /// <summary>当前工程目录（= <c>.stproj</c> 所在目录）。</summary>
    public string? ProjectDirectory => Project?.ProjectDirectory;

    /// <summary>当前工程的显示名（未写 &lt;Name&gt; 时回退为文件名）。</summary>
    public string ProjectName
    {
        get
        {
            if (Project is null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(Project.Name))
                return Project.Name;

            return Path.GetFileNameWithoutExtension(Project.ProjectPath);
        }
    }

    /// <summary>
    /// 新建工程（<paramref name="projectPath"/> 决定目录与工程名），建好后立即作为当前工程打开。
    /// </summary>
    /// <returns>写出的 <c>.stproj</c> 绝对路径。</returns>
    /// <exception cref="ProjectFileException">
    /// 路径不是 <c>.stproj</c>、目标已存在，或写入失败（见 <see cref="STProjectWriter.CreateProject"/>）。
    /// </exception>
    public string Create(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            throw new ProjectFileException("缺少工程文件路径。");

        string full = Path.GetFullPath(projectPath);
        if (!full.EndsWith(".stproj", StringComparison.OrdinalIgnoreCase))
            throw new ProjectFileException($"工程文件必须以 .stproj 结尾: '{projectPath}'。");

        string name = Path.GetFileNameWithoutExtension(full);
        string directory = Path.GetDirectoryName(full)!;

        STProjectWriter.CreateProject(directory, new STProjectTemplate { Name = name });
        Open(full);

        return full;
    }

    /// <summary>打开已有工程；解析失败时抛出，且**不改变**当前工程。</summary>
    /// <exception cref="ProjectFileException">工程文件缺失或格式非法。</exception>
    public void Open(string projectPath)
    {
        Project = STProjectFile.Load(projectPath);
    }

    /// <summary>关闭当前工程。</summary>
    public void Close() => Project = null;

    // ═══════════════════════════════════════════════════════════
    //  往工程里加文件
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 在工程目录内新建一个 <c>.st</c>（模板可编译）并登记进工程。
    /// </summary>
    /// <returns>创建的文件绝对路径。</returns>
    /// <exception cref="ProjectFileException">
    /// 没有打开工程、路径在工程目录外、文件已存在，或文件名不合法。
    /// </exception>
    public string AddNewSource(string filePath)
    {
        var project = RequireProject();

        if (string.IsNullOrWhiteSpace(filePath))
            throw new ProjectFileException("缺少源文件路径。");

        string full = Path.GetFullPath(filePath);
        string dir = project.ProjectDirectory;
        EnsureInside(dir, full, "新源文件");

        STProjectWriter.CreateSourceFile(full);

        try
        {
            STProjectWriter.AddSource(project.ProjectPath, Path.GetRelativePath(dir, full));
        }
        catch
        {
            // 登记失败就别留下刚造出来的孤儿文件。
            try { if (File.Exists(full)) File.Delete(full); } catch { /* best effort */ }
            throw;
        }

        Reload();
        return full;
    }

    /// <summary>
    /// 添加已有文件：<c>.st</c> → 源文件，<c>.stmod</c> → 模块引用。
    /// 工程目录**之外**的文件会先复制进工程目录（保持工程自足）。
    /// </summary>
    /// <returns>该文件在工程内的绝对路径。</returns>
    /// <exception cref="ProjectFileException">
    /// 没有打开工程、文件不存在、扩展名不支持、同名冲突，或已在工程中。
    /// </exception>
    public string AddExistingFile(string filePath)
    {
        var project = RequireProject();

        if (string.IsNullOrWhiteSpace(filePath))
            throw new ProjectFileException("缺少文件路径。");

        string source = Path.GetFullPath(filePath);
        if (!File.Exists(source))
            throw new ProjectFileException($"文件不存在: '{source}'。");

        string extension = Path.GetExtension(source);
        bool isSource = extension.Equals(".st", StringComparison.OrdinalIgnoreCase);
        bool isModule = extension.Equals(".stmod", StringComparison.OrdinalIgnoreCase);
        if (!isSource && !isModule)
            throw new ProjectFileException(
                $"只支持 .st 源文件与 .stmod 模块，实际为 '{source}'。");

        string dir = project.ProjectDirectory;
        string target = source;
        bool copied = false;

        if (!IsInside(dir, source))
        {
            target = Path.Combine(dir, Path.GetFileName(source));
            if (File.Exists(target))
                throw new ProjectFileException($"工程目录下已存在同名文件: '{target}'。");

            Directory.CreateDirectory(dir);
            File.Copy(source, target);
            copied = true;
        }

        try
        {
            string relative = Path.GetRelativePath(dir, target);
            if (isSource)
                STProjectWriter.AddSource(project.ProjectPath, relative);
            else
                STProjectWriter.AddModuleReference(project.ProjectPath, relative);
        }
        catch
        {
            // 登记失败就不要留下复制过来的副本。
            if (copied)
            {
                try { if (File.Exists(target)) File.Delete(target); } catch { /* best effort */ }
            }

            throw;
        }

        Reload();
        return target;
    }

    private void Reload()
    {
        if (Project is not null)
            Project = STProjectFile.Load(Project.ProjectPath);
    }

    private STProjectFile RequireProject() =>
        Project ?? throw new ProjectFileException("没有打开的工程。");

    private static bool IsInside(string dir, string absolute)
    {
        string prefix = dir.EndsWith(Path.DirectorySeparatorChar)
            ? dir
            : dir + Path.DirectorySeparatorChar;

        return absolute.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureInside(string dir, string absolute, string what)
    {
        if (!IsInside(dir, absolute))
            throw new ProjectFileException($"{what} '{absolute}' 必须位于工程目录 '{dir}' 内。");
    }

    // ═══════════════════════════════════════════════════════════
    //  生成 / 运行（复用 CLI 的 build / run，不另建管线）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 生成工程：等价于 CLI <c>build &lt;proj&gt;</c>，输出路径取项目 OutputPath 或
    /// <c>&lt;项目目录&gt;/&lt;Name&gt;.isb</c>。
    /// </summary>
    /// <exception cref="ProjectFileException">没有打开的工程。</exception>
    public ProjectCommandResult Build() => RunCli("build", RequireProjectPath());

    /// <summary>
    /// 运行工程：等价于 CLI <c>run &lt;proj&gt;</c>（入口取项目 StartupProgram）。
    /// </summary>
    /// <exception cref="ProjectFileException">没有打开的工程。</exception>
    public ProjectCommandResult Run() => RunCli("run", RequireProjectPath());

    private string RequireProjectPath() =>
        Project?.ProjectPath ?? throw new ProjectFileException("没有打开的工程。");

    /// <summary>
    /// 在进程内调用 CLI 命令并捕获它写到 Console 的输出。
    ///
    /// 为什么这么干：工程构建/执行必须与命令行**同一套**实现（前端合并、宿主环境、
    /// 字节码写出、VM 执行），所以在 IDE 里直接调 <c>CliRouter</c>，而不是重写一遍。
    /// Console 是进程级的，故只在同步的短窗口内重定向（调用方负责串行化）。
    /// </summary>
    private static ProjectCommandResult RunCli(params string[] args)
    {
        var outWriter = new StringWriter();
        var errWriter = new StringWriter();
        var previousOut = Console.Out;
        var previousErr = Console.Error;

        try
        {
            Console.SetOut(outWriter);
            Console.SetError(errWriter);

            int exitCode = IronST.CLI.Commands.CliRouter.Run(args);
            return new ProjectCommandResult(exitCode, outWriter.ToString(), errWriter.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousErr);
        }
    }

    /// <summary>
    /// 把当前工程摊平成解决方案树。未打开工程时返回空列表。
    /// </summary>
    public IReadOnlyList<SolutionNode> BuildTree()
    {
        if (Project is null)
            return [];

        var root = new SolutionNode
        {
            Name = ProjectName,
            FilePath = Project.ProjectPath,
            IsFolder = true,       // 根节点双击不打开文件
            IsExpanded = true,
        };

        foreach (string source in Project.SourceFiles)
        {
            root.Children.Add(new SolutionNode
            {
                Name = Relative(source),
                FilePath = source,
            });
        }

        foreach (string reference in Project.ModuleReferences)
        {
            // 模块不是源文件：留空 FilePath，双击不会把二进制当文本打开。
            root.Children.Add(new SolutionNode
            {
                Name = Relative(reference) + ModuleSuffix,
            });
        }

        return [root];
    }

    private string Relative(string absolutePath)
    {
        string baseDir = Project?.ProjectDirectory ?? "";
        try
        {
            return Path.GetRelativePath(baseDir, absolutePath);
        }
        catch (ArgumentException)
        {
            return absolutePath;
        }
    }
}
