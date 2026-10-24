using System.Text;
using System.Text.Json.Nodes;

namespace IronST.LanguageServer.JsonRpc;

/// <summary>
/// LSP 传输层写出端：把消息序列化为 Content-Length 帧。
/// 内部加锁，保证多线程（主循环 + 分析线程）写 stdout 不会交错。
/// </summary>
internal sealed class RpcWriter
{
    private readonly Stream _output;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RpcWriter(Stream output) => _output = output;

    public async Task WriteAsync(JsonNode message, CancellationToken ct = default)
    {
        // LSP 规范要求 body 为 UTF-8 无 BOM，紧凑 JSON
        var body = message.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = false,
        });
        var bytes = Encoding.UTF8.GetBytes(body);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _output.WriteAsync(header, ct).ConfigureAwait(false);
            await _output.WriteAsync(bytes, ct).ConfigureAwait(false);
            await _output.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>发送通知。</summary>
    public Task NotifyAsync(string method, JsonNode? @params, CancellationToken ct = default)
        => WriteAsync(RpcMessages.Notification(method, @params), ct);

    /// <summary>发送响应。</summary>
    public Task RespondAsync(JsonNode? id, JsonNode? result, CancellationToken ct = default)
        => WriteAsync(RpcMessages.Response(id, result), ct);

    /// <summary>发送错误响应。</summary>
    public Task RespondErrorAsync(JsonNode? id, int code, string message, CancellationToken ct = default)
        => WriteAsync(RpcMessages.ErrorResponse(id, code, message), ct);
}
