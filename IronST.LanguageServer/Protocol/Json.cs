using System.Text.Json.Nodes;

namespace IronST.LanguageServer.Protocol;

/// <summary>构造 LSP 协议对象的小工具。</summary>
internal static class Json
{
    public static JsonObject Obj(params (string Key, JsonNode? Value)[] props)
    {
        var o = new JsonObject();
        foreach (var (k, v) in props)
            o[k] = v?.DeepClone();
        return o;
    }

    public static JsonArray Arr(params JsonNode?[] items)
    {
        var a = new JsonArray();
        foreach (var item in items)
            a.Add(item?.DeepClone());
        return a;
    }

    public static JsonArray Arr<T>(IEnumerable<T> items, Func<T, JsonNode?> map)
    {
        var a = new JsonArray();
        foreach (var item in items)
            a.Add(map(item)?.DeepClone());
        return a;
    }

    public static JsonArray IntArray(params int[] values)
    {
        var a = new JsonArray();
        foreach (var v in values) a.Add(v);
        return a;
    }

    /// <summary>Position (0 基行、0 基 UTF-16 列)。</summary>
    public static JsonObject Position(int line, int character) =>
        Obj(("line", line), ("character", character));

    /// <summary>Range。</summary>
    public static JsonObject Range(int sl, int sc, int el, int ec) =>
        Obj(("start", Position(sl, sc)), ("end", Position(el, ec)));

    /// <summary>由 Frontend SourceSpan 得到的 Range。</summary>
    public static JsonObject SpanRange(in Common.Diagnostics.SourceSpan span, TextIndex index)
    {
        var (sl, sc, el, ec) = index.ToLspRange(span);
        return Range(sl, sc, el, ec);
    }

    /// <summary>Location。</summary>
    public static JsonObject Location(string uri, JsonObject range) =>
        Obj(("uri", uri), ("range", range));
}

/// <summary>LSP 协议常量（只列用到的）。</summary>
internal static class Lsp
{
    // TextDocumentSyncKind
    public const int SyncNone = 0;
    public const int SyncFull = 1;
    public const int SyncIncremental = 2;

    // DiagnosticSeverity（LSP 规范，注意与 IronST.Common 的枚举顺序不同）
    public const int SeverityError = 1;
    public const int SeverityWarning = 2;
    public const int SeverityInfo = 3;
    public const int SeverityHint = 4;

    // CompletionItemKind
    public const int KindMethod = 2;
    public const int KindFunction = 3;
    public const int KindField = 5;
    public const int KindVariable = 6;
    public const int KindClass = 7;
    public const int KindModule = 9;
    public const int KindEnum = 13;
    public const int KindKeyword = 14;
    public const int KindStruct = 22;
    public const int KindEnumMember = 20;
    public const int KindTypeParameter = 25;

    // SymbolKind
    public const int SymFile = 1;
    public const int SymModule = 2;
    public const int SymNamespace = 3;
    public const int SymClass = 5;
    public const int SymMethod = 6;
    public const int SymProperty = 7;
    public const int SymField = 8;
    public const int SymConstructor = 9;
    public const int SymEnum = 10;
    public const int SymFunction = 12;
    public const int SymVariable = 13;
    public const int SymConstant = 14;
    public const int SymEnumMember = 22;
    public const int SymStruct = 23;
    public const int SymObject = 19;

    // MarkupKind
    public const string MarkupPlainText = "plaintext";
    public const string MarkupMarkdown = "markdown";
}
