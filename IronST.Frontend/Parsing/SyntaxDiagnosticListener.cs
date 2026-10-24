using Antlr4.Runtime;
using IronST.Common.Diagnostics;

namespace IronST.Frontend.Parsing;

/// <summary>
/// 语法分析错误监听器 —— 将 ANTLR 的 RecognitionException 分类为结构化诊断信息。
/// 支持通过 IVocabulary 将 token 编号转换为人类可读的 token 名称。
/// </summary>
internal class SyntaxDiagnosticListener : IAntlrErrorListener<IToken>
{
    private readonly string? _fileName;
    private IVocabulary? _vocabulary;
    public List<Diagnostic> Diagnostics { get; } = new();

    public SyntaxDiagnosticListener(string? fileName = null) => _fileName = fileName;

    /// <summary>
    /// 设置 ANTLR 词表，用于将 token 编号转为可读名称。
    /// 通常由 FrontendPipeline 传入 parser.Vocabulary。
    /// </summary>
    public void SetVocabulary(IVocabulary vocabulary) => _vocabulary = vocabulary;

    public void SyntaxError(
        TextWriter output,
        IRecognizer recognizer,
        IToken offendingSymbol,
        int line,
        int charPositionInLine,
        string msg,
        RecognitionException e)
    {
        int endCol = charPositionInLine + (offendingSymbol?.Text?.Length ?? 1);

        var (code, detail) = Classify(e, msg, offendingSymbol, recognizer);

        Diagnostics.Add(new Diagnostic
        {
            Severity = DiagnosticSeverity.Error,
            Message = detail,
            Span = new SourceSpan(line, charPositionInLine + 1, line, endCol, _fileName),
            Code = code,
        });
    }

    private (ErrorCode code, string detail) Classify(
        RecognitionException? e,
        string msg,
        IToken? token,
        IRecognizer recognizer)
    {
        // 尝试从 recognizer 中获取词表（优先使用外部注入的）
        var vocab = _vocabulary;
        if (vocab == null && recognizer is Parser parser)
            vocab = parser.Vocabulary;

        if (e is InputMismatchException ime) {
            var expected = ime.GetExpectedTokens();
            string expectedStr;

            if (vocab != null && expected != null) {
                // 将 token 编号转为可读名称，最多显示 5 个
                var names = expected.ToList()
                    .Select(t => vocab.GetDisplayName(t))
                    .Where(n => !string.IsNullOrEmpty(n) && n != "'<INVALID>'")
                    .Take(5);
                expectedStr = string.Join(", ", names);
            } else {
                expectedStr = expected?.ToString() ?? "?";
            }

            var detail = $"期望 {expectedStr}，实际得到 '{token?.Text ?? "?"}'";
            return (ErrorCode.E1001, detail);
        }

        if (e is NoViableAltException) {
            var detail = $"无法解析 '{token?.Text ?? "?"}'";
            return (ErrorCode.E1002, detail);
        }

        if (e is LexerNoViableAltException) {
            var detail = $"无法识别的字符 '{token?.Text ?? "?"}'";
            return (ErrorCode.E1004, detail);
        }

        return ClassifyFromMessage(msg, token);
    }

    private static (ErrorCode code, string detail) ClassifyFromMessage(string msg, IToken? token)
    {
        if (msg.Contains("extraneous input", StringComparison.OrdinalIgnoreCase)) {
            var detail = $"多余的输入 '{token?.Text ?? "?"}'";
            return (ErrorCode.E1003, detail);
        }

        if (msg.Contains("missing", StringComparison.OrdinalIgnoreCase)) {
            return (ErrorCode.E1001, msg);
        }

        return (ErrorCode.E1000, msg);
    }
}