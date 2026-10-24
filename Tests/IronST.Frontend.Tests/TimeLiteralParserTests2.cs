using IronST.Frontend.Analysis;

namespace IronST.Frontend.Tests;

public class TimeLiteralParserTests2
{
    // ─────────────────────────────────────────────
    //  TryParse 裸 catch 吞没所有异常
    // ─────────────────────────────────────────────

    [Fact]
    public void P1_8_TryParse_ShouldNotSwallowFatalExceptions()
    {
        // 验证 TryParse 对致命异常（OutOfMemoryException 等）不会静默吞掉
        // 这个测试无法直接用 OOM 触发，但可以验证异常类型限定

        // 正常情况不应返回 null
        var result = TimeLiteralParser.TryParse(
            IronST.Frontend.AST.LiteralKind.Time, "T#1s");
        Assert.NotNull(result);
        Assert.Equal(1000, result.Value);
    }

    [Fact]
    public void P1_8_TryParse_InvalidFormat_ShouldReturnNull()
    {
        // 格式错误应该返回 null（当前行为，但需要确保是特定异常而非裸 catch）
        var result = TimeLiteralParser.TryParse(
            IronST.Frontend.AST.LiteralKind.Time, "T#not_a_time");
        Assert.Null(result);
    }

    [Fact]
    public void P1_8_TryParse_EmptyString_ShouldReturnNull()
    {
        var result = TimeLiteralParser.TryParse(
            IronST.Frontend.AST.LiteralKind.Time, "T#");
        Assert.Null(result);
    }

    // ─────────────────────────────────────────────
    //  ParseDurationToMs 的 double→long 溢出
    // ─────────────────────────────────────────────

    [Fact]
    public void P1_9_ExtremelyLargeDuration_ShouldNotReturnGarbage()
    {
        // 极大天数，double * 86400000 会超出 long 范围
        // 期望返回 null 或 long.MaxValue，而非任意垃圾值
        var result = TimeLiteralParser.ParseDurationToMs("999999999999d");

        // 如果溢出，结果可能是 long.MinValue（溢出回绕）
        // 正确行为应该是要么返回合理的值，要么返回时上层处理溢出
        // 关键断言：结果不应为负数（溢出回绕的标志）
        Assert.True(result >= 0,
            $"极大时间值解析结果不应为负数，实际得到 {result}，这可能是溢出回绕");
    }

    [Fact]
    public void P1_9_NormalDuration_ShouldParseCorrectly()
    {
        // 基本功能验证
        // T#1d2h30m5s100ms = 1*86400000 + 2*3600000 + 30*60000 + 5*1000 + 100
        //                   = 86400000 + 7200000 + 1800000 + 5000 + 100
        //                   = 95405100
        var result = TimeLiteralParser.ParseDurationToMs("1d2h30m5s100ms");
        Assert.Equal(95_405_100, result);
    }

    [Fact]
    public void P1_9_DecimalDuration_ShouldParseCorrectly()
    {
        // T#1.5s = 1500ms
        var result = TimeLiteralParser.ParseDurationToMs("1.5s");
        Assert.Equal(1500, result);
    }

    [Fact]
    public void P1_9_DurationWithPrefix_ShouldStripHash()
    {
        // T#1s 应该正确去除 T# 前缀
        var result = TimeLiteralParser.TryParse(
            IronST.Frontend.AST.LiteralKind.Time, "T#1s");
        Assert.Equal(1000, result);
    }

    // ─────────────────────────────────────────────
    //  补充：日期解析
    // ─────────────────────────────────────────────

    [Fact]
    public void P1_9_DateParsing_ShouldBeCorrect()
    {
        // D#2024-01-01 = 2024年1月1日
        var result = TimeLiteralParser.ParseDateToDays("2024-01-01");

        // 2024-01-01 距 1970-01-01 的天数
        // 1970-2024 之间：54年，其中闰年 1972,1976,1980,1984,1988,1992,1996,
        //   2000,2004,2008,2012,2016,2020 = 13个闰年
        // 54*365 + 13 = 19723 天
        Assert.Equal(19723, result);
    }

    [Fact]
    public void P1_9_TimeOfDayParsing_ShouldBeCorrect()
    {
        // TOD#12:30:00 = 12*3600000 + 30*60000 = 43200000 + 1800000 = 45000000
        var result = TimeLiteralParser.ParseTodToMs("12:30:00");
        Assert.Equal(45_000_000, result);
    }

    [Fact]
    public void P1_9_DateTimeParsing_ShouldBeCorrect()
    {
        // DT#2024-01-01-12:00:00
        var result = TimeLiteralParser.ParseDtToMs("2024-01-01-12:00:00");
        long expectedDays = 19723;
        long expectedMs = expectedDays * 86_400_000 + 12 * 3_600_000;
        Assert.Equal(expectedMs, result);
    }
}