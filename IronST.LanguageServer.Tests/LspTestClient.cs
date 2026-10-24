using System.Text;
using System.Text.Json.Nodes;
using IronST.LanguageServer.Protocol;

namespace IronST.LanguageServer.Tests;

/// <summary>
/// 极简 LSP 测试客户端：把消息写进 ChannelStream，从服务器的输出流读响应。
/// </summary>
internal sealed class LspTestClient : IDisposable
{
    private readonly ChannelStream _toServer;
    private readonly ChannelStream _fromServer;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task<int> _serverTask;
    private readonly StringWriter _logSink = new();

    public LspTestClient()
    {
        _toServer = new ChannelStream();
        _fromServer = new ChannelStream();

        var server = new IronST.LanguageServer.Server.LanguageServer(
            _toServer, _fromServer, log: _logSink);

        _serverTask = server.RunAsync(_cts.Token);
    }

    /// <summary>服务器侧日志（排障用）。</summary>
    public string Logs => _logSink.ToString();

    // ── 客户端 → 服务器 ──

    public async Task InitializeAsync()
    {
        var initializeParams = Json.Obj(
            ("processId", null),
            ("capabilities", new JsonObject()),
            ("rootUri", "file:///"));

        await SendAsync(Json.Obj(
            ("jsonrpc", "2.0"),
            ("id", 1),
            ("method", "initialize"),
            ("params", initializeParams))).ConfigureAwait(false);

        var response = await ReadResponseAsync(1).ConfigureAwait(false);
        Assert.NotNull(response);

        var initializedParams = new JsonObject();
        await SendAsync(Json.Obj(
            ("jsonrpc", "2.0"),
            ("method", "initialized"),
            ("params", initializedParams))).ConfigureAwait(false);
    }

    public Task OpenAsync(string uri, string text, int version = 1) =>
        SendAsync(Json.Obj(
            ("jsonrpc", "2.0"),
            ("method", "textDocument/didOpen"),
            ("params", Json.Obj(
                ("textDocument", Json.Obj(
                    ("uri", uri),
                    ("languageId", "ironst"),
                    ("version", version),
                    ("text", text)))))));

    public Task ChangeAsync(string uri, string text, int version) =>
        SendAsync(Json.Obj(
            ("jsonrpc", "2.0"),
            ("method", "textDocument/didChange"),
            ("params", Json.Obj(
                ("textDocument", Json.Obj(("uri", uri), ("version", version))),
                ("contentChanges", Json.Arr(Json.Obj(("text", text))))))));

    public Task RequestAsync(int id, string method, JsonObject @params) =>
        SendAsync(Json.Obj(
            ("jsonrpc", "2.0"), ("id", id), ("method", method), ("params", @params)));

    public async Task ShutdownAsync()
    {
        await SendAsync(Json.Obj(
            ("jsonrpc", "2.0"), ("id", 9999), ("method", "shutdown"))).ConfigureAwait(false);
        await ReadResponseAsync(9999).ConfigureAwait(false);

        await SendAsync(Json.Obj(("jsonrpc", "2.0"), ("method", "exit")))
            .ConfigureAwait(false);
    }

    // ── 服务器 → 客户端 ──

    /// <summary>读到指定 id 的响应为止，期间的 publishDiagnostics 通知会被收集。</summary>
    public async Task<JsonObject?> ReadResponseAsync(int id, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        for (int i = 0; i < 200; i++)
        {
            JsonObject? msg;
            try
            {
                msg = await ReadMessageAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null; // 超时
            }

            if (msg == null)
                return null;

            if (msg["method"] != null)
            {
                RecordDiagnostics(msg);
                continue;
            }

            if (msg["id"] != null && msg["id"] is JsonValue v && v.GetValue<int>() == id)
                return msg;
        }

        return null; // 读了太多条还没等到目标响应
    }

    /// <summary>等待并取出针对某 URI 的最新诊断。</summary>
    public async Task<JsonArray> WaitForDiagnosticsAsync(string uri, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));

        while (!cts.IsCancellationRequested)
        {
            if (LastDiagnostics.TryGetValue(uri, out var diags))
                return diags;

            JsonObject? msg;
            try
            {
                msg = await ReadMessageAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break; // 超时
            }

            if (msg == null)
                break;

            // 关键：读到的诊断要记下来，否则下一轮 TryGetValue 永远为假
            RecordDiagnostics(msg);
        }

        return LastDiagnostics.TryGetValue(uri, out var d) ? d : new JsonArray();
    }

    /// <summary>丢弃某 URI 已记录的诊断，用于「等下一次推送」的场景。</summary>
    public void ForgetDiagnostics(string uri) => LastDiagnostics.Remove(uri);

    /// <summary>若消息是 publishDiagnostics，则记录到 <see cref="LastDiagnostics"/>。</summary>
    private void RecordDiagnostics(JsonObject msg)
    {
        if (msg["method"]?.GetValue<string>() != "textDocument/publishDiagnostics")
            return;

        var uri = msg["params"]!["uri"]!.GetValue<string>();
        LastDiagnostics[uri] = msg["params"]!["diagnostics"]!.AsArray();
    }

    public Dictionary<string, JsonArray> LastDiagnostics { get; } = new(StringComparer.Ordinal);

    /// <summary>等待服务器主循环结束，返回退出码。</summary>
    public async Task<int> WaitForExitAsync(TimeSpan? timeout = null)
    {
        var completed = await Task.WhenAny(_serverTask, Task.Delay(timeout ?? TimeSpan.FromSeconds(5)));
        return completed == _serverTask
            ? await _serverTask.ConfigureAwait(false)
            : throw new TimeoutException("服务器未在预期时间内退出");
    }

    // ── 底层 ──

    public async Task SendAsync(JsonNode message)
    {
        var body = Encoding.UTF8.GetBytes(message.ToJsonString());
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");

        foreach (var b in header)
            await _toServer.Writer.WriteAsync(b).ConfigureAwait(false);
        foreach (var b in body)
            await _toServer.Writer.WriteAsync(b).ConfigureAwait(false);
    }

    /// <summary>从服务器输出里读一帧。</summary>
    public async Task<JsonObject?> ReadMessageAsync(CancellationToken ct = default)
    {
        // 默认 15 秒兜底，避免服务器无响应时测试永久挂起
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, ct);
        if (!ct.CanBeCanceled)
            linked.CancelAfter(TimeSpan.FromSeconds(15));
        var token = linked.Token;

        var buffer = new List<byte>();

        // 读 header
        int contentLength = -1;
        while (true)
        {
            var line = await ReadLineAsync(token).ConfigureAwait(false);
            if (line == null)
                return null;
            if (line.Length == 0)
                break;

            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;
            if (line[..colon].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                int.TryParse(line[(colon + 1)..].Trim(), out contentLength);
        }

        if (contentLength <= 0)
            return null;

        while (buffer.Count < contentLength)
        {
            var b = new byte[1];
            int n = await _fromServer.ReadAsync(b, 0, 1, token).ConfigureAwait(false);
            if (n == 0)
                return null;
            buffer.Add(b[0]);
        }

        return JsonNode.Parse(Encoding.UTF8.GetString(buffer.ToArray())) as JsonObject;
    }

    private async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        var bytes = new List<byte>();
        var single = new byte[1];

        while (true)
        {
            int n = await _fromServer.ReadAsync(single, 0, 1, ct).ConfigureAwait(false);
            if (n == 0)
                return bytes.Count == 0 ? null : Encoding.UTF8.GetString(bytes.ToArray());

            if (single[0] == (byte)'\n')
                break;

            bytes.Add(single[0]);
        }

        if (bytes.Count > 0 && bytes[^1] == (byte)'\r')
            bytes.RemoveAt(bytes.Count - 1);

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    public void Dispose()
    {
        _cts.Cancel();
        _toServer.Writer.TryComplete();
        _fromServer.Writer.TryComplete();
        _cts.Dispose();
    }
}
