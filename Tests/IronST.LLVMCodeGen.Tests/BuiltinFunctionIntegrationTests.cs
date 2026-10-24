using IronST.LLVMCodeGen.Tests.Helpers;
using Xunit;

namespace IronST.LLVMCodeGen.Tests;

/// <summary>
/// 综合集成测试：多种内置函数组合使用、嵌套调用、与其他语句配合。
/// </summary>
public class BuiltinFunctionIntegrationTests
{
    [Fact]
    public void NestedMathCall_ShouldNotCrash()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                x : LREAL;
            END_VAR
                x := SQRT(EXPT(2.0, 10.0));
            END_PROGRAM
            """);

        // 嵌套调用：先 pow 再 sqrt
        Assert.Contains("call double @pow", ir);
        Assert.Contains("call double @sqrt", ir);
    }

    [Fact]
    public void MathInIf_ShouldGenerateBothBranches()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                x : DINT;
                y : LREAL;
            END_VAR
            IF x > 0 THEN
                y := SQRT(4.0);
            ELSE
                y := SQRT(16.0);
            END_IF;
            END_PROGRAM
            """);

        Assert.Contains("declare double @sqrt", ir);
        // 应有 if.then / if.else 块
        Assert.Contains("if.then", ir);
        Assert.Contains("if.else", ir);
    }

    [Fact]
    public void StringConcatInLoop_ShouldWork()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                result : STRING;
                i : DINT;
            END_VAR
            FOR i := 1 TO 3 BY 1 DO
                result := CONCAT(result, 'x');
            END_FOR;
            END_PROGRAM
            """);

        // 应包含 for 循环结构 + memcpy
        Assert.Contains("for.cond", ir);
        Assert.Contains("call void @llvm.memcpy", ir);
    }

    [Fact]
    public void MixedBuiltinsInProgram_ShouldCompile()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                n : INT;
                x : DINT;
                y : LREAL;
                s1 : STRING;
                s2 : STRING;
                s3 : STRING;
            END_VAR
                n := LEN(s1);
                s3 := CONCAT(s1, s2);
                x := ABS(-42);
                y := SQRT(9.0);
            END_PROGRAM
            """);

        // 全部不崩溃，IR 包含所有调用
        Assert.Contains("declare double @sqrt", ir);
        Assert.Contains("declare void @llvm.memcpy", ir);
        Assert.Contains("icmp slt i32", ir); // ABS 内联
    }

    [Fact]
    public void TypeConversionWithMath_ShouldChainCorrectly()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                intVal : DINT;
                realVal : LREAL;
            END_VAR
                intVal := 42;
                realVal := SQRT(LREAL(intVal));
            END_PROGRAM
            """);

        // DINT → LREAL（sitofp） → sqrt 调用
        Assert.Contains("sitofp i32", ir);
        Assert.Contains("call double @sqrt", ir);
    }
}