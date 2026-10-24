using IronST.Common.Diagnostics;
using IronST.Frontend;
using IronST.Frontend.Analysis;

namespace IronST.Frontend.Tests;

public class FrontendPipelineTests2
{
    // ─────────────────────────────────────────────
    //  Lexer 错误 — 特殊字符
    // ─────────────────────────────────────────────

    [Fact]
    public void P0_7_SpecialCharacters_ShouldProduceDiagnostics()
    {
        var source = """
            PROGRAM Main
                VAR x : INT; END_VAR
                x := 42 # $ %;
            END_PROGRAM
            """;

        var result = TestHelpers.Analyze(source);

        // 词法分析阶段就应该捕获 # $ % 等特殊字符
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void P0_7_CompletelyGarbageInput_ShouldNotCrash()
    {
        // 完全无法解析的输入
        var source = "!!!@@@###$$$%%%^^^&&&***";

        // 不应抛出未处理异常
        var exception = Record.Exception(() => TestHelpers.Analyze(source));
        Assert.Null(exception);

        var result = TestHelpers.Analyze(source);
        Assert.NotEmpty(result.Diagnostics);
    }

    // ─────────────────────────────────────────────
    //  整体管线集成测试
    // ─────────────────────────────────────────────

    [Fact]
    public void Integration_ValidProgram_ShouldAnalyzeWithoutErrors()
    {
        var source = """
        FUNCTION_BLOCK PID_Controller
            VAR_INPUT
                Kp : REAL;
                Ki : REAL;
                Kd : REAL;
                Setpoint : REAL;
                ProcessValue : REAL;
            END_VAR
            VAR_OUTPUT
                Output : REAL;
            END_VAR
            VAR
                Error : REAL;
                Integral : REAL;
                PrevError : REAL;
            END_VAR

            Error := Setpoint - ProcessValue;
            Integral := Integral + Error;
            Output := Kp * Error + Ki * Integral + Kd * (Error - PrevError);
            PrevError := Error;
        END_FUNCTION_BLOCK

        PROGRAM Main
            VAR
                pid : PID_Controller;
                sp : REAL := 100.0;
                pv : REAL;
                out : REAL;
            END_VAR
            pid(Kp := 1.0, Ki := 0.1, Kd := 0.01,
                Setpoint := sp, ProcessValue := pv);
            out := pid.Output;
        END_PROGRAM
        """;

        var result = TestHelpers.AnalyzeExpectSuccess(source);

        Assert.Equal(2, result.POUs.Count);
        Assert.Contains(result.POUs, p => p.Name == "PID_Controller"
            && p.Kind == POUKind.FunctionBlock);
        Assert.Contains(result.POUs, p => p.Name == "Main"
            && p.Kind == POUKind.Program);

        Assert.Contains(result.Variables, v => v.Name == "Kp");
        Assert.Contains(result.Variables, v => v.Name == "Error");

        Assert.Contains(result.CallSites, c => c.CalleeName == "pid");

        // pid(...) 是调用，不是写入
        Assert.Contains(result.References, r =>
            r.SymbolName == "pid" && r.Usage == ReferenceUsage.Call);

        // out := pid.Output 中 out 是写入
        Assert.Contains(result.References, r =>
            r.SymbolName == "out" && r.Usage == ReferenceUsage.Write);
    }
    [Fact]
    public void Integration_EnumAndStruct_ShouldAnalyzeCorrectly()
    {
        var source = """
            TYPE
                Color : (Red, Green, Blue);
                Point :
                    STRUCT
                        x : INT;
                        y : INT;
                    END_STRUCT
                ;
            END_TYPE

            PROGRAM Main
                VAR
                    c : Color;
                    p : Point;
                END_VAR
                c := Color.Red;
                p.x := 10;
                p.y := 20;
            END_PROGRAM
            """;

        var result = TestHelpers.AnalyzeExpectSuccess(source);

        Assert.Contains(result.CustomTypes, t => t.Name == "Color" && t.Kind == TypeKind.Enum);
        Assert.Contains(result.CustomTypes, t => t.Name == "Point" && t.Kind == TypeKind.Struct);
        Assert.Contains(
            result.References,
            r => r.SymbolName == "Color" && r.Usage == ReferenceUsage.TypeUsage
        );
        Assert.Contains(
            result.References,
            r => r.SymbolName == "p" && r.Usage == ReferenceUsage.Write
        );
    }

    // ─────────────────────────────────────────────
    //  CallerName 与 CallerPOU 冗余
    // ─────────────────────────────────────────────

    [Fact]
    public void P2_23_CallSite_ShouldHaveConsistentCallerInfo()
    {
        var source = """
            FUNCTION Helper : INT
                VAR_INPUT x : INT; END_VAR
                Helper := x + 1;
            END_FUNCTION

            PROGRAM Main
                VAR r : INT; END_VAR
                r := Helper(42);
            END_PROGRAM
            """;

        var result = TestHelpers.AnalyzeExpectSuccess(source);

        var call = result.CallSites.FirstOrDefault(c => c.CalleeName == "Helper");
        Assert.NotNull(call);

        // CallerName 和 CallerPOU 应该相同（如果两者都存在的话）
        Assert.Equal("Main", call.CallerName);
        // 如果 CallerPOU 字段被保留，它应该与 CallerName 相同
        if (call.CallerPOU != null)
        {
            Assert.Equal(call.CallerName, call.CallerPOU);
        }
    }

    // ─────────────────────────────────────────────
    //  更多语法错误场景
    // ─────────────────────────────────────────────

    [Fact]
    public void P2_19_MissingEndIf_ShouldProduceReadableError()
    {
        var source = """
            PROGRAM Main
                VAR x : BOOL; END_VAR
                IF x THEN
                    x := FALSE;
            END_PROGRAM
            """;

        var result = TestHelpers.Analyze(source);

        var syntaxErrors = GetSyntaxErrors(result);
        Assert.NotEmpty(syntaxErrors);

        // 错误信息应包含人类可读的期望 token 名称
        // 不应出现 {1..5, 12} 这样的 token 编号
        foreach (var err in syntaxErrors)
        {
            Assert.DoesNotMatch(@"\{\d+\.\.", err.Message);
        }
    }

    [Fact]
    public void P2_19_MissingSemicolon_ShouldProduceReadableError()
    {
        var source = """
            PROGRAM Main
                VAR x : INT END_VAR
            END_PROGRAM
            """;

        var result = TestHelpers.Analyze(source);
        Assert.NotEmpty(GetSyntaxErrors(result));
    }

    // ─────────────────────────────────────────────
    //  辅助
    // ─────────────────────────────────────────────

    private static List<Diagnostic> GetSyntaxErrors(AnalysisResult result) =>
        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
}
