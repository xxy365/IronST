using IronST.Common.Diagnostics;
using IronST.Frontend;
using IronST.Frontend.Analysis;

namespace IronST.Frontend.Tests;

internal static class TestHelpers
{
    /// <summary>
    /// 最小可用源码骨架 -- 包裹用户代码片段，使其成为合法编译单元。
    /// </summary>
    public static string WrapProgram(string body)
    {
        return $"""
            PROGRAM Main
                {body}
            END_PROGRAM
            """;
    }

    /// <summary>
    /// 包裹为 Function
    /// </summary>
    public static string WrapFunction(string name, string returnType, string body,
        string varBlocks = "")
    {
        return $"""
            FUNCTION {name} : {returnType}
                {varBlocks}
                {body}
            END_FUNCTION
            """;
    }

    /// <summary>
    /// 包裹为 FunctionBlock
    /// </summary>
    public static string WrapFunctionBlock(string name, string body)
    {
        return $"""
            FUNCTION_BLOCK {name}
                {body}
            END_FUNCTION_BLOCK
            """;
    }

    /// <summary>
    /// 分析给定源码并返回结果
    /// </summary>
    public static AnalysisResult Analyze(string source, string? fileName = null)
    {
        return FrontendPipeline.Analyze(source, fileName);
    }

    /// <summary>
    /// 分析并断言无错误（无文件名）
    /// </summary>
    public static AnalysisResult AnalyzeExpectSuccess(string source)
    {
        var result = Analyze(source);
        Assert.True(result.Success,
            $"期望分析成功，但有 {result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error)} 个错误:\n"
            + string.Join("\n", result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"  [{d.Code}] {d.Message} @ {d.Span}")));
        return result;
    }

    /// <summary>
    /// 分析并断言无错误（指定文件名）
    /// </summary>
    public static AnalysisResult AnalyzeExpectSuccess(string source, string? fileName)
    {
        var result = Analyze(source, fileName);
        Assert.True(result.Success,
            $"期望分析成功，但有 {result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error)} 个错误:\n"
            + string.Join("\n", result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"  [{d.Code}] {d.Message} @ {d.Span}")));
        return result;
    }

    /// <summary>
    /// 获取指定错误码的诊断列表
    /// </summary>
    public static List<Diagnostic> GetErrors(AnalysisResult result, ErrorCode? code = null)
    {
        return result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => code == null || d.Code == code)
            .ToList();
    }

    /// <summary>
    /// 获取所有诊断（含 Warning）
    /// </summary>
    public static List<Diagnostic> GetDiagnostics(AnalysisResult result,
        DiagnosticSeverity? severity = null)
    {
        return result.Diagnostics
            .Where(d => severity == null || d.Severity == severity)
            .ToList();
    }
}