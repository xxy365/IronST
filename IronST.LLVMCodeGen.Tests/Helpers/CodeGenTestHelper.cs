using IronST.Frontend;
using IronST.Frontend.Analysis;
using IronST.IRBuilder.Emitter;
using IronST.LLVMCodeGen.CodeGen;
using IronST.LLVMCodeGen.Debug;

namespace IronST.LLVMCodeGen.Tests.Helpers;

internal static class CodeGenTestHelper
{
    /// <summary>默认库列表：IEC 61131-3 标准库</summary>
    private static readonly IReadOnlyList<ExternalLibrary> DefaultLibraries =
        [IECStandardLibrary.Create()];

    public static string EmitIR(
        string source,
        IReadOnlyList<ExternalLibrary>? libraries = null)       // ← 新增
    {
        var analysis = FrontendPipeline.Analyze(
            source, "test.st", libraries ?? DefaultLibraries);  // ← 透传
        if (!analysis.Success) {
            var errors = analysis.Diagnostics
                .Where(d => d.Severity == Common.Diagnostics.DiagnosticSeverity.Error)
                .Select(d => $"  {d}");
            throw new InvalidOperationException(
                $"前端分析失败:\n{string.Join("\n", errors)}");
        }
        var generator = new IRGenerator(new NullDebugEmitter());
        var module = generator.Generate(analysis);
        return new LLEmitter().Emit(module);
    }

    public static (AnalysisResult analysis, string ir) AnalyzeAndEmit(
        string source,
        IReadOnlyList<ExternalLibrary>? libraries = null)       // ← 新增
    {
        var analysis = FrontendPipeline.Analyze(
            source, "test.st", libraries ?? DefaultLibraries);  // ← 透传
        var generator = new IRGenerator(new NullDebugEmitter());
        var module = generator.Generate(analysis);
        var ir = new LLEmitter().Emit(module);
        return (analysis, ir);
    }

    public static int CountOccurrences(string text, string pattern)
    {
        int count = 0, idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0) {
            count++;
            idx += pattern.Length;
        }
        return count;
    }
}