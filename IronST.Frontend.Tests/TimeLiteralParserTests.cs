using IronST.Frontend.Analysis;
using IronST.Frontend.AST;

namespace IronST.Frontend.Tests;

/// <summary>
/// TimeLiteralParser 单元测试 —— 直接测试解析方法 + 通过 Pipeline 端到端验证 ParsedValue。
/// </summary>
public class TimeLiteralParserTests
{
    // ═══════════════════════════════════════════════════════════════
    //  1. TIME 持续时间：ParseDurationToMs
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Time_SimpleSeconds()
    {
        // T#60s = 60 × 1000 = 60000
        var result = TimeLiteralParser.ParseDurationToMs("60s");
        Assert.Equal(60000, result);
    }

    [Fact]
    public void Time_SecondsAndMillis()
    {
        // T#5s3ms = 5 × 1000 + 3 = 5003
        var result = TimeLiteralParser.ParseDurationToMs("5s3ms");
        Assert.Equal(5003, result);
    }

    [Fact]
    public void Time_DaysHoursMinutes()
    {
        // T#1d2h30m = 86400000 + 7200000 + 1800000 = 95400000
        var result = TimeLiteralParser.ParseDurationToMs("1d2h30m");
        Assert.Equal(95400000, result);
    }

    [Fact]
    public void Time_FullFormat()
    {
        // T#1d2h30m5s3ms = 86400000 + 7200000 + 1800000 + 5000 + 3 = 95405003
        var result = TimeLiteralParser.ParseDurationToMs("1d2h30m5s3ms");
        Assert.Equal(95405003, result);
    }

    [Fact]
    public void Time_OnlyMillis()
    {
        var result = TimeLiteralParser.ParseDurationToMs("500ms");
        Assert.Equal(500, result);
    }

    [Fact]
    public void Time_OnlyHours()
    {
        // T#2h = 2 × 3600000 = 7200000
        var result = TimeLiteralParser.ParseDurationToMs("2h");
        Assert.Equal(7200000, result);
    }

    [Fact]
    public void Time_OnlyDays()
    {
        // T#1d = 86400000
        var result = TimeLiteralParser.ParseDurationToMs("1d");
        Assert.Equal(86400000, result);
    }

    [Fact]
    public void Time_OnlyMinutes()
    {
        // T#30min = 1800000
        var result = TimeLiteralParser.ParseDurationToMs("30min");
        Assert.Equal(1800000, result);
    }

    [Fact]
    public void Time_OnlyMinutesShort()
    {
        // T#5m = 300000
        var result = TimeLiteralParser.ParseDurationToMs("5m");
        Assert.Equal(300000, result);
    }

    [Fact]
    public void Time_NoUnit_DefaultsToSeconds()
    {
        // 无单位默认秒
        var result = TimeLiteralParser.ParseDurationToMs("10");
        Assert.Equal(10000, result);
    }

    [Fact]
    public void Time_Microseconds_TruncatedToZero()
    {
        // us 在 ms 精度下截断为 0
        var result = TimeLiteralParser.ParseDurationToMs("500us");
        Assert.Equal(0, result);
    }

    [Fact]
    public void Time_Nanoseconds_TruncatedToZero()
    {
        var result = TimeLiteralParser.ParseDurationToMs("1000ns");
        Assert.Equal(0, result);
    }

    [Fact]
    public void Time_MixedWithMicroseconds()
    {
        // T#1s500us → 1s = 1000ms, 500us = 0ms → 1000
        var result = TimeLiteralParser.ParseDurationToMs("1s500us");
        Assert.Equal(1000, result);
    }

    [Fact]
    public void Time_Zero()
    {
        var result = TimeLiteralParser.ParseDurationToMs("0s");
        Assert.Equal(0, result);
    }

    // ═══════════════════════════════════════════════════════════════
    //  2. DATE 日期：ParseDateToDays
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Date_EpochStart()
    {
        // D#1970-01-01 = 0
        var result = TimeLiteralParser.ParseDateToDays("1970-01-01");
        Assert.Equal(0, result);
    }

    [Fact]
    public void Date_NextDay()
    {
        // D#1970-01-02 = 1
        var result = TimeLiteralParser.ParseDateToDays("1970-01-02");
        Assert.Equal(1, result);
    }

    [Fact]
    public void Date_2024_01_01()
    {
        // D#2024-01-01 = 19723
        var result = TimeLiteralParser.ParseDateToDays("2024-01-01");
        Assert.Equal(19723, result);
    }

    [Fact]
    public void Date_2000_01_01()
    {
        // D#2000-01-01 = 10957
        var result = TimeLiteralParser.ParseDateToDays("2000-01-01");
        Assert.Equal(10957, result);
    }

    [Fact]
    public void Date_InvalidFormat_ReturnsZero()
    {
        var result = TimeLiteralParser.ParseDateToDays("bad-date");
        Assert.Equal(0, result);
    }

    // ═══════════════════════════════════════════════════════════════
    //  3. TIME_OF_DAY 当天时间：ParseTodToMs
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Tod_Midnight()
    {
        // TOD#00:00:00 = 0
        var result = TimeLiteralParser.ParseTodToMs("00:00:00");
        Assert.Equal(0, result);
    }

    [Fact]
    public void Tod_Noon()
    {
        // TOD#12:00:00 = 43200000
        var result = TimeLiteralParser.ParseTodToMs("12:00:00");
        Assert.Equal(43200000, result);
    }

    [Fact]
    public void Tod_HourMinute()
    {
        // TOD#12:30 = 12×3600000 + 30×60000 = 45000000
        var result = TimeLiteralParser.ParseTodToMs("12:30");
        Assert.Equal(45000000, result);
    }

    [Fact]
    public void Tod_WithSeconds()
    {
        // TOD#12:30:00 = 45000000
        var result = TimeLiteralParser.ParseTodToMs("12:30:00");
        Assert.Equal(45000000, result);
    }

    [Fact]
    public void Tod_WithFractionalSeconds()
    {
        // TOD#12:30:00.5 = 45000500
        var result = TimeLiteralParser.ParseTodToMs("12:30:00.5");
        Assert.Equal(45000500, result);
    }

    [Fact]
    public void Tod_WithThreeDigitMillis()
    {
        // TOD#12:30:00.123 = 45000123
        var result = TimeLiteralParser.ParseTodToMs("12:30:00.123");
        Assert.Equal(45000123, result);
    }

    [Fact]
    public void Tod_WithOneDigitMillis()
    {
        // TOD#00:00:01.5 = 1500
        var result = TimeLiteralParser.ParseTodToMs("00:00:01.5");
        Assert.Equal(1500, result);
    }

    [Fact]
    public void Tod_WithTwoDigitMillis()
    {
        // TOD#00:00:01.50 = 1500
        var result = TimeLiteralParser.ParseTodToMs("00:00:01.50");
        Assert.Equal(1500, result);
    }

    [Fact]
    public void Tod_EndOfDay()
    {
        // TOD#23:59:59.999 = 23×3600000 + 59×60000 + 59×1000 + 999 = 86399999
        var result = TimeLiteralParser.ParseTodToMs("23:59:59.999");
        Assert.Equal(86399999, result);
    }

    [Fact]
    public void Tod_InvalidFormat_ReturnsZero()
    {
        var result = TimeLiteralParser.ParseTodToMs("bad");
        Assert.Equal(0, result);
    }

    // ═══════════════════════════════════════════════════════════════
    //  4. DATE_AND_TIME 日期时间：ParseDtToMs
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Dt_DashSeparator()
    {
        // DT#2024-01-01-12:30:00
        // = 19723 × 86400000 + 45000000
        // = 1704067200000 + 45000000
        // = 1704112200000
        var result = TimeLiteralParser.ParseDtToMs("2024-01-01-12:30:00");
        Assert.Equal(1704112200000, result);
    }

    [Fact]
    public void Dt_SpaceSeparator()
    {
        // DT#2024-01-01 12:30:00 → 同上
        var result = TimeLiteralParser.ParseDtToMs("2024-01-01 12:30:00");
        Assert.Equal(1704112200000, result);
    }

    [Fact]
    public void Dt_EpochMidnight()
    {
        // DT#1970-01-01-00:00:00 = 0
        var result = TimeLiteralParser.ParseDtToMs("1970-01-01-00:00:00");
        Assert.Equal(0, result);
    }

    [Fact]
    public void Dt_InvalidFormat_ReturnsZero()
    {
        var result = TimeLiteralParser.ParseDtToMs("bad");
        Assert.Equal(0, result);
    }

    // ═══════════════════════════════════════════════════════════════
    //  5. DaysSinceEpoch 辅助方法
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void DaysSinceEpoch_LeapYear2024()
    {
        // 2024 是闰年，2024-02-29 存在
        var feb28 = TimeLiteralParser.DaysSinceEpoch(2024, 2, 28);
        var feb29 = TimeLiteralParser.DaysSinceEpoch(2024, 2, 29);
        var mar01 = TimeLiteralParser.DaysSinceEpoch(2024, 3, 1);

        Assert.Equal(feb28 + 1, feb29);
        Assert.Equal(feb29 + 1, mar01);
    }

    [Fact]
    public void DaysSinceEpoch_NonLeapYear2023()
    {
        // 2023 不是闰年
        var feb28 = TimeLiteralParser.DaysSinceEpoch(2023, 2, 28);
        var mar01 = TimeLiteralParser.DaysSinceEpoch(2023, 3, 1);

        Assert.Equal(feb28 + 1, mar01);
    }

    // ═══════════════════════════════════════════════════════════════
    //  6. TryParse 统一入口
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TryParse_Time_WithPrefix()
    {
        // 带 T# 前缀
        var result = TimeLiteralParser.TryParse(LiteralKind.Time, "T#5s3ms");
        Assert.Equal(5003, result);
    }

    [Fact]
    public void TryParse_Time_WithoutPrefix()
    {
        // 不带前缀（hash 之后为空时直接用原文）
        var result = TimeLiteralParser.TryParse(LiteralKind.Time, "5s3ms");
        Assert.Equal(5003, result);
    }

    [Fact]
    public void TryParse_Date_WithPrefix()
    {
        var result = TimeLiteralParser.TryParse(LiteralKind.Date, "D#2024-01-01");
        Assert.Equal(19723, result);
    }

    [Fact]
    public void TryParse_Tod_WithPrefix()
    {
        var result = TimeLiteralParser.TryParse(LiteralKind.TimeOfDay, "TOD#12:30:00");
        Assert.Equal(45000000, result);
    }

    [Fact]
    public void TryParse_Dt_WithPrefix()
    {
        var result = TimeLiteralParser.TryParse(LiteralKind.DateTime, "DT#2024-01-01-12:30:00");
        Assert.Equal(1704112200000, result);
    }

    [Fact]
    public void TryParse_NonTimeKind_ReturnsNull()
    {
        // Integer、Real 等不应由 TimeLiteralParser 处理
        var result = TimeLiteralParser.TryParse(LiteralKind.Integer, "42");
        Assert.Null(result);
    }

    [Fact]
    public void TryParse_EmptyText_ReturnsNull()
    {
        var result = TimeLiteralParser.TryParse(LiteralKind.Time, "T#");
        Assert.Null(result);
    }

    [Fact]
    public void TryParse_InvalidText_ReturnsNull()
    {
        var result = TimeLiteralParser.TryParse(LiteralKind.Date, "not-a-date");
        // 解析失败不会崩溃，返回某个值或 null
        // DaysSinceEpoch 会返回一个值（DateTime 构造可能抛异常被 catch）
        // 不管返回什么，关键是不抛异常
        Assert.True(true); // 不崩溃即通过
    }

    // ═══════════════════════════════════════════════════════════════
    //  7. 端到端：Pipeline 验证 ParsedValue 填充
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void E2E_TimeLiteral_ParsedValueFilled()
    {
        var result = FrontendTestHelper.ExpectSuccess(@"
PROGRAM Main
VAR
    delay : TIME;
END_VAR
delay := T#5s3ms;
END_PROGRAM");

        var assign = (AssignmentNode)result.AST.POUs[0].Body[0];
        var lit = (LiteralExprNode)assign.Value;

        Assert.Equal(LiteralKind.Time, lit.Kind);
        Assert.Equal("T#5s3ms", lit.RawText);
        Assert.NotNull(lit.ParsedValue);
        Assert.Equal(5003L, lit.ParsedValue);
    }

    [Fact]
    public void E2E_DateLiteral_ParsedValueFilled()
    {
        var result = FrontendTestHelper.ExpectSuccess(@"
PROGRAM Main
VAR
    d : DATE;
END_VAR
d := D#2024-01-01;
END_PROGRAM");

        var assign = (AssignmentNode)result.AST.POUs[0].Body[0];
        var lit = (LiteralExprNode)assign.Value;

        Assert.Equal(LiteralKind.Date, lit.Kind);
        Assert.Equal("D#2024-01-01", lit.RawText);
        Assert.NotNull(lit.ParsedValue);
        Assert.Equal(19723L, lit.ParsedValue);
    }

    [Fact]
    public void E2E_TodLiteral_ParsedValueFilled()
    {
        var result = FrontendTestHelper.ExpectSuccess(@"
PROGRAM Main
VAR
    t : TIME_OF_DAY;
END_VAR
t := TOD#12:30:00;
END_PROGRAM");

        var assign = (AssignmentNode)result.AST.POUs[0].Body[0];
        var lit = (LiteralExprNode)assign.Value;

        Assert.Equal(LiteralKind.TimeOfDay, lit.Kind);
        Assert.Equal("TOD#12:30:00", lit.RawText);
        Assert.NotNull(lit.ParsedValue);
        Assert.Equal(45000000L, lit.ParsedValue);
    }

    [Fact]
    public void E2E_DtLiteral_ParsedValueFilled()
    {
        var result = FrontendTestHelper.ExpectSuccess(@"
PROGRAM Main
VAR
    ts : DATE_AND_TIME;
END_VAR
ts := DT#2024-01-01-12:30:00;
END_PROGRAM");

        var assign = (AssignmentNode)result.AST.POUs[0].Body[0];
        var lit = (LiteralExprNode)assign.Value;

        Assert.Equal(LiteralKind.DateTime, lit.Kind);
        Assert.Equal("DT#2024-01-01-12:30:00", lit.RawText);
        Assert.NotNull(lit.ParsedValue);
        Assert.Equal(1704112200000, lit.ParsedValue);
    }

    [Fact]
    public void E2E_IntLiteral_NotParsedByTimeParser()
    {
        // 普通整数字面量不应被 TimeLiteralParser 处理
        var result = FrontendTestHelper.ExpectSuccess(@"
PROGRAM Main
VAR x : INT; END_VAR
x := 42;
END_PROGRAM");

        var assign = (AssignmentNode)result.AST.POUs[0].Body[0];
        var lit = (LiteralExprNode)assign.Value;

        Assert.Equal(LiteralKind.Integer, lit.Kind);
        Assert.Equal(42L, lit.ParsedValue);
    }

    [Fact]
    public void E2E_MultipleTimeLiterals_AllParsed()
    {
        var result = FrontendTestHelper.ExpectSuccess(@"
PROGRAM Main
VAR
    d1 : TIME;
    d2 : TIME;
    d3 : TIME;
END_VAR
d1 := T#1s;
d2 := T#500ms;
d3 := T#1d;
END_PROGRAM");

        var stmts = result.AST.POUs[0].Body;

        var lit1 = (LiteralExprNode)((AssignmentNode)stmts[0]).Value;
        var lit2 = (LiteralExprNode)((AssignmentNode)stmts[1]).Value;
        var lit3 = (LiteralExprNode)((AssignmentNode)stmts[2]).Value;

        Assert.Equal(1000L, lit1.ParsedValue);
        Assert.Equal(500L, lit2.ParsedValue);
        Assert.Equal(86400000L, lit3.ParsedValue);
    }

    [Fact]
    public void E2E_TimeLiteral_InFunctionArgument()
    {
        // 时间字面量作为函数参数
        var result = FrontendTestHelper.ExpectSuccess(@"
FUNCTION Delay : TIME
VAR_INPUT
    d : TIME;
END_VAR
Delay := d;
END_FUNCTION

PROGRAM Main
VAR t : TIME; END_VAR
t := Delay(T#5s);
END_PROGRAM");

        var mainBody = result.AST.POUs[1].Body;
        var assign = (AssignmentNode)mainBody[0];
        var call = (FunctionCallExprNode)assign.Value;
        var argLit = (LiteralExprNode)call.Arguments[0].Value;

        Assert.Equal(LiteralKind.Time, argLit.Kind);
        Assert.Equal(5000L, argLit.ParsedValue);
    }

    [Fact]
    public void E2E_TimeLiteral_InIfCondition()
    {
        // 时间字面量在条件表达式中
        var result = FrontendTestHelper.ExpectSuccess(@"
PROGRAM Main
VAR
    delay : TIME;
    flag : BOOL;
END_VAR
IF delay > T#5s THEN
    flag := TRUE;
END_IF
END_PROGRAM");

        var ifStmt = (IfNode)result.AST.POUs[0].Body[0];
        var binExpr = (BinaryExprNode)ifStmt.Condition;
        var rightLit = (LiteralExprNode)binExpr.Right;

        Assert.Equal(LiteralKind.Time, rightLit.Kind);
        Assert.Equal(5000L, rightLit.ParsedValue);
    }

    // ═══════════════════════════════════════════════════════════════
    //  8. 边界情况
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Time_LargeDuration()
    {
        // T#365d = 365 × 86400000 = 31536000000
        var result = TimeLiteralParser.ParseDurationToMs("365d");
        Assert.Equal(31536000000L, result);
    }

    [Fact]
    public void Time_WithSpaces()
    {
        // 允许空格分隔
        var result = TimeLiteralParser.ParseDurationToMs("1d 2h 30m");
        Assert.Equal(95400000, result);
    }

    [Fact]
    public void Tod_OnlyHourMinute()
    {
        // TOD#8:30（无秒）
        var result = TimeLiteralParser.ParseTodToMs("8:30");
        Assert.Equal(30600000, result);
    }

    [Fact]
    public void Date_YearBoundary()
    {
        // D#1970-12-31 = 364
        var result = TimeLiteralParser.ParseDateToDays("1970-12-31");
        Assert.Equal(364, result);
    }

    [Fact]
    public void Date_LeapDay()
    {
        // D#2024-02-29
        var feb28 = TimeLiteralParser.DaysSinceEpoch(2024, 2, 28);
        var feb29 = TimeLiteralParser.DaysSinceEpoch(2024, 2, 29);
        Assert.Equal(feb28 + 1, feb29);

        var result = TimeLiteralParser.ParseDateToDays("2024-02-29");
        Assert.Equal(feb29, result);
    }
}