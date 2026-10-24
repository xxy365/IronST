using IronST.Common.Diagnostics;
using IronST.Common.Symbol;
using IronST.Common.Types;
using IronST.Frontend.Analysis;
using IronST.Frontend.AST;

namespace IronST.Frontend.Tests;

/// <summary>
/// 测试三项改动：
/// 1. 标准函数注册（不报 E2006）
/// 2. 枚举成员值计算
/// 3. 枚举类型引用（Color.Red / Red）
/// </summary>
public class EnumAndBuiltinTests
{
    // ═══════════════════════════════════════════════════════════════
    //  1. 标准函数注册
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Builtin_SQRT_NoE2006()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR x : LREAL; END_VAR
x := SQRT(4.0);
END_PROGRAM"
        );
        Assert.Empty(result.Diagnostics.Where(d => d.Code == ErrorCode.E2006));
    }

    [Fact]
    public void Builtin_SIN_NoE2006()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR x : LREAL; END_VAR
x := SIN(3.14);
END_PROGRAM"
        );
        Assert.Empty(result.Diagnostics.Where(d => d.Code == ErrorCode.E2006));
    }

    [Fact]
    public void Builtin_ABS_NoE2006()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR x : DINT; END_VAR
x := ABS(-5);
END_PROGRAM"
        );
        Assert.Empty(result.Diagnostics.Where(d => d.Code == ErrorCode.E2006));
    }

    [Fact]
    public void Builtin_LEN_NoE2006()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR s : STRING; n : INT; END_VAR
n := LEN(s);
END_PROGRAM"
        );
        Assert.Empty(result.Diagnostics.Where(d => d.Code == ErrorCode.E2006));
    }

    [Fact]
    public void Builtin_CONCAT_NoE2006()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR a : STRING; b : STRING; c : STRING; END_VAR
c := CONCAT(a, b);
END_PROGRAM"
        );
        Assert.Empty(result.Diagnostics.Where(d => d.Code == ErrorCode.E2006));
    }

    [Fact]
    public void Builtin_FIND_NoE2006()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR a : STRING; b : STRING; n : INT; END_VAR
n := FIND(a, b);
END_PROGRAM"
        );
        Assert.Empty(result.Diagnostics.Where(d => d.Code == ErrorCode.E2006));
    }

    [Fact]
    public void Builtin_SQRT_ResolvedTypeIsLReal()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR x : LREAL; END_VAR
x := SQRT(4.0);
END_PROGRAM"
        );
        var assign = (AssignmentNode)result.AST.POUs[0].Body[0];
        Assert.Equal(IECType.LReal, assign.Value.ResolvedType);
    }

    [Fact]
    public void Builtin_LEN_ResolvedTypeIsInt()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR s : STRING; n : INT; END_VAR
n := LEN(s);
END_PROGRAM"
        );
        var assign = (AssignmentNode)result.AST.POUs[0].Body[0];
        Assert.Equal(IECType.Int, assign.Value.ResolvedType);
    }

    [Fact]
    public void Builtin_InCallSite_ParametersCollected()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR s : STRING; n : INT; END_VAR
n := LEN(s);
END_PROGRAM"
        );

        // LEN 是内置函数，LookupBuiltin 命中，不会生成 CallSite
        // 但符号表中有签名，可以通过 Symbols.Resolve 查到
        var sym = result.Symbols.Resolve("LEN") as FunctionSymbol;
        Assert.NotNull(sym);
        Assert.Equal(IECType.Int, sym!.ReturnType);
        Assert.Single(sym.Parameters);
        Assert.Equal("input", sym.Parameters[0].Name);
    }

    [Fact]
    public void Builtin_SQRT_RegisteredInSymbolTable()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR x : LREAL; END_VAR
x := SQRT(4.0);
END_PROGRAM"
        );

        var sym = result.Symbols.Resolve("SQRT") as FunctionSymbol;
        Assert.NotNull(sym);
        Assert.Equal(IECType.LReal, sym!.ReturnType);
        Assert.Single(sym.Parameters);
    }

    [Fact]
    public void Builtin_Concat_RegisteredInSymbolTable()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
PROGRAM Main
VAR a : STRING; b : STRING; c : STRING; END_VAR
c := CONCAT(a, b);
END_PROGRAM"
        );

        var sym = result.Symbols.Resolve("CONCAT") as FunctionSymbol;
        Assert.NotNull(sym);
        Assert.Equal(2, sym!.Parameters.Count);
    }

    // ═══════════════════════════════════════════════════════════════
    //  2. 枚举成员值计算
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Enum_ImplicitValues_StartFromZero()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE Color : (Red, Green, Blue);
END_TYPE;

PROGRAM P
VAR c : Color; END_VAR
END_PROGRAM"
        );

        var colorType = result.CustomTypes.First(t => t.Name == "Color");
        Assert.NotNull(colorType.EnumMemberValues);
        Assert.Equal(3, colorType.EnumMemberValues!.Count);
        Assert.Equal(0, colorType.EnumMemberValues["Red"]);
        Assert.Equal(1, colorType.EnumMemberValues["Green"]);
        Assert.Equal(2, colorType.EnumMemberValues["Blue"]);
    }

    [Fact]
    public void Enum_ExplicitValues_CorrectlyStored()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE Color : (Red, Green, Blue := 10);
END_TYPE;

PROGRAM P
VAR c : Color; END_VAR
END_PROGRAM"
        );

        var colorType = result.CustomTypes.First(t => t.Name == "Color");
        Assert.NotNull(colorType.EnumMemberValues);
        Assert.Equal(0, colorType.EnumMemberValues!["Red"]);
        Assert.Equal(1, colorType.EnumMemberValues["Green"]);
        Assert.Equal(10, colorType.EnumMemberValues["Blue"]);
    }

    [Fact]
    public void Enum_MixedValues_AutoIncrementAfterExplicit()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE Color : (Red, Green, Blue := 10, Yellow);
END_TYPE;

PROGRAM P
VAR c : Color; END_VAR
END_PROGRAM"
        );

        var colorType = result.CustomTypes.First(t => t.Name == "Color");
        Assert.NotNull(colorType.EnumMemberValues);
        Assert.Equal(4, colorType.EnumMemberValues!.Count);
        Assert.Equal(0, colorType.EnumMemberValues["Red"]);
        Assert.Equal(1, colorType.EnumMemberValues["Green"]);
        Assert.Equal(10, colorType.EnumMemberValues["Blue"]);
        Assert.Equal(11, colorType.EnumMemberValues["Yellow"]);
    }

    [Fact]
    public void Enum_AllExplicitValues()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE Status : (Off := 0, Starting := 10, Running := 20, Stopping := 30);
END_TYPE;

PROGRAM P
VAR s : Status; END_VAR
END_PROGRAM"
        );

        var statusType = result.CustomTypes.First(t => t.Name == "Status");
        Assert.NotNull(statusType.EnumMemberValues);
        Assert.Equal(0, statusType.EnumMemberValues!["Off"]);
        Assert.Equal(10, statusType.EnumMemberValues["Starting"]);
        Assert.Equal(20, statusType.EnumMemberValues["Running"]);
        Assert.Equal(30, statusType.EnumMemberValues["Stopping"]);
    }

    [Fact]
    public void Enum_IECType_HasEnumMemberValues()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE Color : (Red, Green, Blue := 10);
END_TYPE;

PROGRAM P
VAR c : Color; END_VAR
END_PROGRAM"
        );

        var colorSym = result.Symbols.Resolve("Color") as TypeSymbol;
        Assert.NotNull(colorSym);
        Assert.NotNull(colorSym!.Type.EnumMemberValues);
        Assert.Equal(0, colorSym.Type.EnumMemberValues!["Red"]);
        Assert.Equal(10, colorSym.Type.EnumMemberValues["Blue"]);
    }

    [Fact]
    public void Enum_TypeInfoAndIECType_ValuesMatch()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE Level : (Low := 1, Mid := 5, High := 9);
END_TYPE;

PROGRAM P
VAR l : Level; END_VAR
END_PROGRAM"
        );

        var typeInfo = result.CustomTypes.First(t => t.Name == "Level");
        var typeSymbol = result.Symbols.Resolve("Level") as TypeSymbol;

        Assert.NotNull(typeInfo.EnumMemberValues);
        Assert.NotNull(typeSymbol!.Type.EnumMemberValues);

        // 两者一致
        Assert.Equal(typeInfo.EnumMemberValues!["Low"], typeSymbol.Type.EnumMemberValues!["Low"]);
        Assert.Equal(typeInfo.EnumMemberValues["Mid"], typeSymbol.Type.EnumMemberValues["Mid"]);
        Assert.Equal(typeInfo.EnumMemberValues["High"], typeSymbol.Type.EnumMemberValues["High"]);
    }

    [Fact]
    public void Enum_SingleMember()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE Flag : (Active);
END_TYPE;

PROGRAM P
VAR f : Flag; END_VAR
END_PROGRAM"
        );

        var flagType = result.CustomTypes.First(t => t.Name == "Flag");
        Assert.NotNull(flagType.EnumMemberValues);
        Assert.Single(flagType.EnumMemberValues!);
        Assert.Equal(0, flagType.EnumMemberValues["Active"]);
    }

    // ═══════════════════════════════════════════════════════════════
    //  3. 枚举类型引用
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void EnumRef_QualifiedAccess_NoError()
    {
        // Color.Red
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE Color : (Red, Green, Blue);
END_TYPE;

PROGRAM Main
VAR c : Color; END_VAR
c := Color.Red;
END_PROGRAM"
        );

        Assert.Empty(result.Diagnostics.Where(d => d.Code == ErrorCode.E2001));
        Assert.Empty(result.Diagnostics.Where(d => d.Code == ErrorCode.E2006));
    }

    [Fact]
    public void EnumRef_QualifiedAccess_ResolvedTypeIsEnum()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE Color : (Red, Green, Blue);
END_TYPE;

PROGRAM Main
VAR c : Color; END_VAR
c := Color.Red;
END_PROGRAM"
        );

        var assign = (AssignmentNode)result.AST.POUs[0].Body[0];
        Assert.NotNull(assign.Value.ResolvedType);
        Assert.Equal(IECType.KindTag.ENUM, assign.Value.ResolvedType!.Kind);
        Assert.Equal("Color", assign.Value.ResolvedType.Name);
    }

    [Fact]
    public void EnumRef_BareAccess_NoError()
    {
        // 单独写 Red（无前缀）
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE Color : (Red, Green, Blue);
END_TYPE;

PROGRAM Main
VAR c : Color; END_VAR
c := Red;
END_PROGRAM"
        );

        Assert.Empty(result.Diagnostics.Where(d => d.Code == ErrorCode.E2001));
    }

    [Fact]
    public void EnumRef_BareAccess_ResolvedTypeIsEnum()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE Color : (Red, Green, Blue);
END_TYPE;

PROGRAM Main
VAR c : Color; END_VAR
c := Red;
END_PROGRAM"
        );

        var assign = (AssignmentNode)result.AST.POUs[0].Body[0];
        Assert.NotNull(assign.Value.ResolvedType);
        Assert.Equal(IECType.KindTag.ENUM, assign.Value.ResolvedType!.Kind);
    }

    [Fact]
    public void EnumRef_InvalidMember_ReportsE2008()
    {
        // Color.XYZ 不存在
        var result = FrontendTestHelper.Analyze(
            @"
TYPE Color : (Red, Green, Blue);
END_TYPE;

PROGRAM Main
VAR c : Color; END_VAR
c := Color.XYZ;
END_PROGRAM"
        );

        var err = result.Diagnostics.FirstOrDefault(d => d.Code == ErrorCode.E2008);
        Assert.NotNull(err);
        Assert.Contains("XYZ", err!.Message);
    }

    [Fact]
    public void EnumRef_InIfCondition_NoError()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE Color : (Red, Green, Blue);
END_TYPE;

PROGRAM Main
VAR c : Color; f : BOOL; END_VAR
IF c = Color.Green THEN
    f := TRUE;
END_IF
END_PROGRAM"
        );

        Assert.Empty(result.Diagnostics.Where(d => d.Code == ErrorCode.E2001));
        Assert.Empty(result.Diagnostics.Where(d => d.Code == ErrorCode.E2006));
    }

    [Fact]
    public void EnumRef_QualifiedAccess_HasTypeUsageReference()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE Color : (Red, Green, Blue);
END_TYPE;

PROGRAM Main
VAR c : Color; END_VAR
c := Color.Red;
END_PROGRAM"
        );

        // Color 在 Color.Red 中应该有 TypeUsage 引用
        Assert.Contains(
            result.References,
            r =>
                r.SymbolName == "Color"
                && r.Kind == SymbolKind.Type
                && r.Usage == ReferenceUsage.TypeUsage
        );
    }

    [Fact]
    public void EnumRef_MultipleQualifiedAccesses_AllCollected()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE Color : (Red, Green, Blue);
END_TYPE;

PROGRAM Main
VAR c : Color; END_VAR
c := Color.Red;
c := Color.Green;
c := Color.Blue;
END_PROGRAM"
        );

        // 1 次 VAR c : Color + 3 次 Color.Red/Green/Blue = 4 个 TypeUsage
        var colorRefs = result
            .References.Where(r => r.SymbolName == "Color" && r.Usage == ReferenceUsage.TypeUsage)
            .ToList();
        Assert.Equal(4, colorRefs.Count);
    }

    // ═══════════════════════════════════════════════════════════════
    //  4. 端到端：枚举 + 标准函数综合
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void EndToEnd_EnumAndBuiltin_NoErrors()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE Color : (Red := 0, Green := 1, Blue := 10);
END_TYPE;

FUNCTION Clamp : DINT
VAR_INPUT
    value : DINT;
    lo : DINT;
    hi : DINT;
END_VAR
Clamp := ABS(value);
END_FUNCTION

PROGRAM Main
VAR
    c : Color;
    x : DINT;
    s : STRING;
    n : INT;
END_VAR
c := Color.Red;
c := Green;
x := Clamp(ABS(-100), 0, 255);
n := LEN(s);
END_PROGRAM"
        );

        // 无 E2001/E2006 错误
        Assert.Empty(
            result.Diagnostics.Where(d => d.Code == ErrorCode.E2001 || d.Code == ErrorCode.E2006)
        );

        // 枚举值正确
        var colorType = result.CustomTypes.First(t => t.Name == "Color");
        Assert.Equal(0, colorType.EnumMemberValues!["Red"]);
        Assert.Equal(10, colorType.EnumMemberValues["Blue"]);

        // 标准函数注册
        Assert.NotNull(result.Symbols.Resolve("ABS"));
        Assert.NotNull(result.Symbols.Resolve("LEN"));
    }

    [Fact]
    public void EndToEnd_EnumValues_WithCodegenReady()
    {
        // 模拟 CodeGen 侧取值
        var result = FrontendTestHelper.ExpectSuccess(
            @"
TYPE Priority : (Low := 1, Normal := 5, High := 9, Critical := 99);
END_TYPE;

PROGRAM Main
VAR p : Priority; END_VAR
p := Critical;
END_PROGRAM"
        );

        var assign = (AssignmentNode)result.AST.POUs[0].Body[0];
        var lit = assign.Value;

        // ResolvedType 是枚举类型
        Assert.NotNull(lit.ResolvedType);
        Assert.Equal(IECType.KindTag.ENUM, lit.ResolvedType!.Kind);

        // CodeGen 可以通过 EnumMemberValues 取值
        var memberValues = lit.ResolvedType.EnumMemberValues;
        Assert.NotNull(memberValues);

        // 赋值的是 Critical，值为 99
        // （CodeGen 侧需要从 VariableExprNode 的第一个 segment name 查）
        var varExpr = (VariableExprNode)lit;
        var memberName = varExpr.Segments[0].Name;
        Assert.Equal("Critical", memberName);
        Assert.Equal(99, memberValues![memberName]);
    }
}
