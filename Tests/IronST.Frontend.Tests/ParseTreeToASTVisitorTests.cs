using IronST.Common.Diagnostics;
using IronST.Frontend;
using IronST.Frontend.Analysis;
using IronST.Frontend.AST;

namespace IronST.Frontend.Tests;

public class ParseTreeToASTVisitorTests
{
    // ─────────────────────────────────────────────
    //  结构体初始化值被替换为 0
    // ─────────────────────────────────────────────

    [Fact]
    public void P0_1_StructInitValue_ShouldNotBeReplacedWithZero()
    {
        var source = """
        TYPE
            Coord :
                STRUCT
                    x : INT;
                    y : INT;
                END_STRUCT
            ;
        END_TYPE

        PROGRAM Main
            VAR
                p : Coord := (x := 10, y := 20);
            END_VAR
        END_PROGRAM
        """;

        var result = TestHelpers.AnalyzeExpectSuccess(source);

        var mainProg = result.AST.POUs.OfType<ProgramNode>().First();
        var varDeclItems = mainProg.VarBlocks.SelectMany(vb => vb.Items).ToList();
        var pDecl = varDeclItems.FirstOrDefault(item =>
            item.Names.Any(n => n.Name == "p"));

        Assert.NotNull(pDecl);
        Assert.NotNull(pDecl.InitialValue);
        Assert.IsType<StructInitValueNode>(pDecl.InitialValue);

        var structInit = (StructInitValueNode)pDecl.InitialValue;
        Assert.Equal(2, structInit.Fields.Count);
        Assert.Contains(structInit.Fields, f =>
            f.Name == "x" && f.Value is LiteralExprNode lx
                && (long)(lx.ParsedValue ?? 0L) == 10);
        Assert.Contains(structInit.Fields, f =>
            f.Name == "y" && f.Value is LiteralExprNode ly
                && (long)(ly.ParsedValue ?? 0L) == 20);
    }

    // ─────────────────────────────────────────────
    //  数组初始化值被替换为 0
    // ─────────────────────────────────────────────

    [Fact]
    public void P0_1_ArrayInitValue_ShouldNotBeReplacedWithZero()
    {
        var source = """
            PROGRAM Main
                VAR
                    arr : ARRAY[1..3] OF INT := [10, 20, 30];
                END_VAR
            END_PROGRAM
            """;

        var result = TestHelpers.AnalyzeExpectSuccess(source);

        var mainProg = result.AST.POUs.OfType<ProgramNode>().First();
        var varDeclItems = mainProg.VarBlocks.SelectMany(vb => vb.Items).ToList();
        var arrDecl = varDeclItems.FirstOrDefault(item =>
            item.Names.Any(n => n.Name == "arr"));

        Assert.NotNull(arrDecl);
        Assert.NotNull(arrDecl.InitialValue);

        // 应该是 ArrayInitValueNode，而非 LiteralExprNode(0)
        Assert.IsType<ArrayInitValueNode>(arrDecl.InitialValue);

        var arrayInit = (ArrayInitValueNode)arrDecl.InitialValue;
        Assert.Equal(3, arrayInit.Values.Count);
    }

    // ─────────────────────────────────────────────
    //  InitialValue 强转 ExpressionNode 隐患
    // ─────────────────────────────────────────────

    [Fact]
    public void P0_2_InitialValue_ShouldAcceptNonExpressionNodeTypes()
    {
        var source = """
        TYPE
            Pair :
                STRUCT
                    a : INT;
                    b : INT;
                END_STRUCT
            ;
        END_TYPE

        PROGRAM Main
            VAR
                p : Pair := (a := 1, b := 2);
            END_VAR
        END_PROGRAM
        """;

        var exception = Record.Exception(() => TestHelpers.AnalyzeExpectSuccess(source));
        Assert.Null(exception);
    }



    // ─────────────────────────────────────────────
    //  Lexer 错误丢失
    // ─────────────────────────────────────────────

    [Fact]
    public void P0_7_LexerErrors_ShouldBeCollected()
    {
        // 准备：包含词法错误的源码
        // 使用一个 lexer 无法识别的字符序列
        var source = """
            PROGRAM Main
                VAR x : INT; END_VAR
                x := 42;
                @invalid_token_here;
            END_PROGRAM
            """;

        var result = TestHelpers.Analyze(source);

        // 应该至少有一个词法或语法错误
        Assert.NotEmpty(result.Diagnostics);

        // 且错误应包含对 @ 字符的诊断
        var lexerErrors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error
                && (d.Message.Contains("@") || d.Message.Contains("无法识别")
                    || d.Message.Contains("unrecognized")))
            .ToList();

        Assert.NotEmpty(lexerErrors);
    }

    [Fact]
    public void P0_7_UnterminatedString_ShouldProduceLexerError()
    {
        // 未关闭的字符串字面量是典型的 lexer 错误
        var source = """
            PROGRAM Main
                VAR s : STRING; END_VAR
                s := 'hello
            END_PROGRAM
            """;

        var result = TestHelpers.Analyze(source);

        // 应该有错误诊断
        Assert.NotEmpty(result.Diagnostics);
        Assert.Contains(result.Diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error);
    }

    // ─────────────────────────────────────────────
    //  ParseSubrangeBound 使用 int.Parse
    // ─────────────────────────────────────────────

    [Fact]
    public void P1_10_SubrangeWithLargeValues_ShouldNotOverflow()
    {
        // 准备：超出 int 范围的子范围边界
        var source = """
            TYPE
                BigRange : DINT(0 .. 3000000000);
            END_TYPE

            PROGRAM Main
                VAR r : BigRange; END_VAR
            END_PROGRAM
            """;

        // 不应抛出 OverflowException 或 FormatException
        var exception = Record.Exception(() => TestHelpers.Analyze(source));
        Assert.Null(exception);
    }

    [Fact]
    public void P1_10_CaseLabelWithLargeValue_ShouldNotOverflow()
    {
        var source = """
            PROGRAM Main
                VAR selector : DINT; result : INT; END_VAR
                CASE selector OF
                    2000000000:
                        result := 1;
                    3000000000:
                        result := 2;
                END_CASE
            END_PROGRAM
            """;

        var exception = Record.Exception(() => TestHelpers.Analyze(source));
        Assert.Null(exception);
    }

    // ─────────────────────────────────────────────
    //  long.Parse / double.Parse 无异常保护
    // ─────────────────────────────────────────────

    [Fact]
    public void P1_11_ExtremeLongInteger_ShouldNotThrow()
    {
        // 超出 long 范围的整数 — lexer 可能接受它
        var source = """
            PROGRAM Main
                VAR x : INT; END_VAR
                x := 99999999999999999999999;
            END_PROGRAM
            """;

        // 应该产生友好的诊断而非未处理的异常
        var exception = Record.Exception(() => TestHelpers.Analyze(source));
        Assert.Null(exception);
    }

    [Fact]
    public void P1_11_ExtremeBasedInteger_ShouldNotThrow()
    {
        var source = """
            PROGRAM Main
                VAR x : INT; END_VAR
                x := 16#FFFFFFFFFFFFFFFF;
            END_PROGRAM
            """;

        var exception = Record.Exception(() => TestHelpers.Analyze(source));
        Assert.Null(exception);
    }

    // ─────────────────────────────────────────────
    //  语法错误提示信息不可读
    // ─────────────────────────────────────────────

    [Fact]
    public void P2_19_SyntaxErrorMessage_ShouldBeHumanReadable()
    {
        // 准备：典型的语法错误
        var source = """
            PROGRAM Main
                VAR x : INT; END_VAR
                x :=;   // 缺少表达式
            END_PROGRAM
            """;

        var result = TestHelpers.Analyze(source);

        var syntaxErrors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.NotEmpty(syntaxErrors);

        // 错误信息不应包含 {数字..数字} 格式的 token 编号集合
        foreach (var diag in syntaxErrors)
        {
            Assert.DoesNotMatch(@"\{\d+\.\.\d+", diag.Message);
        }
    }

    // ─────────────────────────────────────────────
    //  SubrangeBound 解析方法一致性
    // ─────────────────────────────────────────────

    [Fact]
    public void P2_21_SubrangeBounds_ShouldSupportLongRange()
    {
        // 确保子范围的上下界能正确存储为 long（非 int）
        var source = """
            TYPE
                PosRange : DINT(-2147483648 .. 2147483647);
            END_TYPE

            PROGRAM Main
                VAR p : PosRange; END_VAR
            END_PROGRAM
            """;

        var result = TestHelpers.AnalyzeExpectSuccess(source);

        var posRange = result.CustomTypes.FirstOrDefault(t => t.Name == "PosRange");
        Assert.NotNull(posRange);
    }

    [Fact]
    public void P2_21_NegativeSubrangeBound_ShouldParseCorrectly()
    {
        var source = """
            TYPE
                NegRange : DINT(-100 .. -1);
            END_TYPE

            PROGRAM Main
                VAR n : NegRange; END_VAR
            END_PROGRAM
            """;

        var result = TestHelpers.AnalyzeExpectSuccess(source);

        var negRange = result.CustomTypes.FirstOrDefault(t => t.Name == "NegRange");
        Assert.NotNull(negRange);
        Assert.Equal("DINT", negRange.UnderlyingType);
    }
}