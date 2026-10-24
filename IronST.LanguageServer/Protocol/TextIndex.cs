using IronST.Common.Diagnostics;

namespace IronST.LanguageServer.Protocol;

/// <summary>
/// 文本索引 —— 在三种坐标系之间换算：
///
/// <list type="bullet">
/// <item><b>LSP 位置</b>：0 基行、0 基 UTF-16 码元列</item>
/// <item><b>UTF-16 绝对偏移</b>：.NET string 索引</item>
/// <item><b>Frontend 位置</b>：1 基行、1 基<b>码位</b>列（ANTLR 的 column）</item>
/// </list>
///
/// 关键差异：ANTLR 的 column 数的是<b>码位</b>（code point），LSP 数的是
/// <b>UTF-16 码元</b>。源码里只要出现一个汉字（占 1 个码位但 2 个 UTF-16 码元），
/// 同一行后半段的列号就会整体错位。本类通过按行建码位→UTF-16 映射表来精确换算。
/// </summary>
internal sealed class TextIndex
{
    private readonly string _text;
    private readonly int[] _lineStarts;                 // 每行起始 UTF-16 偏移，索引 = 0 基行号
    private readonly Dictionary<int, int[]> _cpToU16 = new(); // 0 基行号 → 该行 码位索引→UTF-16 偏移

    public TextIndex(string text)
    {
        _text = text;

        var starts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                starts.Add(i + 1);
        }
        _lineStarts = starts.ToArray();
    }

    public string Text => _text;
    public int LineCount => _lineStarts.Length;

    /// <summary>给定 0 基行号，返回该行起始的 UTF-16 偏移。</summary>
    public int LineStartOffset(int line0)
    {
        if (line0 < 0) return 0;
        if (line0 >= _lineStarts.Length) return _text.Length;
        return _lineStarts[line0];
    }

    /// <summary>给定 0 基行号，返回该行结尾（不含换行符）的 UTF-16 偏移。</summary>
    public int LineEndOffset(int line0)
    {
        int next = line0 + 1 < _lineStarts.Length ? _lineStarts[line0 + 1] : _text.Length;
        int end = next;
        while (end > LineStartOffset(line0) && (_text[end - 1] == '\n' || _text[end - 1] == '\r'))
            end--;
        return end;
    }

    /// <summary>LSP 位置 → UTF-16 绝对偏移（越界自动夹取）。</summary>
    public int OffsetOf(int line0, int utf16Char)
    {
        line0 = Math.Clamp(line0, 0, LineCount - 1);
        int start = LineStartOffset(line0);
        int end = LineEndOffset(line0);
        return Math.Clamp(start + Math.Max(0, utf16Char), start, end);
    }

    /// <summary>UTF-16 绝对偏移 → LSP 位置。</summary>
    public (int Line, int Character) PositionOf(int utf16Offset)
    {
        utf16Offset = Math.Clamp(utf16Offset, 0, _text.Length);

        // 二分查找所属行
        int lo = 0, hi = _lineStarts.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_lineStarts[mid] <= utf16Offset) lo = mid;
            else hi = mid - 1;
        }
        return (lo, utf16Offset - _lineStarts[lo]);
    }

    /// <summary>
    /// Frontend 坐标（1 基行、1 基码位列）→ LSP 位置。
    /// 做了码位 → UTF-16 码元的换算。
    /// </summary>
    public (int Line, int Character) ToLspPosition(int line1, int codePointColumn)
    {
        int line0 = Math.Clamp(line1 - 1, 0, LineCount - 1);
        var map = GetCodePointMap(line0);
        int cpIndex = Math.Clamp(codePointColumn - 1, 0, map.Length - 1);
        // map 存的是绝对 UTF-16 偏移，LSP 的 character 是行内偏移，需减去行首
        return (line0, map[cpIndex] - _lineStarts[line0]);
    }

    /// <summary>
    /// Frontend <see cref="SourceSpan"/> → LSP Range。
    /// EndColumn 的口径在前端是「Stop.Column + Stop.Text.Length」（码位列 + UTF-16 长度的混合），
    /// 因此这里用 start + (EndColumn - Column) 估算长度，再夹取到行尾，保证 Range 永远合法。
    /// </summary>
    public (int StartLine, int StartChar, int EndLine, int EndChar) ToLspRange(in SourceSpan span)
    {
        var (sl, sc) = ToLspPosition(span.Line, span.Column);
        int start = OffsetOf(sl, sc);

        int estimated = Math.Max(1, span.EndColumn - span.Column);
        int end = start + estimated;

        // 夹取到起始行的行尾，避免跨行的畸形 Range
        int lineEnd = LineEndOffset(sl);
        if (end > lineEnd) end = lineEnd;
        if (end < start) end = start;

        var (el, ec) = PositionOf(end);
        return (sl, sc, el, ec);
    }

    /// <summary>
    /// 在指定行（1 基）内查找作为独立单词出现的 <paramref name="name"/>，
    /// 返回 UTF-16 起止偏移。用于前端把 Column 填成 0 时补出精确范围。
    /// </summary>
    public (int Start, int End)? FindWordOnLine(int line1, string name)
    {
        if (string.IsNullOrEmpty(name) || line1 < 1 || line1 > LineCount)
            return null;

        int line0 = line1 - 1;
        int start = LineStartOffset(line0);
        int end = LineEndOffset(line0);
        string lineText = _text[start..end];

        int idx = 0;
        while (idx < lineText.Length)
        {
            int found = lineText.IndexOf(name, idx, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
                return null;

            bool okStart = found == 0 || !IsIdentChar(lineText[found - 1]);
            bool okEnd = found + name.Length >= lineText.Length
                         || !IsIdentChar(lineText[found + name.Length]);

            if (okStart && okEnd)
                return (start + found, start + found + name.Length);

            idx = found + 1;
        }

        return null;
    }

    /// <summary>码位索引 → 该行 UTF-16 偏移的映射表（末尾哨兵 = 行尾）。</summary>
    private int[] GetCodePointMap(int line0)
    {
        if (_cpToU16.TryGetValue(line0, out var cached))
            return cached;

        int start = LineStartOffset(line0);
        int end = LineEndOffset(line0);

        var map = new List<int> { start };
        for (int i = start; i < end;)
        {
            int cpLen = char.IsHighSurrogate(_text[i]) && i + 1 < end && char.IsLowSurrogate(_text[i + 1])
                ? 2
                : 1;
            i += cpLen;
            map.Add(i);
        }

        var arr = map.ToArray();
        _cpToU16[line0] = arr;
        return arr;
    }

    internal static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
