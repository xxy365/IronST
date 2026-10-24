using System.Text.Json.Nodes;
using IronST.LanguageServer.Protocol;

namespace IronST.LanguageServer.Tests;

/// <summary>端到端：真实走 JSON-RPC 帧，验证协议层 + 分析 + 推送。</summary>
public class ProtocolTests : IDisposable
{
    private const string Uri = "file:///demo.st";

    private const string Source = """
        PROGRAM Main
        VAR
            counter : INT;
            total : DINT;
        END_VAR
        counter := 1;
        total := counter + 1;
        END_PROGRAM
        """;

    private const string BrokenSource = """
        PROGRAM Main
        VAR
            counter : INT;
        END_VAR
        counter := undefinedVar + 1;
        END_PROGRAM
        """;

    private readonly LspTestClient _client = new();

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task Initialize_返回服务器能力()
    {
        await _client.InitializeAsync();

        // InitializeAsync 内部已经断言拿到了 id=1 的响应
        // 这里再走一遍完整流程验证不崩
        await _client.ShutdownAsync();
    }

    [Fact]
    public async Task 打开文件_未定义变量_会推送诊断()
    {
        await _client.InitializeAsync();
        await _client.OpenAsync(Uri, BrokenSource);

        var diags = await _client.WaitForDiagnosticsAsync(Uri);

        Assert.NotEmpty(diags);
        var first = diags[0]!.AsObject();
        Assert.Contains("undefinedVar", first["message"]!.GetValue<string>());
        Assert.Equal(1, first["severity"]!.GetValue<int>()); // Error
        Assert.Equal("ironst", first["source"]!.GetValue<string>());

        // 未定义变量在第 5 行（0 基第 4 行）
        var range = first["range"]!.AsObject();
        Assert.Equal(4, range["start"]!["line"]!.GetValue<int>());

        await _client.ShutdownAsync();
    }

    [Fact]
    public async Task 无错误文件_推送空诊断数组()
    {
        await _client.InitializeAsync();
        await _client.OpenAsync(Uri, Source);

        var diags = await _client.WaitForDiagnosticsAsync(Uri);

        Assert.Empty(diags);
        await _client.ShutdownAsync();
    }

    [Fact]
    public async Task 编辑后_诊断会更新()
    {
        await _client.InitializeAsync();
        await _client.OpenAsync(Uri, Source);

        var diags = await _client.WaitForDiagnosticsAsync(Uri);
        Assert.Empty(diags);

        // 丢掉上一轮的记录，才能确认等的是「新推送」而不是旧缓存
        _client.ForgetDiagnostics(Uri);
        await _client.ChangeAsync(Uri, BrokenSource, version: 2);
        var updated = await _client.WaitForDiagnosticsAsync(Uri);
        Assert.NotEmpty(updated);

        await _client.ShutdownAsync();
    }

    [Fact]
    public async Task 请求_completion_返回补全项()
    {
        await _client.InitializeAsync();
        await _client.OpenAsync(Uri, Source);
        await _client.WaitForDiagnosticsAsync(Uri);

        // 光标放在 "total := counter + 1" 的 counter 上
        var offset = Source.IndexOf("counter + 1", StringComparison.Ordinal);
        var (line, character) = LineCharacterOf(Source, offset);

        await _client.RequestAsync(100, "textDocument/completion", Json.Obj(
            ("textDocument", Json.Obj(("uri", Uri))),
            ("position", Json.Obj(("line", line), ("character", character)))));

        var response = await _client.ReadResponseAsync(100);
        Assert.NotNull(response);

        var items = response!["result"]!["items"]!.AsArray();
        var labels = items.Select(i => i!["label"]!.GetValue<string>()).ToList();

        Assert.Contains("counter", labels);
        Assert.Contains("total", labels);

        await _client.ShutdownAsync();
    }

    [Fact]
    public async Task 请求_hover_返回类型信息()
    {
        await _client.InitializeAsync();
        await _client.OpenAsync(Uri, Source);
        await _client.WaitForDiagnosticsAsync(Uri);

        var offset = Source.IndexOf("counter + 1", StringComparison.Ordinal);
        var (line, character) = LineCharacterOf(Source, offset);

        await _client.RequestAsync(101, "textDocument/hover", Json.Obj(
            ("textDocument", Json.Obj(("uri", Uri))),
            ("position", Json.Obj(("line", line), ("character", character)))));

        var response = await _client.ReadResponseAsync(101);
        Assert.NotNull(response);

        var value = response!["result"]!["contents"]!["value"]!.GetValue<string>();
        Assert.Contains("counter : INT", value);

        await _client.ShutdownAsync();
    }

    [Fact]
    public async Task 请求_definition_返回位置()
    {
        await _client.InitializeAsync();
        await _client.OpenAsync(Uri, Source);
        await _client.WaitForDiagnosticsAsync(Uri);

        // 光标放在 "counter + 1" 上（第二次出现）
        var offset = Source.IndexOf("counter + 1", StringComparison.Ordinal);
        var (line, character) = LineCharacterOf(Source, offset);

        await _client.RequestAsync(102, "textDocument/definition", Json.Obj(
            ("textDocument", Json.Obj(("uri", Uri))),
            ("position", Json.Obj(("line", line), ("character", character)))));

        var response = await _client.ReadResponseAsync(102);
        Assert.NotNull(response);

        var loc = response!["result"]!.AsObject();
        Assert.Equal(Uri, loc["uri"]!.GetValue<string>());
        // VAR counter 声明在第 2 行（0 基）
        Assert.Equal(2, loc["range"]!["start"]!["line"]!.GetValue<int>());

        await _client.ShutdownAsync();
    }

    [Fact]
    public async Task 请求_documentSymbol_返回POU与变量()
    {
        await _client.InitializeAsync();
        await _client.OpenAsync(Uri, Source);
        await _client.WaitForDiagnosticsAsync(Uri);

        await _client.RequestAsync(103, "textDocument/documentSymbol", Json.Obj(
            ("textDocument", Json.Obj(("uri", Uri)))));

        var response = await _client.ReadResponseAsync(103);
        Assert.NotNull(response);

        var symbols = response!["result"]!.AsArray();
        var names = symbols.Select(s => s!["name"]!.GetValue<string>()).ToList();
        Assert.Contains("Main", names);

        await _client.ShutdownAsync();
    }

    [Fact]
    public async Task 关闭文档_清空诊断()
    {
        await _client.InitializeAsync();
        await _client.OpenAsync(Uri, BrokenSource);
        await _client.WaitForDiagnosticsAsync(Uri);
        Assert.NotEmpty(_client.LastDiagnostics[Uri]);

        await _client.SendAsync(Json.Obj(
            ("jsonrpc", "2.0"),
            ("method", "textDocument/didClose"),
            ("params", Json.Obj(("textDocument", Json.Obj(("uri", Uri)))))));

        // 丢掉旧记录，等服务器推送空诊断
        _client.ForgetDiagnostics(Uri);
        var cleared = await _client.WaitForDiagnosticsAsync(Uri);

        Assert.True(
            cleared.Count == 0,
            $"关闭文档后应收到空诊断，实际收到 {cleared.Count} 条");

        await _client.ShutdownAsync();
    }

    [Fact]
    public async Task 未知方法_返回MethodNotFound()
    {
        await _client.InitializeAsync();

        await _client.RequestAsync(200, "textDocument/formatting", Json.Obj(
            ("textDocument", Json.Obj(("uri", Uri)))));

        var response = await _client.ReadResponseAsync(200);
        Assert.NotNull(response);
        Assert.NotNull(response!["error"]);
        Assert.Equal(-32601, response["error"]!["code"]!.GetValue<int>());

        await _client.ShutdownAsync();
    }

    [Fact]
    public async Task Shutdown与Exit_进程返回0()
    {
        await _client.InitializeAsync();
        await _client.ShutdownAsync();

        // 给主循环一点时间退出
        var exitCode = await _client.WaitForExitAsync();
        Assert.Equal(0, exitCode);
    }

    // ── 工具 ──

    private static (int Line, int Character) LineCharacterOf(string text, int offset)
    {
        int line = 0, last = 0;
        for (int i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                last = i + 1;
            }
        }
        return (line, offset - last);
    }
}
