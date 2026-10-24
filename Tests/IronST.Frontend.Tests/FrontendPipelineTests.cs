// tests/IronST.Frontend.Tests/FrontendPipelineTests.cs
using IronST.Common.Diagnostics;
using IronST.Common.Symbol;
using IronST.Common.Types;
using IronST.Frontend.AST;

namespace IronST.Frontend.Tests;

public class FrontendPipelineTests
{
    // ═══════════════════════════════════════════════════════════════
    //  1. 基本程序解析
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void EmptyProgram_Parses()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
END_PROGRAM
"
        );
        var prog = Assert.Single(result.AST.POUs);
        Assert.Equal("Main", prog.Name);
        Assert.IsType<ProgramNode>(prog);
    }

    [Fact]
    public void ProgramWithVarAndAssignment_ResolvesTypes()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR
    x : INT;
END_VAR
x := 42;
END_PROGRAM
"
        );
        Assert.True(result.Success);
        var assign = (AssignmentNode)result.AST.POUs[0].Body[0];
        Assert.Equal(IECType.Int, assign.Target.ResolvedType);
    }

    [Fact]
    public void MultiplePOUs_InOneUnit()
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
    result : INT;
END_VAR
result := Add(1, 2);
END_PROGRAM
"
        );
        Assert.Equal(2, result.AST.POUs.Count);

        // 现在按源码顺序：Function 在前，Program 在后
        Assert.IsType<FunctionNode>(result.AST.POUs[0]);
        Assert.IsType<ProgramNode>(result.AST.POUs[1]);
        Assert.Equal("Add", result.AST.POUs[0].Name);
        Assert.Equal("Main", result.AST.POUs[1].Name);
    }

    // ═══════════════════════════════════════════════════════════════
    //  2. 函数声明与返回值注入
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Function_RegistersReturnTypeAndParameters()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION Double : DINT
VAR_INPUT
    x : DINT;
END_VAR
Double := x * 2;
END_FUNCTION
"
        );
        var sym = result.Symbols.Resolve("Double") as FunctionSymbol;
        Assert.NotNull(sym);
        Assert.Equal(IECType.DInt, sym!.ReturnType);
        Assert.Single(sym.Parameters);
        Assert.Equal("x", sym.Parameters[0].Name);
        Assert.Equal(VarScope.Input, sym.Parameters[0].Scope);
    }

    [Fact]
    public void FunctionBody_AssignsToFunctionName_ResolvesAsReturnType()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION Inc : INT
VAR_INPUT
    x : INT;
END_VAR
Inc := x + 1;
END_FUNCTION
"
        );
        var fn = (FunctionNode)result.AST.POUs[0];
        var assign = (AssignmentNode)fn.Body[0];
        Assert.Equal(IECType.Int, assign.Target.ResolvedType);
    }

    [Fact]
    public void Function_CaseInsensitiveReturnVarRef()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION Calc : DINT
VAR_INPUT
    x : DINT;
END_VAR
calc := x + 1;
END_FUNCTION
"
        );
        var assign = (AssignmentNode)result.AST.POUs[0].Body[0];
        Assert.Equal(IECType.DInt, assign.Target.ResolvedType);
    }

    [Fact]
    public void Program_RegisteredAsVoidFunction()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM P
VAR x : INT; END_VAR
x := 1;
END_PROGRAM
"
        );
        var sym = result.Symbols.Resolve("P") as FunctionSymbol;
        Assert.NotNull(sym);
        Assert.Equal(IECType.Void, sym!.ReturnType);
        Assert.Empty(sym.Parameters);
    }

    // ═══════════════════════════════════════════════════════════════
    //  3. 变量声明作用域
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void AllVariableScopes_Collected()
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
END_FUNCTION
"
        );
        var sym = result.Symbols.Resolve("F") as FunctionSymbol;
        Assert.NotNull(sym);
        Assert.Equal(3, sym!.Parameters.Count);
        Assert.Contains(sym.Parameters, p => p.Name == "in1" && p.Scope == VarScope.Input);
        Assert.Contains(sym.Parameters, p => p.Name == "out1" && p.Scope == VarScope.Output);
        Assert.Contains(sym.Parameters, p => p.Name == "inout1" && p.Scope == VarScope.InOut);
    }

    [Fact]
    public void MultipleVarsInOneDecl_AllResolve()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM P
VAR
    a, b, c : INT;
END_VAR
a := 1;
b := 2;
c := 3;
END_PROGRAM
"
        );
        Assert.True(result.Success);
        Assert.All(
            result.AST.POUs[0].Body,
            stmt =>
            {
                var assign = (AssignmentNode)stmt;
                Assert.Equal(IECType.Int, assign.Target.ResolvedType);
            }
        );
    }

    // ═══════════════════════════════════════════════════════════════
    //  4. STRING(N)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void STRING_ExplicitLength_ASTNodeCorrect()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM P
VAR
    name : STRING(50);
END_VAR
name := 'hello';
END_PROGRAM
"
        );
        var varDecl = result.AST.POUs[0].VarBlocks[0].Items[0];
        var simpleType = Assert.IsType<SimpleDataTypeNode>(varDecl.TypeName);
        Assert.NotNull(simpleType.SizeParam);
        var sizeLit = Assert.IsType<LiteralExprNode>(simpleType.SizeParam);
        Assert.Equal(50L, sizeLit.ParsedValue);
        Assert.Equal("STRING", simpleType.TypeName);
    }

    [Fact]
    public void WSTRING_ExplicitLength_ASTNodeCorrect()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM P
VAR
    name : WSTRING(100);
END_VAR
name := 'hello';
END_PROGRAM
"
        );
        var varDecl = result.AST.POUs[0].VarBlocks[0].Items[0];
        var simpleType = Assert.IsType<SimpleDataTypeNode>(varDecl.TypeName);
        Assert.NotNull(simpleType.SizeParam);
        var sizeLit = Assert.IsType<LiteralExprNode>(simpleType.SizeParam);
        Assert.Equal(100L, sizeLit.ParsedValue);
        Assert.Equal("WSTRING", simpleType.TypeName);
    }

    [Fact]
    public void STRING_NoExplicitLength_Defaults()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM P
VAR
    name : STRING;
END_VAR
name := 'hello';
END_PROGRAM
"
        );
        var varDecl = result.AST.POUs[0].VarBlocks[0].Items[0];
        var simpleType = Assert.IsType<SimpleDataTypeNode>(varDecl.TypeName);
        Assert.Null(simpleType.SizeParam);
    }

    // ═══════════════════════════════════════════════════════════════
    //  5. 函数调用 / 数组下标
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TypeConversionCall_ResolvesToBuiltinType()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM P
VAR
    r : REAL;
    x : INT;
END_VAR
x := INT(r);
END_PROGRAM
"
        );
        var assign = (AssignmentNode)result.AST.POUs[0].Body[0];
        var call = Assert.IsType<FunctionCallExprNode>(assign.Value);
        Assert.Equal("INT", call.Name);
        Assert.Equal(IECType.Int, call.ResolvedType);
        Assert.Single(call.Arguments);
    }

    [Fact]
    public void UserFunctionCall_ResolvesReturnType()
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
    result : INT;
END_VAR
result := Add(1, 2);
END_PROGRAM
"
        );
        // Main 是第二个 POU（索引 1）
        var mainBody = result.AST.POUs[1].Body;
        var assign = (AssignmentNode)mainBody[0];
        var call = Assert.IsType<FunctionCallExprNode>(assign.Value);
        Assert.Equal("Add", call.Name);
        Assert.Equal(IECType.Int, call.ResolvedType);
    }

    [Fact]
    public void ArraySubscript_ResolvesElementType()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM P
VAR
    arr : ARRAY[0..9] OF DINT;
    x : DINT;
END_VAR
x := arr[3];
END_PROGRAM
"
        );
        var assign = (AssignmentNode)result.AST.POUs[0].Body[0];
        var varExpr = Assert.IsType<VariableExprNode>(assign.Value);
        Assert.Single(varExpr.Segments);
        var suffix = Assert.Single(varExpr.Segments[0].Suffixes);
        Assert.NotNull(suffix.Subscripts);
        Assert.Single(suffix.Subscripts!);
        Assert.Equal(IECType.DInt, varExpr.ResolvedType);
    }

    [Fact]
    public void MultiDimensionalArrayAccess()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM P
VAR
    matrix : ARRAY[0..2, 0..3] OF REAL;
    x : REAL;
END_VAR
x := matrix[1, 2];
END_PROGRAM
"
        );
        var assign = (AssignmentNode)result.AST.POUs[0].Body[0];
        var varExpr = Assert.IsType<VariableExprNode>(assign.Value);
        Assert.Single(varExpr.Segments);
        var suffix = Assert.Single(varExpr.Segments[0].Suffixes);
        Assert.NotNull(suffix.Subscripts);
        Assert.Equal(2, suffix.Subscripts!.Count);
        Assert.Equal(IECType.Real, varExpr.ResolvedType);
    }

    // ═══════════════════════════════════════════════════════════════
    //  6. 表达式类型推导
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BinaryExpr_IntPlusDInt_WidensToDInt()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM P
VAR
    a : INT;
    b : DINT;
    c : DINT;
END_VAR
c := a + b;
END_PROGRAM
"
        );
        var assign = (AssignmentNode)result.AST.POUs[0].Body[0];
        Assert.Equal(IECType.DInt, assign.Value.ResolvedType);
    }

    [Fact]
    public void BinaryExpr_DIntPlusReal_WidensToReal()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM P
VAR
    a : DINT;
    b : REAL;
    c : REAL;
END_VAR
c := a + b;
END_PROGRAM
"
        );
        var assign = (AssignmentNode)result.AST.POUs[0].Body[0];
        Assert.Equal(IECType.Real, assign.Value.ResolvedType);
    }

    [Fact]
    public void ComparisonExpr_ReturnsBool()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM P
VAR
    a : INT;
    b : INT;
    flag : BOOL;
END_VAR
flag := a > b;
END_PROGRAM
"
        );
        var assign = (AssignmentNode)result.AST.POUs[0].Body[0];
        Assert.Equal(IECType.Bool, assign.Value.ResolvedType);
    }

    [Fact]
    public void LogicalExpr_AND_ReturnsBool()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM P
VAR
    a : BOOL;
    b : BOOL;
    c : BOOL;
END_VAR
c := a AND b;
END_PROGRAM
"
        );
        var assign = (AssignmentNode)result.AST.POUs[0].Body[0];
        Assert.Equal(IECType.Bool, assign.Value.ResolvedType);
    }

    [Fact]
    public void UnaryNot_ReturnsBool()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM P
VAR
    flag : BOOL;
    out : BOOL;
END_VAR
out := NOT flag;
END_PROGRAM
"
        );
        var assign = (AssignmentNode)result.AST.POUs[0].Body[0];
        Assert.Equal(IECType.Bool, assign.Value.ResolvedType);
    }

    [Fact]
    public void UnaryNeg_ReturnsOperandType()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM P
VAR
    x : DINT;
    y : DINT;
END_VAR
y := -x;
END_PROGRAM
"
        );
        var assign = (AssignmentNode)result.AST.POUs[0].Body[0];
        Assert.Equal(IECType.DInt, assign.Value.ResolvedType);
    }

    // ═══════════════════════════════════════════════════════════════
    //  7. 幂运算右结合
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void PowerOp_RightAssociative()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM P
VAR
    y : DINT;
END_VAR
y := 2 ** 3 ** 2;
END_PROGRAM
"
        );
        var assign = (AssignmentNode)result.AST.POUs[0].Body[0];

        // 应解析为 2 ** (3 ** 2) = 2^9 = 512
        var outer = Assert.IsType<BinaryExprNode>(assign.Value);
        Assert.Equal(BinaryOp.Power, outer.Op);
        Assert.IsType<LiteralExprNode>(outer.Left); // 2

        var inner = Assert.IsType<BinaryExprNode>(outer.Right);
        Assert.Equal(BinaryOp.Power, inner.Op);
        Assert.IsType<LiteralExprNode>(inner.Left); // 3
        Assert.IsType<LiteralExprNode>(inner.Right); // 2
    }

    // ═══════════════════════════════════════════════════════════════
    //  8. 控制流
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void IfStatement_WithElsIfAndElse()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM P
VAR
    x : INT;
    y : INT;
END_VAR
IF x > 0 THEN
    y := 1;
ELSIF x = 0 THEN
    y := 0;
ELSE
    y := -1;
END_IF
END_PROGRAM
"
        );
        var ifStmt = Assert.IsType<IfNode>(result.AST.POUs[0].Body[0]);
        Assert.NotEmpty(ifStmt.ThenBody);
        Assert.Single(ifStmt.ElsIfBranches);
        Assert.NotNull(ifStmt.ElseBody);
        Assert.Single(ifStmt.ElseBody!);
    }

    [Fact]
    public void ForLoop_WithBy()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM P
VAR
    i : INT;
    sum : INT;
END_VAR
sum := 0;
FOR i := 0 TO 9 BY 1 DO
    sum := sum + i;
END_FOR
END_PROGRAM
"
        );
        var forStmt = Assert.IsType<ForNode>(result.AST.POUs[0].Body[1]);
        Assert.Equal("i", forStmt.Variable);
        Assert.NotNull(forStmt.By);
        Assert.Single(forStmt.Body);
    }

    [Fact]
    public void WhileLoop()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM P
VAR
    i : INT;
END_VAR
i := 0;
WHILE i < 10 DO
    i := i + 1;
END_WHILE
END_PROGRAM
"
        );
        var whileStmt = Assert.IsType<WhileNode>(result.AST.POUs[0].Body[1]);
        Assert.Single(whileStmt.Body);
    }

    [Fact]
    public void RepeatLoop()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM P
VAR
    i : INT;
END_VAR
i := 0;
REPEAT
    i := i + 1;
UNTIL i >= 10
END_REPEAT
END_PROGRAM
"
        );
        var repeatStmt = Assert.IsType<RepeatNode>(result.AST.POUs[0].Body[1]);
        Assert.Single(repeatStmt.Body);
    }

    [Fact]
    public void CaseStatement_WithRangesAndElse()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM P
VAR
    sel : INT;
    out : INT;
END_VAR
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
END_PROGRAM
"
        );
        var caseStmt = Assert.IsType<CaseNode>(result.AST.POUs[0].Body[0]);
        Assert.Equal(3, caseStmt.Branches.Count);
        Assert.NotNull(caseStmt.ElseBody);

        // 第一个分支：单值
        Assert.Single(caseStmt.Branches[0].Labels);
        Assert.Equal(1, caseStmt.Branches[0].Labels[0].SingleValue);

        // 第二个分支：多单值
        Assert.Equal(2, caseStmt.Branches[1].Labels.Count);

        // 第三个分支：范围
        Assert.Single(caseStmt.Branches[2].Labels);
        Assert.Equal(4, caseStmt.Branches[2].Labels[0].RangeStart);
        Assert.Equal(6, caseStmt.Branches[2].Labels[0].RangeEnd);
    }

    // ═══════════════════════════════════════════════════════════════
    //  9. RETURN / EXIT
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ReturnAndExit_ParsedInNestedContext()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM P
VAR
    x : INT;
    i : INT;
END_VAR
IF x < 0 THEN
    RETURN;
END_IF
FOR i := 0 TO 10 DO
    IF i = 5 THEN
        EXIT;
    END_IF
END_FOR
END_PROGRAM
"
        );
        var ifStmt = Assert.IsType<IfNode>(result.AST.POUs[0].Body[0]);
        Assert.IsType<ReturnNode>(ifStmt.ThenBody[0]);

        var forStmt = Assert.IsType<ForNode>(result.AST.POUs[0].Body[1]);
        var innerIf = Assert.IsType<IfNode>(forStmt.Body[0]);
        Assert.IsType<ExitNode>(innerIf.ThenBody[0]);
    }

    // ═══════════════════════════════════════════════════════════════
    //  10. 类型声明
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void StructType_ParsesAndMembersResolve()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE Rect :
STRUCT
    Width : REAL;
    Height : REAL;
END_STRUCT
;
END_TYPE;

PROGRAM P
VAR
    r : Rect;
    area : REAL;
END_VAR
area := r.Width;
END_PROGRAM
"
        );
        var rectSym = result.Symbols.Resolve("Rect") as TypeSymbol;
        Assert.NotNull(rectSym);
        Assert.Equal(IECType.KindTag.STRUCT, rectSym!.Type.Kind);
        Assert.Equal(2, rectSym.Type.Fields!.Count);

        // 字段访问类型解析
        var assign = (AssignmentNode)result.AST.POUs[0].Body[0];
        Assert.Equal(IECType.Real, assign.Value.ResolvedType);
    }

    [Fact]
    public void EnumType_Parses()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE Color : (Red, Green, Blue);
END_TYPE;
"
        );
        var colorSym = result.Symbols.Resolve("Color") as TypeSymbol;
        Assert.NotNull(colorSym);
        Assert.Equal(IECType.KindTag.ENUM, colorSym!.Type.Kind);
        Assert.Equal(new[] { "Red", "Green", "Blue" }, colorSym.Type.EnumValues);
    }

    // ═══════════════════════════════════════════════════════════════
    //  11. Function Block
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void FunctionBlock_MembersRegistered()
    {
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
END_FUNCTION_BLOCK
"
        );
        var fbSym = result.Symbols.Resolve("Counter") as FunctionBlockSymbol;
        Assert.NotNull(fbSym);
        Assert.Equal(2, fbSym!.Members.Count);
        Assert.True(fbSym.Members.ContainsKey("inc"));
        Assert.True(fbSym.Members.ContainsKey("count"));
    }

    // ═══════════════════════════════════════════════════════════════
    //  12. 端到端
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void EndToEnd_PointDistSq()
    {
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
dist := DistanceSq(p1, p2);
END_PROGRAM
"
        );
        Assert.True(result.Success);

        // 源码顺序：FUNCTION 在前，PROGRAM 在后
        Assert.Equal(2, result.AST.POUs.Count);
        Assert.IsType<FunctionNode>(result.AST.POUs[0]);
        Assert.IsType<ProgramNode>(result.AST.POUs[1]);
        Assert.Equal("DistanceSq", result.AST.POUs[0].Name);
        Assert.Equal("Main", result.AST.POUs[1].Name);

        // 全局符号表
        Assert.NotNull(result.Symbols.Resolve("Point"));
        Assert.NotNull(result.Symbols.Resolve("DistanceSq"));
        Assert.NotNull(result.Symbols.Resolve("Main"));

        // DistanceSq 的签名
        var distSym = result.Symbols.Resolve("DistanceSq") as FunctionSymbol;
        Assert.NotNull(distSym);
        Assert.Equal(IECType.Real, distSym!.ReturnType);
        Assert.Equal(2, distSym.Parameters.Count);
        Assert.All(distSym.Parameters, p => Assert.Equal(IECType.KindTag.STRUCT, p.Type.Kind));

        // Main 的函数调用解析正确
        var mainBody = result.AST.POUs[1].Body;
        var mainAssign = (AssignmentNode)mainBody[0];
        var callExpr = Assert.IsType<FunctionCallExprNode>(mainAssign.Value);
        Assert.Equal(IECType.Real, callExpr.ResolvedType);
    }

    // ═══════════════════════════════════════════════════════════════
    //  13. 源码位置 (SourceSpan)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ProgramNode_HasCorrectSpan()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
END_PROGRAM
"
        );
        var prog = result.AST.POUs[0];
        // PROGRAM Main → 行 2, 列 1
        Assert.Equal(2, prog.Span.Line);
        Assert.Equal(1, prog.Span.Column);
        // END_PROGRAM → 行 3, 列 11
        Assert.Equal(3, prog.Span.EndLine);
        Assert.Equal(11, prog.Span.EndColumn);
    }

    [Fact]
    public void AssignmentNode_HasCorrectSpan()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR
    x : INT;
END_VAR
x := 42;
END_PROGRAM
"
        );
        var assign = (AssignmentNode)result.AST.POUs[0].Body[0];
        // x := 42; → 行 6, 列 1 起
        Assert.Equal(6, assign.Span.Line);
        Assert.Equal(1, assign.Span.Column);
    }

    [Fact]
    public void VariableExpr_HasCorrectSpan()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR
    myVar : INT;
END_VAR
myVar := 10;
END_PROGRAM
"
        );
        var assign = (AssignmentNode)result.AST.POUs[0].Body[0];
        var target = (VariableExprNode)assign.Target;
        // myVar → 行 6, 列 1~5
        Assert.Equal(6, target.Span.Line);
        Assert.Equal(1, target.Span.Column);
        Assert.Equal(6, target.Span.EndLine);
        Assert.Equal(5, target.Span.EndColumn);
    }

    [Fact]
    public void LiteralExpr_HasCorrectSpan()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR
    x : INT;
END_VAR
x := 12345;
END_PROGRAM
"
        );
        var assign = (AssignmentNode)result.AST.POUs[0].Body[0];
        var lit = (LiteralExprNode)assign.Value;
        // 12345 → 行 6, 列 6~10
        Assert.Equal(6, lit.Span.Line);
        Assert.Equal(6, lit.Span.Column);
        Assert.Equal(6, lit.Span.EndLine);
        Assert.Equal(10, lit.Span.EndColumn);
    }

    [Fact]
    public void BinaryExpr_SpanCoversWholeSubexpression()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"PROGRAM Main
VAR
    a, b, c : INT;
END_VAR
c := a + b;
END_PROGRAM
"
        );
        var assign = (AssignmentNode)result.AST.POUs[0].Body[0];
        var bin = (BinaryExprNode)assign.Value;
        // 验证 span 覆盖整个 a + b
        Assert.Equal(6, bin.Span.Column);
        Assert.Equal(10, bin.Span.EndColumn);
    }

    [Fact]
    public void BinaryExpr_InAssignment_SpanBetweenOperands()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR
    a, b, c : INT;
END_VAR
c := a + b;
END_PROGRAM
"
        );
        var assign = (AssignmentNode)result.AST.POUs[0].Body[0];
        var bin = (BinaryExprNode)assign.Value;

        // a + b → 行 6, 从 a 的列(6) 到 b 的列(10)
        Assert.Equal(6, bin.Span.Line);
        Assert.Equal(6, bin.Span.Column);
        Assert.Equal(6, bin.Span.EndLine);
        Assert.Equal(10, bin.Span.EndColumn);

        // 左操作数 a 的位置
        Assert.Equal(6, bin.Left.Span.Column);
        Assert.Equal(6, bin.Left.Span.EndColumn);

        // 右操作数 b 的位置
        Assert.Equal(10, bin.Right.Span.Column);
        Assert.Equal(10, bin.Right.Span.EndColumn);
    }

    // ═══════════════════════════════════════════════════════════════
    //  14. 诊断错误码（已改为 ErrorCode 枚举）
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Diagnostic_UndeclaredVar_HasCorrectSpanAndCode()
    {
        var result = FrontendTestHelper.Analyze(
            @"
PROGRAM Main
VAR
    x : INT;
END_VAR
y := x + 1;
END_PROGRAM
"
        );
        var err = result.Diagnostics.FirstOrDefault(d => d.Code == ErrorCode.E2001);
        Assert.NotNull(err);
        var span = err.Span!.Value;
        // y 在第 6 行
        Assert.Equal(6, span.Line);
        Assert.Equal(1, span.Column);
        Assert.Equal("test.st", span.FileName);
        // 验证中文描述
        Assert.Contains("未声明的标识符", err.Code.GetDescription());
    }

    [Fact]
    public void Diagnostic_SyntaxError_HasCorrectSpanAndCode()
    {
        var result = FrontendTestHelper.Analyze(
            @"
PROGRAM Main
VAR
    x : INT;
END_VAR
=== garbage ===
END_PROGRAM
"
        );
        // 语法错误应该有 E1xxx 范围的错误码
        var syntaxErr = result.Diagnostics.FirstOrDefault(d => d.Code.ToString().StartsWith("E1"));
        Assert.NotNull(syntaxErr);

        var span = syntaxErr.Span!.Value;
        // 语法错误在第 6 行
        Assert.Equal(6, span.Line);
        // 验证描述是中文
        var desc = syntaxErr.Code.GetDescription();
        Assert.False(string.IsNullOrEmpty(desc));
        Assert.DoesNotContain("E1", desc); // 描述不应包含编码本身
    }

    [Fact]
    public void Diagnostic_AllCodesHaveChineseDescriptions()
    {
        var result = FrontendTestHelper.Analyze(
            @"
PROGRAM Main
VAR
    x : INT;
END_VAR
y := x + 1;
=== bad ===
END_PROGRAM
"
        );
        // 所有诊断的错误码都应有非空的中文描述
        foreach (var diag in result.Diagnostics)
        {
            var desc = diag.Code.GetDescription();
            Assert.False(string.IsNullOrEmpty(desc), $"ErrorCode.{diag.Code} 缺少中文描述");
            // 描述本身不应是编码名（如 "E2001"）
            Assert.DoesNotContain("E2", desc);
            Assert.DoesNotContain("E1", desc);
        }
    }

    [Fact]
    public void MultiLineProgram_AllSpansHaveCorrectLines()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR
    x : INT;
    y : REAL;
END_VAR
x := 1;
y := 2.5;
x := 3;
END_PROGRAM
"
        );
        var stmts = result.AST.POUs[0].Body;
        Assert.Equal(3, stmts.Count);

        // 三条赋值分别在行 7, 8, 9
        Assert.Equal(7, stmts[0].Span.Line);
        Assert.Equal(8, stmts[1].Span.Line);
        Assert.Equal(9, stmts[2].Span.Line);
    }

    [Fact]
    public void FunctionDecl_HasCorrectSpan()
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
END_PROGRAM
"
        );
        // FUNCTION → 行 2
        Assert.Equal(2, result.AST.POUs[0].Span.Line);
        // PROGRAM → 行 10
        Assert.Equal(10, result.AST.POUs[1].Span.Line);
    }

    [Fact]
    public void FileName_PropagatedToDiagnostic()
    {
        var result = FrontendTestHelper.Analyze(
            @"
PROGRAM Main
END_PROGRAM
"
        );
        // 所有 Span 都应该带文件名
        foreach (var diag in result.Diagnostics)
        {
            Assert.Equal("test.st", diag.Span!.Value.FileName);
        }
    }

    [Fact]
    public void SourceSpan_ToString_WithFileName()
    {
        var span = new SourceSpan(10, 5, 10, 15, "main.st");
        Assert.Equal("main.st:10:5", span.ToString());
    }

    [Fact]
    public void SourceSpan_ToString_WithoutFileName()
    {
        var span = new SourceSpan(10, 5, 10, 15);
        Assert.Equal("10:5", span.ToString());
    }

    [Fact]
    public void SourceSpan_Unknown_IsZeroed()
    {
        var span = SourceSpan.Unknown;
        Assert.Equal(0, span.Line);
        Assert.Equal(0, span.Column);
        Assert.Equal(0, span.EndLine);
        Assert.Equal(0, span.EndColumn);
        Assert.Null(span.FileName);
    }

    // ═══════════════════════════════════════════════════════════════
    //  15. 结构体相关（新增）
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Struct_MultiFieldAccess_ResolvesCorrectTypes()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE Point : STRUCT
    x : INT;
    y : INT;
END_STRUCT; END_TYPE;

PROGRAM Main
VAR
    p : Point;
    sum : INT;
END_VAR
p.x := 10;
p.y := 20;
sum := p.x + p.y;
END_PROGRAM
"
        );
        // p.x + p.y 的结果类型应该是 INT
        var assign = (AssignmentNode)result.AST.POUs[0].Body[2];
        Assert.Equal(IECType.Int, assign.Value.ResolvedType);
    }

    [Fact]
    public void Struct_MixedFieldTypes()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE Sensor : STRUCT
    id : INT;
    value : REAL;
    active : BOOL;
END_STRUCT; END_TYPE;

PROGRAM Main
VAR
    s : Sensor;
    n : INT;
    v : REAL;
    flag : BOOL;
END_VAR
n := s.id;
v := s.value;
flag := s.active;
END_PROGRAM
"
        );
        var stmts = result.AST.POUs[0].Body;

        // n := s.id → INT
        var a0 = (AssignmentNode)stmts[0];
        Assert.Equal(IECType.Int, a0.Value.ResolvedType);

        // v := s.value → REAL
        var a1 = (AssignmentNode)stmts[1];
        Assert.Equal(IECType.Real, a1.Value.ResolvedType);

        // flag := s.active → BOOL
        var a2 = (AssignmentNode)stmts[2];
        Assert.Equal(IECType.Bool, a2.Value.ResolvedType);
    }

    [Fact]
    public void Struct_CrossStructAssignment()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE Point : STRUCT
    x : INT;
    y : INT;
END_STRUCT; END_TYPE;

PROGRAM Main
VAR
    p1 : Point;
    p2 : Point;
    pSum : Point;
END_VAR
p1.x := 10;
p1.y := 20;
p2.x := 30;
p2.y := 40;
pSum.x := p1.x + p2.x;
pSum.y := p1.y + p2.y;
END_PROGRAM
"
        );
        // pSum.x := p1.x + p2.x
        var assign = (AssignmentNode)result.AST.POUs[0].Body[4];
        Assert.Equal(IECType.Int, assign.Value.ResolvedType);
    }

    [Fact]
    public void Struct_FieldAsFunctionArg_Resolves()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE Point : STRUCT
    x : INT;
    y : INT;
END_STRUCT; END_TYPE;

FUNCTION AddXY : INT
VAR_INPUT
    a : INT;
    b : INT;
END_VAR
AddXY := a + b;
END_FUNCTION

PROGRAM Main
VAR
    p : Point;
    result : INT;
END_VAR
p.x := 10;
p.y := 20;
result := AddXY(p.x, p.y);
END_PROGRAM
"
        );
        // result := AddXY(p.x, p.y)
        var mainBody = result.AST.POUs[1].Body;
        var assign = (AssignmentNode)mainBody[2];
        var call = Assert.IsType<FunctionCallExprNode>(assign.Value);
        Assert.Equal("AddXY", call.Name);
        Assert.Equal(IECType.Int, call.ResolvedType);
    }
}
