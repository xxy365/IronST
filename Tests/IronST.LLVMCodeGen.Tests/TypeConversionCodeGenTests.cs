using IronST.LLVMCodeGen.Tests.Helpers;
using Xunit;

namespace IronST.LLVMCodeGen.Tests;

/// <summary>
/// 类型转换内置函数 → IR 测试。
/// </summary>
public class TypeConversionCodeGenTests
{
    [Fact]
    public void INT_FromReal_ShouldGenerateFPToSI()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                x : REAL;
                y : INT;
            END_VAR
                x := 3.14;
                y := INT(x);
            END_PROGRAM
            """);

        Assert.Contains("fptosi", ir);
    }

    [Fact]
    public void REAL_FromInt_ShouldGenerateSIToFP()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                x : INT;
                y : REAL;
            END_VAR
                x := 42;
                y := REAL(x);
            END_PROGRAM
            """);

        Assert.Contains("sitofp", ir);
    }

    [Fact]
    public void DINT_FromINT_ShouldGenerateSExt()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                x : INT;
                y : DINT;
            END_VAR
                x := 100;
                y := DINT(x);
            END_PROGRAM
            """);

        // INT(16bit) → DINT(32bit) = sext
        Assert.Contains("sext i16", ir);
    }

    [Fact]
    public void INT_FromDINT_ShouldGenerateTrunc()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                x : DINT;
                y : INT;
            END_VAR
                x := 1000;
                y := INT(x);
            END_PROGRAM
            """);

        // DINT(32bit) → INT(16bit) = trunc
        Assert.Contains("trunc i32", ir);
    }

    [Fact]
    public void LREAL_FromREAL_ShouldGenerateFPExt()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                x : REAL;
                y : LREAL;
            END_VAR
                x := 1.5;
                y := LREAL(x);
            END_PROGRAM
            """);

        // float → double = fpext
        Assert.Contains("fpext float", ir);
    }

    [Fact]
    public void REAL_FromLREAL_ShouldGenerateFPTrunc()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                x : LREAL;
                y : REAL;
            END_VAR
                x := 1.5;
                y := REAL(x);
            END_PROGRAM
            """);

        // double → float = fptrunc
        Assert.Contains("fptrunc double", ir);
    }

    [Fact]
    public void BOOL_FromInt_NonZero_ShouldCompare()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                x : DINT;
                b : BOOL;
            END_VAR
                x := 42;
                b := BOOL(x);
            END_PROGRAM
            """);

        // BOOL(int) = icmp ne ..., 0
        Assert.Contains("icmp ne i32", ir);
    }
}