// tests/IronST.Frontend.Tests/CallArgumentTests.cs
using IronST.Frontend.Analysis;

namespace IronST.Frontend.Tests;

/// <summary>
/// 测试调用参数的完整信息收集
/// </summary>
public class CallArgumentTests
{
    // ═══════════════════════════════════════════════════════════════
    //  1. 参数方向区分
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void InputArgs_DirectionIsInput()
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
END_PROGRAM
"
        );

        var call = result.CallSites.First(c => c.CalleeName == "Add");

        Assert.Equal(ParameterDirection.Input, call.Arguments[0].Direction);
        Assert.Equal(ParameterDirection.Input, call.Arguments[1].Direction);
    }

    [Fact]
    public void OutputArgs_DirectionIsOutput()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION_BLOCK Divider
VAR_INPUT
    numerator : INT;
    divisor : INT;
END_VAR
VAR_OUTPUT
    quotient : INT;
    remainder : INT;
END_VAR
quotient := numerator / divisor;
remainder := numerator MOD divisor;
END_FUNCTION_BLOCK

PROGRAM Main
VAR
    fb : Divider;
    q : INT;
    r : INT;
END_VAR
fb(numerator := 10, divisor := 3, quotient => q, remainder => r);
END_PROGRAM
"
        );

        var call = result.CallSites.First(c => c.CalleeName == "fb");

        var numArg = call.Arguments.First(a => a.ParameterName == "numerator");
        Assert.Equal(ParameterDirection.Input, numArg.Direction);
        Assert.Equal(ArgumentBindingStyle.NamedInput, numArg.Binding);

        var quotArg = call.Arguments.First(a => a.ParameterName == "quotient");
        Assert.Equal(ParameterDirection.Output, quotArg.Direction);
        Assert.Equal(ArgumentBindingStyle.NamedOutput, quotArg.Binding);
    }

    [Fact]
    public void InOutArgs_DirectionIsInOut()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION Swap : INT
VAR_IN_OUT
    a : INT;
    b : INT;
END_VAR
Swap := 0;
END_FUNCTION

PROGRAM Main
VAR x : INT; y : INT; r : INT; END_VAR
r := Swap(a := x, b := y);
END_PROGRAM
"
        );

        var call = result.CallSites.First(c => c.CalleeName == "Swap");

        Assert.Equal(ParameterDirection.InOut, call.Arguments[0].Direction);
        Assert.Equal(ParameterDirection.InOut, call.Arguments[1].Direction);
        Assert.Equal("a", call.Arguments[0].ParameterName);
        Assert.Equal("b", call.Arguments[1].ParameterName);
    }

    // ═══════════════════════════════════════════════════════════════
    //  2. 混合方向（Input + Output + InOut）
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void MixedDirections_AllDistinguished()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION Process : INT
VAR_INPUT
    inputVal : INT;
END_VAR
VAR_OUTPUT
    outputVal : REAL;
END_VAR
VAR_IN_OUT
    state : BOOL;
END_VAR
VAR
    tmp : INT;
END_VAR
Process := inputVal;
END_FUNCTION

PROGRAM Main
VAR
    in : INT;
    out : REAL;
    st : BOOL;
    r : INT;
END_VAR
r := Process(inputVal := in, outputVal => out, state := st);
END_PROGRAM
"
        );

        var call = result.CallSites.First(c => c.CalleeName == "Process");
        Assert.Equal(3, call.ArgumentCount);

        var inputArg = call.Arguments[0];
        Assert.Equal(ParameterDirection.Input, inputArg.Direction);
        Assert.Equal("inputVal", inputArg.ParameterName);
        Assert.Equal("INT", inputArg.ParameterTypeName);
        Assert.Equal(ArgumentBindingStyle.NamedInput, inputArg.Binding);
        Assert.Equal("in", inputArg.ExpressionSummary);

        var outputArg = call.Arguments[1];
        Assert.Equal(ParameterDirection.Output, outputArg.Direction);
        Assert.Equal("outputVal", outputArg.ParameterName);
        Assert.Equal("REAL", outputArg.ParameterTypeName);
        Assert.Equal(ArgumentBindingStyle.NamedOutput, outputArg.Binding);
        Assert.Equal("out", outputArg.ExpressionSummary);

        var inoutArg = call.Arguments[2];
        Assert.Equal(ParameterDirection.InOut, inoutArg.Direction);
        Assert.Equal("state", inoutArg.ParameterName);
        Assert.Equal("BOOL", inoutArg.ParameterTypeName);
        Assert.Equal(ArgumentBindingStyle.NamedInput, inoutArg.Binding);
        Assert.Equal("st", inoutArg.ExpressionSummary);
    }

    // ═══════════════════════════════════════════════════════════════
    //  3. 位置参数 vs 命名参数
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void PositionalArgs_BindingIsPositional()
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
END_PROGRAM
"
        );

        var call = result.CallSites.First(c => c.CalleeName == "Add");

        Assert.Equal(ArgumentBindingStyle.Positional, call.Arguments[0].Binding);
        Assert.Equal(ArgumentBindingStyle.Positional, call.Arguments[1].Binding);

        // 位置参数仍然能匹配形参名
        Assert.Equal("a", call.Arguments[0].ParameterName);
        Assert.Equal("b", call.Arguments[1].ParameterName);
    }

    [Fact]
    public void NamedArgs_BindingIsNamedInput()
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
r := Add(a := 10, b := 20);
END_PROGRAM
"
        );

        var call = result.CallSites.First(c => c.CalleeName == "Add");

        Assert.Equal(ArgumentBindingStyle.NamedInput, call.Arguments[0].Binding);
        Assert.Equal(ArgumentBindingStyle.NamedInput, call.Arguments[1].Binding);

        Assert.Equal("a", call.Arguments[0].ParameterName);
        Assert.Equal("10", call.Arguments[0].ExpressionSummary);

        Assert.Equal("b", call.Arguments[1].ParameterName);
        Assert.Equal("20", call.Arguments[1].ExpressionSummary);
    }

    [Fact]
    public void NamedOutputArgs_BindingIsNamedOutput()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION_BLOCK FB1
VAR_INPUT
    x : INT;
END_VAR
VAR_OUTPUT
    y : INT;
END_VAR
y := x;
END_FUNCTION_BLOCK

PROGRAM Main
VAR
    fb : FB1;
    outVal : INT;
END_VAR
fb(x := 5, y => outVal);
END_PROGRAM
"
        );

        var call = result.CallSites.First(c => c.CalleeName == "fb");

        var xArg = call.Arguments.First(a => a.ParameterName == "x");
        Assert.Equal(ArgumentBindingStyle.NamedInput, xArg.Binding);
        Assert.Equal(ParameterDirection.Input, xArg.Direction);

        var yArg = call.Arguments.First(a => a.ParameterName == "y");
        Assert.Equal(ArgumentBindingStyle.NamedOutput, yArg.Binding);
        Assert.Equal(ParameterDirection.Output, yArg.Direction);
    }

    // ═══════════════════════════════════════════════════════════════
    //  4. 参数类型信息
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Args_ParameterTypeName_MatchesFormalParamType()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION Calc : REAL
VAR_INPUT
    value : INT;
    factor : REAL;
END_VAR
Calc := 0.0;
END_FUNCTION

PROGRAM Main
VAR r : REAL; END_VAR
r := Calc(42, 3.14);
END_PROGRAM
"
        );

        var call = result.CallSites.First(c => c.CalleeName == "Calc");

        Assert.Equal("INT", call.Arguments[0].ParameterTypeName);
        Assert.Equal("REAL", call.Arguments[1].ParameterTypeName);
    }

    [Fact]
    public void Args_ResolvedTypeName_MatchesActualArgType()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION Add : DINT
VAR_INPUT
    a : DINT;
    b : DINT;
END_VAR
Add := a + b;
END_FUNCTION

PROGRAM Main
VAR
    x : INT;
    y : DINT;
    r : DINT;
END_VAR
r := Add(x, y);
END_PROGRAM
"
        );

        var call = result.CallSites.First(c => c.CalleeName == "Add");

        // 实参 x 是 INT，实参 y 是 DINT
        Assert.Equal("INT", call.Arguments[0].ResolvedTypeName);
        Assert.Equal("DINT", call.Arguments[1].ResolvedTypeName);

        // 形参都是 DINT
        Assert.Equal("DINT", call.Arguments[0].ParameterTypeName);
        Assert.Equal("DINT", call.Arguments[1].ParameterTypeName);
    }

    // ═══════════════════════════════════════════════════════════════
    //  5. 表达式摘要
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Args_LiteralArg_SummaryIsLiteralText()
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
END_PROGRAM
"
        );

        var call = result.CallSites.First(c => c.CalleeName == "Inc");
        Assert.Equal("42", call.Arguments[0].ExpressionSummary);
    }

    [Fact]
    public void Args_VariableArg_SummaryIsVarName()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION Inc : INT
VAR_INPUT x : INT; END_VAR
Inc := x + 1;
END_FUNCTION

PROGRAM Main
VAR val : INT; r : INT; END_VAR
r := Inc(val);
END_PROGRAM
"
        );

        var call = result.CallSites.First(c => c.CalleeName == "Inc");
        Assert.Equal("val", call.Arguments[0].ExpressionSummary);
    }

    [Fact]
    public void Args_ExpressionArg_SummaryShowsExpression()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION Add : INT
VAR_INPUT a : INT; b : INT; END_VAR
Add := a + b;
END_FUNCTION

PROGRAM Main
VAR x : INT; y : INT; r : INT; END_VAR
r := Add(x + 1, y * 2);
END_PROGRAM
"
        );

        var call = result.CallSites.First(c => c.CalleeName == "Add");
        Assert.Equal("x + 1", call.Arguments[0].ExpressionSummary);
        Assert.Equal("y * 2", call.Arguments[1].ExpressionSummary);
    }

    [Fact]
    public void Args_NegationArg_SummaryShowsMinus()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION Abs : DINT
VAR_INPUT x : DINT; END_VAR
Abs := x;
END_FUNCTION

PROGRAM Main
VAR val : DINT; r : DINT; END_VAR
r := Abs(-val);
END_PROGRAM
"
        );

        var call = result.CallSites.First(c => c.CalleeName == "Abs");
        Assert.Equal("-val", call.Arguments[0].ExpressionSummary);
    }

    // ═══════════════════════════════════════════════════════════════
    //  6. 零参数
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Args_ZeroArgs_ArgumentCountIsZero()
    {
        var result = FrontendTestHelper.ExpectSuccess(
            @"
FUNCTION GetDefault : INT
GetDefault := 42;
END_FUNCTION

PROGRAM Main
VAR r : INT; END_VAR
r := GetDefault();
END_PROGRAM
"
        );

        var call = result.CallSites.First(c => c.CalleeName == "GetDefault");
        Assert.Equal(0, call.ArgumentCount);
        Assert.Empty(call.Arguments);
    }

    // ═══════════════════════════════════════════════════════════════
    //  7. FB 实例调用（整体调用语法）
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void FunctionBlockCall_WithAllDirections()
    {
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
VAR_IN_OUT
    integralState : REAL;
END_VAR
VAR
    kp : REAL;
END_VAR
controlOutput := kp * (setpoint - measured);
END_FUNCTION_BLOCK

PROGRAM Main
VAR
    pid : PID;
    sp : REAL;
    pv : REAL;
    output : REAL;
    integral : REAL;
END_VAR
pid(setpoint := sp, measured := pv, controlOutput => output, integralState := integral);
END_PROGRAM
"
        );

        var call = result.CallSites.First(c => c.CalleeName == "pid");
        Assert.Equal(4, call.ArgumentCount);

        // Input 参数
        var spArg = call.Arguments.First(a => a.ParameterName == "setpoint");
        Assert.Equal(ParameterDirection.Input, spArg.Direction);
        Assert.Equal("REAL", spArg.ParameterTypeName);

        // Output 参数
        var outArg = call.Arguments.First(a => a.ParameterName == "controlOutput");
        Assert.Equal(ParameterDirection.Output, outArg.Direction);
        Assert.Equal(ArgumentBindingStyle.NamedOutput, outArg.Binding);
        Assert.Equal("output", outArg.ExpressionSummary);

        // InOut 参数
        var intArg = call.Arguments.First(a => a.ParameterName == "integralState");
        Assert.Equal(ParameterDirection.InOut, intArg.Direction);
    }
}
