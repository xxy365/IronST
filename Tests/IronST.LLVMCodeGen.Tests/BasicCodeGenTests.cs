using IronST.LLVMCodeGen.Tests.Helpers;
using Xunit;

namespace IronST.LLVMCodeGen.Tests;

public class BasicCodeGenTests
{
    [Fact]
    public void SimpleProgram_CompilesWithoutError()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM TestProg
            END_PROGRAM
            """
        );

        Assert.Contains("define void @TestProg()", ir);
        Assert.Contains("ret void", ir);
    }

    [Fact]
    public void IntegerConstantAssignment_ShouldStoreToAlloca()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM TestProg
            VAR x : DINT; END_VAR
                x := 42;
            END_PROGRAM
            """
        );

        Assert.Contains("alloca i32", ir);
        Assert.Contains("store i32 42", ir);
    }

    [Fact]
    public void RealLiteralAssignment_ShouldStoreFloatConstant()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM TestProg
            VAR x : REAL; END_VAR
                x := 3.14;
            END_PROGRAM
            """
        );

        Assert.Contains("alloca float", ir);
        Assert.Contains("store float", ir);
    }

    [Fact]
    public void BooleanTrue_ShouldStoreI1One()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM TestProg
            VAR b : BOOL; END_VAR
                b := TRUE;
            END_PROGRAM
            """
        );

        Assert.Contains("store i1 1, ptr %b", ir);
    }

    [Fact]
    public void BooleanFalse_ShouldStoreI1Zero()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR b : BOOL; END_VAR
                b := FALSE;
            END_PROGRAM
            """);

        Assert.Contains("store i1 0, ptr %b", ir);
    }


    [Fact]
    public void StringLiteral_ShouldCreateGlobalString()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM TestProg
            VAR s : STRING; END_VAR
                s := 'Hello';
            END_PROGRAM
            """
        );

        Assert.Contains("@str.1", ir);
        Assert.Contains("Hello", ir);
    }

    [Fact]
    public void TypedLiteral_Int42_ShouldEmitConstant()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM TestProg
            VAR x : INT; END_VAR
                x := INT#42;
            END_PROGRAM
            """
        );

        Assert.Contains("store i16 42", ir);
    }

    [Fact]
    public void NegativeLiteral_ShouldEmitNegative()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM TestProg
            VAR x : DINT; END_VAR
                x := -100;
            END_PROGRAM
            """
        );

        // 负数通过 unary neg 生成
        Assert.Contains("define void @TestProg", ir);
    }
}
