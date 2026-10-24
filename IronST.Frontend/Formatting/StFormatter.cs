using Antlr4.Runtime;
using IronST.Common.Diagnostics;
using IronST.Frontend.Generated;
using IronST.Frontend.Parsing;

namespace IronST.Frontend.Formatting;

/// <summary>
/// ST source formatter driven by the parse tree plus the hidden-channel token stream
/// (whitespace and comments).
/// </summary>
/// <remarks>
/// <para>
/// The lexer keeps <c>WS</c>, <c>LINE_COMMENT</c> and <c>BLOCK_COMMENT</c> on the
/// hidden channel, so the formatter can rebuild the document without losing
/// comments or blank lines while still deciding indentation and spacing from the
/// grammar structure.
/// </para>
/// <para>
/// If the source cannot be parsed the formatter is conservative: the original text
/// is returned unchanged together with the diagnostics, so a half-written file is
/// never mangled.
/// </para>
/// </remarks>
public static class StFormatter
{
    /// <summary>Format a single ST source file.</summary>
    /// <param name="source">Source text.</param>
    /// <param name="options">Formatting options; defaults to <see cref="FormattingOptions.Default"/>.</param>
    public static FormattingResult Format(string source, FormattingOptions? options = null)
    {
        options ??= FormattingOptions.Default;

        if (string.IsNullOrEmpty(source))
            return new FormattingResult(source ?? string.Empty, false, Array.Empty<Diagnostic>());

        var diagnostics = new List<Diagnostic>();
        CommonTokenStream tokenStream;
        STParser.CompilationUnitContext tree;

        try
        {
            var input = CharStreams.fromString(source);
            var lexer = new STLexer(input);
            lexer.RemoveErrorListeners();
            var lexerListener = new LexerDiagnosticListener();
            lexer.AddErrorListener(lexerListener);

            tokenStream = new CommonTokenStream(lexer);
            tokenStream.Fill();

            var parser = new STParser(tokenStream);
            parser.RemoveErrorListeners();
            var syntaxListener = new SyntaxDiagnosticListener();
            syntaxListener.SetVocabulary(parser.Vocabulary);
            parser.AddErrorListener(syntaxListener);

            tree = parser.compilationUnit();

            diagnostics.AddRange(lexerListener.Diagnostics);
            diagnostics.AddRange(syntaxListener.Diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.Add(
                new Diagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Message = $"格式化失败: {ex.Message}",
                    Span = SourceSpan.Unknown,
                    Code = ErrorCode.E1000,
                });
            return new FormattingResult(source, false, diagnostics);
        }

        // Unparsable input is returned untouched: formatting must never corrupt code.
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return new FormattingResult(source, false, diagnostics);

        var newLine = options.NewLine ?? DetectNewLine(source);
        var plan = FormattingPlan.Build(tree);
        var text = new TokenStreamFormatter(tokenStream.GetTokens(), plan, options, newLine).Format();

        return new FormattingResult(
            text,
            !string.Equals(text, source, StringComparison.Ordinal),
            diagnostics);
    }

    /// <summary>Detect the line ending used by the source so CRLF/LF files keep their style.</summary>
    private static string DetectNewLine(string source)
    {
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] == '\r')
                return i + 1 < source.Length && source[i + 1] == '\n' ? "\r\n" : "\r";
            if (source[i] == '\n')
                return "\n";
        }

        return Environment.NewLine;
    }
}
