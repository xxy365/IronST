// DiagnosticFilterTests.cs — 多文件分析后，哪些诊断算"当前这个文件的"。
//
// AnalyzeFiles 会把所有文件的诊断合并成一张表（诊断顺序 = 文件顺序），
// 而编辑器的波浪线、错误列表、状态栏都只该显示当前标签页那一份。
// 归属靠 SourceSpan.FileName（CLI 输出 `Main.st:21:15` 就是同一个字段）。
using IronST.Common.Diagnostics;
using IronST.IDE.Services;

namespace IronST.IDE.Tests;

public class DiagnosticFilterTests
{
    private static Diagnostic Diag(string? file, int line = 1) => new()
    {
        Severity = DiagnosticSeverity.Error,
        Message = $"{file ?? "(无文件名)"}@{line}",
        Code = ErrorCode.E2006,
        Span = new SourceSpan(line, 1, line, 2, file),
    };

    [Fact]
    public void KeepsOnlyDiagnosticsOfThatFile()
    {
        var all = new List<Diagnostic>
        {
            Diag("Main.st", 1),
            Diag("Utils.st", 2),
            Diag("Main.st", 3),
        };

        var mine = DiagnosticFilter.ForFile(all, "Main.st");

        Assert.Equal(2, mine.Count);
        Assert.All(mine, d => Assert.Equal("Main.st", d.Span!.Value.FileName));
    }

    [Fact]
    public void FileMatch_IsCaseInsensitive()
    {
        var all = new List<Diagnostic> { Diag(@"C:\proj\Main.st", 1) };

        Assert.Single(DiagnosticFilter.ForFile(all, @"C:\PROJ\main.ST"));
    }

    [Fact]
    public void DiagnosticsWithoutFile_GoToCurrentFile()
    {
        // 全局诊断（E1000"没有输入文件"之类）没有文件名。
        // 宁可多报一条，也不要把真错误吞掉。
        var all = new List<Diagnostic>
        {
            Diag(null, 1),
            new()
            {
                Severity = DiagnosticSeverity.Error,
                Message = "完全没有 span",
                Code = ErrorCode.E1000,
                Span = null,
            },
            Diag("Utils.st", 2),
        };

        var mine = DiagnosticFilter.ForFile(all, "Main.st");

        Assert.Equal(2, mine.Count);
    }

    [Fact]
    public void NoDiagnostics_ReturnsEmpty()
    {
        Assert.Empty(DiagnosticFilter.ForFile([], "Main.st"));
    }
}
