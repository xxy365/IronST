using IronST.LLVMCodeGen.Tests.Helpers;
using Xunit;

namespace IronST.LLVMCodeGen.Tests;

public class BuiltinTypeConversionCodeGenTests
{
    [Fact]
    public void Int_FromReal_ShouldGenerateFPToSI()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM P
            VAR x : REAL; y : INT; END_VAR
                x := 3.14;
                y := INT(x);
            END_PROGRAM
            """);

        Assert.Contains("fptosi", ir);
    }

    [Fact]
    public void Real_FromInt_ShouldGenerateSIToFP()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM P
            VAR x : DINT; y : REAL; END_VAR
                x := 42;
                y := REAL(x);
            END_PROGRAM
            """);

        Assert.Contains("sitofp i32", ir);
    }

    [Fact]
    public void Dint_FromInt_ShouldGenerateSExt()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM P
            VAR x : INT; y : DINT; END_VAR
                x := 100;
                y := DINT(x);
            END_PROGRAM
            """);

        // INT(16bit) → DINT(32bit) = sext
        Assert.Contains("sext i16", ir);
    }

    [Fact]
    public void Int_FromDint_ShouldGenerateTrunc()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM P
            VAR x : DINT; y : INT; END_VAR
                x := 1000;
                y := INT(x);
            END_PROGRAM
            """);

        // DINT(32bit) → INT(16bit) = trunc
        Assert.Contains("trunc i32", ir);
    }

    [Fact]
    public void Lreal_FromReal_ShouldGenerateFPExt()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM P
            VAR x : REAL; y : LREAL; END_VAR
                x := 1.5;
                y := LREAL(x);
            END_PROGRAM
            """);

        Assert.Contains("fpext float", ir);
    }

    [Fact]
    public void Real_FromLreal_ShouldGenerateFPTrunc()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM P
            VAR x : LREAL; y : REAL; END_VAR
                x := 1.5;
                y := REAL(x);
            END_PROGRAM
            """);

        Assert.Contains("fptrunc double", ir);
    }

    [Fact]
    public void Bool_FromInt_ShouldGenerateComparison()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM P
            VAR x : DINT; b : BOOL; END_VAR
                x := 42;
                b := BOOL(x);
            END_PROGRAM
            """);

        Assert.Contains("icmp ne i32", ir);
    }

    [Fact]
    public void Sint_FromDint_ShouldGenerateTrunc()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM P
            VAR x : DINT; y : SINT; END_VAR
                x := 42;
                y := SINT(x);
            END_PROGRAM
            """);

        // DINT(32bit) → SINT(8bit) = trunc
        Assert.Contains("trunc i32", ir);
    }
}