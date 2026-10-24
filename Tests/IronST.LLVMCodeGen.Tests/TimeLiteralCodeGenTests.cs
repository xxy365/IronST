using IronST.LLVMCodeGen.Tests.Helpers;
using Xunit;

namespace IronST.LLVMCodeGen.Tests;

public class TimeLiteralCodeGenTests
{
    [Fact]
    public void TimeLiteral_T5s_ShouldEmit5000()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM P
            VAR t1 : TIME; END_VAR
                t1 := T#5s;
            END_PROGRAM
            """);

        Assert.Contains("i64 5000", ir);
    }

    [Fact]
    public void TimeLiteral_T3ms_ShouldEmit3()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM P
            VAR t1 : TIME; END_VAR
                t1 := T#3ms;
            END_PROGRAM
            """);

        Assert.Contains("i64 3", ir);
    }

    [Fact]
    public void TimeLiteral_T5s3ms_ShouldEmit5003()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM P
            VAR t1 : TIME; END_VAR
                t1 := T#5s3ms;
            END_PROGRAM
            """);

        Assert.Contains("i64 5003", ir);
    }

    [Fact]
    public void TimeLiteral_T1d2h30m_ShouldEmit95400000()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM P
            VAR t1 : TIME; END_VAR
                t1 := T#1d2h30m;
            END_PROGRAM
            """);

        // 86400000 + 7200000 + 1800000 = 95400000
        Assert.Contains("i64 95400000", ir);
    }

    [Fact]
    public void DateLiteral_D20240101_ShouldEmitDays()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM P
            VAR d1 : DATE; END_VAR
                d1 := D#2024-01-01;
            END_PROGRAM
            """);

        Assert.Contains("i64 19723", ir);
    }
}