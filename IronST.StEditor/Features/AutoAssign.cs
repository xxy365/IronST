namespace IronST.StEditor.Features;

/// <summary>
/// 自动输入 `:=`
/// 用户输入 `:` 时，自动追加 `=`，光标位于 `=` 之后。
///
/// 安全条件（全部满足才触发）：
/// - 前面不是 `:=`（避免重复）
/// - 后面不是 `=`（避免光标在中间时误触发）
/// - 不在字符串或注释内
/// </summary>
public class AutoAssign : EditorFeatureBase
{
    public override bool OnTextEntered(ICSharpCode.AvalonEdit.TextEditor editor, string inputText)
    {
        if (inputText != ":") return false;

        var doc = editor.Document;
        var offset = editor.CaretOffset;

        // 前面已经是 `:=`
        if (offset >= 2 && doc.GetText(offset - 2, 2) == ":=")
            return false;

        // 后面已经是 `=`
        if (offset < doc.TextLength && doc.GetCharAt(offset) == '=')
            return false;

        // 在字符串或注释内 → 跳过
        if (!IsInCodeContext(doc, offset - 1))
            return false;

        doc.Insert(offset, "=");
        editor.CaretOffset = offset + 1;
        return true;
    }

    /// <summary>
    /// 检查文档偏移是否在代码上下文中（不在字符串或注释内）。
    /// 仅扫描当前行，与原 ANTLR Lexer 单行分析行为等价。
    /// </summary>
    public static bool IsInCodeContext(
        ICSharpCode.AvalonEdit.Document.TextDocument doc, int absOffset)
    {
        try {
            var line = doc.GetLineByOffset(absOffset);
            var lineText = doc.GetText(line);
            var relOffset = absOffset - line.Offset;

            int pos = 0;
            while (pos <= relOffset && pos < lineText.Length) {
                // 块注释 (* ... *)
                if (pos + 1 < lineText.Length
                    && lineText[pos] == '(' && lineText[pos + 1] == '*') {
                    int end = lineText.IndexOf("*)", pos + 2, StringComparison.Ordinal);
                    if (end < 0)
                        return false;               // 到行尾都是注释
                    if (relOffset <= end + 1)
                        return false;               // 目标在块注释内
                    pos = end + 2;
                    continue;
                }

                // 行注释 //
                if (pos + 1 < lineText.Length
                    && lineText[pos] == '/' && lineText[pos + 1] == '/')
                    return false;                   // 到行尾都是注释

                // 字符串字面量 '...'
                if (lineText[pos] == '\'') {
                    pos++;
                    while (pos < lineText.Length) {
                        if (lineText[pos] == '\'') {
                            pos++;
                            if (pos < lineText.Length && lineText[pos] == '\'')
                                pos++;              // 转义的单引号 ''
                            else
                                break;              // 字符串结束
                        } else {
                            pos++;
                        }
                    }
                    if (pos > relOffset)
                        return false;               // 目标在字符串内
                    continue;
                }

                pos++;
            }
        } catch { }

        return true;
    }
}
