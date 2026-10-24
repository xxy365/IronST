// CompilationSources.cs — 「前端该看到哪些源文件」（IDE 阶段 VI）。
//
// 打开工程后只分析当前标签页，会把别的文件里的类型/函数全报成未声明
// （`Main.st` 里 16 个假错误就是这么来的），而同一个工程 `build` 起来是 0 错误 ——
// 因为 CLI 走的是多文件路径。这里统一算出前端要吃的文件列表：
//
//   · 没有工程 / 当前文件不属于工程 → 只有当前标签页（保持既有单文件行为）；
//   · 当前文件属于工程 → **整个工程**，其中当前文件用编辑器里的实时内容
//     （可能还没保存），其余从磁盘读；顺序与 .stproj 里一致。
//
// 分析链与调试链共用这一份，避免"编辑器报未声明、F5 又能跑"这种两套口径。
using IronST.CLI;

namespace IronST.IDE.Services;

public static class CompilationSources
{
    /// <summary>当前标签页在前端里的「文件名」：有路径就用路径，否则用显示名。</summary>
    /// <remarks>
    /// 诊断归属（<c>SourceSpan.FileName</c>）会原样带回这个字符串，
    /// 所以画波浪线、填错误列表时必须用同一个值去 <see cref="DiagnosticFilter"/>。
    /// </remarks>
    public static string NameFor(string? filePath, string fileName) =>
        string.IsNullOrEmpty(filePath) ? fileName : filePath;

    /// <summary>当前文件是否属于该工程（路径比较大小写不敏感）。</summary>
    public static bool IsInProject(STProjectFile project, string filePath) =>
        project.SourceFiles.Any(
            f => string.Equals(f, filePath, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 算出前端要分析的文件列表。
    /// </summary>
    /// <param name="project">当前打开的工程；<c>null</c> 表示没有工程。</param>
    /// <param name="currentPath">当前标签页的文件绝对路径（未保存的新文件为 <c>null</c>）。</param>
    /// <param name="currentFileName">当前标签页的显示名（<paramref name="currentPath"/> 为空时用它）。</param>
    /// <param name="currentCode">当前标签页里的实时文本。</param>
    /// <param name="readFile">读取工程里其它源文件（注入以便单测，生产传 <c>File.ReadAllText</c>）。</param>
    public static IReadOnlyList<(string Source, string FileName)> Build(
        STProjectFile? project,
        string? currentPath,
        string currentFileName,
        string currentCode,
        Func<string, string> readFile)
    {
        string currentName = NameFor(currentPath, currentFileName);

        if (project is null
            || string.IsNullOrEmpty(currentPath)
            || !IsInProject(project, currentPath))
        {
            return [(currentCode, currentName)];
        }

        var list = new List<(string Source, string FileName)>(project.SourceFiles.Count);

        foreach (string file in project.SourceFiles)
        {
            list.Add(string.Equals(file, currentPath, StringComparison.OrdinalIgnoreCase)
                ? (currentCode, currentName)
                : (readFile(file), file));
        }

        return list;
    }
}
