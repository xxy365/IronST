using Antlr4.Runtime;
using System.Text;
using IronST.Frontend.Generated;

namespace IronST.Frontend.Formatting;

/// <summary>
/// Renders the token stream (default channel + hidden channel) back to text using
/// the structural plan produced by <see cref="FormattingPlan"/>.
/// </summary>
internal sealed class TokenStreamFormatter
{
    private readonly IList<IToken> _tokens;
    private readonly FormattingPlan _plan;
    private readonly FormattingOptions _options;
    private readonly string _newLine;
    private readonly StringBuilder _sb = new();

    private bool _hasContent;
    private bool _atLineStart = true;
    private int _currentIndent;
    private bool _pendingLineBreak;
    private int _pendingBlankLines;
    private IToken? _prevToken;
    private bool _prevWasUnary;
    private bool _lastWasComment;
    private bool _forceLineBreak;

    public TokenStreamFormatter(
        IList<IToken> tokens,
        FormattingPlan plan,
        FormattingOptions options,
        string newLine)
    {
        _tokens = tokens;
        _plan = plan;
        _options = options;
        _newLine = newLine;
    }

    public string Format()
    {
        var pending = new StringBuilder();

        for (var i = 0; i < _tokens.Count; i++)
        {
            var token = _tokens[i];
            if (token.Type == TokenConstants.EOF)
                break;

            if (token.Channel == TokenConstants.HiddenChannel)
            {
                if (IsWhitespace(token))
                {
                    pending.Append(token.Text);
                    continue;
                }

                WriteComment(token, pending.ToString(), FindNextVisibleIndent(i));
                pending.Clear();
                continue;
            }

            WriteToken(token, pending.ToString());
            pending.Clear();
        }

        if (_hasContent && _options.InsertFinalNewLine)
            EnsureFinalNewLine();

        return _sb.ToString();
    }

    // ── visible tokens ────────────────────────────────────────────────────

    private void WriteToken(IToken token, string whitespace)
    {
        var hasNewLine = ContainsNewLine(whitespace);
        var blankLines = BlankLineCount(whitespace);

        if (_plan.LineIndent.TryGetValue(token.TokenIndex, out var indent))
        {
            RequestLineBreak(blankLines);
            _currentIndent = indent;
        }
        else if (_forceLineBreak)
        {
            RequestLineBreak(blankLines);
        }
        else if (hasNewLine && (_lastWasComment || blankLines > 0))
        {
            // Keep the line structure around comments and blank lines even when the
            // construct itself is not a line start (e.g. inside an expression).
            RequestLineBreak(blankLines);
        }

        if (_pendingLineBreak)
            FlushLineBreak();

        var unary = (token.Type == STParser.MINUS || token.Type == STParser.PLUS)
            && IsUnaryContext(_prevToken);

        if (_atLineStart)
        {
            WriteIndent();
        }
        else if (_hasContent)
        {
            var needSpace = _lastWasComment
                ? NeedsSpaceAfterComment(token)
                : NeedsSpace(_prevToken, token, _prevWasUnary, _plan);

            if (needSpace)
                _sb.Append(' ');
        }

        _sb.Append(token.Text);
        _hasContent = true;
        _atLineStart = false;
        _prevToken = token;
        _prevWasUnary = unary;
        _lastWasComment = false;
        _forceLineBreak = false;
    }

    // ── comments ──────────────────────────────────────────────────────────

    private void WriteComment(IToken token, string whitespace, int nextIndent)
    {
        var text = token.Text ?? string.Empty;
        var hasNewLine = ContainsNewLine(whitespace);
        var blankLines = BlankLineCount(whitespace);
        var isLineComment = token.Type == STLexer.LINE_COMMENT;

        if (hasNewLine || _forceLineBreak)
        {
            // Comment starts its own line.
            RequestLineBreak(blankLines);
            if (nextIndent >= 0)
                _currentIndent = nextIndent;

            FlushLineBreak();
            WriteIndent();
            _sb.Append(text);

            _hasContent = true;
            _atLineStart = false;
            _lastWasComment = true;
            _forceLineBreak = true;
        }
        else
        {
            // Trailing comment on the current line.
            if (_hasContent && !_atLineStart && NeedsSpaceBeforeComment(_prevToken))
                _sb.Append(' ');

            _sb.Append(text);
            _hasContent = true;
            _atLineStart = false;
            _lastWasComment = true;
            _forceLineBreak = isLineComment;
        }

        if (!isLineComment && ContainsNewLine(text))
        {
            // Multi-line block comment: the text is kept verbatim, only the position
            // of the following token is decided by the plan.
            _atLineStart = text.EndsWith("\n", StringComparison.Ordinal) ||
                           text.EndsWith("\r", StringComparison.Ordinal);
            if (_atLineStart)
                _lastWasComment = false;
        }
    }

    private int FindNextVisibleIndent(int index)
    {
        for (var i = index + 1; i < _tokens.Count; i++)
        {
            var token = _tokens[i];
            if (token.Type == TokenConstants.EOF)
                break;
            if (token.Channel == TokenConstants.HiddenChannel)
                continue;

            return _plan.LineIndent.TryGetValue(token.TokenIndex, out var indent) ? indent : -1;
        }

        return -1;
    }

    // ── line handling ─────────────────────────────────────────────────────

    private void RequestLineBreak(int blankLines)
    {
        _pendingLineBreak = true;

        var limit = _options.PreserveBlankLines ? Math.Max(0, _options.MaxBlankLines) : 0;
        _pendingBlankLines = Math.Max(_pendingBlankLines, Math.Min(blankLines, limit));
    }

    private void FlushLineBreak()
    {
        if (!_hasContent)
        {
            // Never emit leading newlines.
            _pendingLineBreak = false;
            _pendingBlankLines = 0;
            _atLineStart = true;
            return;
        }

        if (_atLineStart)
        {
            for (var i = 0; i < _pendingBlankLines; i++)
                _sb.Append(_newLine);
        }
        else
        {
            _sb.Append(_newLine);
            for (var i = 0; i < _pendingBlankLines; i++)
                _sb.Append(_newLine);
            _atLineStart = true;
        }

        _pendingLineBreak = false;
        _pendingBlankLines = 0;
    }

    private void WriteIndent()
    {
        if (_currentIndent > 0)
            _sb.Append(new string(_options.UseTabs ? '\t' : ' ', _currentIndent * (_options.UseTabs ? 1 : Math.Max(0, _options.IndentSize))));

        _atLineStart = false;
    }

    private void EnsureFinalNewLine()
    {
        if (_pendingLineBreak)
            FlushLineBreak();

        var text = _sb.ToString();
        var end = text.Length;
        while (end > 0 && (text[end - 1] == '\n' || text[end - 1] == '\r'))
            end--;

        if (end == 0)
            return;

        _sb.Clear();
        _sb.Append(text, 0, end);
        _sb.Append(_newLine);
    }

    // ── spacing rules ─────────────────────────────────────────────────────

    private static bool NeedsSpace(
        IToken? prev,
        IToken current,
        bool prevWasUnary,
        FormattingPlan plan)
    {
        if (prev == null)
            return false;

        var p = prev.Type;
        var c = current.Type;

        // Explicit "no space before" tokens.
        if (c is STParser.RPAREN or STParser.RBRACK or STParser.COMMA or STParser.SEMI
            or STParser.DOT or STParser.DOTDOT or STParser.HASH or STParser.CARET)
            return false;

        if (plan.NoSpaceBefore.Contains(current.TokenIndex))
            return false;

        // Explicit "no space after" tokens.
        if (p is STParser.LPAREN or STParser.LBRACK or STParser.DOT or STParser.DOTDOT
            or STParser.HASH or STParser.CARET)
            return false;

        // Unary sign binds directly to its operand: "x := -1".
        if (prevWasUnary)
            return false;

        if (c == STParser.LPAREN)
            return NeedsSpaceBeforeParen(p);

        if (c == STParser.LBRACK)
            return false;

        // "x : INT", "MyType : STRUCT"
        if (c == STParser.COLON || p == STParser.COLON)
            return true;

        if (p == STParser.COMMA)
            return true;

        if (p == STParser.SEMI)
            return false;

        return true;
    }

    private static bool NeedsSpaceBeforeParen(int prevType) =>
        prevType is STParser.IF or STParser.ELSIF or STParser.WHILE or STParser.FOR
            or STParser.REPEAT or STParser.UNTIL or STParser.CASE or STParser.THEN
            or STParser.DO or STParser.OF or STParser.NOT or STParser.AND
            or STParser.OR or STParser.XOR or STParser.MOD or STParser.PLUS
            or STParser.MINUS or STParser.STAR or STParser.SLASH or STParser.POWER
            or STParser.EQ or STParser.NEQ or STParser.LT or STParser.GT
            or STParser.LTE or STParser.GTE or STParser.ASSIGN or STParser.RARROW
            or STParser.REF_ASSIGN or STParser.COLON or STParser.COMMA;

    private static bool NeedsSpaceBeforeComment(IToken? prev)
    {
        if (prev == null)
            return false;

        return prev.Type is not (STParser.LPAREN or STParser.LBRACK or STParser.DOT
            or STParser.DOTDOT or STParser.HASH or STParser.CARET);
    }

    private static bool NeedsSpaceAfterComment(IToken current) =>
        current.Type is not (STParser.RPAREN or STParser.RBRACK or STParser.COMMA
            or STParser.SEMI or STParser.DOT or STParser.DOTDOT or STParser.HASH
            or STParser.CARET);

    private static bool IsUnaryContext(IToken? prev)
    {
        if (prev == null)
            return true;

        return prev.Type is STParser.LPAREN or STParser.LBRACK or STParser.COMMA
            or STParser.COLON or STParser.ASSIGN or STParser.RARROW or STParser.REF_ASSIGN
            or STParser.POWER or STParser.PLUS or STParser.MINUS or STParser.STAR
            or STParser.SLASH or STParser.MOD or STParser.EQ or STParser.NEQ
            or STParser.LT or STParser.GT or STParser.LTE or STParser.GTE
            or STParser.AND or STParser.OR or STParser.XOR or STParser.NOT
            or STParser.THEN or STParser.DO or STParser.OF or STParser.TO
            or STParser.BY or STParser.RETURN or STParser.IF or STParser.WHILE
            or STParser.UNTIL or STParser.ELSIF or STParser.ELSE or STParser.REPEAT
            or STParser.FOR or STParser.CASE or STParser.WITH or STParser.AT
            or STParser.HASH;
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private static bool IsWhitespace(IToken token) => token.Type == STLexer.WS;

    private static bool ContainsNewLine(string text)
    {
        foreach (var c in text)
        {
            if (c == '\n' || c == '\r')
                return true;
        }

        return false;
    }

    private static int CountNewLines(string text)
    {
        var count = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                count++;
            }
            else if (text[i] == '\n')
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Number of empty lines contained in a whitespace run.</summary>
    private static int BlankLineCount(string text)
    {
        var newLines = CountNewLines(text);
        return newLines > 1 ? newLines - 1 : 0;
    }
}
