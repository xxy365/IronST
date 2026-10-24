using IronST.LLVMCodeGen.Tests.Helpers;
using Xunit;

namespace IronST.LLVMCodeGen.Tests;

/// <summary>
/// 数学内置函数 → IR 测试。
/// </summary>
public class MathFunctionCodeGenTests
{
    [Fact]
    public void SQRT_ShouldDeclareExternAndCall()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                x : LREAL;
            END_VAR
                x := SQRT(4.0);
            END_PROGRAM
            """);

        Assert.Contains("declare double @sqrt", ir);
        Assert.Contains("call double @sqrt", ir);
    }

    [Fact]
    public void SIN_ShouldDeclareExternAndCall()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                x : LREAL;
            END_VAR
                x := SIN(1.5708);
            END_PROGRAM
            """);

        Assert.Contains("declare double @sin", ir);
        Assert.Contains("call double @sin", ir);
    }

    [Fact]
    public void COS_ShouldDeclareExternAndCall()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                x : LREAL;
            END_VAR
                x := COS(0.0);
            END_PROGRAM
            """);

        Assert.Contains("declare double @cos", ir);
    }

    [Fact]
    public void LN_ShouldCallLog()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                x : LREAL;
            END_VAR
                x := LN(2.718);
            END_PROGRAM
            """);

        Assert.Contains("declare double @log", ir);
        Assert.Contains("call double @log", ir);
    }

    [Fact]
    public void LOG_ShouldCallLog10()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                x : LREAL;
            END_VAR
                x := LOG(100.0);
            END_PROGRAM
            """);

        Assert.Contains("declare double @log10", ir);
    }

    [Fact]
    public void EXP_ShouldCallExp()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                x : LREAL;
            END_VAR
                x := EXP(1.0);
            END_PROGRAM
            """);

        Assert.Contains("declare double @exp", ir);
    }

    [Fact]
    public void ATAN2_ShouldDeclareAndCall()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                x : LREAL;
            END_VAR
                x := ATAN2(1.0, 1.0);
            END_PROGRAM
            """);

        Assert.Contains("declare double @atan2", ir);
        Assert.Contains("call double @atan2", ir);
    }

    [Fact]
    public void EXPT_ShouldCallPow()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                x : LREAL;
            END_VAR
                x := EXPT(2.0, 10.0);
            END_PROGRAM
            """);

        Assert.Contains("call double @pow", ir);
    }

    [Fact]
    public void ABS_Integer_ShouldInlineWithSelect()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                x : DINT;
            END_VAR
                x := ABS(-5);
            END_PROGRAM
            """);

        // 整数 ABS 内联：icmp slt + sub + select
        Assert.Contains("icmp slt i32", ir);
        Assert.Contains("select i1", ir);
    }

    [Fact]
    public void ABS_NegativeLiteral_ShouldProduceCorrectFlow()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                x : DINT;
                y : DINT;
            END_VAR
                y := -10;
                x := ABS(y);
            END_PROGRAM
            """);

        // 应包含 sub 0, %y（取负）和 select
        Assert.Contains("select i1", ir);
    }
}