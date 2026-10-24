using IronST.StEditor.Editor;

namespace IronST.StEditor.Features;

/// <summary>
/// 关键字自动大写。
/// 输入分隔符（空格/分号/回车等）时，检测光标前的单词是否是关键字，
/// 如果是且不是全大写，替换为大写。
///
/// 安全：用关键字集合 + 代码上下文检查验证，字符串/注释/变量名内不触发。
/// </summary>
public class AutoUppercase : EditorFeatureBase
{
    public override bool OnTextEntered(ICSharpCode.AvalonEdit.TextEditor editor, string inputText)
    {
        // 只在输入分隔符时触发
        if (inputText is not (" " or ";" or "\r" or "\n" or "\t"))
            return false;

        var doc = editor.Document;
        var offset = editor.CaretOffset;

        // 找到光标前的单词边界
        int wordEnd = offset - 1;
        int wordStart = wordEnd;
        while (wordStart > 0 && (char.IsLetterOrDigit(doc.GetCharAt(wordStart - 1))
                                  || doc.GetCharAt(wordStart - 1) == '_'))
            wordStart--;

        if (wordStart >= wordEnd) return false;

        int wordLen = wordEnd - wordStart;
        var word = doc.GetText(wordStart, wordLen);
        var upper = word.ToUpperInvariant();

        // 已经全大写 → 跳过
        if (word == upper) return false;

        // 验证是关键字且不在字符串/注释内
        if (!IsKeywordAtPosition(doc, wordStart, wordLen))
            return false;

        doc.Replace(wordStart, wordLen, upper);
        editor.CaretOffset = offset;
        return true;
    }

    /// <summary>
    /// 检查指定范围是否对应一个 ST 关键字（不在字符串或注释内）。
    /// </summary>
    public static bool IsKeywordAtPosition(
        ICSharpCode.AvalonEdit.Document.TextDocument doc, int absStart, int length)
    {
        try {
            var line = doc.GetLineByOffset(absStart);
            var lineText = doc.GetText(line);
            var relStart = absStart - line.Offset;

            if (relStart < 0 || relStart + length > lineText.Length)
                return false;

            var word = lineText.Substring(relStart, length);

            // 是已知关键字 + 在代码上下文中
            return StCompletionProvider.AllKeywords.Contains(word)
                && AutoAssign.IsInCodeContext(doc, absStart);
        } catch { }

        return false;
    }
}
