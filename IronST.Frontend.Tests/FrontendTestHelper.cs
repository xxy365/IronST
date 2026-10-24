// tests/IronST.Frontend.Tests/FrontendTestHelper.cs
using IronST.Common.Diagnostics;
using IronST.Frontend.Analysis;

namespace IronST.Frontend.Tests;

/// <summary>
/// 前端测试辅助：直接喂 ST 源码，拿到 AnalysisResult。
/// 同时覆盖 Parser → Visitor → SemanticAnalyzer 全链路。
/// </summary>
internal static class FrontendTestHelper
{
    /// <summary>默认库列表：IEC 61131-3 标准库</summary>
    private static readonly IReadOnlyList<ExternalLibrary> DefaultLibraries =
        [IECStandardLibrary.Create()];

    public static AnalysisResult Analyze(
        string source,
        IReadOnlyList<ExternalLibrary>? libraries = null) =>
        FrontendPipeline.Analyze(source, "test.st", libraries ?? DefaultLibraries);

    public static AnalysisResult ExpectSuccess(
        string source,
        IReadOnlyList<ExternalLibrary>? libraries = null)
    {
        var result = Analyze(source, libraries);
        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(
            result.Success,
            $"Expected success but got {errors.Count} error(s):\n"
                + string.Join("\n", errors.Select(d => $"  {d}"))
        );
        return result;
    }

    public static AnalysisResult ExpectErrors(
        string source,
        int? expectedErrorCount = null,
        IReadOnlyList<ExternalLibrary>? libraries = null)
    {
        var result = Analyze(source, libraries);
        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.False(result.Success, "Expected compilation to fail but it succeeded");
        if (expectedErrorCount.HasValue)
            Assert.Equal(expectedErrorCount.Value, errors.Count);
        return result;
    }
}