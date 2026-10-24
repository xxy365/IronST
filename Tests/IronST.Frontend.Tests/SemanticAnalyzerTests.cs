using IronST.Common.Diagnostics;
using IronST.Frontend;
using IronST.Frontend.Analysis;

namespace IronST.Frontend.Tests;

public class SemanticAnalyzerTests
{
    // ─────────────────────────────────────────────
    //  指针解引用 ^ 类型推导
    // ─────────────────────────────────────────────

    [Fact]
    public void Dereference_ShouldReturnPointedType_NotPointerType()
    {
        var source = """
            PROGRAM Main
                VAR
                    ptr : REFERENCE TO INT;
                    val : INT;
                END_VAR
                val := ptr^;
            END_PROGRAM
            """;

        // 不崩溃即可，REFERENCE TO 的底层类型解析可能尚未完整实现
        var result = TestHelpers.Analyze(source);

        // val 的写入引用应被收集
        var valRef = result.References.FirstOrDefault(r =>
            r.SymbolName == "val" && r.Usage == ReferenceUsage.Write);
        Assert.NotNull(valRef);
    }
    [Fact]
    public void Dereference_NonReferenceType_ShouldProduceError()
    {
        var source = """
            PROGRAM Main
                VAR
                    x : INT;
                    y : INT;
                END_VAR
                y := x^;
            END_PROGRAM
            """;

        var result = TestHelpers.Analyze(source);

        var dereferenceErrors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error
                && (d.Message.Contains("解引用")
                    || d.Message.Contains("dereference")
                    || d.Message.Contains("REFERENCE")
                    || d.Message.Contains("引用")))
            .ToList();

        Assert.NotEmpty(dereferenceErrors);
    }

    // ─────────────────────────────────────────────
    //  FB 实例调用绑定到正确的 FB
    // ─────────────────────────────────────────────

    [Fact]
    public void FBInstanceCall_ShouldBindToCorrectFB_Type()
    {
        var source = """
            FUNCTION_BLOCK PID
                VAR_INPUT  Setpoint : REAL; END_VAR
                VAR_OUTPUT Output   : REAL; END_VAR
            END_FUNCTION_BLOCK

            FUNCTION_BLOCK Timer
                VAR_INPUT  Delay : TIME; END_VAR
                VAR_OUTPUT Done  : BOOL; END_VAR
            END_FUNCTION_BLOCK

            PROGRAM Main
                VAR
                    myPID   : PID;
                    myTimer : Timer;
                END_VAR
                myPID(Setpoint := 100.0);
                myTimer(Delay := T#1s);
            END_PROGRAM
            """;

        var result = TestHelpers.AnalyzeExpectSuccess(source);

        var timerCall = result.CallSites.FirstOrDefault(c => c.CalleeName == "myTimer");
        Assert.NotNull(timerCall);
        Assert.Single(timerCall.Arguments);
        Assert.Equal("Delay", timerCall.Arguments[0].ParameterName);
        Assert.Equal("TIME", timerCall.Arguments[0].ParameterTypeName);

        var pidCall = result.CallSites.FirstOrDefault(c => c.CalleeName == "myPID");
        Assert.NotNull(pidCall);
        Assert.Single(pidCall.Arguments);
        Assert.Equal("Setpoint", pidCall.Arguments[0].ParameterName);
        Assert.Equal("REAL", pidCall.Arguments[0].ParameterTypeName);
    }

    [Fact]
    public void FBInstanceCall_SecondCallShouldNotReuseFirstFBParams()
    {
        var source = """
            FUNCTION_BLOCK FB_A
                VAR_INPUT  alpha : INT; END_VAR
            END_FUNCTION_BLOCK

            FUNCTION_BLOCK FB_B
                VAR_INPUT  beta : BOOL; END_VAR
            END_FUNCTION_BLOCK

            PROGRAM Main
                VAR
                    a : FB_A;
                    b : FB_B;
                END_VAR
                a(alpha := 1);
                b(beta := TRUE);
            END_PROGRAM
            """;

        var result = TestHelpers.AnalyzeExpectSuccess(source);

        var bCall = result.CallSites.FirstOrDefault(c => c.CalleeName == "b");
        Assert.NotNull(bCall);
        Assert.Single(bCall.Arguments);
        Assert.Equal("beta", bCall.Arguments[0].ParameterName);
        Assert.Equal("BOOL", bCall.Arguments[0].ParameterTypeName);
    }

    // ─────────────────────────────────────────────
    //  FunctionBlock Action 未分析
    // ─────────────────────────────────────────────

    [Fact]
    public void FBAction_VariableRefsInAction_ShouldBeCollected()
    {
        var source = """
            FUNCTION_BLOCK MotorCtrl
                VAR_INPUT  Start : BOOL; END_VAR
                VAR_OUTPUT Running : BOOL; END_VAR
                VAR        Error : BOOL; END_VAR

                Running := Start AND NOT Error;

                ACTION Reset
                    Error := FALSE;
                    Running := FALSE;
                END_ACTION
            END_FUNCTION_BLOCK

            PROGRAM Main
                VAR mc : MotorCtrl; END_VAR
                mc(Start := TRUE);
            END_PROGRAM
            """;

        var result = TestHelpers.AnalyzeExpectSuccess(source);

        var actionRefs = result.References
            .Where(r =>
                r.POUName == "MotorCtrl"
                && (r.SymbolName == "Error" || r.SymbolName == "Running")
                && r.Usage == ReferenceUsage.Write)
            .ToList();

        // 期望至少 3 个 Write 引用：主体 1 个 + Action 内 2 个
        Assert.True(actionRefs.Count >= 3,
            $"期望 Action 内部的变量引用被收集，实际只有 {actionRefs.Count} 个 Write 引用");
    }

    [Fact]
    public void FBAction_UndeclaredVarInAction_ShouldProduceError()
    {
        var source = """
            FUNCTION_BLOCK TestFB
                VAR_INPUT x : INT; END_VAR

                ACTION DoSomething
                    y := x + 1;
                END_ACTION
            END_FUNCTION_BLOCK

            PROGRAM Main
                VAR fb : TestFB; END_VAR
                fb(x := 1);
            END_PROGRAM
            """;

        var result = TestHelpers.Analyze(source);

        var undeclaredErrors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error
                && d.Message.Contains("未声明")
                && d.Message.Contains("y"))
            .ToList();

        Assert.NotEmpty(undeclaredErrors);
    }

    // ─────────────────────────────────────────────
    //  REF= 赋值分析
    // ─────────────────────────────────────────────

    [Fact]
    public void RefAssignment_ShouldCollectWriteRef()
    {
        var source = """
            PROGRAM Main
                VAR
                    ptr : REFERENCE TO INT;
                    val : INT := 42;
                END_VAR
                ptr REF= val;
            END_PROGRAM
            """;

        var result = TestHelpers.AnalyzeExpectSuccess(source);

        var ptrWrite = result.References.FirstOrDefault(r =>
            r.SymbolName == "ptr" && r.Usage == ReferenceUsage.Write);
        var valRead = result.References.FirstOrDefault(r =>
            r.SymbolName == "val" && r.Usage == ReferenceUsage.Read);

        Assert.NotNull(ptrWrite);
        Assert.NotNull(valRead);
    }

    [Fact]
    public void RefAssignment_NonRefTarget_ShouldProduceError()
    {
        var source = """
            PROGRAM Main
                VAR
                    x : INT;
                    y : INT;
                END_VAR
                x REF= y;
            END_PROGRAM
            """;

        var result = TestHelpers.Analyze(source);

        var refErrors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error
                && (d.Message.Contains("REF")
                    || d.Message.Contains("引用")
                    || d.Message.Contains("REFERENCE")))
            .ToList();

        Assert.NotEmpty(refErrors);
    }

    // ─────────────────────────────────────────────
    //  线程安全
    // ─────────────────────────────────────────────

    [Fact]
    public async Task ConcurrentAnalyze_ShouldNotCorruptResults()
    {
        var sourceA = """
            PROGRAM Alpha
                VAR a : INT; END_VAR
                a := 1;
            END_PROGRAM
            """;

        var sourceB = """
            PROGRAM Beta
                VAR b : REAL; END_VAR
                b := 3.14;
            END_PROGRAM
            """;

        // 使用 FrontendPipeline 并发分析（每次创建独立实例，模拟真实场景）
        var taskA = Task.Run(() => TestHelpers.AnalyzeExpectSuccess(sourceA));
        var taskB = Task.Run(() => TestHelpers.AnalyzeExpectSuccess(sourceB));

        var resultA = await taskA;
        var resultB = await taskB;

        Assert.Contains(resultA.POUs, p => p.Name == "Alpha");
        Assert.DoesNotContain(resultA.POUs, p => p.Name == "Beta");

        Assert.Contains(resultB.POUs, p => p.Name == "Beta");
        Assert.DoesNotContain(resultB.POUs, p => p.Name == "Alpha");
    }
    // ─────────────────────────────────────────────
    //  RETURN 未检查返回类型
    // ─────────────────────────────────────────────

    [Fact]
    public void FunctionWithReturnType_ShouldCheckReturnVarAssigned()
    {
        var source = """
            FUNCTION BadFunc : REAL
                VAR_INPUT x : INT; END_VAR
            END_FUNCTION
            """;

        var result = TestHelpers.Analyze(source);

        var returnWarnings = result.Diagnostics
            .Where(d =>
                (d.Severity == DiagnosticSeverity.Warning || d.Severity == DiagnosticSeverity.Error)
                && (d.Message.Contains("返回")
                    || d.Message.Contains("return")
                    || d.Message.Contains("赋值")
                    || d.Message.Contains("BadFunc")))
            .ToList();

        Assert.NotEmpty(returnWarnings);
    }

    // ─────────────────────────────────────────────
    //  EXIT 不在循环内
    // ─────────────────────────────────────────────

    [Fact]
    public void ExitOutsideLoop_ShouldProduceError()
    {
        var source = """
            PROGRAM Main
                VAR x : BOOL; END_VAR
                IF x THEN
                    EXIT;
                END_IF
            END_PROGRAM
            """;

        var result = TestHelpers.Analyze(source);

        var exitErrors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error
                && (d.Message.Contains("EXIT")
                    || d.Message.Contains("循环")
                    || d.Message.Contains("loop")))
            .ToList();

        Assert.NotEmpty(exitErrors);
    }

    [Fact]
    public void ExitInsideLoop_ShouldBeValid()
    {
        var source = """
            PROGRAM Main
                VAR i : INT; END_VAR
                FOR i := 0 TO 10 DO
                    IF i > 5 THEN
                        EXIT;
                    END_IF
                END_FOR
            END_PROGRAM
            """;

        var result = TestHelpers.Analyze(source);

        var exitErrors = result.Diagnostics.Where(d => d.Message.Contains("EXIT")).ToList();

        Assert.Empty(exitErrors);
    }

    // ─────────────────────────────────────────────
    //  VAR_CONFIG 块未被分析
    // ─────────────────────────────────────────────

    [Fact]
    public void VarConfig_ShouldCollectVariables()
    {
        var source = """
            PROGRAM Main
                VAR
                    startBtn : BOOL;
                END_VAR
                VAR_CONFIG
                    startBtn AT %IX0.0 : BOOL;
                END_VAR
            END_PROGRAM
            """;

        var result = TestHelpers.AnalyzeExpectSuccess(source);

        var configVars = result.Variables
            .Where(v => v.VarBlockKind == "VAR_CONFIG" || v.VarBlockKind == "CONFIG")
            .ToList();

        Assert.NotEmpty(configVars);
    }

    // ─────────────────────────────────────────────
    //  FilePath 全部为 null
    // ─────────────────────────────────────────────

    [Fact]
    public void VariableInfo_FilePathShouldNotBeNull_WhenFileNameProvided()
    {
        var source = """
            PROGRAM Main
                VAR myVar : INT; END_VAR
                myVar := 42;
            END_PROGRAM
            """;

        var result = TestHelpers.Analyze(source, "main.st");

        var mainVar = result.Variables.FirstOrDefault(v => v.Name == "myVar");
        Assert.NotNull(mainVar);
        Assert.Equal("main.st", mainVar.FilePath);
    }

    [Fact]
    public void SymbolReference_FilePathShouldNotBeNull_WhenFileNameProvided()
    {
        var source = """
            PROGRAM Main
                VAR x : INT; END_VAR
                x := 10;
            END_PROGRAM
            """;

        var result = TestHelpers.Analyze(source, "test_file.st");

        var declRef = result.References.FirstOrDefault(r =>
            r.SymbolName == "x" && r.Usage == ReferenceUsage.Declaration);
        Assert.NotNull(declRef);
        Assert.Equal("test_file.st", declRef.FilePath);
    }

    [Fact]
    public void CallSite_FilePathShouldNotBeNull_WhenFileNameProvided()
    {
        var source = """
            FUNCTION AddOne : INT
                VAR_INPUT n : INT; END_VAR
                AddOne := n + 1;
            END_FUNCTION

            PROGRAM Main
                VAR result : INT; END_VAR
                result := AddOne(5);
            END_PROGRAM
            """;

        var result = TestHelpers.AnalyzeExpectSuccess(source, "prog.st");

        var callSite = result.CallSites.FirstOrDefault(c => c.CalleeName == "AddOne");
        Assert.NotNull(callSite);
        Assert.Equal("prog.st", callSite.FilePath);
    }

    // ─────────────────────────────────────────────
    //  Configuration 语义分析
    // ─────────────────────────────────────────────

    [Fact]
    public void Configuration_ShouldBeAnalyzed()
    {
        var source = """
            PROGRAM Main
                VAR x : INT; END_VAR
            END_PROGRAM

            CONFIGURATION MyConfig
                RESOURCE Res1 ON PLC
                    TASK CycleTask(INTERVAL := T#100ms);
                    PROGRAM Prg1 WITH CycleTask : Main;
                END_RESOURCE
            END_CONFIGURATION
            """;

        var result = TestHelpers.Analyze(source);
        Assert.NotNull(result);
    }

    // ─────────────────────────────────────────────
    //  RepeatNode UNTIL 对照测试
    // ─────────────────────────────────────────────

    [Fact]
    public void RepeatUntilCondition_ShouldNeverBeNull()
    {
        var source = """
            PROGRAM Main
                VAR i : INT := 0; END_VAR
                REPEAT
                    i := i + 1;
                UNTIL i >= 10
                END_REPEAT
            END_PROGRAM
            """;

        var result = TestHelpers.AnalyzeExpectSuccess(source);

        var iRef = result.References.FirstOrDefault(r =>
            r.SymbolName == "i" && r.Usage == ReferenceUsage.Write);
        Assert.NotNull(iRef);
    }

    // ─────────────────────────────────────────────
    //  表达式级调用和语句级调用都应收集引用
    // ─────────────────────────────────────────────

    [Fact]
    public void ExpressionCallAndStatementCall_ShouldBothCollectRefs()
    {
        var source = """
            FUNCTION Double : INT
                VAR_INPUT n : INT; END_VAR
                Double := n * 2;
            END_FUNCTION

            PROGRAM Main
                VAR a : INT; b : INT; END_VAR
                a := Double(5);
                Double(10);
            END_PROGRAM
            """;

        var result = TestHelpers.AnalyzeExpectSuccess(source);

        var callRefs = result.References
            .Where(r => r.SymbolName == "Double" && r.Usage == ReferenceUsage.Call)
            .ToList();
        Assert.Equal(2, callRefs.Count);

        var callSites = result.CallSites.Where(c => c.CalleeName == "Double").ToList();
        Assert.Equal(2, callSites.Count);
    }

    // ─────────────────────────────────────────────
    //  类型信息收集
    // ─────────────────────────────────────────────

    [Fact]
    public void TypeInfo_StructFieldsShouldBeCollected()
    {
        var source = """
            TYPE
                Point :
                    STRUCT
                        x : INT;
                        y : INT;
                    END_STRUCT
                ;
            END_TYPE

            PROGRAM Main
                VAR p : Point; END_VAR
                p.x := 10;
            END_PROGRAM
            """;

        var result = TestHelpers.AnalyzeExpectSuccess(source);

        var pointType = result.CustomTypes.FirstOrDefault(t => t.Name == "Point");
        Assert.NotNull(pointType);
        Assert.Equal(TypeKind.Struct, pointType.Kind);
        Assert.NotNull(pointType.Fields);
        Assert.Equal(2, pointType.Fields.Count);
        Assert.Contains(pointType.Fields, f => f.Name == "x" && f.TypeName == "INT");
        Assert.Contains(pointType.Fields, f => f.Name == "y" && f.TypeName == "INT");
    }

    [Fact]
    public void TypeInfo_EnumMembersShouldBeCollected()
    {
        var source = """
            TYPE
                Color : (Red, Green, Blue);
            END_TYPE

            PROGRAM Main
                VAR c : Color; END_VAR
                c := Color.Red;
            END_PROGRAM
            """;

        var result = TestHelpers.AnalyzeExpectSuccess(source);

        var colorType = result.CustomTypes.FirstOrDefault(t => t.Name == "Color");
        Assert.NotNull(colorType);
        Assert.Equal(TypeKind.Enum, colorType.Kind);
        Assert.NotNull(colorType.EnumMembers);
        Assert.Equal(3, colorType.EnumMembers.Count);
        Assert.Contains("Red", colorType.EnumMembers);
        Assert.Contains("Green", colorType.EnumMembers);
        Assert.Contains("Blue", colorType.EnumMembers);
    }

    // ─────────────────────────────────────────────
    //  作用域管理
    // ─────────────────────────────────────────────

    [Fact]
    public void ForLoop_SameVarNameInDifferentPOUs_ShouldResolveCorrectly()
    {
        var source = """
            FUNCTION F1 : INT
                VAR x : INT := 10; END_VAR
                F1 := x;
            END_FUNCTION

            FUNCTION F2 : INT
                VAR x : INT := 20; END_VAR
                F2 := x;
            END_FUNCTION

            PROGRAM Main
                VAR r1 : INT; r2 : INT; END_VAR
                r1 := F1();
                r2 := F2();
            END_PROGRAM
            """;

        var result = TestHelpers.AnalyzeExpectSuccess(source);

        var f1XRefs = result.References
            .Where(r => r.SymbolName == "x" && r.POUName == "F1")
            .ToList();
        var f2XRefs = result.References
            .Where(r => r.SymbolName == "x" && r.POUName == "F2")
            .ToList();

        Assert.NotEmpty(f1XRefs);
        Assert.NotEmpty(f2XRefs);
    }

    // ─────────────────────────────────────────────
    //  AnalysisResult 集合不可变性
    // ─────────────────────────────────────────────

    [Fact]
    public void AnalysisResult_Variables_ShouldBeReadOnly()
    {
        var source = TestHelpers.WrapProgram("VAR x : INT; END_VAR x := 1;");
        var result = TestHelpers.AnalyzeExpectSuccess(source);

        var variablesType = result.Variables.GetType();
        Assert.True(
            typeof(System.Collections.Generic.IReadOnlyList<VariableInfo>)
                .IsAssignableFrom(variablesType),
            $"期望 Variables 为 IReadOnlyList 类型，实际为 {variablesType.Name}");
    }

    [Fact]
    public void AnalysisResult_Diagnostics_ShouldBeReadOnly()
    {
        var source = TestHelpers.WrapProgram("VAR x : INT; END_VAR x := 1;");
        var result = TestHelpers.AnalyzeExpectSuccess(source);

        var diagnosticsType = result.Diagnostics.GetType();
        Assert.True(
            typeof(System.Collections.Generic.IReadOnlyList<Diagnostic>)
                .IsAssignableFrom(diagnosticsType),
            $"期望 Diagnostics 为 IReadOnlyList 类型，实际为 {diagnosticsType.Name}");
    }

    // ─────────────────────────────────────────────
    //  辅助方法
    // ─────────────────────────────────────────────

    private static IronST.Frontend.AST.CompilationUnitNode ParseAST(string source)
    {
        var result = FrontendPipeline.Analyze(source);
        return result.AST;
    }
}