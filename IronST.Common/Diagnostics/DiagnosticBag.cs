// src/IronST.Common/Diagnostics/DiagnosticBag.cs
namespace IronST.Common.Diagnostics;

/// <summary>
/// 诊断信息收集器，各阶段（解析、语义分析、代码生成）统一使用。
/// </summary>
public class DiagnosticBag
{
    private readonly List<Diagnostic> _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;
    public bool HasErrors => _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    public int ErrorCount => _diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);

    public void Report(
        DiagnosticSeverity severity,
        string message,
        SourceSpan span,
        ErrorCode code = ErrorCode.E1000
    )
    {
        _diagnostics.Add(
            new Diagnostic
            {
                Severity = severity,
                Message = message,
                Span = span,
                Code = code,
            }
        );
    }

    public void Error(string message, SourceSpan span, ErrorCode code = ErrorCode.E1000) =>
        Report(DiagnosticSeverity.Error, message, span, code);

    public void Warning(string message, SourceSpan span, ErrorCode code = ErrorCode.E1000) =>
        Report(DiagnosticSeverity.Warning, message, span, code);

    public void Info(string message, SourceSpan span, ErrorCode code = ErrorCode.E1000) =>
        Report(DiagnosticSeverity.Info, message, span, code);

    public void AddRange(IEnumerable<Diagnostic> diagnostics) => _diagnostics.AddRange(diagnostics);

    public void Clear() => _diagnostics.Clear();
}
