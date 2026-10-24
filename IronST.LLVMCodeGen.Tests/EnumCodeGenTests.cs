using IronST.LLVMCodeGen.Tests.Helpers;
using Xunit;

namespace IronST.LLVMCodeGen.Tests;

public class EnumCodeGenTests
{
    [Fact]
    public void EnumMember_ImplicitValue_ShouldEmitZero()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            TYPE Color : (Red, Green, Blue := 10, Yellow); END_TYPE;

            PROGRAM P
            VAR c : DINT; END_VAR
                c := Color.Red;
            END_PROGRAM
            """);

        // Red = 0（隐式初始值）
        Assert.Contains("store i32 0", ir);
    }

    [Fact]
    public void EnumMember_ExplicitValue_ShouldEmitExplicit()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            TYPE Color : (Red, Green, Blue := 10, Yellow); END_TYPE;

            PROGRAM P
            VAR c : DINT; END_VAR
                c := Color.Blue;
            END_PROGRAM
            """);

        // Blue = 10（显式赋值）
        Assert.Contains("store i32 10", ir);
    }

    [Fact]
    public void EnumMember_AutoIncrement_ShouldEmitCorrectValue()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            TYPE Color : (Red, Green, Blue := 10, Yellow); END_TYPE;

            PROGRAM P
            VAR c : DINT; END_VAR
                c := Color.Yellow;
            END_PROGRAM
            """);

        // Yellow = Blue + 1 = 11
        Assert.Contains("store i32 11", ir);
    }

    [Fact]
    public void EnumMember_InComparison_ShouldGenerateICmp()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            TYPE Color : (Red, Green, Blue := 10); END_TYPE;

            PROGRAM P
            VAR c : Color; r : BOOL; END_VAR
                r := (c = Color.Red);
            END_PROGRAM
            """);

        // 比较枚举变量与常量 0
        Assert.Contains("icmp eq i32", ir);
    }

    [Fact]
    public void EnumMember_FirstMemberImplicit_ShouldBeZero()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            TYPE Status : (Idle, Running, Error); END_TYPE;

            PROGRAM P
            VAR s : Status; END_VAR
                s := Status.Idle;
            END_PROGRAM
            """);

        Assert.Contains("store i32 0", ir);
    }

    // ═══════════════════════════════════════════════════════
    //  CASE + 枚举值标签（B1）
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void Case_EnumLabel_QualifiedName_ShouldEmitMemberValue()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            TYPE Color : (Red, Green, Blue := 10, Yellow); END_TYPE;

            PROGRAM P
            VAR c : Color; r : DINT; END_VAR
                r := 0;
                CASE c OF
                    Color.Red:   r := 100;
                    Color.Blue:  r := 200;
                ELSE
                    r := 0;
                END_CASE
            END_PROGRAM
            """);

        // Red = 0（隐式），Blue = 10（显式）
        Assert.Contains("switch i32", ir);
        Assert.Contains("i32 0, label", ir);
        Assert.Contains("i32 10, label", ir);
    }

    [Fact]
    public void Case_EnumLabel_UnqualifiedName_ShouldEmitMemberValue()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            TYPE State : (Idle, Running, Stopped); END_TYPE;

            PROGRAM P
            VAR s : State; r : DINT; END_VAR
                r := 0;
                CASE s OF
                    Idle:    r := 1;
                    Running: r := 2;
                    Stopped: r := 3;
                END_CASE
            END_PROGRAM
            """);

        // 无前缀枚举名 → 按索引 0/1/2
        Assert.Contains("switch i32", ir);
        Assert.Contains("i32 0, label", ir);
        Assert.Contains("i32 1, label", ir);
        Assert.Contains("i32 2, label", ir);
    }
}