using IronST.Frontend;
using IronST.Frontend.Analysis;
using IronST.LanguageServer.Protocol;
using IronST.LanguageServer.Server;

namespace IronST.LanguageServer.Tests;

/// <summary>测试辅助：对一段源码跑前端分析，构造快照。</summary>
internal static class TestHelpers
{
    public const string Uri = "file:///test.st";

    public static AnalysisSnapshot Snapshot(string source, string uri = Uri)
    {
        var result = FrontendPipeline.Analyze(source, uri, [IECStandardLibrary.Create()]);
        return new AnalysisSnapshot(uri, source, result, new TextIndex(source));
    }

    /// <summary>返回标识符在源码中的 UTF-16 偏移（第一个匹配处）。</summary>
    public static int OffsetOf(string source, string identifier)
    {
        var idx = source.IndexOf(identifier, StringComparison.Ordinal);
        return idx < 0 ? throw new InvalidOperationException($"源码里找不到 {identifier}") : idx;
    }
}
