using IronST.LLVMCodeGen.Tests.Helpers;
using Xunit;

namespace IronST.LLVMCodeGen.Tests;

/// <summary>
/// 综合集成测试：多种功能组合使用。
/// </summary>
public class IntegrationCodeGenTests
{
    [Fact]
    public void NestedMathCalls_ShouldNotCrash()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR x : LREAL; END_VAR
                x := SQRT(EXPT(2.0, 10.0));
            END_PROGRAM
            """
        );

        Assert.Contains("call double @pow", ir);
        Assert.Contains("call double @sqrt", ir);
    }

    [Fact]
    public void MathInIf_ShouldWorkInBothBranches()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR x : DINT; y : LREAL; END_VAR
                x := 1;
                IF x > 0 THEN
                    y := SQRT(4.0);
                ELSE
                    y := SQRT(16.0);
                END_IF;
            END_PROGRAM
            """
        );

        Assert.Contains("declare double @sqrt", ir);
        Assert.Contains("if.then", ir);
        Assert.Contains("if.else", ir);
    }

    [Fact]
    public void StringConcatInLoop_ShouldWork()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR result : STRING; i : DINT; END_VAR
                FOR i := 1 TO 3 BY 1 DO
                    result := CONCAT(result, 'x');
                END_FOR;
            END_PROGRAM
            """
        );

        Assert.Contains("for.cond", ir);
        Assert.Contains("call void @llvm.memcpy", ir);
    }

    [Fact]
    public void MixedBuiltinsInProgram_ShouldCompile()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
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
            """
        );

        Assert.Contains("declare double @sqrt", ir);
        Assert.Contains("declare void @llvm.memcpy", ir);
        Assert.Contains("icmp slt i32", ir);
    }

    [Fact]
    public void TypeConversionChainedWithMath_ShouldWork()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR intVal : DINT; realVal : LREAL; END_VAR
                intVal := 42;
                realVal := SQRT(LREAL(intVal));
            END_PROGRAM
            """
        );

        // DINT → LREAL (sitofp) → sqrt
        Assert.Contains("sitofp i32", ir);
        Assert.Contains("call double @sqrt", ir);
    }

    [Fact]
    public void EnumInFunctionWithGlobal_ShouldCompile()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            TYPE State : (Idle, Running, Stopped); END_TYPE;

            FUNCTION GetActionCode : DINT
            VAR_INPUT s : State; END_VAR
                IF s = State.Running THEN
                    GetActionCode := 1;
                ELSE
                    GetActionCode := 0;
                END_IF;
            END_FUNCTION

            PROGRAM P
            VAR
                gState : State;
                result : DINT;
            END_VAR
                gState := State.Running;
                result := GetActionCode(gState);
            END_PROGRAM
            """
        );

        Assert.Contains("define i32 @GetActionCode(i32 %s)", ir);
        Assert.Contains("icmp eq i32", ir);
        Assert.Contains("store i32 1, ptr %gState", ir);
        Assert.Contains("call i32 @GetActionCode(i32", ir);
    }

    [Fact]
    public void CompleteProgram_WithAllFeatures_ShouldCompile()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            TYPE Color : (Red, Green, Blue); END_TYPE;

            FUNCTION_BLOCK Lamp
            VAR_INPUT
                color : Color;
            END_VAR
            VAR
                isOn : BOOL;
            END_VAR
                isOn := (color <> Color.Red);
            END_FUNCTION_BLOCK

            PROGRAM Main
            VAR
                myLamp : Lamp;
                x : DINT;
                t : TIME;
                y : LREAL;
            END_VAR
                myLamp(color := Color.Green);
                x := ABS(-10);
                y := SIN(1.5708);
                t := T#100ms;
            END_PROGRAM
            """
        );

        // FB 结构体和函数
        Assert.Contains("%Lamp = type", ir);
        Assert.Contains("define void @Lamp(ptr", ir);
        // Program
        Assert.Contains("define void @Main()", ir);
        // 数学函数
        Assert.Contains("call double @sin", ir);
        // 时间字面量 T#100ms = 100
        Assert.Contains("i64 100", ir);
        // ABS 内联（整数取绝对值）
        Assert.Contains("icmp slt i32", ir);
        // 枚举成员常量（Color.Red = 0, Color.Green = 1）
        Assert.Contains("icmp ne i32", ir);
    }
}
