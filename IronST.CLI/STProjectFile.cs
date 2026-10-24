// STProjectFile.cs — .stproj 项目文件解析（XML，类 .slnx 的简洁风格）。
//
// 设计要点：
//   · .stproj 所在目录 = 项目根目录；Include 一律相对该目录解析，不依赖 CLI 当前工作目录。
//   · 只描述"一个项目"：源文件清单 + 入口 + 输出路径；不涉及 .slnx 式多项目解决方案。
//   · 解析期做完整校验：文件存在、XML 合法、路径不重复、源文件真实存在。
//   · 入口是否有效由既有入口解析负责（IronRuntime.ResolveEntryName），此处不重复实现。
using System.Xml;
using System.Xml.Linq;

namespace IronST.CLI;

/// <summary>项目文件（或项目构建前置条件）不满足时的可展示错误。</summary>
public sealed class ProjectFileException : Exception
{
    public ProjectFileException(string message) : base(message) { }
}

/// <summary>
/// 已解析并校验过的 <c>.stproj</c>。文件名与源文件路径均已规范化为绝对路径。
/// </summary>
public sealed class STProjectFile
{
    /// <summary>当前唯一支持的格式版本。</summary>
    public const string SupportedVersion = "1";

    /// <summary>项目文件绝对路径。</summary>
    public required string ProjectPath { get; init; }

    /// <summary>项目根目录（= 项目文件所在目录），绝对路径。</summary>
    public required string ProjectDirectory { get; init; }

    /// <summary>项目名（<c>PropertyGroup/Name</c>），缺省为项目文件名（不含扩展名）。</summary>
    public string? Name { get; init; }

    /// <summary>启动 PROGRAM 名（<c>PropertyGroup/StartupProgram</c>），缺省时按既有规则自动选择。</summary>
    public string? StartupProgram { get; init; }

    /// <summary><c>PropertyGroup/OutputPath</c> 解析后的绝对路径；未配置时为 <c>null</c>。</summary>
    public string? OutputPath { get; init; }

    /// <summary>源文件绝对路径，顺序 = 项目文件中 <c>STCompile</c> 的出现顺序。</summary>
    public required IReadOnlyList<string> SourceFiles { get; init; }

    /// <summary>
    /// 静态链接进产物的 `.stmod` 模块绝对路径，顺序 = 项目文件中
    /// <c>STModuleReference</c> 的出现顺序。
    /// </summary>
    /// <remarks>
    /// 由 <c>&lt;ItemGroup&gt;&lt;STModuleReference Include="lib.stmod" /&gt;&lt;/ItemGroup&gt;</c>
    /// 声明。非空时 <c>mod build &lt;proj.stproj&gt;</c> 会把编译产物与这些模块
    /// **真正静态链接**为一个自足模块（链接协议 §8 / §11）。
    /// 该元素为可选新增：旧项目文件不写即保持原有行为。
    /// </remarks>
    public IReadOnlyList<string> ModuleReferences { get; init; } = [];

    /// <summary>解析并校验一个 .stproj；任何问题抛 <see cref="ProjectFileException"/>。</summary>
    public static STProjectFile Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ProjectFileException("缺少项目文件路径。");

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            throw new ProjectFileException($"项目文件路径无效 '{path}': {ex.Message}");
        }

        if (!File.Exists(full))
            throw new ProjectFileException($"找不到项目文件 '{path}'。");

        string text;
        try
        {
            text = File.ReadAllText(full);
        }
        catch (Exception ex)
        {
            throw new ProjectFileException($"无法读取项目文件 '{path}': {ex.Message}");
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(text);
        }
        catch (XmlException ex)
        {
            throw new ProjectFileException($"项目文件 XML 无效: {ex.Message}");
        }

        var root = doc.Root;
        if (root is null || root.Name.LocalName != "STProject")
            throw new ProjectFileException(
                $"项目文件根元素必须是 <STProject>，实际为 <{root?.Name.LocalName ?? "(空)"}>。");

        var version = ((string?)root.Attribute("Version"))?.Trim();
        if (!string.IsNullOrEmpty(version) && version != SupportedVersion)
            throw new ProjectFileException(
                $"不支持的 STProject 版本 '{version}'（当前支持 Version=\"{SupportedVersion}\"）。");

        var dir = Path.GetDirectoryName(full) ?? Directory.GetCurrentDirectory();

        string? Prop(string name) =>
            root.Elements()
                .Where(e => e.Name.LocalName == "PropertyGroup")
                .Elements()
                .Where(e => e.Name.LocalName == name)
                .Select(e => e.Value.Trim())
                .FirstOrDefault(v => v.Length > 0);

        var name = Prop("Name");
        var startup = Prop("StartupProgram");
        var outputRaw = Prop("OutputPath");
        var outputPath = outputRaw is null
            ? null
            : Path.GetFullPath(Path.Combine(dir, outputRaw));

        var items = root.Elements()
            .Where(e => e.Name.LocalName == "ItemGroup")
            .Elements()
            .Where(e => e.Name.LocalName == "STCompile")
            .ToList();

        if (items.Count == 0)
            throw new ProjectFileException(
                "项目未包含任何源文件（需要 <STCompile Include=\"...\" />）。");

        var sources = new List<string>(items.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var include = ((string?)item.Attribute("Include"))?.Trim();
            if (string.IsNullOrEmpty(include))
                throw new ProjectFileException("<STCompile> 缺少 Include 属性。");

            var resolved = Path.GetFullPath(Path.Combine(dir, include));

            if (!seen.Add(resolved))
                throw new ProjectFileException($"项目重复包含源文件 '{include}'（{resolved}）。");

            if (!File.Exists(resolved))
                throw new ProjectFileException(
                    $"项目引用的源文件不存在: '{include}'"
                    + $"（相对项目目录 '{dir}' 解析为 '{resolved}'）。");

            sources.Add(resolved);
        }

        return new STProjectFile
        {
            ProjectPath = full,
            ProjectDirectory = dir,
            Name = name,
            StartupProgram = startup,
            OutputPath = outputPath,
            SourceFiles = sources,
            ModuleReferences = ReadModuleReferences(root, dir),
        };
    }

    /// <summary>
    /// 读取 <c>&lt;STModuleReference Include="..." /&gt;</c> 项。
    /// 校验：Include 非空、扩展名 <c>.stmod</c>、去重、文件必须存在。
    /// </summary>
    private static List<string> ReadModuleReferences(XElement root, string dir)
    {
        var items = root.Elements()
            .Where(e => e.Name.LocalName == "ItemGroup")
            .Elements()
            .Where(e => e.Name.LocalName == "STModuleReference")
            .ToList();

        var references = new List<string>(items.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var include = ((string?)item.Attribute("Include"))?.Trim();
            if (string.IsNullOrEmpty(include))
                throw new ProjectFileException("<STModuleReference> 缺少 Include 属性。");

            if (!include.EndsWith(".stmod", StringComparison.OrdinalIgnoreCase))
                throw new ProjectFileException(
                    $"<STModuleReference> 必须引用 .stmod 模块，实际为 '{include}'。");

            var resolved = Path.GetFullPath(Path.Combine(dir, include));

            if (!seen.Add(resolved))
                throw new ProjectFileException($"项目重复引用模块 '{include}'（{resolved}）。");

            if (!File.Exists(resolved))
                throw new ProjectFileException(
                    $"项目引用的模块不存在: '{include}'"
                    + $"（相对项目目录 '{dir}' 解析为 '{resolved}'）。");

            references.Add(resolved);
        }

        return references;
    }
}
