using IronST.Common.Diagnostics;

namespace IronST.Frontend.Formatting;

/// <summary>
/// Result of formatting an ST source file.
/// </summary>
public sealed record FormattingResult(
    string Text,
    bool Changed,
    IReadOnlyList<Diagnostic> Diagnostics
)
{
    /// <summary>True when the formatter found syntax/lexer errors and left the source untouched.</summary>
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}
