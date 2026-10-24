using IronST.LLVMCodeGen.Tests.Helpers;
using Xunit;

namespace IronST.LLVMCodeGen.Tests;

public class VariableAccessCodeGenTests
{
    [Fact]
    public void ArrayAccess1D_ShouldGenerateGEP()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM P
            VAR
                arr : ARRAY[0..9] OF DINT;
                idx : DINT;
                val : DINT;
            END_VAR
                idx := 3;
                val := arr[idx];
            END_PROGRAM
            """);

        Assert.Contains("getelementptr", ir);
    }

    [Fact]
    public void ArrayAccess1D_ConstantIndex_ShouldGEPWithIndex()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM P
            VAR
                arr : ARRAY[0..9] OF DINT;
            END_VAR
                arr[3] := 42;
            END_PROGRAM
            """);

        // 常量索引应直接出现在 GEP 中
        Assert.Contains("i32 3", ir);
        Assert.Contains("store i32 42", ir);
    }

    [Fact]
    public void ArrayAccess2D_ShouldGenerateNestedGEP()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM P
            VAR
                matrix : ARRAY[0..3, 0..5] OF DINT;
            END_VAR
                matrix[1, 2] := 99;
            END_PROGRAM
            """);

        // 2D 数组映射为 [4 x [6 x i32]]
        Assert.Contains("[6 x i32]", ir);
        Assert.Contains("getelementptr", ir);
    }

    [Fact]
    public void StructFieldAccess_ShouldGenerateGEP()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            TYPE Point : STRUCT
                x : DINT;
                y : DINT;
            END_STRUCT; END_TYPE;

            PROGRAM P
            VAR p : Point; END_VAR
                p.x := 10;
                p.y := 20;
            END_PROGRAM
            """);

        Assert.Contains("%Point = type", ir);
        Assert.Contains("store i32 10", ir);
        Assert.Contains("store i32 20", ir);
    }

    [Fact]
    public void NestedStructAccess_ShouldChainGEP()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            TYPE Inner : STRUCT val : DINT; END_STRUCT; END_TYPE;
            TYPE Outer : STRUCT inner : Inner; END_STRUCT; END_TYPE;

            PROGRAM P
            VAR o : Outer; END_VAR
                o.inner.val := 42;
            END_PROGRAM
            """);

        Assert.Contains("%Inner = type", ir);
        Assert.Contains("%Outer = type", ir);
        // 应有两次 getelementptr（一层一个）
        var gepCount = CodeGenTestHelper.CountOccurrences(ir, "getelementptr");
        Assert.True(gepCount >= 2, $"应有至少 2 个 GEP，实际 {gepCount}");
    }

    [Fact]
    public void ArrayAssignment_ShouldStoreToArrayElement()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM P
            VAR
                arr : ARRAY[0..4] OF DINT;
                i : DINT;
            END_VAR
                FOR i := 0 TO 4 BY 1 DO
                    arr[i] := i * 2;
                END_FOR;
            END_PROGRAM
            """);

        Assert.Contains("for.cond", ir);
        Assert.Contains("getelementptr", ir);
        Assert.Contains("mul i32", ir);
    }
}