using IronST.Common.Diagnostics;
using IronST.LanguageServer.Protocol;
using System.Text.Json.Nodes;

namespace IronST.LanguageServer.Features;

/// <summary>
/// IronST 诊断 → LSP 诊断。
/// </summary>
internal static class DiagnosticsMapper
{
    public static JsonObject ToLsp(Diagnostic d, TextIndex index)
    {
        JsonObject range;
        if (d.Span is { } span && span.Line >= 1)
            range = Json.SpanRange(span, index);
        else
            range = Json.Range(0, 0, 0, 0);

        return Json.Obj(
            ("range", range),
            ("severity", ToSeverity(d.Severity)),
            // code 用 ErrorCode 编号，客户端可据此做规则开关
            ("code", d.Code.ToString()),
            ("source", "ironst"),
            ("message", BuildMessage(d)));
    }

    private static int ToSeverity(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => Lsp.SeverityError,
        DiagnosticSeverity.Warning => Lsp.SeverityWarning,
        DiagnosticSeverity.Info => Lsp.SeverityInfo,
        DiagnosticSeverity.Hint => Lsp.SeverityHint,
        _ => Lsp.SeverityError,
    };

    private static string BuildMessage(Diagnostic d)
    {
        var desc = d.Code.GetDescription();
        if (string.IsNullOrWhiteSpace(desc))
            return d.Message;
        // 例：E2015 未知的类型 'REAL1' —— 未知的类型 'REAL1'（E2015: 未知的类型）
        return $"{d.Message}（{d.Code}）";
    }
}
