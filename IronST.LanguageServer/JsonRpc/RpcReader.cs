using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IronST.LanguageServer.JsonRpc;

/// <summary>
/// LSP 传输层读取端：解析 <c>Content-Length: N\r\n\r\n&lt;body&gt;</c> 帧。
/// </summary>
internal sealed class RpcReader
{
    private readonly Stream _input;

    public RpcReader(Stream input) => _input = input;

    /// <summary>读取下一帧；流结束或帧不完整时返回 null。</summary>
    public async Task<JsonNode?> ReadAsync(CancellationToken ct = default)
    {
        int contentLength = -1;

        while (true)
        {
            var line = await ReadLineAsync(ct).ConfigureAwait(false);
            if (line == null)
                return null; // EOF

            if (line.Length == 0)
                break; // header 结束，空行

            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue; // 无法识别的 header 行，忽略

            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();

            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(value, out var n))
            {
                contentLength = n;
            }
            // Content-Type 等其他 header 直接忽略
        }

        if (contentLength < 0)
            return null; // 缺 Content-Length，无法继续

        var body = new byte[contentLength];
        int read = 0;
        while (read < contentLength)
        {
            int n = await _input
                .ReadAsync(body.AsMemory(read, contentLength - read), ct)
                .ConfigureAwait(false);
            if (n == 0)
                return null; // 流被截断
            read += n;
        }

        try
        {
            return JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return null; // JSON 损坏，交给上层当作协议错误
        }
    }

    /// <summary>读一行（不含换行符），EOF 返回 null。</summary>
    private async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        var buffer = new MemoryStream(64);

        while (true)
        {
            var b = new byte[1];
            int n = await _input.ReadAsync(b.AsMemory(0, 1), ct).ConfigureAwait(false);
            if (n == 0)
                return buffer.Length == 0 ? null : Encoding.UTF8.GetString(buffer.ToArray());

            if (b[0] == (byte)'\n')
                break;

            buffer.WriteByte(b[0]);
        }

        var bytes = buffer.ToArray();
        // 去掉结尾的 \r
        int len = bytes.Length;
        if (len > 0 && bytes[len - 1] == (byte)'\r')
            len--;
        return Encoding.UTF8.GetString(bytes, 0, len);
    }
}
