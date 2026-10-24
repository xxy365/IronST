// tests/IronST.Common.Tests/DiagnosticBagTests.cs
using IronST.Common.Diagnostics;

namespace IronST.Common.Tests;

public class DiagnosticBagTests
{
    private static SourceSpan MakeSpan(int line = 1, int col = 1) => new(line, col, line, col + 5);

    // ═══════════════════════════════════════════════════════════════
    //  报告与计数
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void EmptyBag_HasNoErrors()
    {
        var bag = new DiagnosticBag();

        Assert.Empty(bag.Diagnostics);
        Assert.False(bag.HasErrors);
        Assert.Equal(0, bag.ErrorCount);
    }

    [Fact]
    public void Error_ReportsAndIncrementsCount()
    {
        var bag = new DiagnosticBag();
        bag.Error("something wrong", MakeSpan(), ErrorCode.E2003);

        Assert.Single(bag.Diagnostics);
        Assert.True(bag.HasErrors);
        Assert.Equal(1, bag.ErrorCount);
        Assert.Equal(DiagnosticSeverity.Error, bag.Diagnostics[0].Severity);
        Assert.Equal("something wrong", bag.Diagnostics[0].Message);
        Assert.Equal(ErrorCode.E2003, bag.Diagnostics[0].Code);
    }

    [Fact]
    public void Error_DefaultsToE1000()
    {
        var bag = new DiagnosticBag();
        bag.Error("unknown", MakeSpan());

        Assert.Equal(ErrorCode.E1000, bag.Diagnostics[0].Code);
    }

    [Fact]
    public void Warning_DoesNotSetHasErrors()
    {
        var bag = new DiagnosticBag();
        bag.Warning("watch out", MakeSpan());

        Assert.Single(bag.Diagnostics);
        Assert.False(bag.HasErrors);
        Assert.Equal(0, bag.ErrorCount);
        Assert.Equal(DiagnosticSeverity.Warning, bag.Diagnostics[0].Severity);
    }

    [Fact]
    public void Warning_PreservesErrorCode()
    {
        var bag = new DiagnosticBag();
        bag.Warning("deprecated", MakeSpan(), ErrorCode.E2014);

        Assert.Equal(ErrorCode.E2014, bag.Diagnostics[0].Code);
    }

    [Fact]
    public void Info_ReportsCorrectly()
    {
        var bag = new DiagnosticBag();
        bag.Info("fyi", MakeSpan());

        Assert.Single(bag.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Info, bag.Diagnostics[0].Severity);
    }

    [Fact]
    public void Report_WithExplicitSeverity()
    {
        var bag = new DiagnosticBag();
        bag.Report(DiagnosticSeverity.Hint, "hint msg", MakeSpan(), ErrorCode.E2011);

        Assert.Equal(DiagnosticSeverity.Hint, bag.Diagnostics[0].Severity);
        Assert.Equal(ErrorCode.E2011, bag.Diagnostics[0].Code);
    }

    [Fact]
    public void ErrorCount_OnlyCountsErrors()
    {
        var bag = new DiagnosticBag();
        bag.Error("e1", MakeSpan());
        bag.Warning("w1", MakeSpan());
        bag.Error("e2", MakeSpan());
        bag.Info("i1", MakeSpan());

        Assert.Equal(4, bag.Diagnostics.Count);
        Assert.Equal(2, bag.ErrorCount);
        Assert.True(bag.HasErrors);
    }

    // ═══════════════════════════════════════════════════════════════
    //  诊断详情
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Diagnostic_PreservesSpanInformation()
    {
        var bag = new DiagnosticBag();
        var span = new SourceSpan(10, 5, 10, 15, "test.st");
        bag.Error("bad thing", span, ErrorCode.E2003);

        Assert.Equal(span, bag.Diagnostics[0].Span);
    }

    [Fact]
    public void Diagnostic_ToString_IncludesCodeAndDescription()
    {
        var d = new Diagnostic
        {
            Severity = DiagnosticSeverity.Error,
            Message = "无法将 INT 赋值给 BOOL",
            Span = new SourceSpan(3, 1, 3, 10),
            Code = ErrorCode.E2003,
        };

        var s = d.ToString();
        Assert.Contains("E2003", s);
        Assert.Contains("类型不兼容", s);
        Assert.Contains("无法将 INT 赋值给 BOOL", s);
        Assert.Contains("Error", s);
    }

    [Fact]
    public void Diagnostic_ToString_AlwaysHasCode()
    {
        // ErrorCode 是值类型枚举，默认值是 0，不会为 null
        var d = new Diagnostic
        {
            Severity = DiagnosticSeverity.Warning,
            Message = "unused var",
            Span = new SourceSpan(1, 1, 1, 5),
            Code = ErrorCode.E2000,
        };

        var s = d.ToString();
        Assert.Contains("[E2000]", s);
        Assert.Contains("未知语义错误", s);
        Assert.Contains("unused var", s);
    }

    // ═══════════════════════════════════════════════════════════════
    //  ErrorCode 描述
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(ErrorCode.E1000, "未知语法错误")]
    [InlineData(ErrorCode.E1001, "不匹配的输入")]
    [InlineData(ErrorCode.E1003, "多余的输入")]
    [InlineData(ErrorCode.E1004, "无法识别的字符")]
    [InlineData(ErrorCode.E2001, "未声明的标识符")]
    [InlineData(ErrorCode.E2002, "重复声明")]
    [InlineData(ErrorCode.E2003, "类型不兼容")]
    [InlineData(ErrorCode.E2004, "参数数量不匹配")]
    [InlineData(ErrorCode.E2005, "参数类型不匹配")]
    [InlineData(ErrorCode.E2006, "未知的函数或程序块")]
    [InlineData(ErrorCode.E2007, "未知的结构体类型")]
    [InlineData(ErrorCode.E2008, "结构体字段不存在")]
    [InlineData(ErrorCode.E2009, "赋值目标不可写")]
    [InlineData(ErrorCode.E2010, "编译期除零")]
    [InlineData(ErrorCode.E2011, "函数返回值未赋值")]
    [InlineData(ErrorCode.E2012, "保留关键字不能用作标识符")]
    [InlineData(ErrorCode.E3001, "LLVM IR 生成失败")]
    [InlineData(ErrorCode.E3002, "字节码生成失败")]
    [InlineData(ErrorCode.E3003, "不支持的语言特性")]
    [InlineData(ErrorCode.E3004, "类型转换失败")]
    [InlineData(ErrorCode.E3005, "内部编译器错误")]
    [InlineData(ErrorCode.E4001, "数组越界")]
    [InlineData(ErrorCode.E4003, "宿主函数未注册")]
    public void ErrorCode_HasCorrectDescription(ErrorCode code, string expectedDesc)
    {
        Assert.Equal(expectedDesc, code.GetDescription());
    }

    [Fact]
    public void ErrorCode_EnumValues_AreInCorrectRanges()
    {
        foreach (var code in Enum.GetValues<ErrorCode>())
        {
            int val = (int)code;
            int range = val / 1000;

            // 每个值必须落在自己编码区间内
            Assert.InRange(val, range * 1000, range * 1000 + 999);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Clear / AddRange
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Clear_RemovesAllDiagnostics()
    {
        var bag = new DiagnosticBag();
        bag.Error("e", MakeSpan(), ErrorCode.E2001);
        bag.Warning("w", MakeSpan(), ErrorCode.E2014);

        bag.Clear();

        Assert.Empty(bag.Diagnostics);
        Assert.False(bag.HasErrors);
    }

    [Fact]
    public void AddRange_AppendsDiagnostics()
    {
        var bag = new DiagnosticBag();
        bag.Error("first", MakeSpan(), ErrorCode.E2001);

        var extras = new List<Diagnostic>
        {
            new()
            {
                Severity = DiagnosticSeverity.Warning,
                Message = "second",
                Span = MakeSpan(),
                Code = ErrorCode.E2014,
            },
            new()
            {
                Severity = DiagnosticSeverity.Error,
                Message = "third",
                Span = MakeSpan(),
                Code = ErrorCode.E2003,
            },
        };
        bag.AddRange(extras);

        Assert.Equal(3, bag.Diagnostics.Count);
        Assert.Equal(2, bag.ErrorCount);
    }

    // ═══════════════════════════════════════════════════════════════
    //  ToString 完整格式
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Diagnostic_ToString_FullFormat()
    {
        var d = new Diagnostic
        {
            Severity = DiagnosticSeverity.Error,
            Message = "标识符 'abc' 未声明",
            Span = new SourceSpan(15, 1, 15, 4, "demo.st"),
            Code = ErrorCode.E2001,
        };

        var s = d.ToString();
        // 格式: file:line:col-line:col: Error [E2001] 未声明的标识符: 标识符 'abc' 未声明
        Assert.Contains("demo.st", s);
        Assert.Contains("15", s);
        Assert.Contains("Error", s);
        Assert.Contains("[E2001]", s);
        Assert.Contains("未声明的标识符", s);
        Assert.Contains("标识符 'abc' 未声明", s);
    }

    [Fact]
    public void Diagnostic_ToString_UnknownRange()
    {
        var d = new Diagnostic
        {
            Severity = DiagnosticSeverity.Error,
            Message = "internal error",
            Code = ErrorCode.E3005,
        };

        var s = d.ToString();
        Assert.Contains("<unknown>", s);
        Assert.Contains("[E3005]", s);
        Assert.Contains("内部编译器错误", s);
    }
}
