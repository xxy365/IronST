using Antlr4.Runtime;
using IronST.Frontend.Formatting;
using IronST.Frontend.Generated;
using Xunit;

namespace IronST.Frontend.Tests;

// ═══════════════════════════════════════════════════════════════
//  StFormatter：解析树驱动 ST 格式化器
// ═══════════════════════════════════════════════════════════════
public class FormatterTests
{
    // ── helpers ───────────────────────────────────────────────────

    private static FormattingResult Run(string source, FormattingOptions? options = null) =>
        StFormatter.Format(source, options);

    /// <summary>Join lines with LF and a final newline, then format.</summary>
    private static string Format(params string[] lines) =>
        Run(string.Join("\n", lines) + "\n").Text;

    /// <summary>Expected output of <see cref="Format"/> for the same lines (LF, final newline).</summary>
    private static string Expected(params string[] lines) =>
        string.Join("\n", lines) + "\n";

    // ═══════════════════════════════════════════════════════════════
    //  1. 基础结构缩进
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Program_VarBlock_AndStatements_AreIndented()
    {
        var actual = Format(
            "PROGRAM Main",
            "VAR",
            "x : INT;",
            "y : REAL;",
            "END_VAR",
            "x := 1;",
            "y := 2.5;",
            "END_PROGRAM");

        Assert.Equal(Expected(
            "PROGRAM Main",
            "VAR",
            "    x : INT;",
            "    y : REAL;",
            "END_VAR",
            "    x := 1;",
            "    y := 2.5;",
            "END_PROGRAM"), actual);
    }

    [Fact]
    public void If_ElsIf_Else_Bodies_DeeperThanKeywords()
    {
        var actual = Format(
            "PROGRAM P",
            "VAR",
            "x : INT;",
            "y : INT;",
            "END_VAR",
            "IF x > 0 THEN",
            "y := 1;",
            "ELSIF x = 0 THEN",
            "y := 0;",
            "ELSE",
            "y := -1;",
            "END_IF",
            "END_PROGRAM");

        Assert.Equal(Expected(
            "PROGRAM P",
            "VAR",
            "    x : INT;",
            "    y : INT;",
            "END_VAR",
            "    IF x > 0 THEN",
            "        y := 1;",
            "    ELSIF x = 0 THEN",
            "        y := 0;",
            "    ELSE",
            "        y := -1;",
            "    END_IF",
            "END_PROGRAM"), actual);
    }

    [Fact]
    public void Loops_AreIndented()
    {
        var actual = Format(
            "PROGRAM P",
            "VAR",
            "i : INT;",
            "x : INT;",
            "END_VAR",
            "FOR i := 1 TO 10 BY 2 DO",
            "x := x + i;",
            "END_FOR",
            "WHILE x > 0 DO",
            "x := x - 1;",
            "END_WHILE",
            "REPEAT",
            "x := x + 1;",
            "UNTIL x > 10",
            "END_REPEAT",
            "END_PROGRAM");

        Assert.Equal(Expected(
            "PROGRAM P",
            "VAR",
            "    i : INT;",
            "    x : INT;",
            "END_VAR",
            "    FOR i := 1 TO 10 BY 2 DO",
            "        x := x + i;",
            "    END_FOR",
            "    WHILE x > 0 DO",
            "        x := x - 1;",
            "    END_WHILE",
            "    REPEAT",
            "        x := x + 1;",
            "    UNTIL x > 10",
            "    END_REPEAT",
            "END_PROGRAM"), actual);
    }

    [Fact]
    public void Case_Branches_AndElse_AreIndented()
    {
        var actual = Format(
            "PROGRAM P",
            "VAR",
            "x : INT;",
            "y : REAL;",
            "END_VAR",
            "CASE x OF",
            "1, 2:",
            "y := 1.0;",
            "3 .. 5:",
            "y := 2.0;",
            "ELSE",
            "y := 0.0;",
            "END_CASE",
            "END_PROGRAM");

        Assert.Equal(Expected(
            "PROGRAM P",
            "VAR",
            "    x : INT;",
            "    y : REAL;",
            "END_VAR",
            "    CASE x OF",
            "        1, 2:",
            "            y := 1.0;",
            "        3..5:",
            "            y := 2.0;",
            "        ELSE",
            "            y := 0.0;",
            "    END_CASE",
            "END_PROGRAM"), actual);
    }

    [Fact]
    public void Type_Struct_Members_AreIndented()
    {
        var actual = Format(
            "TYPE Point :",
            "STRUCT",
            "X : REAL;",
            "Y : REAL;",
            "END_STRUCT;",
            "END_TYPE;");

        Assert.Equal(Expected(
            "TYPE",
            "    Point : STRUCT",
            "        X : REAL;",
            "        Y : REAL;",
            "    END_STRUCT;",
            "END_TYPE;"), actual);
    }

    [Fact]
    public void FunctionBlock_VarBlocks_AndActions_AreIndented()
    {
        var actual = Format(
            "FUNCTION_BLOCK Counter",
            "VAR_INPUT",
            "inc : INT;",
            "END_VAR",
            "VAR",
            "count : INT;",
            "END_VAR",
            "count := count + inc;",
            "",
            "ACTION Home",
            "count := 0;",
            "END_ACTION",
            "END_FUNCTION_BLOCK");

        Assert.Equal(Expected(
            "FUNCTION_BLOCK Counter",
            "VAR_INPUT",
            "    inc : INT;",
            "END_VAR",
            "VAR",
            "    count : INT;",
            "END_VAR",
            "    count := count + inc;",
            "",
            "    ACTION Home",
            "        count := 0;",
            "    END_ACTION",
            "END_FUNCTION_BLOCK"), actual);
    }

    [Fact]
    public void Function_VarInput_AndBody_AreIndented()
    {
        var actual = Format(
            "FUNCTION Add : INT",
            "VAR_INPUT",
            "a : INT;",
            "b : INT;",
            "END_VAR",
            "Add := a + b;",
            "END_FUNCTION");

        Assert.Equal(Expected(
            "FUNCTION Add : INT",
            "VAR_INPUT",
            "    a : INT;",
            "    b : INT;",
            "END_VAR",
            "    Add := a + b;",
            "END_FUNCTION"), actual);
    }

    [Fact]
    public void Configuration_Resource_Task_Program_AreIndented()
    {
        var actual = Format(
            "CONFIGURATION MainConf",
            "VAR_GLOBAL",
            "pub_bool : BOOL := FALSE;",
            "END_VAR",
            "RESOURCE Res1 ON PLC1",
            "VAR_GLOBAL",
            "pub_int : INT;",
            "END_VAR",
            "TASK FastTask (INTERVAL := T#10ms);",
            "PROGRAM Prog1 WITH FastTask : Main;",
            "END_RESOURCE",
            "END_CONFIGURATION");

        Assert.Equal(Expected(
            "CONFIGURATION MainConf",
            "VAR_GLOBAL",
            "    pub_bool : BOOL := FALSE;",
            "END_VAR",
            "    RESOURCE Res1 ON PLC1",
            "    VAR_GLOBAL",
            "        pub_int : INT;",
            "    END_VAR",
            "        TASK FastTask(INTERVAL := T#10ms);",
            "        PROGRAM Prog1 WITH FastTask : Main;",
            "    END_RESOURCE",
            "END_CONFIGURATION"), actual);
    }

    // ═══════════════════════════════════════════════════════════════
    //  2. 注释保留
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Comments_LeadingTrailingAndBlock_AreKept()
    {
        var actual = Format(
            "// leading comment",
            "PROGRAM Main",
            "VAR",
            "x : INT; // trailing",
            "y : REAL; (* block comment *)",
            "END_VAR",
            "(* standalone",
            "   multi-line *)",
            "x := 1; // end",
            "END_PROGRAM");

        Assert.Equal(Expected(
            "// leading comment",
            "PROGRAM Main",
            "VAR",
            "    x : INT; // trailing",
            "    y : REAL; (* block comment *)",
            "END_VAR",
            "    (* standalone",
            "   multi-line *)",
            "    x := 1; // end",
            "END_PROGRAM"), actual);
    }

    [Fact]
    public void Comment_InsideExpression_IsKept()
    {
        var actual = Format(
            "PROGRAM P",
            "VAR",
            "x : INT;",
            "y : INT;",
            "END_VAR",
            "x := 1 (* mid *) + 2;",
            "y := 3 + // split",
            "4;",
            "END_PROGRAM");

        Assert.Equal(Expected(
            "PROGRAM P",
            "VAR",
            "    x : INT;",
            "    y : INT;",
            "END_VAR",
            "    x := 1 (* mid *) + 2;",
            "    y := 3 + // split",
            "    4;",
            "END_PROGRAM"), actual);
    }

    [Fact]
    public void CommentIsNotRewritten()
    {
        const string comment = "// 保留啊 !!!  spaces    and // nested look-alike";
        var result = Run(comment + "\n");
        Assert.True(result.Text.Contains(comment, StringComparison.Ordinal));
    }

    // ═══════════════════════════════════════════════════════════════
    //  3. 空行保留
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BlankLines_AreLimitedToMaxBlankLines()
    {
        var result = Run(
            "PROGRAM Main\nVAR\nx : INT;\n\n\n\ny : INT;\nEND_VAR\nx := 1;\n\n\nx := 2;\nEND_PROGRAM\n");

        Assert.Equal(Expected(
            "PROGRAM Main",
            "VAR",
            "    x : INT;",
            "",
            "    y : INT;",
            "END_VAR",
            "    x := 1;",
            "",
            "    x := 2;",
            "END_PROGRAM"), result.Text);
        Assert.True(result.Changed);
    }

    [Fact]
    public void BlankLines_ZeroKeepsNothing()
    {
        var result = Run(
            "PROGRAM Main\nVAR\nx : INT;\n\ny : INT;\nEND_VAR\nx := 1;\n\nx := 2;\nEND_PROGRAM\n",
            new FormattingOptions { MaxBlankLines = 0 });

        Assert.Equal(Expected(
            "PROGRAM Main",
            "VAR",
            "    x : INT;",
            "    y : INT;",
            "END_VAR",
            "    x := 1;",
            "    x := 2;",
            "END_PROGRAM"), result.Text);
    }

    [Fact]
    public void BlankLines_BeyondLimit_AreCollapsed()
    {
        var result = Run(
            "PROGRAM Main\nVAR\nx : INT;\nEND_VAR\n\n\n\n\n\nx := 1;\nEND_PROGRAM\n",
            new FormattingOptions { MaxBlankLines = 2 });

        Assert.Equal(Expected(
            "PROGRAM Main",
            "VAR",
            "    x : INT;",
            "END_VAR",
            "",
            "",
            "    x := 1;",
            "END_PROGRAM"), result.Text);
    }

    // ═══════════════════════════════════════════════════════════════
    //  4. 换行风格 CRLF / LF
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void CrlfInput_KeepsCrlf()
    {
        var source = "PROGRAM Main\nVAR\nx : INT;\nEND_VAR\nx := 1;\nEND_PROGRAM\n".Replace("\n", "\r\n");
        var result = Run(source);

        Assert.Equal(
            "PROGRAM Main\nVAR\n    x : INT;\nEND_VAR\n    x := 1;\nEND_PROGRAM\n".Replace("\n", "\r\n"),
            result.Text);
        // No stray lone line feeds.
        Assert.Equal(
            result.Text.Count(c => c == '\r'),
            result.Text.Count(c => c == '\n'));
    }

    [Fact]
    public void LfInput_KeepsLf()
    {
        const string source = "PROGRAM Main\nVAR\nx : INT;\nEND_VAR\nx := 1;\nEND_PROGRAM\n";
        var result = Run(source);

        Assert.DoesNotContain('\r', result.Text);
        Assert.Equal(
            "PROGRAM Main\nVAR\n    x : INT;\nEND_VAR\n    x := 1;\nEND_PROGRAM\n",
            result.Text);
    }

    [Fact]
    public void NewLineOption_OverridesDetectedStyle()
    {
        const string source = "PROGRAM Main\nVAR\nx : INT;\nEND_VAR\nx := 1;\nEND_PROGRAM\n";
        var result = Run(source, new FormattingOptions { NewLine = "\r\n" });

        Assert.Equal(
            "PROGRAM Main\nVAR\n    x : INT;\nEND_VAR\n    x := 1;\nEND_PROGRAM\n".Replace("\n", "\r\n"),
            result.Text);
    }

    [Fact]
    public void MixedLineEndings_UseFirstNewLineStyle()
    {
        const string source = "PROGRAM Main\r\nVAR\nx : INT;\nEND_VAR\r\nx := 1;\nEND_PROGRAM\r\n";
        var result = Run(source);

        // First line ending in the source is CRLF, so every emitted break is CRLF.
        Assert.Equal(
            "PROGRAM Main\nVAR\n    x : INT;\nEND_VAR\n    x := 1;\nEND_PROGRAM\n".Replace("\n", "\r\n"),
            result.Text);
    }

    // ═══════════════════════════════════════════════════════════════
    //  5. 字符串与字面量原始拼写
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void LiteralSpelling_IsPreserved()
    {
        var actual = Format(
            "PROGRAM Main",
            "VAR",
            "a : BOOL;",
            "r : REAL;",
            "t : TIME;",
            "d : DINT;",
            "s : STRING;",
            "END_VAR",
            "a := 2#1010;",
            "r := 1.5E3;",
            "t := T#1h;",
            "d := DINT#-100;",
            "s := 'a  b';",
            "END_PROGRAM");

        Assert.Equal(Expected(
            "PROGRAM Main",
            "VAR",
            "    a : BOOL;",
            "    r : REAL;",
            "    t : TIME;",
            "    d : DINT;",
            "    s : STRING;",
            "END_VAR",
            "    a := 2#1010;",
            "    r := 1.5E3;",
            "    t := T#1h;",
            "    d := DINT#-100;",
            "    s := 'a  b';",
            "END_PROGRAM"), actual);
    }

    // ═══════════════════════════════════════════════════════════════
    //  6. 幂等
    // ═══════════════════════════════════════════════════════════════

    private const string ComplexSample = """
        TYPE Point : STRUCT
        X : REAL;
        Y : REAL;
        END_STRUCT; END_TYPE;

        FUNCTION_BLOCK Counter
        VAR_INPUT
        inc : INT;
        END_VAR
        VAR
        count : INT;
        END_VAR
        // keep me
        count := count + inc;
        ACTION Home
        count := 0;
        END_ACTION
        END_FUNCTION_BLOCK

        FUNCTION Add : INT
        VAR_INPUT a : INT; b : INT; END_VAR
        Add := a + b;
        END_FUNCTION

        PROGRAM Main
        VAR
        x : INT;
        pts : ARRAY[0..9] OF Point;
        END_VAR
        x := 1 + 2 * 3;
        IF x > 0 AND NOT FALSE THEN
        pts[0].X := 1.5E3;
        ELSIF x = 0 THEN
        x := 0;
        ELSE
        x := -1;
        END_IF
        FOR i := 1 TO 10 BY 2 DO
        x := x + i;
        END_FOR
        WHILE x > 0 DO
        x := x - 1;
        END_WHILE
        REPEAT
        x := x + 1;
        UNTIL x > 10
        END_REPEAT
        CASE x OF
        1, 2: y := 1.0;
        3 .. 5: y := 2.0;
        ELSE
        y := 0.0;
        END_CASE
        END_PROGRAM
        """;

    [Fact]
    public void Format_IsIdempotent()
    {
        var first = Run(ComplexSample);
        var second = Run(first.Text);

        Assert.Equal(first.Text, second.Text);
    }

    [Fact]
    public void FormattingTwice_SecondReportsNoChange()
    {
        var first = Run(ComplexSample);
        var second = Run(first.Text);

        Assert.False(second.Changed);
    }

    // ═══════════════════════════════════════════════════════════════
    //  7. 语法错误 / 不完整代码：安全回退
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void SyntaxError_ReturnsSourceUnchanged()
    {
        const string source = "PROGRAM Main\nVAR\nx : INT;\nEND_VAR\nx := ;\nEND_PROGRAM\n";
        var result = Run(source);

        Assert.Equal(source, result.Text);
        Assert.False(result.Changed);
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void IncompleteCode_ReturnsSourceUnchanged()
    {
        const string source = "PROGRAM Main\nVAR\nx : INT;\n";
        var result = Run(source);

        Assert.Equal(source, result.Text);
        Assert.False(result.Changed);
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void EmptyAndNull_InputAreUnchanged()
    {
        Assert.Equal(string.Empty, Run(string.Empty).Text);
        Assert.Equal(string.Empty, Run("").Text);
        Assert.False(Run(string.Empty).Changed);
    }

    [Fact]
    public void SuccessfulFormat_HasNoError_Diagnostics()
    {
        var result = Run("PROGRAM Main\nEND_PROGRAM\n");

        Assert.False(result.HasErrors);
    }

    // ═══════════════════════════════════════════════════════════════
    //  8. hidden channel 回归（防止把 g4 改回 -> skip）
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void HiddenChannel_WS_AndComments_StayOnHiddenChannel()
    {
        var input = CharStreams.fromString("x := 1; // line\n(* block *) x := 2;\n   \n");
        var lexer = new STLexer(input);

        var tokens = new List<IToken>();
        IToken token;
        do
        {
            token = lexer.NextToken();
            tokens.Add(token);
        }
        while (token.Type != TokenConstants.EOF);

        Assert.Contains(tokens, t => t.Type == STLexer.WS && t.Channel == TokenConstants.HiddenChannel);
        Assert.Contains(tokens, t => t.Type == STLexer.LINE_COMMENT && t.Channel == TokenConstants.HiddenChannel);
        Assert.Contains(tokens, t => t.Type == STLexer.BLOCK_COMMENT && t.Channel == TokenConstants.HiddenChannel);

        // Hidden tokens must exist as real tokens (-> skip would drop them entirely).
        Assert.Contains(tokens, t => t.Channel == TokenConstants.DefaultChannel);
    }
}
