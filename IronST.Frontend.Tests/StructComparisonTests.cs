// StructComparisonTests.cs — A1/F5：结构体按值比较的前端准入规则
//
// 首版（Option A）只接受：
//   • 两侧是同一个命名结构体类型
//   • 操作符只有 = / <>
//   • 两侧都是可寻址的左值（变量、字段、数组元素）
//   • 字段可递归展开为 BOOL / 整数 / REAL / 枚举 / 嵌套结构体 / 数组
// 明确拒绝：
//   • < > <= >=、结构体与非结构体、不同类型
//   • REFERENCE / STRING / WSTRING 字段（含嵌套、数组元素）
//   • 顶层数组整体比较
//   • A8 修复后全局/外部聚合同样可比较（后端按全局槽逐字段降级）
using IronST.Common.Diagnostics;
using IronST.Frontend.Analysis;

namespace IronST.Frontend.Tests;

public class StructComparisonTests
{
    private const string FullTypePrelude = """
        TYPE Color : (Red, Green, Blue); END_TYPE;
        TYPE Inner : STRUCT i : DINT; r : REAL; f : BOOL; c : Color; END_STRUCT; END_TYPE;
        TYPE P : STRUCT
            a : DINT;
            inner : Inner;
            arr : ARRAY[0..2] OF INT;
            m : ARRAY[0..1, 0..1] OF DINT;
        END_STRUCT; END_TYPE;
        """;

    private static List<Diagnostic> E2003s(AnalysisResult result) =>
        result.Diagnostics.Where(d => d.Code == ErrorCode.E2003).ToList();

    // ── 接受的写法 ────────────────────────────────────────────────

    [Fact]
    public void SameStruct_EqNe_AndNestedOrArrayElementOperands_AreAccepted()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            FullTypePrelude
                + """

                PROGRAM Main
                VAR
                    x : P;
                    y : P;
                    xs : ARRAY[0..1] OF P;
                    ok : BOOL;
                END_VAR
                    ok := x = y;
                    ok := x <> y;
                    ok := x.inner = y.inner;
                    ok := xs[0] = xs[1];
                END_PROGRAM
                """
        );

        Assert.Empty(E2003s(result));
    }

    [Fact]
    public void NumericBoolEnumRealFields_AreAccepted()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            """
            TYPE Color : (Red, Green, Blue); END_TYPE;
            TYPE P : STRUCT
                b : BOOL;
                i : INT;
                d : DINT;
                l : LINT;
                r : REAL;
                lr : LREAL;
                c : Color;
            END_STRUCT; END_TYPE;

            PROGRAM Main
            VAR x : P; y : P; ok : BOOL; END_VAR
                ok := x = y;
                ok := x <> y;
            END_PROGRAM
            """
        );

        Assert.Empty(E2003s(result));
    }

    // ── 操作符 ────────────────────────────────────────────────────

    [Theory]
    [InlineData("<")]
    [InlineData(">")]
    [InlineData("<=")]
    [InlineData(">=")]
    public void OtherComparisonOperators_ReportE2003(string op)
    {
        var result = FrontendTestHelper.ExpectErrors(
            """
            TYPE P : STRUCT v : DINT; END_STRUCT; END_TYPE;
            PROGRAM Main
            VAR x : P; y : P; ok : BOOL; END_VAR
                ok := x OP y;
            END_PROGRAM
            """.Replace("OP", op),
            expectedErrorCount: 1
        );

        var err = Assert.Single(E2003s(result));
        Assert.Contains("只支持 = 和 <>", err.Message);
    }

    // ── 类型匹配 ──────────────────────────────────────────────────

    [Fact]
    public void DifferentStructTypes_ReportE2003()
    {
        var result = FrontendTestHelper.ExpectErrors(
            """
            TYPE A : STRUCT v : DINT; END_STRUCT; END_TYPE;
            TYPE B : STRUCT v : DINT; END_STRUCT; END_TYPE;
            PROGRAM Main
            VAR a : A; b : B; ok : BOOL; END_VAR
                ok := a = b;
            END_PROGRAM
            """,
            expectedErrorCount: 1
        );

        var err = Assert.Single(E2003s(result));
        Assert.Contains("类型相同", err.Message);
    }

    [Fact]
    public void StructVersusScalar_ReportE2003()
    {
        var result = FrontendTestHelper.ExpectErrors(
            """
            TYPE P : STRUCT v : DINT; END_STRUCT; END_TYPE;
            PROGRAM Main
            VAR x : P; ok : BOOL; END_VAR
                ok := x = 1;
            END_PROGRAM
            """,
            expectedErrorCount: 1
        );

        var err = Assert.Single(E2003s(result));
        Assert.Contains("不能与非结构体", err.Message);
    }

    // ── 不支持的字段类型 ──────────────────────────────────────────

    [Fact]
    public void ReferenceField_ReportsE2003()
    {
        var result = FrontendTestHelper.ExpectErrors(
            """
            TYPE T : STRUCT v : DINT; END_STRUCT; END_TYPE;
            TYPE P : STRUCT v : DINT; p : REFERENCE TO T; END_STRUCT; END_TYPE;
            PROGRAM Main
            VAR x : P; y : P; ok : BOOL; END_VAR
                ok := x = y;
            END_PROGRAM
            """,
            expectedErrorCount: 1
        );

        var err = Assert.Single(E2003s(result));
        Assert.Contains("REFERENCE", err.Message);
        Assert.Contains("p", err.Message);
    }

    [Fact]
    public void StringField_ReportsE2003()
    {
        var result = FrontendTestHelper.ExpectErrors(
            """
            TYPE P : STRUCT s : STRING; END_STRUCT; END_TYPE;
            PROGRAM Main
            VAR x : P; y : P; ok : BOOL; END_VAR
                ok := x = y;
            END_PROGRAM
            """,
            expectedErrorCount: 1
        );

        var err = Assert.Single(E2003s(result));
        Assert.Contains("STRING", err.Message);
    }

    [Fact]
    public void WStringField_ReportsE2003()
    {
        var result = FrontendTestHelper.ExpectErrors(
            """
            TYPE P : STRUCT w : WSTRING; END_STRUCT; END_TYPE;
            PROGRAM Main
            VAR x : P; y : P; ok : BOOL; END_VAR
                ok := x = y;
            END_PROGRAM
            """,
            expectedErrorCount: 1
        );

        var err = Assert.Single(E2003s(result));
        Assert.Contains("WSTRING", err.Message);
    }

    [Fact]
    public void NestedReferenceField_ReportsE2003WithPath()
    {
        var result = FrontendTestHelper.ExpectErrors(
            """
            TYPE T : STRUCT v : DINT; END_STRUCT; END_TYPE;
            TYPE Inner : STRUCT p : REFERENCE TO T; END_STRUCT; END_TYPE;
            TYPE Outer : STRUCT inner : Inner; END_STRUCT; END_TYPE;
            PROGRAM Main
            VAR x : Outer; y : Outer; ok : BOOL; END_VAR
                ok := x = y;
            END_PROGRAM
            """,
            expectedErrorCount: 1
        );

        var err = Assert.Single(E2003s(result));
        Assert.Contains("inner.p", err.Message);
        Assert.Contains("REFERENCE", err.Message);
    }

    [Fact]
    public void ArrayOfStringField_ReportsE2003()
    {
        var result = FrontendTestHelper.ExpectErrors(
            """
            TYPE P : STRUCT a : ARRAY[0..1] OF STRING; END_STRUCT; END_TYPE;
            PROGRAM Main
            VAR x : P; y : P; ok : BOOL; END_VAR
                ok := x = y;
            END_PROGRAM
            """,
            expectedErrorCount: 1
        );

        var err = Assert.Single(E2003s(result));
        Assert.Contains("a[]", err.Message);
        Assert.Contains("STRING", err.Message);
    }

    // ── 操作数形态 / 作用域 ───────────────────────────────────────

    [Fact]
    public void TopLevelArrayComparison_ReportsE2003()
    {
        var result = FrontendTestHelper.ExpectErrors(
            """
            PROGRAM Main
            VAR a1 : ARRAY[0..1] OF DINT; a2 : ARRAY[0..1] OF DINT; ok : BOOL; END_VAR
                ok := a1 = a2;
            END_PROGRAM
            """,
            expectedErrorCount: 1
        );

        var err = Assert.Single(E2003s(result));
        Assert.Contains("数组整体比较暂不支持", err.Message);
    }

    [Fact]
    public void NonAddressableStructOperand_ReportsE2003()
    {
        var result = FrontendTestHelper.ExpectErrors(
            """
            TYPE P : STRUCT v : DINT; END_STRUCT; END_TYPE;
            FUNCTION Make : P
            VAR t : P; END_VAR
                t.v := 1;
                Make := t;
            END_FUNCTION
            PROGRAM Main
            VAR x : P; ok : BOOL; END_VAR
                ok := Make() = x;
            END_PROGRAM
            """,
            expectedErrorCount: 1
        );

        var err = Assert.Single(E2003s(result));
        Assert.Contains("可寻址", err.Message);
    }

    [Fact]
    public void GlobalStructComparison_Compiles()
    {
        // A8：全局聚合已有真实存储（按 FlatSlotCount 展开的全局槽），
        // 比较不再需要在前端拒绝。
        var result = FrontendTestHelper.ExpectSuccess(
            """
            TYPE P : STRUCT v : DINT; END_STRUCT; END_TYPE;
            PROGRAM Main
            VAR_GLOBAL
                x : P;
                y : P;
            END_VAR
            VAR
                ok : BOOL;
            END_VAR
                ok := x = y;
            END_PROGRAM
            """
        );

        Assert.Empty(E2003s(result));
    }

    // ── 标量比较不回归 ────────────────────────────────────────────

    [Fact]
    public void ScalarComparisons_StillCompile()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            """
            TYPE Color : (Red, Green, Blue); END_TYPE;
            PROGRAM Main
            VAR
                i : DINT;
                r : REAL;
                b : BOOL;
                c : Color;
                ok : BOOL;
            END_VAR
                ok := i = i;
                ok := i <> i;
                ok := r = r;
                ok := b = b;
                ok := c = c;
            END_PROGRAM
            """
        );

        Assert.Empty(E2003s(result));
    }
}
