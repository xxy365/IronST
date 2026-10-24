using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;

namespace IronST.StEditor.Editor;

public class StFoldingStrategy
{
    private static readonly (string Start, string End)[] BlockPairs =
    {
        ("PROGRAM",        "END_PROGRAM"),
        ("FUNCTION",       "END_FUNCTION"),
        ("FUNCTION_BLOCK", "END_FUNCTION_BLOCK"),
        ("ACTION",         "END_ACTION"),
        ("IF",             "END_IF"),
        ("FOR",            "END_FOR"),
        ("WHILE",          "END_WHILE"),
        ("REPEAT",         "END_REPEAT"),
        ("CASE",           "END_CASE"),
        ("TYPE",           "END_TYPE"),
        ("STRUCT",         "END_STRUCT"),
        ("CONFIGURATION",  "END_CONFIGURATION"),
        ("RESOURCE",       "END_RESOURCE"),
    };

    public void UpdateFoldings(FoldingManager manager, TextDocument document)
    {
        var foldings = new List<NewFolding>();
        var stack = new Stack<(string keyword, int offset, string title)>();

        foreach (var line in document.Lines) {
            var text = document.GetText(line).Trim();
            var upper = text.ToUpperInvariant();

            // 检查开始关键字
            foreach (var (start, _) in BlockPairs) {
                if (StartsWithKeyword(upper, start)) {
                    stack.Push((start, line.Offset, text));
                    break;
                }
            }

            // 检查结束关键字
            foreach (var (_, end) in BlockPairs) {
                if (StartsWithKeyword(upper, end)) {
                    if (stack.Count > 0) {
                        var (kw, startOffset, title) = stack.Pop();
                        if (line.EndOffset > startOffset) {
                            // 折叠标题取开始行的前 60 个字符
                            var displayTitle = title.Length > 60
                                ? title[..60] + " ..."
                                : title + " ...";

                            foldings.Add(new NewFolding(startOffset, line.EndOffset)
                            {
                                Name = displayTitle
                            });
                        }
                    }
                    break;
                }
            }
        }

        foldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        manager.UpdateFoldings(foldings, -1);
    }

    private static bool StartsWithKeyword(string text, string keyword)
    {
        if (!text.StartsWith(keyword)) return false;
        // 确保关键字后面不是字母（避免 PARTIAL 匹配 PARTIAL_xxx）
        return text.Length == keyword.Length || !char.IsLetter(text[keyword.Length]);
    }
}
