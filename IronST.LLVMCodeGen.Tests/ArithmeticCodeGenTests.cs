using IronST.LLVMCodeGen.Tests.Helpers;
using Xunit;

namespace IronST.LLVMCodeGen.Tests;

public class ArithmeticCodeGenTests
{
    // ═══ 整数运算 ═══

    [Fact]
    public void IntegerAdd_ShouldGenerateAdd()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR a : DINT; b : DINT; c : DINT; END_VAR
                a := 10; b := 20; c := a + b;
            END_PROGRAM
            """
        );

        Assert.Contains("add i32", ir);
    }

    [Fact]
    public void IntegerSub_ShouldGenerateSub()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR a : DINT; b : DINT; c : DINT; END_VAR
                a := 30; b := 10; c := a - b;
            END_PROGRAM
            """
        );

        Assert.Contains("sub i32", ir);
    }

    [Fact]
    public void IntegerMul_ShouldGenerateMul()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR a : DINT; b : DINT; c : DINT; END_VAR
                a := 3; b := 7; c := a * b;
            END_PROGRAM
            """
        );

        Assert.Contains("mul i32", ir);
    }

    [Fact]
    public void IntegerDiv_ShouldGenerateSDiv()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR a : DINT; b : DINT; c : DINT; END_VAR
                a := 100; b := 3; c := a / b;
            END_PROGRAM
            """
        );

        Assert.Contains("sdiv i32", ir);
    }

    [Fact]
    public void IntegerMod_ShouldGenerateSRem()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR a : DINT; b : DINT; c : DINT; END_VAR
                a := 17; b := 5; c := a MOD b;
            END_PROGRAM
            """
        );

        Assert.Contains("srem i32", ir);
    }

    [Fact]
    public void IntegerAnd_ShouldGenerateAnd()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR a : DINT; b : DINT; c : DINT; END_VAR
                a := 15; b := 9; c := a AND b;
            END_PROGRAM
            """
        );

        Assert.Contains("and i32", ir);
    }

    [Fact]
    public void IntegerOr_ShouldGenerateOr()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR a : DINT; b : DINT; c : DINT; END_VAR
                a := 5; b := 3; c := a OR b;
            END_PROGRAM
            """
        );

        Assert.Contains("or i32", ir);
    }

    [Fact]
    public void IntegerXor_ShouldGenerateXor()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR a : DINT; b : DINT; c : DINT; END_VAR
                a := 15; b := 9; c := a XOR b;
            END_PROGRAM
            """
        );

        Assert.Contains("xor i32", ir);
    }

    // ═══ 浮点运算 ═══

    [Fact]
    public void FloatAdd_ShouldGenerateFAdd()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR a : REAL; b : REAL; c : REAL; END_VAR
                a := 1.5; b := 2.5; c := a + b;
            END_PROGRAM
            """
        );

        Assert.Contains("fadd float", ir);
    }

    [Fact]
    public void FloatSub_ShouldGenerateFSub()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR a : REAL; b : REAL; c : REAL; END_VAR
                a := 5.0; b := 1.5; c := a - b;
            END_PROGRAM
            """
        );

        Assert.Contains("fsub float", ir);
    }

    [Fact]
    public void FloatMul_ShouldGenerateFMul()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR a : REAL; b : REAL; c : REAL; END_VAR
                a := 3.0; b := 2.0; c := a * b;
            END_PROGRAM
            """
        );

        Assert.Contains("fmul float", ir);
    }

    [Fact]
    public void FloatDiv_ShouldGenerateFDiv()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR a : LREAL; b : LREAL; c : LREAL; END_VAR
                a := 10.0; b := 3.0; c := a / b;
            END_PROGRAM
            """
        );

        Assert.Contains("fdiv double", ir);
    }

    [Fact]
    public void FloatMod_ShouldGenerateFRem()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR a : LREAL; b : LREAL; c : LREAL; END_VAR
                a := 10.0; b := 3.0; c := a MOD b;
            END_PROGRAM
            """
        );

        Assert.Contains("frem double", ir);
    }

    [Fact]
    public void Power_ShouldCallPow()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR a : LREAL; b : LREAL; c : LREAL; END_VAR
                a := 2.0; b := 10.0; c := a ** b;
            END_PROGRAM
            """
        );

        Assert.Contains("declare double @pow", ir);
        Assert.Contains("call double @pow", ir);
    }

    // ═══ 比较运算 ═══

    [Fact]
    public void IntegerEq_ShouldGenerateICmpEq()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR a : DINT; b : DINT; c : BOOL; END_VAR
                a := 1; b := 1; c := (a = b);
            END_PROGRAM
            """
        );

        Assert.Contains("icmp eq i32", ir);
    }

    [Fact]
    public void IntegerNeq_ShouldGenerateICmpNe()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR a : DINT; b : DINT; c : BOOL; END_VAR
                a := 1; b := 2; c := (a <> b);
            END_PROGRAM
            """
        );

        Assert.Contains("icmp ne i32", ir);
    }

    [Fact]
    public void IntegerLt_ShouldGenerateICmpSlt()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR a : DINT; b : DINT; c : BOOL; END_VAR
                a := 1; b := 2; c := (a < b);
            END_PROGRAM
            """
        );

        Assert.Contains("icmp slt i32", ir);
    }

    [Fact]
    public void IntegerGt_ShouldGenerateICmpSgt()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR a : DINT; b : DINT; c : BOOL; END_VAR
                a := 3; b := 2; c := (a > b);
            END_PROGRAM
            """
        );

        Assert.Contains("icmp sgt i32", ir);
    }

    [Fact]
    public void IntegerLte_ShouldGenerateICmpSle()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR a : DINT; b : DINT; c : BOOL; END_VAR
                a := 2; b := 2; c := (a <= b);
            END_PROGRAM
            """
        );

        Assert.Contains("icmp sle i32", ir);
    }

    [Fact]
    public void IntegerGte_ShouldGenerateICmpSge()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR a : DINT; b : DINT; c : BOOL; END_VAR
                a := 3; b := 2; c := (a >= b);
            END_PROGRAM
            """
        );

        Assert.Contains("icmp sge i32", ir);
    }

    [Fact]
    public void FloatLt_ShouldGenerateFCmpOlt()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR a : REAL; b : REAL; c : BOOL; END_VAR
                a := 1.0; b := 2.0; c := (a < b);
            END_PROGRAM
            """
        );

        Assert.Contains("fcmp olt float", ir);
    }

    [Fact]
    public void FloatGt_ShouldGenerateFCmpOgt()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR a : REAL; b : REAL; c : BOOL; END_VAR
                a := 3.0; b := 2.0; c := (a > b);
            END_PROGRAM
            """
        );

        Assert.Contains("fcmp ogt float", ir);
    }

    // ═══ 一元运算 ═══

    [Fact]
    public void UnaryNegInteger_ShouldGenerateSub()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR a : DINT; b : DINT; END_VAR
                a := 42;
                b := -a;
            END_PROGRAM
            """
        );

        Assert.Contains("sub i32 0", ir);
    }

    [Fact]
    public void UnaryNegFloat_ShouldGenerateFSub()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR a : REAL; b : REAL; END_VAR
                a := 3.14;
                b := -a;
            END_PROGRAM
            """
        );

        Assert.Contains("fsub float", ir);
    }

    [Fact]
    public void UnaryNot_ShouldGenerateXor()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM P
            VAR a : BOOL; b : BOOL; END_VAR
                a := TRUE;
                b := NOT a;
            END_PROGRAM
            """);

        // NOT a → xor i1 1, %a.ld → store to %b
        Assert.Contains("xor i1 1", ir);
        Assert.Contains("store i1", ir);
    }

}
