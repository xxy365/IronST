// CallArityTests.cs — 函数调用实参数量/命名绑定校验（CALL-ARITY）
//
// 裁定（2026-10-16，针对本项目调用契约）：函数调用实参个数与形参数量不一致 ⇒ 应当编译失败。
// 诊断统一沿用既有 ErrorCode.E2004「参数数量不匹配」，不另造编号。
//
// 边界：本切片只覆盖**用户定义函数**（FunctionSymbol）。FB 实例调用
// （`myFb(...)`）保留原有容忍（FB 输入可部分赋值属另一待裁定项，见 §6.3.5），
// 不在本切片内改动。

using IronST.Common.Diagnostics;

namespace IronST.Frontend.Tests;

public class CallArityTests
{
    // ═══════════════════════════════════════════════════════════════
    //  位置实参：个数不符
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void StatementCall_MissingTrailingArg_ReportsE2004()
    {
        var result = FrontendTestHelper.ExpectErrors(
            """
            FUNCTION F : INT
            VAR_INPUT a : INT; b : INT; END_VAR
                F := a + b;
            END_FUNCTION

            PROGRAM Main
            VAR r : INT; END_VAR
                F(1);
                r := 0;
            END_PROGRAM
            """
        );

        Assert.Contains(result.Diagnostics, d => d.Code == ErrorCode.E2004);
    }

    [Fact]
    public void ExpressionCall_MissingTrailingArg_ReportsE2004()
    {
        var result = FrontendTestHelper.ExpectErrors(
            """
            FUNCTION F : INT
            VAR_INPUT a : INT; b : INT; END_VAR
                F := a + b;
            END_FUNCTION

            PROGRAM Main
            VAR r : INT; END_VAR
                r := F(1);
            END_PROGRAM
            """
        );

        Assert.Contains(result.Diagnostics, d => d.Code == ErrorCode.E2004);
    }

    [Fact]
    public void TooManyArgs_ReportsE2004()
    {
        var result = FrontendTestHelper.ExpectErrors(
            """
            FUNCTION F : INT
            VAR_INPUT a : INT; END_VAR
                F := a;
            END_FUNCTION

            PROGRAM Main
            VAR r : INT; END_VAR
                r := F(1, 2, 3);
            END_PROGRAM
            """
        );

        Assert.Contains(result.Diagnostics, d => d.Code == ErrorCode.E2004);
    }

    [Fact]
    public void MixedDirection_InputThenInOut_MissingInOutArg_ReportsE2004()
    {
        // 探针 A 的形状：VAR_INPUT + VAR_IN_OUT，单实参调用。
        var result = FrontendTestHelper.ExpectErrors(
            """
            FUNCTION Bump : DINT
            VAR_INPUT n : DINT; END_VAR
            VAR_IN_OUT x : DINT; END_VAR
                x := x + 100;
                Bump := x;
            END_FUNCTION

            PROGRAM Main
            VAR v : DINT; END_VAR
                Bump(v);
            END_PROGRAM
            """
        );

        Assert.Contains(result.Diagnostics, d => d.Code == ErrorCode.E2004);
    }

    [Fact]
    public void ZeroArgCall_ToZeroParamFunction_Compiles()
    {
        FrontendTestHelper.ExpectSuccess(
            """
            FUNCTION F : INT
                F := 7;
            END_FUNCTION

            PROGRAM Main
            VAR r : INT; END_VAR
                r := F();
            END_PROGRAM
            """
        );
    }

    [Fact]
    public void ExactPositionalArgs_Compiles()
    {
        FrontendTestHelper.ExpectSuccess(
            """
            FUNCTION Add : INT
            VAR_INPUT a : INT; b : INT; END_VAR
                Add := a + b;
            END_FUNCTION

            PROGRAM Main
            VAR r : INT; END_VAR
                r := Add(1, 2);
            END_PROGRAM
            """
        );
    }

    [Fact]
    public void SingleInOutParam_ExactArg_Compiles()
    {
        // 探针 B：首形参即引用，个数齐全 ⇒ 必须仍成功。
        FrontendTestHelper.ExpectSuccess(
            """
            FUNCTION Inc : DINT
            VAR_IN_OUT x : DINT; END_VAR
                x := x + 1;
                Inc := x;
            END_FUNCTION

            PROGRAM Main
            VAR v : DINT; END_VAR
                Inc(v);
            END_PROGRAM
            """
        );
    }

    // ═══════════════════════════════════════════════════════════════
    //  命名实参
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void NamedAllParams_Compiles()
    {
        FrontendTestHelper.ExpectSuccess(
            """
            FUNCTION Bump : DINT
            VAR_INPUT n : DINT; END_VAR
            VAR_IN_OUT x : DINT; END_VAR
                x := x + n;
                Bump := x;
            END_FUNCTION

            PROGRAM Main
            VAR v : DINT; END_VAR
                Bump(n := 100, x := v);
            END_PROGRAM
            """
        );
    }

    [Fact]
    public void NamedMissingParam_ReportsE2004()
    {
        // 探针 G 的形状：只给非首位形参命名实参 ⇒ 缺参，且不得触发编译器崩溃。
        var result = FrontendTestHelper.ExpectErrors(
            """
            FUNCTION Bump : DINT
            VAR_INPUT n : DINT; END_VAR
            VAR_IN_OUT x : DINT; END_VAR
                x := x + 100;
                Bump := x;
            END_FUNCTION

            PROGRAM Main
            VAR v : DINT; END_VAR
                Bump(x := v);
            END_PROGRAM
            """
        );

        Assert.Contains(result.Diagnostics, d => d.Code == ErrorCode.E2004);
    }

    [Fact]
    public void NamedDuplicateParam_ReportsE2004()
    {
        var result = FrontendTestHelper.ExpectErrors(
            """
            FUNCTION F : INT
            VAR_INPUT a : INT; END_VAR
                F := a;
            END_FUNCTION

            PROGRAM Main
            VAR r : INT; x : INT; END_VAR
                r := F(a := 1, a := 2);
            END_PROGRAM
            """
        );

        Assert.Contains(result.Diagnostics, d => d.Code == ErrorCode.E2004);
    }

    [Fact]
    public void NamedUnknownParam_ReportsE2004()
    {
        var result = FrontendTestHelper.ExpectErrors(
            """
            FUNCTION F : INT
            VAR_INPUT a : INT; END_VAR
                F := a;
            END_FUNCTION

            PROGRAM Main
            VAR r : INT; END_VAR
                r := F(z := 1);
            END_PROGRAM
            """
        );

        Assert.Contains(result.Diagnostics, d => d.Code == ErrorCode.E2004);
    }

    [Fact]
    public void MixedPositionalAndNamed_ReportsE2004()
    {
        var result = FrontendTestHelper.ExpectErrors(
            """
            FUNCTION F : INT
            VAR_INPUT a : INT; b : INT; END_VAR
                F := a + b;
            END_FUNCTION

            PROGRAM Main
            VAR r : INT; END_VAR
                r := F(1, b := 2);
            END_PROGRAM
            """
        );

        Assert.Contains(result.Diagnostics, d => d.Code == ErrorCode.E2004);
    }

    // ═══════════════════════════════════════════════════════════════
    //  诊断内容：指出函数名 / 期望数 / 实际数
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void MissingArg_DiagnosticNamesFunctionAndCounts()
    {
        var result = FrontendTestHelper.ExpectErrors(
            """
            FUNCTION F : INT
            VAR_INPUT a : INT; b : INT; END_VAR
                F := a + b;
            END_FUNCTION

            PROGRAM Main
            VAR r : INT; END_VAR
                r := F(1);
            END_PROGRAM
            """
        );

        var diag = Assert.Single(result.Diagnostics, d => d.Code == ErrorCode.E2004);
        Assert.Contains("F", diag.Message);
        Assert.Contains("2", diag.Message);
        Assert.Contains("1", diag.Message);
    }

    // ═══════════════════════════════════════════════════════════════
    //  内置函数不受影响
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BuiltinCall_NotAffectedByArityCheck()
    {
        FrontendTestHelper.ExpectSuccess(
            """
            PROGRAM Main
            VAR r : REAL; END_VAR
                r := SQRT(4.0);
            END_PROGRAM
            """
        );
    }
}
