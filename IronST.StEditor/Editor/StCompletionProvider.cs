// src/IronST.StEditor/Editor/StCompletionProvider.cs
using System.Text.RegularExpressions;

namespace IronST.StEditor.Editor;

internal partial class StCompletionProvider
{
    // ══════════════════════════════════════════
    //  关键字库（完全不变）
    // ══════════════════════════════════════════

    private static readonly (string Text, string Desc)[] ControlKeywords =
    {
        ("PROGRAM",        "程序声明"),       ("END_PROGRAM",    "结束程序"),
        ("FUNCTION",       "函数声明"),       ("END_FUNCTION",   "结束函数"),
        ("FUNCTION_BLOCK", "功能块声明"),     ("END_FUNCTION_BLOCK", "结束功能块"),
        ("ACTION",         "动作声明"),       ("END_ACTION",     "结束动作"),
        ("VAR",            "局部变量"),       ("VAR_INPUT",      "输入变量"),
        ("VAR_OUTPUT",     "输出变量"),       ("VAR_IN_OUT",     "输入输出变量"),
        ("VAR_GLOBAL",     "全局变量"),       ("VAR_EXTERNAL",   "外部变量"),
        ("VAR_TEMP",       "临时变量"),       ("END_VAR",        "结束变量"),
        ("IF",             "条件分支"),       ("THEN",           ""),
        ("ELSIF",          ""),              ("ELSE",           ""),
        ("END_IF",         "结束 IF"),        ("FOR",            "FOR 循环"),
        ("TO",             ""),              ("BY",             "步长"),
        ("DO",             ""),              ("END_FOR",        "结束 FOR"),
        ("WHILE",          "WHILE 循环"),     ("END_WHILE",      "结束 WHILE"),
        ("REPEAT",         "REPEAT 循环"),    ("UNTIL",          ""),
        ("END_REPEAT",     "结束 REPEAT"),    ("CASE",           "CASE 分支"),
        ("OF",             ""),              ("END_CASE",       "结束 CASE"),
        ("RETURN",         "返回"),           ("EXIT",           "退出循环"),
        ("AND",            "逻辑与"),         ("OR",             "逻辑或"),
        ("XOR",            "逻辑异或"),       ("NOT",            "逻辑非"),
        ("MOD",            "取模"),           ("TRUE",           "布尔真"),
        ("FALSE",          "布尔假"),         ("TYPE",           "类型声明"),
        ("END_TYPE",       "结束类型"),       ("STRUCT",         "结构体"),
        ("END_STRUCT",     "结束结构体"),     ("ARRAY",          "数组"),
        ("CONSTANT",       "常量修饰符"),     ("RETAIN",         "断电保持"),
        ("CONFIGURATION",  "配置"),           ("RESOURCE",       "资源"),
        ("TASK",           "任务"),
    };

    private static readonly (string Text, string Desc)[] Types =
    {
        ("BOOL",       "布尔 (1 bit)"),      ("BYTE",       "字节 (8 bit)"),
        ("WORD",       "字 (16 bit)"),        ("DWORD",      "双字 (32 bit)"),
        ("LWORD",      "长字 (64 bit)"),      ("SINT",       "短整型 (8 bit)"),
        ("INT",        "整型 (16 bit)"),      ("DINT",       "双整型 (32 bit)"),
        ("LINT",       "长整型 (64 bit)"),    ("USINT",      "无符号短整型 (8 bit)"),
        ("UINT",       "无符号整型 (16 bit)"), ("UDINT",      "无符号双整型 (32 bit)"),
        ("ULINT",      "无符号长整型 (64 bit)"), ("REAL",     "实数 (32 bit)"),
        ("LREAL",      "长实数 (64 bit)"),    ("TIME",       "时间"),
        ("DATE",       "日期"),              ("TIME_OF_DAY", "当日时间"),
        ("TOD",        "当日时间"),           ("DATE_AND_TIME", "日期时间"),
        ("DT",         "日期时间"),           ("STRING",     "字符串"),
        ("WSTRING",    "宽字符串"),           ("REFERENCE",  "引用类型"),
    };

    /// <summary>
    /// 所有 ST 关键字的快速查找集合（用于自动大写等功能）。
    /// </summary>
    internal static readonly HashSet<string> AllKeywords = BuildKeywordSet();

    private static HashSet<string> BuildKeywordSet()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (text, _) in ControlKeywords)
            set.Add(text);
        foreach (var (text, _) in Types)
            set.Add(text);
        // 语法高亮正则中的额外关键字，不在上面两个数组中
        set.Add("WITH");
        set.Add("ON");
        set.Add("AT");
        set.Add("NON_RETAIN");
        set.Add("VAR_ACCESS");
        set.Add("VAR_CONFIG");
        return set;
    }


    // ══════════════════════════════════════════
    //  正则（保留，作为兜底）
    // ══════════════════════════════════════════

    [GeneratedRegex(
        @"(?i)\b([a-zA-Z_]\w*)\s*:\s*(" +
        @"BOOL|BYTE|WORD|DWORD|LWORD|SINT|INT|DINT|LINT|USINT|UINT|UDINT|ULINT" +
        @"|REAL|LREAL|TIME|DATE|TIME_OF_DAY|TOD|DATE_AND_TIME|DT|STRING|WSTRING|REFERENCE" +
        @")\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex VariableDeclRegex();

    // ══════════════════════════════════════════
    //  ★ 主入口：接收 AnalysisResult
    // ══════════════════════════════════════════

    /// <summary>
    /// 获取补全项。
    /// analysisResult 由 FrontendPipeline 产出，包含精确的变量列表。
    /// </summary>
    public static List<StCompletionData> GetCompletions(
        string fullText,
        int caretOffset,
        EditorAnalysisData? analysisData = null)
    {
        var result = new List<StCompletionData>();

        var partial = GetPartialWord(fullText, caretOffset);
        if (partial.Length == 0) return result;

        var context = AnalyzeContext(fullText, caretOffset);

        if (context == CompletionContext.InsideVarBlock) {
            AddMatches(result, partial, Types, CompletionKind.Type);
            AddSymbolMatches(result, partial, fullText, analysisData);
        } else {
            AddMatches(result, partial, ControlKeywords, CompletionKind.Keyword);
            AddMatches(result, partial, Types, CompletionKind.Type);
            AddSymbolMatches(result, partial, fullText, analysisData);
        }

        result.Sort((a, b) =>
        {
            bool aExact = a.Text.Equals(partial, StringComparison.OrdinalIgnoreCase);
            bool bExact = b.Text.Equals(partial, StringComparison.OrdinalIgnoreCase);
            if (aExact != bExact) return aExact ? -1 : 1;
            return string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase);
        });

        return result;
    }

    private static void AddSymbolMatches(
      List<StCompletionData> result,
      string partial,
      string fullText,
      EditorAnalysisData? analysisData)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 方法 1：从外部注入的分析数据读取
        if (analysisData is not null) {
            foreach (var v in analysisData.Variables)
                variables.TryAdd(v.Name, v.TypeName);

            // 函数 / 功能块
            foreach (var f in analysisData.Functions) {
                if (f.Name.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                    result.Add(new StCompletionData(
                        f.Name, f.Description, CompletionKind.Keyword));
            }

            // 用户自定义类型
            foreach (var t in analysisData.TypeDefinitions) {
                if (t.Name.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                    result.Add(new StCompletionData(
                        t.Name, t.Description, CompletionKind.Type));
            }
        }

        // 方法 2：正则兜底（分析数据为 null 或代码正在编辑时）
        foreach (var (name, type) in ExtractVariablesFromRegex(fullText))
            variables.TryAdd(name, type);

        foreach (var (name, typeName) in variables) {
            if (name.StartsWith(partial, StringComparison.OrdinalIgnoreCase)) {
                result.Add(new StCompletionData(
                    name,
                    $"变量: {typeName}",
                    CompletionKind.Variable));
            }
        }
    }

    private static List<(string Name, string Type)> ExtractVariablesFromRegex(string fullText)
    {
        var variables = new List<(string Name, string Type)>();

        foreach (Match m in VariableDeclRegex().Matches(fullText))
        {
            if (m.Success && m.Groups.Count >= 3)
            {
                var name = m.Groups[1].Value;
                var typeName = m.Groups[2].Value.ToUpperInvariant();
                variables.Add((name, typeName));
            }
        }

        return variables;
    }

    // ══════════════════════════════════════════
    //  ★ 已删除：ExtractVariablesFromAST
    //  ★ 已删除：ExtractFromPOU
    //  ★ 已删除：ExtractFromVarBlock
    //  ★ 已删除：GetDataTypeDisplayString
    //  ★ 已删除：对 STLexer / STParser / ParseTreeToASTVisitor 的所有引用
    // ══════════════════════════════════════════

    // ══════════════════════════════════════════
    //  工具方法（不变）
    // ══════════════════════════════════════════

    private static void AddMatches(
        List<StCompletionData> result, string partial,
        (string Text, string Desc)[] candidates, CompletionKind kind)
    {
        foreach (var (text, desc) in candidates)
        {
            if (text.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                result.Add(new StCompletionData(text, desc, kind));
        }
    }

    private static string GetPartialWord(string text, int offset)
    {
        int start = offset - 1;
        while (start >= 0 && (char.IsLetterOrDigit(text[start]) || text[start] == '_'))
            start--;
        return text[(start + 1)..offset];
    }

    private static CompletionContext AnalyzeContext(string fullText, int caretOffset)
    {
        var textBefore = fullText[..caretOffset];
        var upper = textBefore.ToUpperInvariant();

        int depth = 0;
        int pos = 0;

        while (pos < upper.Length)
        {
            if (pos + 3 <= upper.Length && upper[pos..].StartsWith("VAR"))
            {
                char after = pos + 3 < upper.Length ? upper[pos + 3] : '\0';
                if (!char.IsLetter(after) && after != '_')
                {
                    depth++;
                    pos += 3;
                    continue;
                }
                if (upper[pos..].StartsWith("VAR_INPUT") ||
                    upper[pos..].StartsWith("VAR_OUTPUT") ||
                    (pos + 10 <= upper.Length && upper[pos..].StartsWith("VAR_IN_OUT")) ||
                    (pos + 10 <= upper.Length && upper[pos..].StartsWith("VAR_GLOBAL")) ||
                    (pos + 13 <= upper.Length && upper[pos..].StartsWith("VAR_EXTERNAL")) ||
                    (pos + 9 <= upper.Length && upper[pos..].StartsWith("VAR_TEMP")))
                {
                    depth++;
                    pos += 3;
                    continue;
                }
            }

            if (pos + 7 <= upper.Length && upper[pos..].StartsWith("END_VAR"))
            {
                char after = pos + 7 < upper.Length ? upper[pos + 7] : '\0';
                if (!char.IsLetter(after) && after != '_')
                {
                    depth = Math.Max(0, depth - 1);
                    pos += 7;
                    continue;
                }
            }

            pos++;
        }

        return depth > 0 ? CompletionContext.InsideVarBlock : CompletionContext.Default;
    }
}

public enum CompletionContext { Default, InsideVarBlock }
public enum CompletionKind { Keyword, Type, Variable }
