using IronST.Frontend;
using IronST.Frontend.Analysis;
using IronST.LanguageServer.Features;
using IronST.LanguageServer.JsonRpc;
using IronST.LanguageServer.Protocol;
using System.Text.Json.Nodes;

namespace IronST.LanguageServer.Server;

/// <summary>一次分析的快照：供各功能 Provider 复用。</summary>
internal sealed record AnalysisSnapshot(
    string Uri,
    string Text,
    AnalysisResult Result,
    TextIndex Index);

/// <summary>
/// 分析调度：文档变化 → 防抖 → 前端分析 → 缓存快照 → 推送诊断。
///
/// 分析是<b>工作区级</b>的：把所有打开文档交给 AnalyzeFiles，跨文件符号共享。
/// 因此一次分析产出的 <see cref="AnalysisResult"/> 对所有文档都有效，
/// 只是每个文档用各自的 TextIndex 来做位置换算。
/// </summary>
internal sealed class AnalysisService
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(150);

    private readonly DocumentStore _docs;
    private readonly RpcWriter _writer;
    private readonly object _gate = new();
    private readonly Dictionary<string, AnalysisSnapshot> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CancellationTokenSource> _timers = new(StringComparer.Ordinal);

    /// <summary>错误日志出口（默认静默；服务器会接到 stderr）。</summary>
    public Action<string>? ErrorLogger { get; set; }

    public AnalysisService(DocumentStore docs, RpcWriter writer)
    {
        _docs = docs;
        _writer = writer;
    }

    private void LogError(string message) => ErrorLogger?.Invoke(message);

    /// <summary>取某文档的最新快照；未分析过返回 null。</summary>
    public AnalysisSnapshot? GetSnapshot(string uri)
    {
        lock (_gate)
            return _cache.TryGetValue(uri, out var s) ? s : null;
    }

    /// <summary>文档被关闭：丢弃缓存并清空诊断。</summary>
    public async Task RemoveAsync(string uri)
    {
        lock (_gate)
        {
            _cache.Remove(uri);
            if (_timers.TryGetValue(uri, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
                _timers.Remove(uri);
            }
        }

        await _writer
            .NotifyAsync(
                "textDocument/publishDiagnostics",
                Json.Obj(("uri", uri), ("diagnostics", new JsonArray())))
            .ConfigureAwait(false);
    }

    /// <summary>文档内容变化：防抖后重新分析。</summary>
    public void Schedule(string uri)
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            if (_timers.TryGetValue(uri, out var old))
                old.Dispose();

            cts = new CancellationTokenSource();
            _timers[uri] = cts;
        }

        _ = DebouncedAsync(uri, cts);
    }

    private async Task DebouncedAsync(string uri, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(Debounce, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return; // 期间又有新的编辑，这次作废
        }
        finally
        {
            lock (_gate)
            {
                if (_timers.TryGetValue(uri, out var cur) && ReferenceEquals(cur, cts))
                    _timers.Remove(uri);
            }
        }

        await AnalyzeAsync().ConfigureAwait(false);
    }

    private async Task AnalyzeAsync()
    {
        var docs = _docs.ToList();
        if (docs.Count == 0)
            return;

        // 记下分析开始时各文档的版本，分析完若已变则丢弃结果（避免过期结果覆盖新结果）
        var versions = docs.ToDictionary(d => d.Uri, d => d.Version, StringComparer.Ordinal);

        var files = docs.Select(d => (d.Text, d.Uri)).ToList();

        AnalysisResult result;
        try
        {
            result = FrontendPipeline.AnalyzeFiles(files,
                [IECStandardLibrary.Create(), HostBuiltins.Create()]);
        }
        catch (Exception ex)
        {
            // 分析器异常不应让服务器崩溃，但也不能无声吞掉
            LogError($"前端分析异常: {ex}");
            return;
        }

        // 版本校验：任一文档在分析期间又被改了，本次结果作废
        foreach (var (uri, version) in versions)
        {
            var current = _docs.Get(uri);
            if (current == null || current.Version != version)
                return;
        }

        // 建每个文档的快照（同一个 result，各自的 TextIndex）
        lock (_gate)
        {
            foreach (var doc in docs)
            {
                _cache[doc.Uri] = new AnalysisSnapshot(
                    doc.Uri, doc.Text, result, new TextIndex(doc.Text));
            }
        }

        await PublishDiagnosticsAsync(result, docs).ConfigureAwait(false);
    }

    private async Task PublishDiagnosticsAsync(AnalysisResult result, List<TextDocument> docs)
    {
        // 按 Span.FileName（我们在分析时传的就是 uri）分组
        var byUri = result.Diagnostics
            .Where(d => d.Span != null && !string.IsNullOrEmpty(d.Span.Value.FileName))
            .GroupBy(d => d.Span!.Value.FileName!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var doc in docs)
        {
            var diags = byUri.TryGetValue(doc.Uri, out var list) ? list : [];

            // 该文档的快照 Index 用于把 SourceSpan 换算成 LSP Range
            AnalysisSnapshot? snapshot;
            lock (_gate)
                _cache.TryGetValue(doc.Uri, out snapshot);

            var index = snapshot?.Index ?? new TextIndex(doc.Text);

            var arr = new JsonArray();
            foreach (var d in diags)
                arr.Add(DiagnosticsMapper.ToLsp(d, index));

            await _writer
                .NotifyAsync(
                    "textDocument/publishDiagnostics",
                    Json.Obj(("uri", doc.Uri), ("diagnostics", arr)))
                .ConfigureAwait(false);
        }
    }
}
