using System.Text.RegularExpressions;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace IronST.StEditor.Editor;

/// <summary>
/// 正则 + 块注释状态机的语法高亮。
/// 精度接近 ANTLR token 级别，支持多行块注释。
/// </summary>
public partial class StSyntaxHighlightingTransformer : DocumentColorizingTransformer
{
    // ══════════════════════════════════════════
    //  块注释状态
    // ══════════════════════════════════════════

    private enum BlockState : byte { Normal = 0, InBlockComment = 1 }

    /// <summary>每行的结束块注释状态</summary>
    private BlockState[] _lineEndStates = Array.Empty<BlockState>();

    // ══════════════════════════════════════════
    //  颜色定义（实例级，可由主题切换）
    // ══════════════════════════════════════════

    private SolidColorBrush _commentBrush = MakeBrush(108, 112, 134);
    private SolidColorBrush _stringBrush = MakeBrush(166, 227, 161);
    private SolidColorBrush _numberBrush = MakeBrush(250, 179, 135);
    private SolidColorBrush _keywordBrush = MakeBrush(203, 166, 247);
    private SolidColorBrush _typeBrush = MakeBrush(137, 180, 250);
    private SolidColorBrush _operatorBrush = MakeBrush(249, 226, 175);
    private SolidColorBrush _directAddrBrush = MakeBrush(137, 220, 235);

    private static SolidColorBrush MakeBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// 切换语法高亮配色方案，然后强制重绘所有可见行。
    /// </summary>
    public void UpdateSyntaxColors(SyntaxHighlightColors colors)
    {
        _commentBrush = MakeBrush(colors.Comment.R, colors.Comment.G, colors.Comment.B);
        _stringBrush = MakeBrush(colors.String.R, colors.String.G, colors.String.B);
        _numberBrush = MakeBrush(colors.Number.R, colors.Number.G, colors.Number.B);
        _keywordBrush = MakeBrush(colors.Keyword.R, colors.Keyword.G, colors.Keyword.B);
        _typeBrush = MakeBrush(colors.Type.R, colors.Type.G, colors.Type.B);
        _operatorBrush = MakeBrush(colors.Operator.R, colors.Operator.G, colors.Operator.B);
        _directAddrBrush = MakeBrush(colors.DirectAddr.R, colors.DirectAddr.G, colors.DirectAddr.B);
    }

    // ══════════════════════════════════════════
    //  合并正则（交替顺序 = 匹配优先级）
    // ══════════════════════════════════════════

    [GeneratedRegex(
        @"(?<linecomment>//[^\r\n]*)" +
        @"|(?<string>'(?:''|[^'\r\n])*(?:'|$))" +
        @"|(?<timelit>(?:T(?:IME)?|D(?:ATE)?|TOD(?:_OF_DAY)?|DT(?:_AND_TIME)?)#[^\s;,)\]$$\}]+)" +
        @"|(?<basedint>\b\d[\d_]*#[0-9a-fA-F_]+\b)" +
        @"|(?<real>\b\d[\d_]*\.\d[\d_]*(?:[eE][+-]?\d+)?\b)" +
        @"|(?<int>\b\d[\d_]*\b)" +
        @"|(?<directvar>%[IQM][XBWDL]?\d+(?:\.\d+)*)" +
        @"|(?<keyword>\b(?:" +
            @"PROGRAM|END_PROGRAM|FUNCTION|END_FUNCTION|" +
            @"FUNCTION_BLOCK|END_FUNCTION_BLOCK|ACTION|END_ACTION|" +
            @"CONFIGURATION|END_CONFIGURATION|RESOURCE|END_RESOURCE|TASK|" +
            @"IF|THEN|ELSIF|ELSE|END_IF|" +
            @"FOR|TO|BY|DO|END_FOR|" +
            @"WHILE|END_WHILE|REPEAT|UNTIL|END_REPEAT|" +
            @"CASE|OF|END_CASE|" +
            @"RETURN|EXIT|" +
            @"AND|OR|XOR|NOT|MOD|" +
            @"TRUE|FALSE|WITH|ON" +
        @")\b)" +
        @"|(?<scope>\b(?:" +
            @"VAR|VAR_INPUT|VAR_OUTPUT|VAR_IN_OUT|" +
            @"VAR_GLOBAL|VAR_EXTERNAL|VAR_TEMP|VAR_ACCESS|VAR_CONFIG|" +
            @"END_VAR|AT|CONSTANT|RETAIN|NON_RETAIN" +
        @")\b)" +
        @"|(?<type>\b(?:" +
            @"TYPE|END_TYPE|STRUCT|END_STRUCT|ARRAY|REFERENCE|" +
            @"BOOL|BYTE|WORD|DWORD|LWORD|" +
            @"SINT|INT|DINT|LINT|USINT|UINT|UDINT|ULINT|" +
            @"REAL|LREAL|" +
            @"TIME|DATE|TIME_OF_DAY|TOD|DATE_AND_TIME|DT|" +
            @"STRING|WSTRING" +
        @")\b)" +
        @"|(?<op>:=|=>|REF=|\*\*|<=|>=|<>|\.\.|[+\-*/=<>()\[\]{},;.#\^:])" +
        @"|(?<ident>[A-Za-z_]\w*)",
        RegexOptions.IgnoreCase)]
    private static partial Regex TokenRegex();

    // ══════════════════════════════════════════
    //  公共 API：重建行状态缓存
    // ══════════════════════════════════════════

    public void RebuildLineStates(TextDocument doc)
    {
        var lineCount = doc.LineCount;
        if (_lineEndStates.Length != lineCount)
            _lineEndStates = new BlockState[lineCount];

        var state = BlockState.Normal;
        foreach (var line in doc.Lines) {
            var text = SafeGetLineText(doc, line);
            state = ScanBlockCommentState(text, state);
            _lineEndStates[line.LineNumber - 1] = state;
        }
    }

    // ══════════════════════════════════════════
    //  每行着色
    // ══════════════════════════════════════════

    protected override void ColorizeLine(DocumentLine line)
    {
        var doc = CurrentContext.Document;
        var lineText = SafeGetLineText(doc, line);
        if (lineText.Length == 0) return;

        int lineStart = line.Offset;

        var state = line.LineNumber > 1 && line.LineNumber - 2 < _lineEndStates.Length
            ? _lineEndStates[line.LineNumber - 2]
            : BlockState.Normal;

        int pos = 0;
        var regex = TokenRegex();

        while (pos < lineText.Length) {
            if (state == BlockState.InBlockComment) {
                int endIdx = lineText.IndexOf("*)", pos, StringComparison.Ordinal);
                if (endIdx < 0) {
                    ApplyBrush(lineStart + pos, lineStart + lineText.Length, _commentBrush);
                    return;
                } else {
                    ApplyBrush(lineStart + pos, lineStart + endIdx + 2, _commentBrush);
                    pos = endIdx + 2;
                    state = BlockState.Normal;
                    continue;
                }
            }

            if (pos + 1 < lineText.Length && lineText[pos] == '(' && lineText[pos + 1] == '*') {
                state = BlockState.InBlockComment;
                continue;
            }

            var m = regex.Match(lineText, pos);
            if (m.Success && m.Index == pos) {
                var brush = GetBrush(m);
                if (brush != null)
                    ApplyBrush(lineStart + pos, lineStart + pos + m.Length, brush);
                pos += m.Length;
            } else {
                pos++;
            }
        }
    }

    // ══════════════════════════════════════════
    //  Token → 颜色映射（改为实例字段）
    // ══════════════════════════════════════════

    private SolidColorBrush? GetBrush(Match m)
    {
        if (m.Groups["linecomment"].Success) return _commentBrush;
        if (m.Groups["string"].Success) return _stringBrush;
        if (m.Groups["timelit"].Success) return _stringBrush;
        if (m.Groups["basedint"].Success) return _numberBrush;
        if (m.Groups["real"].Success) return _numberBrush;
        if (m.Groups["int"].Success) return _numberBrush;
        if (m.Groups["directvar"].Success) return _directAddrBrush;
        if (m.Groups["keyword"].Success) return _keywordBrush;
        if (m.Groups["scope"].Success) return _keywordBrush;
        if (m.Groups["type"].Success) return _typeBrush;
        if (m.Groups["op"].Success) return _operatorBrush;
        return null;
    }

    // ══════════════════════════════════════════
    //  块注释状态扫描
    // ══════════════════════════════════════════

    private static BlockState ScanBlockCommentState(string text, BlockState startState)
    {
        var state = startState;
        int i = 0;

        while (i < text.Length) {
            if (state == BlockState.InBlockComment) {
                if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == ')') {
                    state = BlockState.Normal;
                    i += 2;
                } else {
                    i++;
                }
            } else {
                if (text[i] == '(' && i + 1 < text.Length && text[i + 1] == '*') {
                    state = BlockState.InBlockComment;
                    i += 2;
                } else if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '/') {
                    break;
                } else if (text[i] == '\'') {
                    i++;
                    while (i < text.Length) {
                        if (text[i] == '\'') {
                            i++;
                            if (i < text.Length && text[i] == '\'')
                                i++;
                            else
                                break;
                        } else {
                            i++;
                        }
                    }
                } else {
                    i++;
                }
            }
        }

        return state;
    }

    // ══════════════════════════════════════════
    //  工具
    // ══════════════════════════════════════════

    private void ApplyBrush(int start, int end, SolidColorBrush brush)
    {
        ChangeLinePart(start, end, element => {
            element.TextRunProperties.SetForegroundBrush(brush);
        });
    }

    private static string SafeGetLineText(TextDocument doc, DocumentLine line)
    {
        int start = line.Offset;
        int length = line.Length;
        if (start + length > doc.TextLength)
            length = Math.Max(0, doc.TextLength - start);
        return length > 0 ? doc.GetText(start, length) : string.Empty;
    }
}
