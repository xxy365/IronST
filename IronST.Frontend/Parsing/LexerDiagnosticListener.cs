using Antlr4.Runtime;
using IronST.Common.Diagnostics;

namespace IronST.Frontend.Parsing;

/// <summary>
/// 词法分析错误监听器 —— 收集 lexer 阶段产生的错误诊断。
/// ANTLR 默认的 ConsoleErrorListener 在 RemoveErrorListeners() 后被移除，
/// 若不补充自定义监听器，词法错误将被静默丢弃。
/// </summary>
internal class LexerDiagnosticListener : IAntlrErrorListener<int>
{
    private readonly string? _fileName;
    public List<Diagnostic> Diagnostics { get; } = new();

    public LexerDiagnosticListener(string? fileName = null) => _fileName = fileName;

    public void SyntaxError(
        TextWriter output,
        IRecognizer recognizer,
        int offendingSymbol,
        int line,
        int charPositionInLine,
        string msg,
        RecognitionException e)
    {
        int endCol = charPositionInLine + 1;

        string detail;
        if (offendingSymbol >= 0) {
            char ch = (char)offendingSymbol;
            detail = $"无法识别的字符 '{ch}' (U+{offendingSymbol:X4})";
        } else {
            detail = $"词法错误: {msg}";
        }

        Diagnostics.Add(new Diagnostic
        {
            Severity = DiagnosticSeverity.Error,
            Message = detail,
            Span = new SourceSpan(line, charPositionInLine + 1, line, endCol, _fileName),
            Code = ErrorCode.E1004,
        });
    }
}