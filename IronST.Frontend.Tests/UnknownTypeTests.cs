// tests/IronST.Frontend.Tests/UnknownTypeTests.cs
using IronST.Common.Diagnostics;
using IronST.Common.Symbol;
using IronST.Common.Types;
using IronST.Frontend.Analysis;
using IronST.Frontend.AST;

namespace IronST.Frontend.Tests;

/// <summary>
/// E2015: 未知的类型 —— 确保所有出现未知类型名的地方都能被检测到
/// </summary>
public class UnknownTypeTests
{
    // ═══════════════════════════════════════════════════════════════
    //  应该报错的场景
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ProgramVar_UnknownType_ReportsE2015()
    {
        var result = FrontendTestHelper.Analyze(
            @"
PROGRAM Main
VAR
    x : NO_SUCH_TYPE;
END_VAR
x := 1;
END_PROGRAM
"
        );
        var err = result.Diagnostics.FirstOrDefault(d => d.Code == ErrorCode.E2015);
        Assert.NotNull(err);
        Assert.Contains("NO_SUCH_TYPE", err!.Message);
    }

    [Fact]
    public void FunctionVar_UnknownType_ReportsE2015()
    {
        var result = FrontendTestHelper.Analyze(
            @"
FUNCTION F : INT
VAR
    tmp : FAKE_TYPE;
END_VAR
F := 0;
END_FUNCTION
"
        );
        var err = result.Diagnostics.FirstOrDefault(d => d.Code == ErrorCode.E2015);
        Assert.NotNull(err);
        Assert.Contains("FAKE_TYPE", err!.Message);
    }

    [Fact]
    public void FunctionInputParam_UnknownType_ReportsE2015()
    {
        var result = FrontendTestHelper.Analyze(
            @"
FUNCTION F : INT
VAR_INPUT
    x : FAKE_TYPE;
END_VAR
F := 0;
END_FUNCTION
"
        );
        var err = result.Diagnostics.FirstOrDefault(d => d.Code == ErrorCode.E2015);
        Assert.NotNull(err);
        Assert.Contains("FAKE_TYPE", err!.Message);
    }

    [Fact]
    public void FunctionOutputParam_UnknownType_ReportsE2015()
    {
        var result = FrontendTestHelper.Analyze(
            @"
FUNCTION F : INT
VAR_OUTPUT
    y : GHOST;
END_VAR
F := 0;
END_FUNCTION
"
        );
        var err = result.Diagnostics.FirstOrDefault(d => d.Code == ErrorCode.E2015);
        Assert.NotNull(err);
        Assert.Contains("GHOST", err!.Message);
    }

    [Fact]
    public void FunctionInOutParam_UnknownType_ReportsE2015()
    {
        var result = FrontendTestHelper.Analyze(
            @"
FUNCTION F : INT
VAR_IN_OUT
    z : PHANTOM;
END_VAR
F := 0;
END_FUNCTION
"
        );
        var err = result.Diagnostics.FirstOrDefault(d => d.Code == ErrorCode.E2015);
        Assert.NotNull(err);
        Assert.Contains("PHANTOM", err!.Message);
    }

    [Fact]
    public void FunctionBlockMember_UnknownType_ReportsE2015()
    {
        var result = FrontendTestHelper.Analyze(
            @"
FUNCTION_BLOCK FB
VAR_INPUT
    x : FAKE_TYPE;
END_VAR
VAR
    y : INT;
END_VAR
END_FUNCTION_BLOCK
"
        );
        var err = result.Diagnostics.FirstOrDefault(d => d.Code == ErrorCode.E2015);
        Assert.NotNull(err);
        Assert.Contains("FAKE_TYPE", err!.Message);
    }

    [Fact]
    public void StructMember_UnknownType_ReportsE2015()
    {
        var result = FrontendTestHelper.Analyze(
            @"
TYPE MyStruct :
STRUCT
    field1 : INT;
    field2 : FAKE_TYPE;
END_STRUCT
;
END_TYPE;
"
        );
        var err = result.Diagnostics.FirstOrDefault(d => d.Code == ErrorCode.E2015);
        Assert.NotNull(err);
        Assert.Contains("FAKE_TYPE", err!.Message);
    }

    [Fact]
    public void MultipleUnknownTypes_AllReported()
    {
        var result = FrontendTestHelper.Analyze(
            @"
PROGRAM Main
VAR
    a : FAKE1;
    b : FAKE2;
    c : INT;
END_VAR
a := 1;
END_PROGRAM
"
        );
        var e2015s = result.Diagnostics.Where(d => d.Code == ErrorCode.E2015).ToList();
        Assert.Equal(2, e2015s.Count);
        Assert.Contains(e2015s, e => e.Message.Contains("FAKE1"));
        Assert.Contains(e2015s, e => e.Message.Contains("FAKE2"));
    }

    [Fact]
    public void MixedKnownAndUnknown_OnlyUnknownReports()
    {
        var result = FrontendTestHelper.Analyze(
            @"
PROGRAM Main
VAR
    a : INT;
    b : FAKE_TYPE;
    c : REAL;
END_VAR
a := 1;
END_PROGRAM
"
        );
        var e2015s = result.Diagnostics.Where(d => d.Code == ErrorCode.E2015).ToList();
        Assert.Single(e2015s);
        Assert.Contains("FAKE_TYPE", e2015s[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════
    //  不应该报错的场景
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BuiltinType_Int_NoError()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR
    x : INT;
END_VAR
x := 1;
END_PROGRAM
"
        );
        Assert.Empty(result.Diagnostics.Where(d => d.Code == ErrorCode.E2015));
    }

    [Fact]
    public void BuiltinType_AllTypes_NoError()
    {
        // 测试所有常见内置类型
        var types = new[] {
            "BOOL", "BYTE", "WORD", "DWORD", "LWORD",
            "SINT", "INT", "DINT", "LINT",
            "USINT", "UINT", "UDINT", "ULINT",
            "REAL", "LREAL",
            "TIME", "DATE",
            "STRING", "WSTRING"
        };

        foreach (var typeName in types) {
            var result = FrontendTestHelper.Analyze(
                $@"
PROGRAM P
VAR
    x : {typeName};
END_VAR
x := 0;
END_PROGRAM
"
            );
            var e2015s = result.Diagnostics.Where(d => d.Code == ErrorCode.E2015).ToList();
            Assert.True(e2015s.Count == 0,
                $"内置类型 '{typeName}' 不应报 E2015，但报了: {e2015s.FirstOrDefault()?.Message}");
        }
    }

    [Fact]
    public void BuiltinType_StringWithSize_NoError()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR
    s1 : STRING;
    s2 : STRING(50);
    w1 : WSTRING;
    w2 : WSTRING(100);
END_VAR
s1 := 'hello';
END_PROGRAM
"
        );
        Assert.Empty(result.Diagnostics.Where(d => d.Code == ErrorCode.E2015));
    }

    [Fact]
    public void CustomStructType_NoError()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE Point :
STRUCT
    X : INT;
    Y : INT;
END_STRUCT
;
END_TYPE;

PROGRAM Main
VAR
    p : Point;
END_VAR
p.X := 1;
END_PROGRAM
"
        );
        Assert.Empty(result.Diagnostics.Where(d => d.Code == ErrorCode.E2015));
    }

    [Fact]
    public void CustomEnumType_NoError()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE Color : (Red, Green, Blue);
END_TYPE;

PROGRAM Main
VAR
    c : Color;
END_VAR
END_PROGRAM
"
        );
        Assert.Empty(result.Diagnostics.Where(d => d.Code == ErrorCode.E2015));
    }

    [Fact]
    public void CustomSimpleTypeAlias_NoError()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE MyInt : DINT;
END_TYPE;

PROGRAM Main
VAR
    x : MyInt;
END_VAR
x := 1;
END_PROGRAM
"
        );
        Assert.Empty(result.Diagnostics.Where(d => d.Code == ErrorCode.E2015));
    }

    [Fact]
    public void FunctionBlockAsType_NoError()
    {
        // ★ 关键测试：FUNCTION_BLOCK 名字可以当类型用
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

PROGRAM Main
VAR
    ctr : Counter;
END_VAR
ctr(inc := 1);
END_PROGRAM
"
        );
        Assert.Empty(result.Diagnostics.Where(d => d.Code == ErrorCode.E2015));
    }

    [Fact]
    public void FunctionBlockAsType_MultipleInstances_NoError()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION_BLOCK Motor
VAR_INPUT
    speed : INT;
END_VAR
VAR
    running : BOOL;
END_VAR
END_FUNCTION_BLOCK

PROGRAM Main
VAR
    motor1 : Motor;
    motor2 : Motor;
END_VAR
motor1(speed := 100);
motor2(speed := 200);
END_PROGRAM
"
        );
        Assert.Empty(result.Diagnostics.Where(d => d.Code == ErrorCode.E2015));
    }

    // ═══════════════════════════════════════════════════════════════
    //  边界情况
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void EmptyProgram_NoError()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
END_PROGRAM
"
        );
        Assert.Empty(result.Diagnostics.Where(d => d.Code == ErrorCode.E2015));
    }

    [Fact]
    public void OnlyTypeDeclarations_NoVariables_NoE2015()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE Color : (Red, Green, Blue);
END_TYPE;

TYPE Point :
STRUCT
    X : REAL;
    Y : REAL;
END_STRUCT
;
END_TYPE;
"
        );
        Assert.Empty(result.Diagnostics.Where(d => d.Code == ErrorCode.E2015));
    }

    [Fact]
    public void UnknownTypeInStructButNotUsed_StillReportsE2015()
    {
        // STRUCT 声明了未知类型，但程序体不引用它——仍应报错
        var result = FrontendTestHelper.Analyze(
            @"
TYPE BadStruct :
STRUCT
    field : DOES_NOT_EXIST;
END_STRUCT
;
END_TYPE;
"
        );
        var err = result.Diagnostics.FirstOrDefault(d => d.Code == ErrorCode.E2015);
        Assert.NotNull(err);
        Assert.Contains("DOES_NOT_EXIST", err!.Message);
    }

    [Fact]
    public void ErrorSpan_PointsToTypeName_NotVarName()
    {
        var result = FrontendTestHelper.Analyze(
            @"
PROGRAM Main
VAR
    x : FAKE_TYPE;
END_VAR
END_PROGRAM
"
        );
        var err = result.Diagnostics.FirstOrDefault(d => d.Code == ErrorCode.E2015);
        Assert.NotNull(err);
        // 错误应指向类型名 FAKE_TYPE 的位置，不是变量名 x
        // FAKE_TYPE 在第 4 行，列 7（"x : FAKE_TYPE;" 中的 F）
        Assert.NotNull(err!.Span);
    }

    [Fact]
    public void ErrorDoesNotBreakAnalysis_OtherVarsStillResolved()
    {
        var result = FrontendTestHelper.Analyze(
            @"
PROGRAM Main
VAR
    good : INT;
    bad : FAKE_TYPE;
END_VAR
good := 42;
END_PROGRAM
"
        );

        // 应该有 E2015 错误
        Assert.Contains(result.Diagnostics, d => d.Code == ErrorCode.E2015);

        // 但 good 变量应该正常解析
        var assign = (AssignmentNode)result.AST.POUs[0].Body[0];
        Assert.Equal(IECType.Int, assign.Target.ResolvedType);
    }

    [Fact]
    public void ErrorDoesNotBreakAnalysis_FunctionBlockMembersStillResolved()
    {
        var result = FrontendTestHelper.Analyze(
            @"
FUNCTION_BLOCK FB
VAR_INPUT
    bad : FAKE_TYPE;
END_VAR
VAR
    good : INT;
END_VAR
good := 1;
END_FUNCTION_BLOCK
"
        );

        // 应该有 E2015 错误
        Assert.Contains(result.Diagnostics, d => d.Code == ErrorCode.E2015);

        // 但 good 成员应该正常注册
        var fbSym = result.Symbols.Resolve("FB") as FunctionBlockSymbol;
        Assert.NotNull(fbSym);
        Assert.True(fbSym!.Members.ContainsKey("good"));
        Assert.Equal(IECType.Int, fbSym.Members["good"].Type);
    }
}
