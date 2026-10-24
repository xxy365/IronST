// VariableInitializerTests.cs — F3：显式初始值的前端语义
// （缺陷登记簿 §3.1 · 增量编译契约 §7 L1）
//
// F3 的根因在**前端边界**：AST 解析出了 `InitialValue`，但语义层没有字段承载它。
// 修复后语义层必须：
//   1. 对初值表达式做类型推导（IR 层靠 ResolvedType 决定发什么指令）；
//   2. 对全局变量的初值做**编译期常量求值**（IEC 的硬性要求），
//      非常量 ⇒ E2017 错误；是常量但类型暂不支持 ⇒ 警告。
using IronST.Common.Diagnostics;
using IronST.Frontend.Analysis;

namespace IronST.Frontend.Tests;

public class VariableInitializerTests
{
    // ═══════════════════════════════════════════════════════════════
    //  全局初值：常量要求
    // ═══════════════════════════════════════════════════════════════

    /// <summary>全局初值是编译期常量 ⇒ 通过。</summary>
    [Fact]
    public void GlobalInitializer_Constant_IsAccepted()
    {
        var result = FrontendTestHelper.ExpectSuccess("""
            PROGRAM Main
            VAR_GLOBAL
                g : DINT := 42;
            END_VAR
            END_PROGRAM
            """);

        var info = Assert.Single(result.Variables, v => v.Name == "g");
        Assert.NotNull(info.InitialValue);
        Assert.False(info.InitialValue!.IsReal);
        Assert.Equal(42, info.InitialValue.IntValue);
    }

    /// <summary>全局初值引用了其它变量 ⇒ E2017（不是常量）。</summary>
    [Fact]
    public void GlobalInitializer_NonConstant_ReportsE2017()
    {
        var result = FrontendTestHelper.ExpectErrors("""
            PROGRAM Main
            VAR_GLOBAL
                g : DINT := h;
            END_VAR
            VAR
                h : DINT;
            END_VAR
            END_PROGRAM
            """);

        Assert.Contains(result.Diagnostics, d => d.Code == ErrorCode.E2017);
    }

    /// <summary>
    /// 全局初值是字符串常量 ⇒ 是常量，但目标类型暂不支持 ⇒ **警告**而非错误。
    /// </summary>
    [Fact]
    public void GlobalInitializer_UnsupportedType_ReportsWarningNotError()
    {
        var result = FrontendTestHelper.ExpectSuccess("""
            PROGRAM Main
            VAR_GLOBAL
                s : STRING := 'hello';
            END_VAR
            END_PROGRAM
            """);

        Assert.Contains(
            result.Diagnostics,
            d => d.Code == ErrorCode.E2017 && d.Severity == DiagnosticSeverity.Warning);
    }

    // ═══════════════════════════════════════════════════════════════
    //  初值的类型推导
    // ═══════════════════════════════════════════════════════════════

    /// <summary>实数全局初值落在实数域。</summary>
    [Fact]
    public void GlobalInitializer_Real_StaysInRealDomain()
    {
        var result = FrontendTestHelper.ExpectSuccess("""
            PROGRAM Main
            VAR_GLOBAL
                g : REAL := 2.5;
            END_VAR
            END_PROGRAM
            """);

        var info = Assert.Single(result.Variables, v => v.Name == "g");
        Assert.NotNull(info.InitialValue);
        Assert.True(info.InitialValue!.IsReal);
        Assert.Equal(2.5, info.InitialValue.RealValue, 6);
    }

    /// <summary>一元负号参与常量求值。</summary>
    [Fact]
    public void GlobalInitializer_NegativeLiteral_IsEvaluated()
    {
        var result = FrontendTestHelper.ExpectSuccess("""
            PROGRAM Main
            VAR_GLOBAL
                g : DINT := -7;
            END_VAR
            END_PROGRAM
            """);

        var info = Assert.Single(result.Variables, v => v.Name == "g");
        Assert.NotNull(info.InitialValue);
        Assert.Equal(-7, info.InitialValue.IntValue);
    }

    /// <summary>布尔初值按 1 / 0 归一。</summary>
    [Fact]
    public void GlobalInitializer_Boolean_IsNormalizedToInt()
    {
        var result = FrontendTestHelper.ExpectSuccess("""
            PROGRAM Main
            VAR_GLOBAL
                g : BOOL := TRUE;
            END_VAR
            END_PROGRAM
            """);

        var info = Assert.Single(result.Variables, v => v.Name == "g");
        Assert.NotNull(info.InitialValue);
        Assert.Equal(1, info.InitialValue.IntValue);
    }

    /// <summary>实数初值赋给整数全局 ⇒ 向零取整（与 §3.9 的 FPToSI 语义一致）。</summary>
    [Fact]
    public void GlobalInitializer_RealToInt_TruncatesTowardZero()
    {
        var result = FrontendTestHelper.ExpectSuccess("""
            PROGRAM Main
            VAR_GLOBAL
                g : DINT := 2.9;
            END_VAR
            END_PROGRAM
            """);

        var info = Assert.Single(result.Variables, v => v.Name == "g");
        Assert.NotNull(info.InitialValue);
        Assert.False(info.InitialValue!.IsReal);
        Assert.Equal(2, info.InitialValue.IntValue);
    }

    /// <summary>
    /// 类型化字面量初值（<c>DINT#5</c> / <c>REAL#2.5</c>）也是编译期常量。
    /// </summary>
    [Fact]
    public void GlobalInitializer_TypedLiteral_IsEvaluated()
    {
        var result = FrontendTestHelper.ExpectSuccess("""
            PROGRAM Main
            VAR_GLOBAL
                g : DINT := DINT#5;
                r : REAL := REAL#2.5;
            END_VAR
            END_PROGRAM
            """);

        var g = Assert.Single(result.Variables, v => v.Name == "g");
        Assert.NotNull(g.InitialValue);
        Assert.False(g.InitialValue!.IsReal);
        Assert.Equal(5, g.InitialValue.IntValue);

        var r = Assert.Single(result.Variables, v => v.Name == "r");
        Assert.NotNull(r.InitialValue);
        Assert.True(r.InitialValue!.IsReal);
        Assert.Equal(2.5, r.InitialValue.RealValue, 6);
    }

    // ═══════════════════════════════════════════════════════════════
    //  局部初值：允许非常量表达式（每次调用重新求值）
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 局部初值可以是**非常量**表达式（引用另一个变量）—— 不报 E2017。
    /// 全局才受常量要求约束。
    /// </summary>
    [Fact]
    public void LocalInitializer_NonConstant_IsAccepted()
    {
        var result = FrontendTestHelper.ExpectSuccess("""
            PROGRAM Main
            VAR
                a : DINT := 1;
                b : DINT := a;
            END_VAR
            END_PROGRAM
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == ErrorCode.E2017);
    }

    /// <summary>局部初值引用了未声明的标识符 ⇒ E2001（前端就该拦下）。</summary>
    [Fact]
    public void LocalInitializer_UnknownIdentifier_ReportsE2001()
    {
        var result = FrontendTestHelper.ExpectErrors("""
            PROGRAM Main
            VAR
                b : DINT := nope;
            END_VAR
            END_PROGRAM
            """);

        Assert.Contains(result.Diagnostics, d => d.Code == ErrorCode.E2001);
    }

    /// <summary>
    /// 局部初值引用**后声明**的变量 ⇒ E2001。
    /// </summary>
    /// <remarks>
    /// 前端按**声明序**解析符号：<c>b</c> 的初值在 <c>a</c> 声明之前被分析，
    /// 彼时 <c>a</c> 还不存在 ⇒ 报未声明。
    /// （这是前端的既定行为，不是 F3 的缺口 —— F3 只管「初值不丢」。）
    /// </remarks>
    [Fact]
    public void LocalInitializer_ForwardReference_ReportsE2001()
    {
        var result = FrontendTestHelper.ExpectErrors("""
            PROGRAM Main
            VAR
                b : DINT := a;
                a : DINT := 5;
            END_VAR
            END_PROGRAM
            """);

        Assert.Contains(result.Diagnostics, d => d.Code == ErrorCode.E2001);
    }
}
