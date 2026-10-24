using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.Integration.Tests;

public class ControlFlowIntegrationTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(5, 1)]
    [InlineData(6, 2)]
    [InlineData(10, 2)]
    [InlineData(11, 3)]
    [InlineData(99, 3)]
    public void IfElsifElse_SelectsExpectedBranch(int input, int expected)
    {
        var run = StRunner.Run($"""
            PROGRAM Main
            VAR
                x : DINT;
                y : DINT;
            END_VAR
                x := {input};
                y := 0;
                IF x <= 5 THEN
                    y := 1;
                ELSIF x <= 10 THEN
                    y := 2;
                ELSE
                    y := 3;
                END_IF
                Log(y);
            END_PROGRAM
            """, host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(expected, Assert.Single(run.Calls).Arguments[0].IntValue);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(50, 10)]
    [InlineData(51, 20)]
    [InlineData(80, 20)]
    [InlineData(81, 30)]
    [InlineData(200, 30)]
    [InlineData(201, 40)]
    public void CaseWithRanges_SelectsExpectedBranch(int input, int expected)
    {
        var run = StRunner.Run($"""
            PROGRAM Main
            VAR
                x : DINT;
                y : DINT;
            END_VAR
                x := {input};
                y := 0;
                CASE x OF
                    0..50   : y := 10;
                    51..80  : y := 20;
                    81..200 : y := 30;
                ELSE
                    y := 40;
                END_CASE
                Log(y);
            END_PROGRAM
            """, host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(expected, Assert.Single(run.Calls).Arguments[0].IntValue);
    }

    [Fact]
    public void ForLoop_AccumulatesExpectedSum()
    {
        var run = StRunner.Run("""
            PROGRAM Main
            VAR
                i : DINT;
                sum : DINT;
            END_VAR
                sum := 0;
                FOR i := 1 TO 10 DO
                    sum := sum + i;
                END_FOR
                Log(sum);
            END_PROGRAM
            """, host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(55, Assert.Single(run.Calls).Arguments[0].IntValue);
    }

    [Fact]
    public void WhileLoop_AccumulatesExpectedSum()
    {
        var run = StRunner.Run("""
            PROGRAM Main
            VAR
                i : DINT;
                sum : DINT;
            END_VAR
                i := 1;
                sum := 0;
                WHILE i <= 10 DO
                    sum := sum + i;
                    i := i + 1;
                END_WHILE
                Log(sum);
            END_PROGRAM
            """, host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(55, Assert.Single(run.Calls).Arguments[0].IntValue);
    }

    [Fact]
    public void RepeatLoop_AccumulatesExpectedSum()
    {
        var run = StRunner.Run("""
            PROGRAM Main
            VAR
                i : DINT;
                sum : DINT;
            END_VAR
                i := 1;
                sum := 0;
                REPEAT
                    sum := sum + i;
                    i := i + 1;
                UNTIL i > 10 END_REPEAT
                Log(sum);
            END_PROGRAM
            """, host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(55, Assert.Single(run.Calls).Arguments[0].IntValue);
    }

    [Fact]
    public void BooleanLogic_AndOrNot_EvaluatesCorrectly()
    {
        var run = StRunner.Run("""
            PROGRAM Main
            VAR
                a : BOOL;
                b : BOOL;
                c : BOOL;
            END_VAR
                a := TRUE;
                b := FALSE;
                c := (a AND NOT b) OR b;
                IF c THEN
                    Log(1);
                ELSE
                    Log(0);
                END_IF
            END_PROGRAM
            """, host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(1, Assert.Single(run.Calls).Arguments[0].IntValue);
    }
}
