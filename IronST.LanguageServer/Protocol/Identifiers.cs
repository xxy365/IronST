namespace IronST.LanguageServer.Protocol;

/// <summary>
/// 标识符定位与调用扫描等文本层工具。
/// 全部基于 UTF-16 绝对偏移操作。
/// </summary>
internal static class Identifiers
{
    /// <summary>
    /// 取偏移处（或其左侧紧邻处）的标识符。
    /// 返回 (名称, 起始偏移, 结束偏移)；偏移不在标识符上返回 null。
    /// </summary>
    public static (string Name, int Start, int End)? FindAt(TextIndex index, int offset)
    {
        var text = index.Text;
        offset = Math.Clamp(offset, 0, text.Length);

        int start = offset;
        while (start > 0 && TextIndex.IsIdentChar(text[start - 1]))
            start--;

        int end = offset;
        while (end < text.Length && TextIndex.IsIdentChar(text[end]))
            end++;

        if (start == end)
            return null;

        return (text[start..end], start, end);
    }

    /// <summary>
    /// 取标识符左侧紧邻的限定名（跳过空白），例如 <c>a.b</c> 中的 <c>a</c>。
    /// 返回 null 表示左侧不是「.」。
    /// </summary>
    public static string? QualifierOf(TextIndex index, int identStart)
    {
        var text = index.Text;
        int i = identStart;

        // 跳过空白
        while (i > 0 && char.IsWhiteSpace(text[i - 1]))
            i--;

        if (i == 0 || text[i - 1] != '.')
            return null;

        // 取点之前的标识符
        i--;
        int end = i;
        while (i > 0 && TextIndex.IsIdentChar(text[i - 1]))
            i--;

        if (i == end)
            return null;

        return text[i..end];
    }

    /// <summary>
    /// 扫描到光标处，定位当前所在的函数调用。
    /// 跳过字符串字面量与注释，维护括号栈；返回调用名与开括号偏移。
    /// 光标不在任何调用内时返回 null。
    /// </summary>
    public static (string Callee, int OpenParen, int ActiveParameter)? FindEnclosingCall(
        TextIndex index, int cursor)
    {
        var text = index.Text;
        cursor = Math.Clamp(cursor, 0, text.Length);

        var stack = new Stack<int>(); // 未匹配的 '(' 偏移
        int i = 0;

        while (i < cursor && i < text.Length)
        {
            char c = text[i];

            // ── 注释 / 字符串：整段跳过 ──
            if (c == '/' && i + 1 < cursor && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n' && i < cursor) i++;
                continue;
            }
            if (c == '(' && i + 1 < cursor && text[i + 1] == '*')
            {
                i += 2;
                while (i < cursor && !(text[i] == '*' && i + 1 < text.Length && text[i + 1] == ')'))
                    i++;
                i = Math.Min(i + 2, cursor);
                continue;
            }
            if (c == '\'')
            {
                i++;
                while (i < cursor && text[i] != '\'')
                {
                    if (text[i] == '$' && i + 1 < text.Length) i++; // $X 转义
                    i++;
                }
                i = Math.Min(i + 1, cursor);
                continue;
            }

            if (c == '(')
            {
                stack.Push(i);
            }
            else if (c == ')')
            {
                if (stack.Count > 0) stack.Pop();
            }
            else if (c == ',')
            {
                // 逗号只有落在最内层括号里才推进 activeParameter
            }

            i++;
        }

        if (stack.Count == 0)
            return null;

        int open = stack.Peek();

        // 开括号左侧紧邻的标识符
        int j = open;
        while (j > 0 && char.IsWhiteSpace(text[j - 1]))
            j--;

        int end = j;
        while (j > 0 && TextIndex.IsIdentChar(text[j - 1]))
            j--;

        string callee = j == end ? "" : text[j..end];

        // 统计 open 之后到光标之间的顶层逗号
        int activeParam = 0;
        int depth = 0;
        for (int k = open + 1; k < cursor && k < text.Length; k++)
        {
            char ch = text[k];
            if (ch == '\'')
            {
                k++;
                while (k < cursor && text[k] != '\'') k++;
                continue;
            }
            if (ch == '(') depth++;
            else if (ch == ')') { if (depth > 0) depth--; }
            else if (ch == ',' && depth == 0) activeParam++;
        }

        return (callee, open, activeParam);
    }
}
