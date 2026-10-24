// tests/IronST.BytecodeGen.Tests/FrontendTestHelper.cs
using IronST.Common.Diagnostics;
using IronST.Frontend;
using IronST.Frontend.Analysis;

namespace IronST.BytecodeGen.Tests;

/// <summary>
/// 前端测试辅助：直接喂 ST 源码，拿到 AnalysisResult。
/// 与 Frontend.Tests 中的同名类逻辑一致，避免跨测试项目引用。
/// </summary>
internal static class FrontendTestHelper
{

    public static AnalysisResult Analyze(string source)
        => FrontendPipeline.Analyze(source, "test.st");

    public static AnalysisResult ExpectSuccess(string source)
    {
        var result = Analyze(source);
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(result.Success,
            $"Expected success but got {errors.Count} error(s):\n" +
            string.Join("\n", errors.Select(d => $"  {d}")));
        return result;
    }

    public static AnalysisResult ExpectErrors(string source, int? expectedErrorCount = null)
    {
        var result = Analyze(source);
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.False(result.Success,
            "Expected compilation to fail but it succeeded");
        if (expectedErrorCount.HasValue)
            Assert.Equal(expectedErrorCount.Value, errors.Count);
        return result;
    }
}
