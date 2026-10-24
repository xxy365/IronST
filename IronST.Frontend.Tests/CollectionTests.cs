using IronST.Common.Symbol;
using IronST.Frontend.Analysis;
using IronST.Frontend.AST;

namespace IronST.Frontend.Tests;

/// <summary>
/// 测试 SemanticAnalyzer 在分析过程中收集的各类信息
/// （Variables / POUs / CustomTypes / References / CallSites）
/// </summary>
public class CollectionTests
{
    // ═══════════════════════════════════════════════════════════════
    //  1. Variables 收集
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Variables_SingleVarBlock_CollectsAllVars()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR
    x : INT;
    y : REAL;
    flag : BOOL;
END_VAR
x := 1;
END_PROGRAM
"
        );

        Assert.Equal(3, result.Variables.Count);

        var vx = result.Variables.First(v => v.Name == "x");
        Assert.Equal("INT", vx.TypeName);
        Assert.Equal("Main", vx.POUName);
        Assert.Equal("VAR", vx.VarBlockKind);

        var vy = result.Variables.First(v => v.Name == "y");
        Assert.Equal("REAL", vy.TypeName);

        var vf = result.Variables.First(v => v.Name == "flag");
        Assert.Equal("BOOL", vf.TypeName);
    }

    [Fact]
    public void Variables_MultipleVarsInOneDecl_CollectsAll()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR
    a, b, c : INT;
END_VAR
a := 1;
END_PROGRAM
"
        );

        Assert.Equal(3, result.Variables.Count);
        Assert.Contains(result.Variables, v => v.Name == "a");
        Assert.Contains(result.Variables, v => v.Name == "b");
        Assert.Contains(result.Variables, v => v.Name == "c");
    }

    [Fact]
    public void Variables_DifferentVarBlocks_CorrectKinds()
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

        Assert.Equal("VAR_INPUT", result.Variables.First(v => v.Name == "in1").VarBlockKind);
        Assert.Equal("VAR_OUTPUT", result.Variables.First(v => v.Name == "out1").VarBlockKind);
        Assert.Equal("VAR_IN_OUT", result.Variables.First(v => v.Name == "inout1").VarBlockKind);
        Assert.Equal("VAR", result.Variables.First(v => v.Name == "local1").VarBlockKind);
    }

    [Fact]
    public void Variables_FunctionBlock_CollectsMembers()
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

        Assert.Equal(2, result.Variables.Count);
        Assert.Contains(result.Variables, v => v.Name == "inc" && v.VarBlockKind == "VAR_INPUT");
        Assert.Contains(result.Variables, v => v.Name == "count" && v.VarBlockKind == "VAR");
    }

    [Fact]
    public void Variables_MultiplePOUs_CorrectPOUName()
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

        Assert.Equal(2, result.Variables.Count(v => v.POUName == "Add"));
        Assert.Single(result.Variables.Where(v => v.POUName == "Main"));

        Assert.Contains(result.Variables, v => v.Name == "a" && v.POUName == "Add");
        Assert.Contains(result.Variables, v => v.Name == "result" && v.POUName == "Main");
    }

    [Fact]
    public void Variables_TypeName_DisplayedCorrectly()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR
    name : STRING(50);
    temp : REAL;
    big : LREAL;
END_VAR
name := 'hello';
END_PROGRAM"
        );

        Assert.Contains(result.Variables, v => v.Name == "name" && v.TypeName == "STRING(?)");
        Assert.Contains(result.Variables, v => v.Name == "temp" && v.TypeName == "REAL");
        Assert.Contains(result.Variables, v => v.Name == "big" && v.TypeName == "LREAL");
    }

    [Fact]
    public void Variables_EmptyProgram_ReturnsEmptyList()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
END_PROGRAM"
        );

        Assert.Empty(result.Variables);
    }

    // ═══════════════════════════════════════════════════════════════
    //  2. POUs 收集
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void POUs_SingleProgram_CollectsCorrectly()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR x : INT; END_VAR
x := 1;
END_PROGRAM"
        );

        var userPOUs = result.POUs.Where(p => !p.IsExternal).ToList();
        Assert.Single(userPOUs);
        var pou = userPOUs[0];
        Assert.Equal("Main", pou.Name);
        Assert.Equal(POUKind.Program, pou.Kind);
        Assert.Null(pou.ReturnTypeName);
        Assert.Empty(pou.Parameters);
    }

    [Fact]
    public void POUs_Function_HasReturnTypeAndParameters()
    {
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

        var pou = result.POUs.First(p => p.Name == "Add" && !p.IsExternal);
        Assert.Equal(POUKind.Function, pou.Kind);
        Assert.Equal("INT", pou.ReturnTypeName);
        Assert.Equal(2, pou.Parameters.Count);
        Assert.Contains(pou.Parameters, p => p.Name == "a" && p.Direction == "VAR_INPUT");
        Assert.Contains(pou.Parameters, p => p.Name == "b" && p.Direction == "VAR_INPUT");
    }

    [Fact]
    public void POUs_FunctionBlock_HasMembers()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION_BLOCK MotorCtrl
VAR_INPUT
    targetSpeed : INT;
END_VAR
VAR_OUTPUT
    actualSpeed : INT;
END_VAR
VAR
    running : BOOL;
END_VAR
actualSpeed := targetSpeed;
END_FUNCTION_BLOCK"
        );

        var pou = result.POUs.First(p => p.Name == "MotorCtrl" && !p.IsExternal);
        Assert.Equal(POUKind.FunctionBlock, pou.Kind);
        Assert.Null(pou.ReturnTypeName);
        Assert.Equal(3, pou.Parameters.Count);

        Assert.Contains(pou.Parameters, p => p.Name == "targetSpeed" && p.Direction == "VAR_INPUT");
        Assert.Contains(pou.Parameters, p => p.Name == "actualSpeed" && p.Direction == "VAR_OUTPUT");
        Assert.Contains(pou.Parameters, p => p.Name == "running" && p.Direction == "VAR");
    }

    [Fact]
    public void POUs_MultiplePOUs_AllCollected()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION Double : DINT
VAR_INPUT x : DINT; END_VAR
Double := x * 2;
END_FUNCTION

FUNCTION_BLOCK Counter
VAR_INPUT inc : INT; END_VAR
VAR count : INT; END_VAR
count := count + inc;
END_FUNCTION_BLOCK

PROGRAM Main
VAR r : INT; END_VAR
r := 1;
END_PROGRAM"
        );

        var userPOUs = result.POUs.Where(p => !p.IsExternal).ToList();
        Assert.Equal(3, userPOUs.Count);
        Assert.Contains(userPOUs, p => p.Kind == POUKind.Function && p.Name == "Double");
        Assert.Contains(userPOUs, p => p.Kind == POUKind.FunctionBlock && p.Name == "Counter");
        Assert.Contains(userPOUs, p => p.Kind == POUKind.Program && p.Name == "Main");
    }

    [Fact]
    public void POUs_FunctionWithMultipleDirections_ParametersCorrect()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION F : INT
VAR_INPUT x : INT; END_VAR
VAR_OUTPUT y : REAL; END_VAR
VAR_IN_OUT z : BOOL; END_VAR
F := x;
END_FUNCTION"
        );

        var pou = result.POUs.First(p => p.Name == "F" && !p.IsExternal);
        Assert.Equal(3, pou.Parameters.Count);
        Assert.Contains(pou.Parameters, p => p.Name == "x" && p.Direction == "VAR_INPUT");
        Assert.Contains(pou.Parameters, p => p.Name == "y" && p.Direction == "VAR_OUTPUT");
        Assert.Contains(pou.Parameters, p => p.Name == "z" && p.Direction == "VAR_IN_OUT");
    }

    // ═══════════════════════════════════════════════════════════════
    //  3. CustomTypes 收集
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void CustomTypes_StructType_CollectsFields()
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

PROGRAM P
VAR p : Point; END_VAR
p.X := 1.0;
END_PROGRAM"
        );

        var pointType = result.CustomTypes.FirstOrDefault(t => t.Name == "Point");
        Assert.NotNull(pointType);
        Assert.Equal(TypeKind.Struct, pointType!.Kind);
        Assert.NotNull(pointType.Fields);
        Assert.Equal(2, pointType.Fields!.Count);
        Assert.Contains(pointType.Fields, f => f.Name == "X" && f.TypeName == "REAL");
        Assert.Contains(pointType.Fields, f => f.Name == "Y" && f.TypeName == "REAL");
    }

    [Fact]
    public void CustomTypes_EnumType_CollectsMembers()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE Color : (Red, Green, Blue);
END_TYPE;"
        );

        var colorType = result.CustomTypes.FirstOrDefault(t => t.Name == "Color");
        Assert.NotNull(colorType);
        Assert.Equal(TypeKind.Enum, colorType!.Kind);
        Assert.NotNull(colorType.EnumMembers);
        Assert.Equal(new[] { "Red", "Green", "Blue" }, colorType.EnumMembers);
    }

    [Fact]
    public void CustomTypes_SimpleTypeAlias_CollectsCorrectly()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE MyInt : DINT;
END_TYPE;"
        );

        var myIntType = result.CustomTypes.FirstOrDefault(t => t.Name == "MyInt");
        Assert.NotNull(myIntType);
        Assert.Equal(TypeKind.SimpleType, myIntType!.Kind);
        Assert.Equal("DINT", myIntType.UnderlyingType);
    }

    [Fact]
    public void CustomTypes_MultipleTypes_AllCollected()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE Color : (Red, Green, Blue);
END_TYPE;

TYPE Point :
STRUCT
    X : INT;
    Y : INT;
END_STRUCT
;
END_TYPE;

TYPE MyReal : REAL;
END_TYPE;

PROGRAM P
VAR p : Point; END_VAR
p.X := 1;
END_PROGRAM"
        );

        Assert.Equal(3, result.CustomTypes.Count);
        Assert.Contains(result.CustomTypes, t => t.Name == "Color" && t.Kind == TypeKind.Enum);
        Assert.Contains(result.CustomTypes, t => t.Name == "Point" && t.Kind == TypeKind.Struct);
        Assert.Contains(result.CustomTypes, t => t.Name == "MyReal" && t.Kind == TypeKind.SimpleType);
    }

    [Fact]
    public void CustomTypes_NoTypeDeclarations_ReturnsEmpty()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR x : INT; END_VAR
x := 1;
END_PROGRAM"
        );

        Assert.Empty(result.CustomTypes);
    }

    // ═══════════════════════════════════════════════════════════════
    //  4. References 收集
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void References_VariableDeclaration_HasDeclarationUsage()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR
    x : INT;
END_VAR
x := 1;
END_PROGRAM"
        );

        var declRef = result.References.FirstOrDefault(r =>
            r.SymbolName == "x" && r.Usage == ReferenceUsage.Declaration);
        Assert.NotNull(declRef);
        Assert.Equal(SymbolKind.Variable, declRef!.Kind);
        Assert.Equal("Main", declRef.POUName);
    }

    [Fact]
    public void References_VariableRead_HasReadUsage()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR
    x : INT;
    y : INT;
END_VAR
y := x + 1;
END_PROGRAM"
        );

        var readRef = result.References.FirstOrDefault(r =>
            r.SymbolName == "x" && r.Usage == ReferenceUsage.Read);
        Assert.NotNull(readRef);
        Assert.Equal(SymbolKind.Variable, readRef!.Kind);
    }

    [Fact]
    public void References_VariableWrite_HasWriteUsage()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR
    x : INT;
END_VAR
x := 42;
END_PROGRAM"
        );

        var writeRef = result.References.FirstOrDefault(r =>
            r.SymbolName == "x" && r.Usage == ReferenceUsage.Write);
        Assert.NotNull(writeRef);
    }

    [Fact]
    public void References_Assignment_HasBothReadAndWrite()
    {
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

        Assert.Contains(xRefs, r => r.Usage == ReferenceUsage.Declaration);
        Assert.Contains(xRefs, r => r.Usage == ReferenceUsage.Write);
        Assert.Contains(xRefs, r => r.Usage == ReferenceUsage.Read);
    }

    [Fact]
    public void References_FunctionCall_HasCallUsage()
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

        var callRef = result.References.FirstOrDefault(r =>
            r.SymbolName == "Add" && r.Usage == ReferenceUsage.Call);
        Assert.NotNull(callRef);
        Assert.Equal(SymbolKind.Function, callRef!.Kind);
        Assert.Equal("Main", callRef.POUName);
    }

    [Fact]
    public void References_FunctionDeclaration_HasDeclarationUsage()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION Calc : INT
VAR_INPUT x : INT; END_VAR
Calc := x;
END_FUNCTION

PROGRAM Main
VAR r : INT; END_VAR
r := Calc(1);
END_PROGRAM"
        );

        var declRef = result.References.FirstOrDefault(r =>
            r.SymbolName == "Calc" && r.Usage == ReferenceUsage.Declaration);
        Assert.NotNull(declRef);
        Assert.Equal(SymbolKind.Function, declRef!.Kind);
    }

    [Fact]
    public void References_MultipleReadsOfSameVar_AllCollected()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR
    x : INT;
    y : INT;
END_VAR
y := x + x + x;
END_PROGRAM"
        );

        var xReads = result
            .References.Where(r => r.SymbolName == "x" && r.Usage == ReferenceUsage.Read)
            .ToList();

        Assert.Equal(3, xReads.Count);
    }

    // ═══════════════════════════════════════════════════════════════
    //  5. CallSites 收集
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void CallSites_SingleFunctionCall_Recorded()
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
VAR r : INT; END_VAR
r := Add(1, 2);
END_PROGRAM"
        );

        var call = result.CallSites.FirstOrDefault(c => c.CalleeName == "Add");
        Assert.NotNull(call);
        Assert.Equal("Main", call!.CallerName);
        Assert.Equal("Main", call.CallerPOU);
    }

    [Fact]
    public void CallSites_MultipleCalls_AllRecorded()
    {
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

        var incCalls = result.CallSites.Where(c => c.CalleeName == "Inc").ToList();
        Assert.Equal(2, incCalls.Count);
        Assert.All(incCalls, c => Assert.Equal("Main", c.CallerName));
    }

    [Fact]
    public void CallSites_ChainCalls_RecordsAll()
    {
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

        Assert.Contains(result.CallSites, c => c.CallerName == "Main" && c.CalleeName == "B");
        Assert.Contains(result.CallSites, c => c.CallerName == "B" && c.CalleeName == "A");
    }

    [Fact]
    public void CallSites_NoFunctionCalls_ReturnsEmpty()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR x : INT; END_VAR
x := 42;
END_PROGRAM"
        );

        Assert.Empty(result.CallSites);
    }

    // ═══════════════════════════════════════════════════════════════
    //  6. 端到端：多 POU 综合验证
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void EndToEnd_PointDistSq_CollectionsComplete()
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
END_PROGRAM"
        );

        // 类型
        Assert.Single(result.CustomTypes);
        var pointType = result.CustomTypes[0];
        Assert.Equal("Point", pointType.Name);
        Assert.Equal(TypeKind.Struct, pointType.Kind);
        Assert.Equal(2, pointType.Fields!.Count);

        // POU（只看用户定义的）
        var userPOUs = result.POUs.Where(p => !p.IsExternal).ToList();
        Assert.Equal(2, userPOUs.Count);
        Assert.Equal(POUKind.Function, userPOUs[0].Kind);
        Assert.Equal("REAL", userPOUs[0].ReturnTypeName);
        Assert.Equal(POUKind.Program, userPOUs[1].Kind);

        // 变量
        var distSqVars = result.Variables.Where(v => v.POUName == "DistanceSq").ToList();
        Assert.Equal(4, distSqVars.Count);

        var mainVars = result.Variables.Where(v => v.POUName == "Main").ToList();
        Assert.Equal(3, mainVars.Count);

        // 调用
        Assert.Single(result.CallSites);
        Assert.Equal("Main", result.CallSites[0].CallerName);
        Assert.Equal("DistanceSq", result.CallSites[0].CalleeName);

        // 引用
        Assert.Contains(
            result.References,
            r => r.SymbolName == "DistanceSq" && r.Usage == ReferenceUsage.Call);
    }
}