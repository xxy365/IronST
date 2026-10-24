using IronST.LanguageServer.Features;
using IronST.LanguageServer.JsonRpc;
using IronST.LanguageServer.Protocol;
using System.Text.Json.Nodes;

namespace IronST.LanguageServer.Server;

/// <summary>
/// LSP 服务器主体：读帧 → 分发 → 回响应 / 推通知。
/// stdout 只用于协议；所有日志走 stderr 或日志文件。
/// </summary>
internal sealed class LanguageServer
{
    private static readonly JsonNode True = JsonValue.Create(true)!;

    private readonly RpcReader _reader;
    private readonly RpcWriter _writer;
    private readonly TextWriter _log;
    private readonly DocumentStore _docs = new();
    private readonly AnalysisService _analysis;

    private bool _shutdownRequested;
    private bool _exit;

    public LanguageServer(Stream input, Stream output, TextWriter? log = null)
    {
        _reader = new RpcReader(input);
        _writer = new RpcWriter(output);
        _log = log ?? TextWriter.Null;
        _analysis = new AnalysisService(_docs, _writer) { ErrorLogger = Log };
    }

    /// <summary>运行主循环。返回进程退出码：正常 shutdown+exit 为 0，否则 1。</summary>
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            JsonNode? message;
            try
            {
                message = await _reader.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"读取消息失败: {ex.Message}");
                break;
            }

            if (message == null)
                break; // 流结束

            try
            {
                await DispatchAsync(message, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"处理消息异常: {ex}");
            }

            if (_exit)
                break;
        }

        return _shutdownRequested ? 0 : 1;
    }

    // ═══════════════════════════════════════
    //  分发
    // ═══════════════════════════════════════

    private async Task DispatchAsync(JsonNode message, CancellationToken ct)
    {
        if (message is not JsonObject obj)
            return;

        var method = obj["method"]?.GetValue<string>();
        var id = obj["id"];
        var @params = obj["params"] as JsonObject;

        if (method == null)
            return; // 纯响应：本服务器不发请求，忽略

        Log($"← {method}");

        // ── 通知 ──
        if (id == null)
        {
            await HandleNotificationAsync(method, @params, ct).ConfigureAwait(false);
            return;
        }

        // ── 请求 ──
        var result = HandleRequest(method, @params, out var error);
        if (error != null)
            await _writer.RespondErrorAsync(id, error.Value.code, error.Value.message, ct)
                .ConfigureAwait(false);
        else
            await _writer.RespondAsync(id, result, ct).ConfigureAwait(false);
    }

    private async Task HandleNotificationAsync(string method, JsonObject? p, CancellationToken ct)
    {
        switch (method)
        {
            case "initialized":
                Log("客户端已就绪");
                return;

            case "exit":
                _exit = true;
                return;

            case "textDocument/didOpen":
            {
                var doc = p?["textDocument"] as JsonObject;
                var uri = doc?["uri"]?.GetValue<string>();
                var text = doc?["text"]?.GetValue<string>() ?? "";
                var version = doc?["version"]?.GetValue<int>() ?? 0;
                if (uri != null)
                {
                    _docs.Open(uri, text, version);
                    _analysis.Schedule(uri);
                }
                return;
            }

            case "textDocument/didChange":
            {
                var doc = p?["textDocument"] as JsonObject;
                var uri = doc?["uri"]?.GetValue<string>();
                var version = doc?["version"]?.GetValue<int>() ?? 0;

                // 我们 advertise 的是 SyncKind.Full，contentChanges 只有一项且带全量 text
                var text = (p?["contentChanges"] as JsonArray)?
                    .OfType<JsonObject>()
                    .LastOrDefault()?["text"]?
                    .GetValue<string>();

                if (uri != null && text != null)
                {
                    _docs.Change(uri, text, version);
                    _analysis.Schedule(uri);
                }
                return;
            }

            case "textDocument/didSave":
            {
                var uri = (p?["textDocument"] as JsonObject)?["uri"]?.GetValue<string>();
                if (uri != null)
                    _analysis.Schedule(uri);
                return;
            }

            case "textDocument/didClose":
            {
                var uri = (p?["textDocument"] as JsonObject)?["uri"]?.GetValue<string>();
                if (uri != null)
                {
                    _docs.Close(uri);
                    await _analysis.RemoveAsync(uri).ConfigureAwait(false);
                }
                return;
            }

            // 我们不支持取消，静默忽略即可（规范要求不得因忽略而报错）
            case "$/cancelRequest":
            case "workspace/didChangeConfiguration":
            case "workspace/didChangeWatchedFiles":
            case "$/setTrace":
                return;

            default:
                Log($"忽略未知通知: {method}");
                return;
        }
    }

    private JsonNode? HandleRequest(string method, JsonObject? p, out (int code, string message)? error)
    {
        error = null;

        switch (method)
        {
            case "initialize":
                return ServerCapabilities();

            case "shutdown":
                _shutdownRequested = true;
                return null;

            case "textDocument/completion":
                return WithDocumentAt(p, (snap, offset) =>
                {
                    var qualifier = QualifierAt(snap, offset);
                    return Json.Obj(
                        ("isIncomplete", false),
                        ("items", CompletionProvider.Complete(snap, offset, qualifier)));
                }, out error);

            case "textDocument/hover":
                return WithDocumentAt(p, (snap, offset) => HoverProvider.Hover(snap, offset), out error);

            case "textDocument/definition":
                return WithDocumentAt(
                    p, (snap, offset) => NavigationProvider.Definition(snap, offset), out error);

            case "textDocument/references":
                return WithDocumentAt(
                    p, (snap, offset) => NavigationProvider.References(snap, offset), out error);

            case "textDocument/documentSymbol":
                return WithDocument(
                    p, snap => DocumentSymbolProvider.Symbols(snap), out error);

            case "textDocument/signatureHelp":
                return WithDocumentAt(
                    p, (snap, offset) => SignatureHelpProvider.SignatureHelp(snap, offset), out error);

            // 尚未实现的能力：返回 MethodNotFound，客户端会优雅降级
            default:
                error = (RpcErrorCodes.MethodNotFound, $"未实现的方法: {method}");
                Log($"→ MethodNotFound: {method}");
                return null;
        }
    }

    // ═══════════════════════════════════════
    //  能力声明
    // ═══════════════════════════════════════

    private static JsonObject ServerCapabilities() =>
        Json.Obj(
            ("positionEncoding", "utf-16"),
            ("textDocumentSync", Json.Obj(
                ("openClose", True),
                ("change", Lsp.SyncFull),
                ("save", Json.Obj(("includeText", false))))),
            ("completionProvider", Json.Obj(
                ("resolveProvider", false),
                ("triggerCharacters", Json.Arr(".", ":")))),
            ("hoverProvider", True),
            ("definitionProvider", True),
            ("referencesProvider", True),
            ("documentSymbolProvider", True),
            ("signatureHelpProvider", Json.Obj(
                ("triggerCharacters", Json.Arr("(", ",")),
                ("retriggerCharacters", Json.Arr(",")))),
            ("diagnosticProvider", Json.Obj(
                ("interFileDependencies", true),
                ("workspaceDiagnostics", false))));

    // ═══════════════════════════════════════
    //  工具
    // ═══════════════════════════════════════

    /// <summary>
    /// 文档级请求（无 position）：只取快照交给 Provider。
    /// 快照缺失（还没分析完）时返回 null，客户端保持安静。
    /// </summary>
    private JsonNode? WithDocument(
        JsonObject? p,
        Func<AnalysisSnapshot, JsonNode?> provider,
        out (int code, string message)? error)
    {
        error = null;

        var uri = (p?["textDocument"] as JsonObject)?["uri"]?.GetValue<string>();
        if (uri == null)
        {
            error = (RpcErrorCodes.InvalidParams, "缺少 textDocument.uri");
            return null;
        }

        var snap = _analysis.GetSnapshot(uri);
        if (snap == null)
            return null;

        return provider(snap);
    }

    /// <summary>
    /// 位置级请求：取请求里的 position → 快照 + UTF-16 偏移，再交给具体 Provider。
    /// 快照缺失（还没分析完）时返回 null，客户端保持安静。
    /// </summary>
    private JsonNode? WithDocumentAt(
        JsonObject? p,
        Func<AnalysisSnapshot, int, JsonNode?> provider,
        out (int code, string message)? error)
    {
        error = null;

        var doc = p?["textDocument"] as JsonObject;
        var uri = doc?["uri"]?.GetValue<string>();
        var pos = p?["position"] as JsonObject;

        if (uri == null || pos == null)
        {
            error = (RpcErrorCodes.InvalidParams, "缺少 textDocument.uri 或 position");
            return null;
        }

        var snap = _analysis.GetSnapshot(uri);
        if (snap == null)
            return null; // 未分析过（didOpen 刚到、分析还在防抖中）

        int line = pos["line"]?.GetValue<int>() ?? 0;
        int character = pos["character"]?.GetValue<int>() ?? 0;
        int offset = snap.Index.OffsetOf(line, character);

        return provider(snap, offset);
    }

    /// <summary>光标左侧若是 <c>标识符.</c>，返回该限定名，否则 null。</summary>
    private static string? QualifierAt(AnalysisSnapshot snap, int offset)
    {
        var ident = Identifiers.FindAt(snap.Index, offset);
        if (ident == null)
            return null;
        return Identifiers.QualifierOf(snap.Index, ident.Value.Start);
    }

    private void Log(string message)
    {
        try
        {
            _log.WriteLine($"[ironst-ls] {message}");
            _log.Flush();
        }
        catch
        {
            // 日志失败不影响协议
        }
    }
}
