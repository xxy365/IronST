using IronST.Frontend.Analysis;
using IronST.Frontend.AST;

namespace IronST.Frontend.Tests;

/// <summary>
/// 断点相关场景测试 —— 验证前端产出的元数据能否支撑各类断点需求。
/// 覆盖：行断点、函数断点、写入监控断点、调用处断点、
///       断点命中后的作用域变量、调用栈构建、综合端到端场景。
/// </summary>
public class BreakpointTests
{
    // ═══════════════════════════════════════════════════════════════
    //  辅助方法：递归收集 AST 中所有 StatementNode 及其行号
    //  模拟调试器中「给定行号 → 找到对应语句 → 判断是否可断」的逻辑
    // ═══════════════════════════════════════════════════════════════

    private static List<(int Line, StatementNode Node)> CollectStatementLineMap(
        CompilationUnitNode ast
    )
    {
        var map = new List<(int Line, StatementNode Node)>();
        foreach (var pou in ast.POUs)
            CollectStatementsFromPOU(pou, map);
        return map;
    }

    private static void CollectStatementsFromPOU(POUNode pou, List<(int Line, StatementNode)> map)
    {
        // Function / Program 使用 Body
        foreach (var stmt in pou.Body)
            CollectStatementRecursive(stmt, map);

        // FunctionBlock 使用 BodyItems（保真交错顺序）
        if (pou is FunctionBlockNode fb)
        {
            foreach (var item in fb.BodyItems)
            {
                if (item is FBStatementItemNode si)
                    CollectStatementRecursive(si.Statement, map);
                else if (item is FBActionItemNode ai)
                    foreach (var stmt in ai.Action.Body)
                        CollectStatementRecursive(stmt, map);
            }
        }
    }

    private static void CollectStatementRecursive(
        StatementNode stmt,
        List<(int Line, StatementNode)> map
    )
    {
        map.Add((stmt.Span.Line, stmt));

        switch (stmt)
        {
            case IfNode ifn:
                foreach (var s in ifn.ThenBody)
                    CollectStatementRecursive(s, map);
                foreach (var (_, body) in ifn.ElsIfBranches)
                foreach (var s in body)
                    CollectStatementRecursive(s, map);
                if (ifn.ElseBody != null)
                    foreach (var s in ifn.ElseBody)
                        CollectStatementRecursive(s, map);
                break;

            case ForNode fn:
                foreach (var s in fn.Body)
                    CollectStatementRecursive(s, map);
                break;

            case WhileNode wn:
                foreach (var s in wn.Body)
                    CollectStatementRecursive(s, map);
                break;

            case RepeatNode rn:
                foreach (var s in rn.Body)
                    CollectStatementRecursive(s, map);
                break;

            case CaseNode cn:
                foreach (var branch in cn.Branches)
                foreach (var s in branch.Body)
                    CollectStatementRecursive(s, map);
                if (cn.ElseBody != null)
                    foreach (var s in cn.ElseBody)
                        CollectStatementRecursive(s, map);
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  1. 行断点：可执行语句定位
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void LineBreakpoint_ExecutableLine_FindsStatement()
    {
        //  ① (empty)
        //  ② PROGRAM Main
        //  ③ VAR
        //  ④     x : INT;
        //  ⑤ END_VAR
        //  ⑥ x := 42;
        //  ⑦ END_PROGRAM
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR
    x : INT;
END_VAR
x := 42;
END_PROGRAM"
        );

        var stmtMap = CollectStatementLineMap(result.AST);

        // 第 6 行 x := 42; 应该找到 AssignmentNode
        Assert.Contains(stmtMap, s => s.Line == 6 && s.Node is AssignmentNode);
    }

    [Fact]
    public void LineBreakpoint_MultipleStatements_AllFound()
    {
        //  ⑧ x := 1;
        //  ⑨ y := 2;
        //  ⑩ z := 3;
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR
    x : INT;
    y : INT;
    z : INT;
END_VAR
x := 1;
y := 2;
z := 3;
END_PROGRAM"
        );

        var stmtMap = CollectStatementLineMap(result.AST);

        Assert.Contains(stmtMap, s => s.Line == 8);
        Assert.Contains(stmtMap, s => s.Line == 9);
        Assert.Contains(stmtMap, s => s.Line == 10);
        Assert.Equal(3, stmtMap.Count);
    }

    [Fact]
    public void LineBreakpoint_ReturnStatement_FindsReturnNode()
    {
        //  ④ IF x < 0 THEN
        //  ⑤     RETURN;
        //  ⑥ END_IF
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR x : INT; END_VAR
IF x < 0 THEN
    RETURN;
END_IF
x := 1;
END_PROGRAM"
        );

        var stmtMap = CollectStatementLineMap(result.AST);

        Assert.Contains(stmtMap, s => s.Node is ReturnNode);
    }

    [Fact]
    public void LineBreakpoint_ExitStatement_FindsExitNode()
    {
        //  ⑤ IF i = 5 THEN
        //  ⑥     EXIT;
        //  ⑦ END_IF
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR i : INT; END_VAR
FOR i := 0 TO 10 DO
    IF i = 5 THEN
        EXIT;
    END_IF
END_FOR
END_PROGRAM"
        );

        var stmtMap = CollectStatementLineMap(result.AST);

        Assert.Contains(stmtMap, s => s.Node is ExitNode);
    }

    // ═══════════════════════════════════════════════════════════════
    //  2. 行断点：非可执行行
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void LineBreakpoint_DeclarationLine_NoStatement()
    {
        //  ④ x : INT;  ← 变量声明，不是可执行语句
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR
    x : INT;
END_VAR
x := 42;
END_PROGRAM"
        );

        var stmtMap = CollectStatementLineMap(result.AST);

        Assert.DoesNotContain(stmtMap, s => s.Line == 4);
    }

    [Fact]
    public void LineBreakpoint_EndKeywordLine_NoStatement()
    {
        //  ⑤ END_VAR   ← 非可执行
        //  ⑦ END_PROGRAM ← 非可执行
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR
    x : INT;
END_VAR
x := 42;
END_PROGRAM"
        );

        var stmtMap = CollectStatementLineMap(result.AST);

        Assert.DoesNotContain(stmtMap, s => s.Line == 5);
        Assert.DoesNotContain(stmtMap, s => s.Line == 7);
    }

    [Fact]
    public void LineBreakpoint_ProgramDeclarationLine_NoStatement()
    {
        //  ② PROGRAM Main ← 声明，不可执行
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR x : INT; END_VAR
x := 1;
END_PROGRAM"
        );

        var stmtMap = CollectStatementLineMap(result.AST);

        Assert.DoesNotContain(stmtMap, s => s.Line == 2);
    }

    // ═══════════════════════════════════════════════════════════════
    //  3. 行断点：嵌套语句定位
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void LineBreakpoint_NestedInIf_FindsStatement()
    {
        //  ④ IF x > 0 THEN
        //  ⑤     y := 1;
        //  ⑥ ELSIF x = 0 THEN
        //  ⑦     y := 0;
        //  ⑧ ELSE
        //  ⑨     y := -1;
        //  ⑩ END_IF
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR x : INT; y : INT; END_VAR
IF x > 0 THEN
    y := 1;
ELSIF x = 0 THEN
    y := 0;
ELSE
    y := -1;
END_IF
END_PROGRAM"
        );

        var stmtMap = CollectStatementLineMap(result.AST);

        Assert.Contains(stmtMap, s => s.Line == 4 && s.Node is IfNode);
        Assert.Contains(stmtMap, s => s.Line == 5 && s.Node is AssignmentNode);
        Assert.Contains(stmtMap, s => s.Line == 7 && s.Node is AssignmentNode);
        Assert.Contains(stmtMap, s => s.Line == 9 && s.Node is AssignmentNode);
    }

    [Fact]
    public void LineBreakpoint_NestedInFor_FindsStatement()
    {
        //  ⑤ FOR i := 0 TO 9 BY 1 DO
        //  ⑥     sum := sum + i;
        //  ⑦ END_FOR
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR i : INT; sum : INT; END_VAR
sum := 0;
FOR i := 0 TO 9 BY 1 DO
    sum := sum + i;
END_FOR
END_PROGRAM"
        );

        var stmtMap = CollectStatementLineMap(result.AST);

        Assert.Contains(stmtMap, s => s.Line == 5 && s.Node is ForNode);
        Assert.Contains(stmtMap, s => s.Line == 6 && s.Node is AssignmentNode);
    }

    [Fact]
    public void LineBreakpoint_NestedInWhile_FindsStatement()
    {
        //  ⑤ WHILE i < 10 DO
        //  ⑥     i := i + 1;
        //  ⑦ END_WHILE
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR i : INT; END_VAR
i := 0;
WHILE i < 10 DO
    i := i + 1;
END_WHILE
END_PROGRAM"
        );

        var stmtMap = CollectStatementLineMap(result.AST);

        Assert.Contains(stmtMap, s => s.Line == 5 && s.Node is WhileNode);
        Assert.Contains(stmtMap, s => s.Line == 6 && s.Node is AssignmentNode);
    }

    [Fact]
    public void LineBreakpoint_NestedInRepeat_FindsStatement()
    {
        //  ⑤ REPEAT
        //  ⑥     i := i + 1;
        //  ⑦ UNTIL i >= 10
        //  ⑧ END_REPEAT
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR i : INT; END_VAR
i := 0;
REPEAT
    i := i + 1;
UNTIL i >= 10
END_REPEAT
END_PROGRAM"
        );

        var stmtMap = CollectStatementLineMap(result.AST);

        Assert.Contains(stmtMap, s => s.Line == 5 && s.Node is RepeatNode);
        Assert.Contains(stmtMap, s => s.Line == 6 && s.Node is AssignmentNode);
    }

    [Fact]
    public void LineBreakpoint_NestedInCase_FindsStatement()
    {
        //  ④ CASE sel OF
        //  ⑤     1:
        //  ⑥         out := 10;
        //  ⑦     2, 3:
        //  ⑧         out := 20;
        //  ⑨     4 .. 6:
        //  ⑩         out := 30;
        //  ⑪ ELSE
        //  ⑫     out := 0;
        //  ⑬ END_CASE
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR sel : INT; out : INT; END_VAR
CASE sel OF
    1:
        out := 10;
    2, 3:
        out := 20;
    4 .. 6:
        out := 30;
ELSE
    out := 0;
END_CASE
END_PROGRAM"
        );

        var stmtMap = CollectStatementLineMap(result.AST);

        Assert.Contains(stmtMap, s => s.Line == 4 && s.Node is CaseNode);
        Assert.Contains(stmtMap, s => s.Line == 6 && s.Node is AssignmentNode);
        Assert.Contains(stmtMap, s => s.Line == 8 && s.Node is AssignmentNode);
        Assert.Contains(stmtMap, s => s.Line == 10 && s.Node is AssignmentNode);
        Assert.Contains(stmtMap, s => s.Line == 12 && s.Node is AssignmentNode);
    }

    [Fact]
    public void LineBreakpoint_DeeplyNested_WhileForIf_FindsStatement()
    {
        //  ⑤ WHILE i < 10 DO
        //  ⑥     FOR j := 0 TO 5 DO
        //  ⑦         IF j > 2 THEN
        //  ⑧             x := i + j;   ← 三层嵌套
        //  ⑨         END_IF
        //  ⑩     END_FOR
        //  ⑪     i := i + 1;
        //  ⑫ END_WHILE
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR i : INT; j : INT; x : INT; END_VAR
i := 0;
WHILE i < 10 DO
    FOR j := 0 TO 5 DO
        IF j > 2 THEN
            x := i + j;
        END_IF
    END_FOR
    i := i + 1;
END_WHILE
END_PROGRAM"
        );

        var stmtMap = CollectStatementLineMap(result.AST);

        // 三层嵌套的赋值语句
        Assert.Contains(stmtMap, s => s.Line == 8 && s.Node is AssignmentNode);
        // WHILE 体内、FOR 外的赋值
        Assert.Contains(stmtMap, s => s.Line == 11 && s.Node is AssignmentNode);
    }

    [Fact]
    public void LineBreakpoint_CallStatement_FindsCallNode()
    {
        //  ⑧ m(speed := 100);  ← FB 实例调用
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION_BLOCK Motor
VAR_INPUT speed : INT; END_VAR
END_FUNCTION_BLOCK

PROGRAM Main
VAR m : Motor; END_VAR
m(speed := 100);
END_PROGRAM"
        );

        var stmtMap = CollectStatementLineMap(result.AST);

        Assert.Contains(stmtMap, s => s.Line == 8 && s.Node is CallStatementNode);
    }

    [Fact]
    public void LineBreakpoint_FunctionBlockBody_FindsStatement()
    {
        //  ⑨ count := count + inc;  ← FB 内部语句（在 BodyItems 中）
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION_BLOCK Counter
VAR_INPUT
    inc : INT;
END_VAR
VAR
    count : INT;
END_VAR
count := count + inc;
END_FUNCTION_BLOCK"
        );

        var stmtMap = CollectStatementLineMap(result.AST);

        Assert.Contains(stmtMap, s => s.Line == 9 && s.Node is AssignmentNode);
    }

    // ═══════════════════════════════════════════════════════════════
    //  4. 函数断点：POU 入口定位
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void FunctionBreakpoint_FindsFunctionEntryLine()
    {
        //  ② FUNCTION Add : INT
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION Add : INT
VAR_INPUT
    a : INT;
    b : INT;
END_VAR
Add := a + b;
END_FUNCTION"
        );

        var pou = result.POUs.FirstOrDefault(p => p.Name == "Add");

        Assert.NotNull(pou);
        Assert.Equal(POUKind.Function, pou!.Kind);
        Assert.Equal(2, pou.Line);
    }

    [Fact]
    public void FunctionBreakpoint_FindsProgramEntryLine()
    {
        //  ② PROGRAM Main
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR x : INT; END_VAR
x := 1;
END_PROGRAM"
        );

        var pou = result.POUs.FirstOrDefault(p => p.Name == "Main");

        Assert.NotNull(pou);
        Assert.Equal(POUKind.Program, pou!.Kind);
        Assert.Equal(2, pou.Line);
    }

    [Fact]
    public void FunctionBreakpoint_FindsFunctionBlockEntryLine()
    {
        //  ② FUNCTION_BLOCK Counter
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION_BLOCK Counter
VAR_INPUT
    inc : INT;
END_VAR
VAR
    count : INT;
END_VAR
count := count + inc;
END_FUNCTION_BLOCK"
        );

        var pou = result.POUs.FirstOrDefault(p => p.Name == "Counter");

        Assert.NotNull(pou);
        Assert.Equal(POUKind.FunctionBlock, pou!.Kind);
        Assert.Equal(2, pou.Line);
    }

    [Fact]
    public void FunctionBreakpoint_NonExistentPOU_ReturnsNull()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
END_PROGRAM"
        );

        var pou = result.POUs.FirstOrDefault(p => p.Name == "NotExist");

        Assert.Null(pou);
    }

    [Fact]
    public void FunctionBreakpoint_MultiplePOUs_SelectCorrect()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION Double : DINT
VAR_INPUT x : DINT; END_VAR
Double := x * 2;
END_FUNCTION

FUNCTION Triple : DINT
VAR_INPUT x : DINT; END_VAR
Triple := x * 3;
END_FUNCTION

PROGRAM Main
VAR r : DINT; END_VAR
r := Double(5);
END_PROGRAM"
        );

        var double_ = result.POUs.FirstOrDefault(p => p.Name == "Double");
        var triple = result.POUs.FirstOrDefault(p => p.Name == "Triple");
        var main = result.POUs.FirstOrDefault(p => p.Name == "Main");

        Assert.NotNull(double_);
        Assert.Equal(2, double_!.Line);
        Assert.Equal(POUKind.Function, double_.Kind);

        Assert.NotNull(triple);
        Assert.Equal(7, triple!.Line);
        Assert.Equal(POUKind.Function, triple.Kind);

        Assert.NotNull(main);
        Assert.Equal(12, main!.Line);
        Assert.Equal(POUKind.Program, main.Kind);
    }

    // ═══════════════════════════════════════════════════════════════
    //  5. 写入监控断点（Data Breakpoint）
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void DataBreakpoint_SingleWrite_FindsLocation()
    {
        //  ⑥ x := 42;
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR
    x : INT;
END_VAR
x := 42;
END_PROGRAM"
        );

        var writes = result
            .References.Where(r => r.SymbolName == "x" && r.Usage == ReferenceUsage.Write)
            .ToList();

        Assert.Single(writes);
        Assert.Equal(6, writes[0].Line);
    }

    [Fact]
    public void DataBreakpoint_MultipleWrites_AllFound()
    {
        //  ⑥ x := 1;  ⑦ x := 2;  ⑧ x := 3;
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR
    x : INT;
END_VAR
x := 1;
x := 2;
x := 3;
END_PROGRAM"
        );

        var writes = result
            .References.Where(r => r.SymbolName == "x" && r.Usage == ReferenceUsage.Write)
            .ToList();

        Assert.Equal(3, writes.Count);
        Assert.Contains(writes, w => w.Line == 6);
        Assert.Contains(writes, w => w.Line == 7);
        Assert.Contains(writes, w => w.Line == 8);
    }

    [Fact]
    public void DataBreakpoint_ReadVsWrite_Distinguished()
    {
        //  ⑦ x := 1;      ← Write
        //  ⑧ y := x + 1;  ← Read
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR
    x : INT;
    y : INT;
END_VAR
x := 1;
y := x + 1;
END_PROGRAM"
        );

        var xWrites = result
            .References.Where(r => r.SymbolName == "x" && r.Usage == ReferenceUsage.Write)
            .ToList();
        var xReads = result
            .References.Where(r => r.SymbolName == "x" && r.Usage == ReferenceUsage.Read)
            .ToList();

        Assert.Single(xWrites);
        Assert.Equal(7, xWrites[0].Line);

        Assert.Single(xReads);
        Assert.Equal(8, xReads[0].Line);
    }

    [Fact]
    public void DataBreakpoint_WriteInNestedBlock()
    {
        //  ⑧ x := 1;   ← THEN 分支
        //  ⑩ x := 2;   ← ELSE 分支
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR
    x : INT;
    flag : BOOL;
END_VAR
IF flag THEN
    x := 1;
ELSE
    x := 2;
END_IF
END_PROGRAM"
        );

        var writes = result
            .References.Where(r => r.SymbolName == "x" && r.Usage == ReferenceUsage.Write)
            .ToList();

        Assert.Equal(2, writes.Count);
        Assert.Contains(writes, w => w.Line == 8);
        Assert.Contains(writes, w => w.Line == 10);
    }

    [Fact]
    public void DataBreakpoint_NoWrites_OnlyReads()
    {
        // 函数参数 a, b 在函数体内只有读取，没有写入
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION Add : INT
VAR_INPUT
    a : INT;
    b : INT;
END_VAR
Add := a + b;
END_FUNCTION

PROGRAM Main
VAR r : INT; END_VAR
r := Add(1, 2);
END_PROGRAM"
        );

        var aWrites = result
            .References.Where(r => r.SymbolName == "a" && r.Usage == ReferenceUsage.Write)
            .ToList();
        var aReads = result
            .References.Where(r => r.SymbolName == "a" && r.Usage == ReferenceUsage.Read)
            .ToList();

        Assert.Empty(aWrites);
        Assert.NotEmpty(aReads);
    }

    [Fact]
    public void DataBreakpoint_AssignmentBothSides_ReadAndWrite()
    {
        //  ⑥ x := x + 1;  ← 左侧 Write，右侧 Read
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR
    x : INT;
END_VAR
x := x + 1;
END_PROGRAM"
        );

        var xRefs = result.References.Where(r => r.SymbolName == "x").ToList();

        // x: 1 次 Declaration + 1 次 Write（左侧）+ 1 次 Read（右侧）
        Assert.Contains(xRefs, r => r.Usage == ReferenceUsage.Declaration);
        Assert.Contains(xRefs, r => r.Usage == ReferenceUsage.Write);
        Assert.Contains(xRefs, r => r.Usage == ReferenceUsage.Read);
    }

    // ═══════════════════════════════════════════════════════════════
    //  6. 调用处断点
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void CallSiteBreakpoint_FindByFunctionName()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION Inc : INT
VAR_INPUT x : INT; END_VAR
Inc := x + 1;
END_FUNCTION

PROGRAM Main
VAR r : INT; END_VAR
r := Inc(42);
END_PROGRAM"
        );

        var call = result.CallSites.FirstOrDefault(c => c.CalleeName == "Inc");

        Assert.NotNull(call);
        Assert.Equal("Main", call!.CallerName);
        Assert.Equal(1, call.ArgumentCount);
    }

    [Fact]
    public void CallSiteBreakpoint_MultipleCalls_AllFound()
    {
        //  ⑨ a := Inc(1);
        //  ⑩ b := Inc(2);
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION Inc : INT
VAR_INPUT x : INT; END_VAR
Inc := x + 1;
END_FUNCTION

PROGRAM Main
VAR a : INT; b : INT; END_VAR
a := Inc(1);
b := Inc(2);
END_PROGRAM"
        );

        var calls = result.CallSites.Where(c => c.CalleeName == "Inc").ToList();

        Assert.Equal(2, calls.Count);
        Assert.All(calls, c => Assert.Equal("Main", c.CallerName));
    }

    [Fact]
    public void CallSiteBreakpoint_ArgumentsAvailable()
    {
        // FB 实例调用：参数方向、类型、绑定方式
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION_BLOCK PID
VAR_INPUT
    setpoint : REAL;
    measured : REAL;
END_VAR
VAR_OUTPUT
    controlOutput : REAL;
END_VAR
END_FUNCTION_BLOCK

PROGRAM Main
VAR
    pid : PID;
    sp : REAL;
    pv : REAL;
    out : REAL;
END_VAR
pid(setpoint := sp, measured := pv, controlOutput => out);
END_PROGRAM"
        );

        var call = result.CallSites.FirstOrDefault(c => c.CalleeName == "pid");

        Assert.NotNull(call);
        Assert.Equal(3, call!.ArgumentCount);

        // 输入参数
        var spArg = call.Arguments.First(a => a.ParameterName == "setpoint");
        Assert.Equal(ParameterDirection.Input, spArg.Direction);
        Assert.Equal("REAL", spArg.ParameterTypeName);
        Assert.Equal("sp", spArg.ExpressionSummary);
        Assert.Equal(ArgumentBindingStyle.NamedInput, spArg.Binding);

        // 输出参数
        var outArg = call.Arguments.First(a => a.ParameterName == "controlOutput");
        Assert.Equal(ParameterDirection.Output, outArg.Direction);
        Assert.Equal(ArgumentBindingStyle.NamedOutput, outArg.Binding);
        Assert.Equal("out", outArg.ExpressionSummary);
    }

    [Fact]
    public void CallSiteBreakpoint_ChainCallHierarchy()
    {
        //  Main → B → A 的调用链
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION A : INT
VAR_INPUT x : INT; END_VAR
A := x;
END_FUNCTION

FUNCTION B : INT
VAR_INPUT x : INT; END_VAR
B := A(x);
END_FUNCTION

PROGRAM Main
VAR r : INT; END_VAR
r := B(1);
END_PROGRAM"
        );

        // 谁调用了 A？→ B
        var callersOfA = result
            .CallSites.Where(c => c.CalleeName == "A")
            .Select(c => c.CallerName)
            .ToList();
        Assert.Single(callersOfA);
        Assert.Equal("B", callersOfA[0]);

        // 谁调用了 B？→ Main
        var callersOfB = result
            .CallSites.Where(c => c.CalleeName == "B")
            .Select(c => c.CallerName)
            .ToList();
        Assert.Single(callersOfB);
        Assert.Equal("Main", callersOfB[0]);
    }

    // ═══════════════════════════════════════════════════════════════
    //  7. 断点命中：作用域变量查询
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BreakpointHit_CurrentPOUVariables_AllCollected()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION Calc : REAL
VAR_INPUT
    value : INT;
    factor : REAL;
END_VAR
VAR
    temp : REAL;
END_VAR
Calc := 0.0;
END_FUNCTION"
        );

        var calcVars = result.Variables.Where(v => v.POUName == "Calc").ToList();

        Assert.Equal(3, calcVars.Count);
        Assert.Contains(calcVars, v => v.Name == "value" && v.VarBlockKind == "VAR_INPUT");
        Assert.Contains(calcVars, v => v.Name == "factor" && v.VarBlockKind == "VAR_INPUT");
        Assert.Contains(calcVars, v => v.Name == "temp" && v.VarBlockKind == "VAR");
    }

    [Fact]
    public void BreakpointHit_DifferentPOUs_DifferentVariableSets()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION Add : INT
VAR_INPUT
    a : INT;
    b : INT;
END_VAR
Add := a + b;
END_FUNCTION

PROGRAM Main
VAR
    r : INT;
END_VAR
r := Add(1, 2);
END_PROGRAM"
        );

        var addVars = result.Variables.Where(v => v.POUName == "Add").ToList();
        var mainVars = result.Variables.Where(v => v.POUName == "Main").ToList();

        // Add 有 a, b
        Assert.Equal(2, addVars.Count);
        Assert.Contains(addVars, v => v.Name == "a");
        Assert.Contains(addVars, v => v.Name == "b");

        // Main 有 r
        Assert.Single(mainVars);
        Assert.Contains(mainVars, v => v.Name == "r");
    }

    [Fact]
    public void BreakpointHit_AllVarBlockKinds_Included()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION F : INT
VAR_INPUT
    in1 : INT;
END_VAR
VAR_OUTPUT
    out1 : REAL;
END_VAR
VAR_IN_OUT
    inout1 : BOOL;
END_VAR
VAR
    local1 : DINT;
END_VAR
F := in1;
END_FUNCTION"
        );

        var fVars = result.Variables.Where(v => v.POUName == "F").ToList();

        Assert.Equal(4, fVars.Count);
        Assert.Contains(fVars, v => v.VarBlockKind == "VAR_INPUT" && v.Name == "in1");
        Assert.Contains(fVars, v => v.VarBlockKind == "VAR_OUTPUT" && v.Name == "out1");
        Assert.Contains(fVars, v => v.VarBlockKind == "VAR_IN_OUT" && v.Name == "inout1");
        Assert.Contains(fVars, v => v.VarBlockKind == "VAR" && v.Name == "local1");
    }

    [Fact]
    public void BreakpointHit_VariableTypes_Available()
    {
        // 调试器变量窗口需要类型信息
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR
    speed : INT;
    position : REAL;
    flag : BOOL;
END_VAR
speed := 1;
END_PROGRAM"
        );

        var vars = result.Variables.Where(v => v.POUName == "Main").ToList();

        Assert.Contains(vars, v => v.Name == "speed" && v.TypeName == "INT");
        Assert.Contains(vars, v => v.Name == "position" && v.TypeName == "REAL");
        Assert.Contains(vars, v => v.Name == "flag" && v.TypeName == "BOOL");
    }

    // ═══════════════════════════════════════════════════════════════
    //  8. 断点命中：调用栈构建
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BreakpointHit_CallStackSingleLevel()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION Add : INT
VAR_INPUT a : INT; b : INT; END_VAR
Add := a + b;
END_FUNCTION

PROGRAM Main
VAR r : INT; END_VAR
r := Add(1, 2);
END_PROGRAM"
        );

        // 断点命中在 Add，谁调用了它？
        var callers = result
            .CallSites.Where(c => c.CalleeName == "Add")
            .Select(c => c.CallerName)
            .Distinct()
            .ToList();

        Assert.Single(callers);
        Assert.Equal("Main", callers[0]);
    }

    [Fact]
    public void BreakpointHit_CallStackMultiLevel()
    {
        //  Main → Square → Multiply
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION Multiply : INT
VAR_INPUT a : INT; b : INT; END_VAR
Multiply := a * b;
END_FUNCTION

FUNCTION Square : INT
VAR_INPUT x : INT; END_VAR
Square := Multiply(x, x);
END_FUNCTION

PROGRAM Main
VAR r : INT; END_VAR
r := Square(5);
END_PROGRAM"
        );

        // 第 1 层回溯：谁调用了 Multiply？→ Square
        var level1 = result
            .CallSites.Where(c => c.CalleeName == "Multiply")
            .Select(c => c.CallerName)
            .Distinct()
            .ToList();
        Assert.Single(level1);
        Assert.Equal("Square", level1[0]);

        // 第 2 层回溯：谁调用了 Square？→ Main
        var level2 = result
            .CallSites.Where(c => c.CalleeName == "Square")
            .Select(c => c.CallerName)
            .Distinct()
            .ToList();
        Assert.Single(level2);
        Assert.Equal("Main", level2[0]);
    }

    [Fact]
    public void BreakpointHit_CallStackWithCallLine()
    {
        //  ⑨ a := Calc(10);
        //  ⑩ b := Calc(20);
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION Calc : INT
VAR_INPUT x : INT; END_VAR
Calc := x * 2;
END_FUNCTION

PROGRAM Main
VAR a : INT; b : INT; END_VAR
a := Calc(10);
b := Calc(20);
END_PROGRAM"
        );

        // 每个调用点都有精确行号，调试器可以显示「从哪一行调用的」
        var calls = result.CallSites.Where(c => c.CalleeName == "Calc").ToList();

        Assert.Equal(2, calls.Count);
        Assert.Contains(calls, c => c.Line == 9);
        Assert.Contains(calls, c => c.Line == 10);
    }

    // ═══════════════════════════════════════════════════════════════
    //  9. 综合场景：端到端断点验证
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void EndToEnd_BreakpointMap_AllExecutableLinesIdentified()
    {
        //  ⑧ x := 10;
        //  ⑨ y := 20;
        //  ⑩ IF x > y THEN
        //  ⑪     x := y;
        //  ⑫ END_IF
        //  ⑬ FOR i := 0 TO 9 DO
        //  ⑭     y := y + i;
        //  ⑮ END_FOR
        //  ⑯ x := x + y;
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR
    x : INT;
    y : INT;
    i : INT;
END_VAR
x := 10;
y := 20;
IF x > y THEN
    x := y;
END_IF
FOR i := 0 TO 9 DO
    y := y + i;
END_FOR
x := x + y;
END_PROGRAM"
        );

        var stmtMap = CollectStatementLineMap(result.AST);

        // 7 个可执行语句
        Assert.Equal(7, stmtMap.Count);

        Assert.Contains(stmtMap, s => s.Line == 8 && s.Node is AssignmentNode); // x := 10
        Assert.Contains(stmtMap, s => s.Line == 9 && s.Node is AssignmentNode); // y := 20
        Assert.Contains(stmtMap, s => s.Line == 10 && s.Node is IfNode); // IF x > y
        Assert.Contains(stmtMap, s => s.Line == 11 && s.Node is AssignmentNode); // x := y
        Assert.Contains(stmtMap, s => s.Line == 13 && s.Node is ForNode); // FOR i := 0
        Assert.Contains(stmtMap, s => s.Line == 14 && s.Node is AssignmentNode); // y := y + i
        Assert.Contains(stmtMap, s => s.Line == 16 && s.Node is AssignmentNode); // x := x + y
    }

    [Fact]
    public void EndToEnd_FullDebugScenario()
    {
        // 综合验证：函数断点 + 变量窗口 + 写入监控 + 调用栈 + 参数信息 + 类型信息 + 引用
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE Point :
STRUCT
    X : REAL;
    Y : REAL;
END_STRUCT
;
END_TYPE;

FUNCTION DistanceSq : REAL
VAR_INPUT
    a : Point;
    b : Point;
END_VAR
VAR
    dx : REAL;
    dy : REAL;
END_VAR
dx := a.X - b.X;
dy := a.Y - b.Y;
DistanceSq := dx * dx + dy * dy;
END_FUNCTION

PROGRAM Main
VAR
    p1 : Point;
    p2 : Point;
    dist : REAL;
END_VAR
p1.X := 1.0;
p1.Y := 2.0;
p2.X := 4.0;
p2.Y := 6.0;
dist := DistanceSq(p1, p2);
END_PROGRAM"
        );

        // 1. 函数断点：DistanceSq 入口行
        var distFunc = result.POUs.FirstOrDefault(p => p.Name == "DistanceSq");
        Assert.NotNull(distFunc);
        Assert.Equal(POUKind.Function, distFunc.Kind);
        Assert.Equal("REAL", distFunc.ReturnTypeName);

        // 2. 断点命中后变量窗口
        var distVars = result.Variables.Where(v => v.POUName == "DistanceSq").ToList();
        Assert.Equal(4, distVars.Count);
        Assert.Contains(distVars, v => v.Name == "a" && v.VarBlockKind == "VAR_INPUT");
        Assert.Contains(distVars, v => v.Name == "dx" && v.VarBlockKind == "VAR");

        // 3. 写入监控：dx 被写入的位置
        var dxWrites = result
            .References.Where(r => r.SymbolName == "dx" && r.Usage == ReferenceUsage.Write)
            .ToList();
        Assert.Single(dxWrites);

        // 4. 调用栈：谁调用了 DistanceSq？
        var callers = result.CallSites.Where(c => c.CalleeName == "DistanceSq").ToList();
        Assert.Single(callers);
        Assert.Equal("Main", callers[0].CallerName);

        // 5. 调用参数信息
        Assert.Equal(2, callers[0].ArgumentCount);
        Assert.Equal("STRUCT(2 fields)", callers[0].Arguments[0].ParameterTypeName);
        Assert.Equal("p1", callers[0].Arguments[0].ExpressionSummary);
        Assert.Equal("p2", callers[0].Arguments[1].ExpressionSummary);

        // 6. 自定义类型（结构体字段提示）
        var pointType = result.CustomTypes.FirstOrDefault(t => t.Name == "Point");
        Assert.NotNull(pointType);
        Assert.Equal(TypeKind.Struct, pointType!.Kind);
        Assert.Equal(2, pointType.Fields!.Count);

        // 7. 符号引用：声明和调用
        Assert.Contains(
            result.References,
            r => r.SymbolName == "DistanceSq" && r.Usage == ReferenceUsage.Declaration
        );
        Assert.Contains(
            result.References,
            r => r.SymbolName == "DistanceSq" && r.Usage == ReferenceUsage.Call
        );

        // 8. 行断点：验证语句映射存在
        var stmtMap = CollectStatementLineMap(result.AST);
        Assert.NotEmpty(stmtMap);
    }

    [Fact]
    public void EndToEnd_IsBreakableLine_ExecutableVsNonExecutable()
    {
        //  ② PROGRAM Main        ← 不可断
        //  ③ VAR                  ← 不可断
        //  ④     x : INT;         ← 不可断
        //  ⑤     y : INT;         ← 不可断
        //  ⑥ END_VAR             ← 不可断
        //  ⑦ x := 10;            ← 可断
        //  ⑧ IF x > 5 THEN       ← 可断
        //  ⑨     y := x;         ← 可断
        //  ⑩ END_IF              ← 不可断
        //  ⑪ END_PROGRAM         ← 不可断
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR
    x : INT;
    y : INT;
END_VAR
x := 10;
IF x > 5 THEN
    y := x;
END_IF
END_PROGRAM"
        );

        var stmtMap = CollectStatementLineMap(result.AST);
        var breakableLines = stmtMap.Select(s => s.Line).Distinct().ToHashSet();

        // 可断点行
        Assert.Contains(7, breakableLines); // x := 10
        Assert.Contains(8, breakableLines); // IF x > 5
        Assert.Contains(9, breakableLines); // y := x

        // 不可断点行
        Assert.DoesNotContain(2, breakableLines); // PROGRAM Main
        Assert.DoesNotContain(3, breakableLines); // VAR
        Assert.DoesNotContain(4, breakableLines); // x : INT;
        Assert.DoesNotContain(5, breakableLines); // y : INT;
        Assert.DoesNotContain(6, breakableLines); // END_VAR
        Assert.DoesNotContain(10, breakableLines); // END_IF
        Assert.DoesNotContain(11, breakableLines); // END_PROGRAM
    }
}
