namespace IronST.StEditor.Features;

/// <summary>
/// 自动插入 END 块 + snippet 模板。
///
/// 效果：行首输入 "IF " → 生成：
///   IF condition THEN
///       [光标在此]
///   END_IF;
///
/// "condition" 被选中，用户可以直接输入覆盖。
///
/// 触发方式：
///   1. TextEntered 收到空格（无补全窗口时）
///   2. 补全窗口插入关键字后通过 AfterInsert 回调
/// </summary>
public class AutoEndBlock : EditorFeatureBase
{
    // ══════════════════════════════════════════════════════════════
    //  Snippet 模板定义
    //
    //  {0} = 占位符文本（被选中，用户可直接输入覆盖）
    //  {1} = 当前行的缩进
    //  {2} = 当前行的缩进 + 一级缩进（4 空格）
    // ══════════════════════════════════════════════════════════════

    private record SnippetTemplate(string Format, string? Placeholder);

    private static readonly Dictionary<string, SnippetTemplate> Templates = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ["IF"] = new(
            "IF {0} THEN\n{2}\n{1}END_IF;",
            "condition"),

        ["FOR"] = new(
            "FOR {0} := 0 TO 10 BY 1 DO\n{2}\n{1}END_FOR;",
            "i"),

        ["WHILE"] = new(
            "WHILE {0} DO\n{2}\n{1}END_WHILE;",
            "condition"),

        ["REPEAT"] = new(
            "REPEAT\n{2}\n{1}UNTIL {0};",
            "condition"),

        ["CASE"] = new(
            "CASE {0} OF\n{2}0: \n{1}ELSE\n{2}\n{1}END_CASE;",
            "expression"),

        ["PROGRAM"] = new(
            "PROGRAM {0}\n\n{1}END_PROGRAM;",
            "name"),

        ["FUNCTION"] = new(
            "FUNCTION {0} : ReturnType\n\n{1}END_FUNCTION;",
            "name"),

        ["FUNCTION_BLOCK"] = new(
            "FUNCTION_BLOCK {0}\n\n{1}END_FUNCTION_BLOCK;",
            "name"),

        ["ACTION"] = new(
            "ACTION {0}\n\n{1}END_ACTION;",
            "name"),

        ["TYPE"] = new(
            "TYPE {0}\n\n{1}END_TYPE;",
            "name"),
    };

    // 也作为简单的块关键字查询（兼容旧逻辑）
    public static readonly HashSet<string> BlockKeywordSet = new(
        Templates.Keys, StringComparer.OrdinalIgnoreCase);

    // ══════════════════════════════════════════════════════════════
    //  TextEntered 入口（空格触发）
    // ══════════════════════════════════════════════════════════════

    public override bool OnTextEntered(ICSharpCode.AvalonEdit.TextEditor editor, string inputText)
    {
        if (inputText != " ") return false;
        return TryInsert(editor);
    }

    // ══════════════════════════════════════════════════════════════
    //  外部调用入口（补全窗口插入后回调）
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 检查光标前的单词是否是块关键字，如果是则插入 snippet。
    /// 可从 TextEntered 或补全回调中调用。
    /// </summary>
    public bool TryInsert(ICSharpCode.AvalonEdit.TextEditor editor)
    {
        var doc = editor.Document;
        var offset = editor.CaretOffset;

        // 找到光标前的单词
        int wordEnd = offset - 1;
        int wordStart = wordEnd;
        while (wordStart > 0 && (char.IsLetterOrDigit(doc.GetCharAt(wordStart - 1))
                                  || doc.GetCharAt(wordStart - 1) == '_'))
            wordStart--;

        if (wordStart >= wordEnd) return false;

        var word = doc.GetText(wordStart, wordEnd - wordStart);
        if (!Templates.TryGetValue(word, out var template)) return false;

        var line = doc.GetLineByOffset(wordStart);

        // 行首检查：单词前面只有空白
        var textBeforeWord = doc.GetText(line.Offset, wordStart - line.Offset);
        if (!string.IsNullOrWhiteSpace(textBeforeWord)) return false;

        // 行尾检查：光标后面只有空白（或在行尾）
        var textAfterCursor = doc.GetText(offset, line.EndOffset - offset);
        if (!string.IsNullOrWhiteSpace(textAfterCursor)) return false;

        // 防重复：300 字符内没有对应的 END 关键字
        string endMarker = word.ToUpperInvariant() switch
        {
            "IF" => "END_IF",
            "FOR" => "END_FOR",
            "WHILE" => "END_WHILE",
            "REPEAT" => "END_REPEAT",
            "CASE" => "END_CASE",
            "PROGRAM" => "END_PROGRAM",
            "FUNCTION" => "END_FUNCTION",
            "FUNCTION_BLOCK" => "END_FUNCTION_BLOCK",
            "ACTION" => "END_ACTION",
            "TYPE" => "END_TYPE",
            _ => ""
        };

        if (endMarker.Length > 0) {
            int searchEnd = Math.Min(doc.TextLength, offset + 300);
            if (searchEnd > offset) {
                var ahead = doc.GetText(offset, searchEnd - offset);
                if (ahead.Contains(endMarker, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }

        // ── 生成 snippet ──

        // 缩进：当前行的前导空白
        var indent = GetLineIndent(doc, line);
        var indentExtra = indent + "    ";

        // 填充模板
        var placeholder = template.Placeholder ?? "";
        var snippet = string.Format(template.Format, placeholder, indent, indentExtra);

        // 删除光标前的单词（用模板中的关键字替代）
        var keywordUpper = word.ToUpperInvariant();
        doc.Remove(wordStart, wordEnd - wordStart);

        // 计算插入位置（删除单词后 offset 变了）
        int insertOffset = wordStart;

        // 构建最终文本：用大写关键字 + snippet 的其余部分
        // snippet 的 Format 以关键字开头，例如 "IF {0} THEN\n..."
        // 所以用 keywordUpper + snippet[关键字长度..] 来确保大写
        var snippetText = keywordUpper + snippet[keywordUpper.Length..];

        doc.Insert(insertOffset, snippetText);

        // ── 选中占位符 ──
        if (placeholder.Length > 0) {
            // 占位符在插入文本中的位置：关键字 + 一个空格
            int placeholderStart = insertOffset + keywordUpper.Length + 1;
            editor.Select(placeholderStart, placeholder.Length);
        } else {
            // 无占位符：光标放在关键字后面
            editor.CaretOffset = insertOffset + keywordUpper.Length + 1;
        }

        return true;
    }

    /// <summary>
    /// 获取行的前导空白（缩进）
    /// </summary>
    private static string GetLineIndent(
        ICSharpCode.AvalonEdit.Document.TextDocument doc,
        ICSharpCode.AvalonEdit.Document.DocumentLine line)
    {
        var lineText = doc.GetText(line);
        int i = 0;
        while (i < lineText.Length && (lineText[i] == ' ' || lineText[i] == '\t'))
            i++;
        return lineText[..i];
    }
}
