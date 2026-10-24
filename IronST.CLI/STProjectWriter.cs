// STProjectWriter.cs — 新建/写出 `.stproj`（与 STProjectFile 对称）。
//
// 设计要点：
//   · 只写既有 schema（STProject / PropertyGroup / ItemGroup + STCompile / STModuleReference），
//     不新增 XML 元素；写出的文件必须能被 STProjectFile.Load 读通（落盘后自校验）。
//   · **绝不覆盖**已存在的文件：.stproj 或任一源文件已存在 → 拒绝，且不改动任何文件。
//   · 全部校验在落盘前完成；落盘后自校验失败则删掉本次创建的文件（不留半成品）。
//   · 只做"创建 + 校验"，不编译、不新增第二套构建管线。
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace IronST.CLI;

/// <summary>新建工程所需的模板参数（见 <see cref="STProjectWriter.CreateProject"/>）。</summary>
public sealed class STProjectTemplate
{
    /// <summary>工程名；同时用作 <c>&lt;Name&gt;</c> 与 <c>&lt;工程名&gt;.stproj</c> 的文件名。</summary>
    public required string Name { get; init; }

    /// <summary>入口 PROGRAM 名；缺省取首个源文件的主文件名（如 <c>Main.st</c> → <c>Main</c>）。</summary>
    public string? StartupProgram { get; init; }

    /// <summary>输出路径（相对工程目录，如 <c>dist/App.isb</c>）；为 <c>null</c> 时不写该属性。</summary>
    public string? OutputPath { get; init; }

    /// <summary>源文件（相对工程目录、须以 <c>.st</c> 结尾）。默认 <c>Main.st</c>。</summary>
    public IReadOnlyList<string> Sources { get; init; } = ["Main.st"];

    /// <summary>静态链接进产物的 <c>.stmod</c>（相对工程目录、须已存在）。默认无。</summary>
    public IReadOnlyList<string> ModuleReferences { get; init; } = [];
}

/// <summary>
/// 写出 <c>.stproj</c> 文本，并按模板新建工程目录。
/// 只覆盖"创建"语义：不覆盖已有文件、失败不留半成品。
/// </summary>
public static class STProjectWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// 由模板生成 <c>.stproj</c> 文本（不落盘）。产出的文本必须能被
    /// <see cref="STProjectFile.Load"/> 读通。
    /// </summary>
    public static string WriteXml(STProjectTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        ValidateTemplate(template);

        var root = new XElement(
            "STProject", new XAttribute("Version", STProjectFile.SupportedVersion));

        var props = new XElement(
            "PropertyGroup",
            new XElement("Name", template.Name),
            new XElement("StartupProgram", EffectiveStartup(template)));

        if (!string.IsNullOrWhiteSpace(template.OutputPath))
            props.Add(new XElement("OutputPath", template.OutputPath.Trim()));

        root.Add(props);

        var sources = new XElement("ItemGroup");
        foreach (string source in template.Sources)
            sources.Add(new XElement("STCompile", new XAttribute("Include", Normalize(source))));
        root.Add(sources);

        if (template.ModuleReferences.Count > 0)
        {
            var references = new XElement("ItemGroup");
            foreach (string reference in template.ModuleReferences)
                references.Add(new XElement(
                    "STModuleReference", new XAttribute("Include", Normalize(reference))));
            root.Add(references);
        }

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), root);

        return RenderXml(doc);
    }

    /// <summary>
    /// 新建工程：在 <paramref name="directory"/>（不存在则创建）里生成
    /// <c>&lt;Name&gt;.stproj</c> 与模板源文件，返回 <c>.stproj</c> 的绝对路径。
    /// </summary>
    /// <exception cref="ProjectFileException">
    /// 参数非法、目标文件已存在、引用的模块不存在，或写出的工程无法读回。
    /// </exception>
    public static string CreateProject(string directory, STProjectTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        if (string.IsNullOrWhiteSpace(directory))
            throw new ProjectFileException("缺少工程目录。");

        ValidateTemplate(template);

        string dir = Path.GetFullPath(directory);
        string projectPath = Path.Combine(dir, template.Name + ".stproj");

        // ── 落盘前把一切检查完：不覆盖任何已存在的文件 ──
        if (File.Exists(projectPath))
            throw new ProjectFileException($"工程文件已存在: '{projectPath}'。");

        var sourcePaths = new List<(string Relative, string Absolute)>(template.Sources.Count);
        foreach (string relative in template.Sources)
        {
            string absolute = Path.GetFullPath(Path.Combine(dir, relative));
            EnsureInside(dir, absolute, relative, "源文件");

            if (File.Exists(absolute))
                throw new ProjectFileException($"源文件已存在: '{absolute}'。");

            sourcePaths.Add((relative, absolute));
        }

        foreach (string reference in template.ModuleReferences)
        {
            string absolute = Path.GetFullPath(Path.Combine(dir, reference));
            EnsureInside(dir, absolute, reference, "模块引用");

            if (!File.Exists(absolute))
                throw new ProjectFileException(
                    $"引用的模块不存在: '{reference}'"
                    + $"（相对工程目录 '{dir}' 解析为 '{absolute}'）。");
        }

        // ── 落盘（失败即回滚本次创建的文件）──
        Directory.CreateDirectory(dir);

        var created = new List<string>();
        try
        {
            string startup = EffectiveStartup(template);
            for (int i = 0; i < sourcePaths.Count; i++)
            {
                var (relative, absolute) = sourcePaths[i];
                Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
                File.WriteAllText(
                    absolute, SourceTemplate(i, relative, template.Name, startup), Utf8NoBom);
                created.Add(absolute);
            }

            SaveAtomic(projectPath, WriteXml(template));
            created.Add(projectPath);

            // 写后自校验：产出的工程必须能被读取器读通（防止写出"看起来像工程"的坏文件）。
            _ = STProjectFile.Load(projectPath);

            return projectPath;
        }
        catch
        {
            foreach (string path in created)
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
            }

            throw;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  往已有工程里追加条目
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 把一个**已存在**的源文件加进工程（<c>&lt;STCompile Include="..." /&gt;</c>）。
    /// </summary>
    /// <param name="projectPath">目标 <c>.stproj</c>。</param>
    /// <param name="relativePath">相对工程目录的 <c>.st</c> 路径（不允许跳出工程目录）。</param>
    /// <exception cref="ProjectFileException">
    /// 工程不可解析、路径非法、文件不存在，或该文件已在工程中。
    /// </exception>
    public static void AddSource(string projectPath, string relativePath) =>
        AddItem(projectPath, relativePath, "STCompile", ".st", "源文件");

    /// <summary>
    /// 把一个**已存在**的 <c>.stmod</c> 加进工程的模块引用
    /// （<c>&lt;STModuleReference Include="..." /&gt;</c>，供 <c>mod build &lt;proj&gt;</c> 静态链接）。
    /// </summary>
    /// <exception cref="ProjectFileException">同上，扩展名须为 <c>.stmod</c>。</exception>
    public static void AddModuleReference(string projectPath, string relativePath) =>
        AddItem(projectPath, relativePath, "STModuleReference", ".stmod", "模块引用");

    /// <summary>
    /// 新建一个源文件（<c>.st</c>），内容为可编译的最小 PROGRAM 模板。
    /// 程序名取文件名（须是合法 ST 标识符），避免生成编不过的代码。
    /// </summary>
    /// <returns>写出的文件绝对路径。</returns>
    /// <exception cref="ProjectFileException">
    /// 路径非法、不是 <c>.st</c>、文件已存在，或文件名派生不出合法 ST 标识符。
    /// </exception>
    public static string CreateSourceFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ProjectFileException("缺少源文件路径。");

        string full = Path.GetFullPath(filePath);
        if (!full.EndsWith(".st", StringComparison.OrdinalIgnoreCase))
            throw new ProjectFileException($"源文件必须以 .st 结尾: '{filePath}'。");

        string program = Path.GetFileNameWithoutExtension(full);
        if (!IsIdentifier(program))
            throw new ProjectFileException(
                $"文件名 '{Path.GetFileName(full)}' 派生不出合法的 ST 程序名 '{program}'"
                + "（程序名只能用字母/数字/下划线，且不能以数字开头）。");

        if (File.Exists(full))
            throw new ProjectFileException($"文件已存在: '{full}'。");

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, SourceFileStub(program), Utf8NoBom);

        return full;
    }

    /// <summary>往工程里追加一项（XML 级编辑：只增不改，保留注释/未知元素/顺序）。</summary>
    private static void AddItem(
        string projectPath, string relativePath, string elementName, string extension, string what)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            throw new ProjectFileException("缺少工程文件路径。");

        ValidateRelativePath(relativePath, extension, what);

        // 先确认工程本身是好的（解析期会校验已列出的文件都存在）。
        var project = STProjectFile.Load(projectPath);
        string dir = project.ProjectDirectory;
        string relative = Normalize(relativePath);
        string absolute = Path.GetFullPath(Path.Combine(dir, relative));
        EnsureInside(dir, absolute, relative, what);

        if (!File.Exists(absolute))
            throw new ProjectFileException($"{what}不存在: '{absolute}'。");

        var existing = elementName == "STCompile"
            ? project.SourceFiles
            : project.ModuleReferences;

        if (existing.Any(p => string.Equals(p, absolute, StringComparison.OrdinalIgnoreCase)))
            throw new ProjectFileException($"{what} '{relative}' 已在工程中。");

        XDocument doc;
        try
        {
            doc = XDocument.Parse(File.ReadAllText(project.ProjectPath));
        }
        catch (XmlException ex)
        {
            throw new ProjectFileException($"工程文件 XML 无效: {ex.Message}");
        }

        var root = doc.Root!;
        var group = root.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "ItemGroup"
                && e.Elements().Any(x => x.Name.LocalName == elementName));

        if (group is null)
        {
            group = root.Elements().FirstOrDefault(e => e.Name.LocalName == "ItemGroup");
            if (group is null)
            {
                group = new XElement("ItemGroup");
                root.Add(group);
            }
        }

        group.Add(new XElement(elementName, new XAttribute("Include", relative)));

        SaveAtomic(project.ProjectPath, RenderXml(doc));

        // 落盘后自校验：追加结果必须仍能被读取器读通。
        _ = STProjectFile.Load(project.ProjectPath);
    }

    private static string SourceFileStub(string program) =>
        $"""
        // {program}.st — 由 IronST 添加到工程，可自由编辑。
        PROGRAM {program}
        END_PROGRAM

        """;

    // ═══════════════════════════════════════════════════════════
    //  模板内容
    // ═══════════════════════════════════════════════════════════

    /// <summary>首个源文件放可运行入口程序，其余源文件放占位注释。</summary>
    private static string SourceTemplate(
        int index, string relative, string projectName, string startup)
    {
        if (index != 0)
            return $"// {relative} — 由 IronST 新建工程“{projectName}”生成（占位，可自由编辑）。\n";

        return $"""
            // {relative} — 由 IronST 新建工程“{projectName}”生成。
            // 入口程序 {startup}：F5 运行 / build 生成 .isb。
            PROGRAM {startup}
            VAR
                value : DINT;
            END_VAR
                value := 42;
                Print(value);
            END_PROGRAM

            """;
    }

    // ═══════════════════════════════════════════════════════════
    //  校验
    // ═══════════════════════════════════════════════════════════

    private static void ValidateTemplate(STProjectTemplate template)
    {
        string name = template.Name ?? "";
        if (string.IsNullOrWhiteSpace(name))
            throw new ProjectFileException("工程名不能为空。");

        if (name != name.Trim())
            throw new ProjectFileException("工程名首尾不能有空白。");

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.IndexOf('/') >= 0
            || name.IndexOf('\\') >= 0)
        {
            throw new ProjectFileException($"工程名 '{name}' 含非法字符。");
        }

        if (name.EndsWith(".stproj", StringComparison.OrdinalIgnoreCase))
            throw new ProjectFileException("工程名不应包含 .stproj 扩展名。");

        if (template.Sources.Count == 0)
            throw new ProjectFileException("工程至少需要一个源文件。");

        foreach (string source in template.Sources)
            ValidateRelativePath(source, ".st", "源文件");

        foreach (string reference in template.ModuleReferences)
            ValidateRelativePath(reference, ".stmod", "模块引用");

        string startup = EffectiveStartup(template);
        if (!IsIdentifier(startup))
            throw new ProjectFileException(
                $"入口程序名 '{startup}' 不是合法的 ST 标识符（请显式指定 StartupProgram）。");
    }

    private static void ValidateRelativePath(string value, string extension, string what)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ProjectFileException($"{what}路径不能为空。");

        if (Path.IsPathRooted(value))
            throw new ProjectFileException($"{what} '{value}' 必须是相对工程目录的路径。");

        if (!value.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            throw new ProjectFileException($"{what} '{value}' 必须以 {extension} 结尾。");

        foreach (string segment in value.Replace('\\', '/').Split('/'))
            if (segment == "..")
                throw new ProjectFileException($"{what} '{value}' 不能跳出工程目录。");
    }

    private static void EnsureInside(string dir, string absolute, string relative, string what)
    {
        string prefix = dir.EndsWith(Path.DirectorySeparatorChar)
            ? dir
            : dir + Path.DirectorySeparatorChar;

        if (!absolute.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new ProjectFileException(
                $"{what} '{relative}' 必须位于工程目录 '{dir}' 内。");
    }

    private static bool IsIdentifier(string value)
    {
        if (value.Length == 0)
            return false;

        if (!char.IsLetter(value[0]) && value[0] != '_')
            return false;

        foreach (char c in value)
            if (!char.IsLetterOrDigit(c) && c != '_')
                return false;

        return true;
    }

    // ═══════════════════════════════════════════════════════════
    //  工具
    // ═══════════════════════════════════════════════════════════

    private static string EffectiveStartup(STProjectTemplate template)
    {
        if (!string.IsNullOrWhiteSpace(template.StartupProgram))
            return template.StartupProgram.Trim();

        return Path.GetFileNameWithoutExtension(template.Sources[0]);
    }

    private static string Normalize(string relativePath) => relativePath.Replace('\\', '/');

    /// <summary>
    /// 把 XML 渲染成文本。
    ///
    /// 注意：<c>XDocument.Save(TextWriter)</c> 会把声明写成 TextWriter 的编码（<c>StringWriter</c>
    /// 是 UTF-16），与落盘的 UTF-8 字节不一致。必须经 <c>XmlWriter</c> + UTF-8 输出，
    /// 声明的编码才与实际字节一致。
    /// </summary>
    private static string RenderXml(XDocument doc)
    {
        doc.Declaration ??= new XDeclaration("1.0", "utf-8", null);

        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = Utf8NoBom,
        };

        using var stream = new MemoryStream();
        using (var xmlWriter = XmlWriter.Create(stream, settings))
            doc.Save(xmlWriter);

        return Utf8NoBom.GetString(stream.ToArray());
    }

    /// <summary>同目录临时文件 + 原子替换（失败清理临时文件）。</summary>
    private static void SaveAtomic(string path, string text)
    {
        string full = Path.GetFullPath(path);
        string dir = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(dir);

        string temp = Path.Combine(dir, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, text, Utf8NoBom);
            File.Move(temp, full, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
            throw;
        }
    }
}
