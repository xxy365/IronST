using System.Collections;

namespace IronST.LanguageServer.Server;

/// <summary>一个编辑器里打开的文档。</summary>
internal sealed class TextDocument
{
    public required string Uri { get; init; }
    public required string Text { get; set; }
    public int Version { get; set; }
}

/// <summary>
/// 打开文档集合。分析时把所有打开文档一起喂给
/// <c>FrontendPipeline.AnalyzeFiles</c>，以获得跨文件的符号与诊断。
/// </summary>
internal sealed class DocumentStore : IEnumerable<TextDocument>
{
    private readonly Dictionary<string, TextDocument> _docs = new(StringComparer.Ordinal);

    public int Count => _docs.Count;

    public TextDocument? Get(string uri) =>
        _docs.TryGetValue(uri, out var d) ? d : null;

    public void Open(string uri, string text, int version)
    {
        _docs[uri] = new TextDocument { Uri = uri, Text = text, Version = version };
    }

    /// <summary>全量同步（TextDocumentSyncKind.Full）。</summary>
    public void Change(string uri, string text, int version)
    {
        if (_docs.TryGetValue(uri, out var doc))
        {
            doc.Text = text;
            doc.Version = version;
        }
        else
        {
            Open(uri, text, version);
        }
    }

    public bool Close(string uri) => _docs.Remove(uri);

    public IEnumerator<TextDocument> GetEnumerator() => _docs.Values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
