// MultiFileTests.cs — 多文件编译（B4）
// 验证 FrontendPipeline.AnalyzeFiles：跨文件函数/类型引用、部分失败、空输入。
using IronST.Frontend.Analysis;

namespace IronST.Frontend.Tests;

public class MultiFileTests
{
    private static readonly IReadOnlyList<ExternalLibrary> Libraries =
        [IECStandardLibrary.Create()];

    private static AnalysisResult AnalyzeFiles(params (string Source, string FileName)[] files) =>
        FrontendPipeline.AnalyzeFiles(files, Libraries);

    private static string[] Errors(AnalysisResult r) =>
        r.Diagnostics.Where(d => d.Severity == Common.Diagnostics.DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();

    [Fact]
    public void FunctionInOneFile_CalledFromAnother_ShouldSucceed()
    {
        var result = AnalyzeFiles(
            ("FUNCTION Add : DINT\nVAR_INPUT a : DINT; b : DINT; END_VAR\nAdd := a + b;\nEND_FUNCTION", "math.st"),
            ("PROGRAM Main\nVAR x : DINT; END_VAR\nx := Add(1, 2);\nEND_PROGRAM", "main.st"));

        Assert.True(result.Success, string.Join("\n", Errors(result)));
        Assert.Contains(result.POUs, p => p.Name == "Add");
        Assert.Contains(result.POUs, p => p.Name == "Main");
        Assert.Contains(result.CallSites, c => c.CallerName == "Main" && c.CalleeName == "Add");
    }

    [Fact]
    public void TypeInOneFile_UsedInAnother_ShouldSucceed()
    {
        var result = AnalyzeFiles(
            ("TYPE Point : STRUCT\nx : INT;\ny : INT;\nEND_STRUCT;\nEND_TYPE;", "types.st"),
            ("PROGRAM Main\nVAR p : Point; d : DINT; END_VAR\np.x := 3;\nd := p.x;\nEND_PROGRAM", "main.st"));

        Assert.True(result.Success, string.Join("\n", Errors(result)));
        Assert.Contains(result.CustomTypes, t => t.Name == "Point");
    }

    [Fact]
    public void SyntaxErrorInOneFile_ShouldNotBlockOtherFile()
    {
        var result = AnalyzeFiles(
            ("FUNCTION Helper : DINT\nHelper := 1;\nEND_FUNCTION", "ok.st"),
            ("PROGRAM Bad\nVAR x : DINT; END_VAR\nx :=\nEND_PROGRAM", "bad.st"));

        // 至少一个语法错误，但 ok.st 的 Helper 仍被注册
        Assert.False(result.Success);
        Assert.Contains(Errors(result), e => e.Contains("bad.st"));
        Assert.Contains(result.POUs, p => p.Name == "Helper");
    }

    [Fact]
    public void EmptyFileList_ShouldReportError()
    {
        var result = AnalyzeFiles();

        Assert.False(result.Success);
        Assert.Contains(Errors(result), e => e.Contains("没有输入文件"));
    }

    [Fact]
    public void FileSpan_ShouldPreserveSourceFileName()
    {
        var result = AnalyzeFiles(
            ("FUNCTION F : DINT\nF := 1;\nEND_FUNCTION", "lib.st"),
            ("PROGRAM Main\nVAR x : DINT; END_VAR\nx := F();\nEND_PROGRAM", "main.st"));

        Assert.True(result.Success, string.Join("\n", Errors(result)));

        var fnDecl = result.References.First(r =>
            r.SymbolName == "F" && r.Usage == ReferenceUsage.Declaration);
        Assert.Equal("lib.st", fnDecl.FilePath);
    }
}
