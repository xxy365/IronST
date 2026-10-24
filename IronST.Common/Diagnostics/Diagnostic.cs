// src/IronST.Common/Diagnostics/Diagnostic.cs
namespace IronST.Common.Diagnostics;

public class Diagnostic
{
    public DiagnosticSeverity Severity { get; init; }
    public string Message { get; init; } = "";
    public SourceSpan? Span { get; init; }
    public ErrorCode Code { get; init; }

    public override string ToString()
    {
        var loc = Span != null ? $"{Span}" : "<unknown>";
        var desc = Code.GetDescription();
        return $"{loc}: {Severity} [{Code}] {desc}: {Message}";
    }
}
