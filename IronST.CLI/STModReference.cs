// STModReference.cs — 从已编译的 `.stmod` 导出符号构造**前端外部声明**（P2）。
//
// 用途：`ironst mod build Main.st --ref Math.stmod` 时，让前端知道
// `Math.stmod` 导出了哪些函数、签名是什么，从而 Main.st 可以调用它们，
// 而**不需要把 Math.st 一起编译**。
//
// 关键点：签名是 IR 文本（与 `FunctionIndex.Signature` 同源），例如
// `i32 (i32, i32)`、`void (%STRING_254)`。本文件把它映射回 **ST 内建类型名**
// （DINT / INT / REAL / STRING …），前端会据此重建等价的 IR 声明。
//
// 限制：当前只支持标量与 STRING；其它聚合/自定义类型会明确拒绝，
// 绝不猜一个"差不多"的类型去生成调用约定。
using IronST.Frontend.Analysis;
using IronST.Runtime.Shared.Modules;

namespace IronST.CLI;

internal static class STModReference
{
    public sealed record ParsedSignature(
        string? ReturnTypeName, IReadOnlyList<string> ParameterTypeNames);

    /// <summary>把多个引用模块的导出表转换为前端可注入的外部库声明。</summary>
    public static List<ExternalLibrary> BuildLibraries(IReadOnlyList<STModModule> modules)
    {
        var libraries = new List<ExternalLibrary>(modules.Count);

        foreach (var module in modules)
        {
            var functions = new List<ExternalFunction>();

            foreach (var symbol in module.Exports)
            {
                if (symbol.Kind != STModSymbolKind.Function)
                    continue;

                if (!TryParseSignature(symbol.Signature, out var parsed, out string error))
                {
                    throw new InvalidOperationException(
                        $"引用模块导出 '{symbol.Name}' 的{error}");
                }

                functions.Add(new ExternalFunction
                {
                    Name = symbol.Name,
                    ReturnTypeName = parsed!.ReturnTypeName,
                    Parameters = parsed.ParameterTypeNames
                        .Select((typeName, index) => new ExternalParameter
                        {
                            Name = $"arg{index}",
                            TypeName = typeName,
                        })
                        .ToList(),
                });
            }

            string name = module.Metadata.TryGetValue(
                    STModMetadataKeys.ModuleName, out var nameEntry)
                && nameEntry.ValueType == STModValueType.String
                ? nameEntry.AsString()
                : "referenced";

            libraries.Add(new ExternalLibrary { Name = name, Functions = functions });
        }

        return libraries;
    }

    /// <summary>解析 <c>ret (p1, p2, …)</c> 形式的 IR 签名。</summary>
    public static bool TryParseSignature(
        string signature, out ParsedSignature? parsed, out string error)
    {
        parsed = null;
        error = "";

        signature = signature.Trim();
        if (signature.Length == 0)
        {
            error = "签名文本为空";
            return false;
        }

        int open = signature.IndexOf('(');
        int close = signature.LastIndexOf(')');
        if (open < 0 || close < open)
        {
            error = $"签名 '{signature}' 缺少参数括号";
            return false;
        }

        string returnText = signature[..open].Trim();
        if (!TryMap(returnText, allowVoid: true, out string? returnType, out error))
            return false;

        string parametersText = signature[(open + 1)..close].Trim();
        var parameterTypes = new List<string>();

        if (parametersText.Length > 0)
        {
            foreach (string part in SplitTopLevel(parametersText))
            {
                if (!TryMap(part.Trim(), allowVoid: false, out string? parameterType, out error))
                    return false;
                parameterTypes.Add(parameterType!);
            }
        }

        parsed = new ParsedSignature(returnType, parameterTypes);
        return true;
    }

    private static bool TryMap(
        string irType, bool allowVoid, out string? stTypeName, out string error)
    {
        stTypeName = null;
        error = "";

        switch (irType)
        {
            case "void":
                if (!allowVoid)
                {
                    error = "参数类型不能是 void";
                    return false;
                }
                return true;

            case "i1": stTypeName = "BOOL"; return true;
            case "i8": stTypeName = "SINT"; return true;
            case "i16": stTypeName = "INT"; return true;
            case "i32": stTypeName = "DINT"; return true;
            case "i64": stTypeName = "LINT"; return true;
            case "float": stTypeName = "REAL"; return true;
            case "double": stTypeName = "LREAL"; return true;
        }

        if (irType.StartsWith("%WSTRING_", StringComparison.Ordinal))
        {
            stTypeName = "WSTRING";
            return true;
        }

        if (irType.StartsWith("%STRING_", StringComparison.Ordinal))
        {
            stTypeName = "STRING";
            return true;
        }

        error = $"签名类型 '{irType}' 不受支持（当前仅支持标量与 STRING）";
        return false;
    }

    /// <summary>按顶层逗号切分（不切进 <c>()[]&lt;&gt;</c> 内部）。</summary>
    private static IEnumerable<string> SplitTopLevel(string text)
    {
        int depth = 0;
        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '(' or '[' or '<')
                depth++;
            else if (c is ')' or ']' or '>')
                depth--;
            else if (c == ',' && depth == 0)
            {
                yield return text[start..i];
                start = i + 1;
            }
        }

        yield return text[start..];
    }
}
