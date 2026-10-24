// DiagnosticFilter.cs — 多文件分析后，挑出属于某个文件的诊断（IDE 阶段 VI）。
//
// AnalyzeFiles 把所有文件的诊断并成一张表（顺序 = 文件顺序），而编辑器的波浪线、
// 错误列表、状态栏都只该显示当前标签页那一份。归属靠 SourceSpan.FileName ——
// CLI 输出里的 `Main.st:21:15` 用的就是同一个字段。
using IronST.Common.Diagnostics;

namespace IronST.IDE.Services;

public static class DiagnosticFilter
{
    /// <summary>取出属于 <paramref name="fileName"/> 的诊断（大小写不敏感）。</summary>
    /// <remarks>
    /// <c>Span.FileName</c> 为空的诊断（E1000「没有输入文件」这类全局诊断，或
    /// 根本没带 Span 的）按当前文件处理 —— 宁可多报一条，也不要把真错误吞掉。
    /// </remarks>
    public static IReadOnlyList<Diagnostic> ForFile(
        IReadOnlyList<Diagnostic> diagnostics, string fileName)
    {
        var mine = new List<Diagnostic>();

        foreach (var d in diagnostics)
        {
            string? file = d.Span?.FileName;

            if (string.IsNullOrEmpty(file)
                || string.Equals(file, fileName, StringComparison.OrdinalIgnoreCase))
            {
                mine.Add(d);
            }
        }

        return mine;
    }
}
