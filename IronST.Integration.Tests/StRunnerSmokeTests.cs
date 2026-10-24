using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.Integration.Tests;

public class StRunnerSmokeTests
{
    [Fact]
    public void StCallsCSharpMethod_WithArrayAndMultiDimValues_RecordsCallsInOrder()
    {
        var run = StRunner.Run("""
            PROGRAM Main
            VAR
                a : ARRAY[0..9] OF DINT;
                m : ARRAY[0..2, 0..3] OF DINT;
            END_VAR
                a[3] := 11;
                m[1, 2] := 42;
                Log(a[3]);
                Log(m[1, 2]);
                Log(7);
            END_PROGRAM
            """, host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(3, run.Calls.Count);
        Assert.Equal(new long[] { 11, 42, 7 }, run.Calls.Select(c => c.Arguments[0].IntValue));
        Assert.All(run.Calls, c => Assert.Equal("Log", c.Name));
    }

    [Fact]
    public void CSharpCallsStFunction_WithArguments_ReturnsValueInA0()
    {
        var run = StRunner.Call("""
            FUNCTION Add : DINT
            VAR_INPUT
                a : DINT;
                b : DINT;
            END_VAR
                Add := a + b;
            END_FUNCTION
            """,
            "Add",
            configureHost: null,
            new BcValue(20L),
            new BcValue(22L));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(42, run.ReturnValue.IntValue);
        Assert.Empty(run.Calls);
    }

    [Fact]
    public void HostFunction_FourArguments_AllArriveInA0ToA3()
    {
        var run = StRunner.Run("""
            PROGRAM Main
            VAR x : DINT; END_VAR
                x := Sum4(1, 2, 3, 4);
                Log(x);
            END_PROGRAM
            """, host => host
                .RegisterFunction("Sum4", 4, args =>
                    new BcValue(args.Int(0) * 1000 + args.Int(1) * 100 + args.Int(2) * 10 + args.Int(3)))
                .RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(new long[] { 1, 2, 3, 4 }, run.Calls[0].Arguments.Select(a => a.IntValue));
        Assert.Equal(1234, run.Calls[0].Result.IntValue);
        Assert.Equal(1234, run.Calls[1].Arguments[0].IntValue);
    }

    [Fact]
    public void HostFunction_ReturnValue_IsUsableInsideSt_AndRecorded()
    {
        var run = StRunner.Run("""
            PROGRAM Main
            VAR
                x : DINT;
            END_VAR
                x := Square(6);
                Log(x);
            END_PROGRAM
            """, host => host
                .RegisterFunction("Square", 1, args => new BcValue(args.Int(0) * args.Int(0)))
                .RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(new[] { "Square", "Log" }, run.Calls.Select(c => c.Name));
        Assert.Equal(36, run.Calls[0].Result.IntValue);
        Assert.Equal(36, run.Calls[1].Arguments[0].IntValue);
    }

    [Fact]
    public void HostFunction_StringReturnValue_ReachesSt()
    {
        var run = StRunner.Run("""
            PROGRAM Main
            VAR
                unused : INT;
            END_VAR
                Log(Greeting());
            END_PROGRAM
            """, host => host
                .RegisterFunction("Greeting", 0, _ => new BcValue("hello"), returnType: "STRING")
                .RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        var log = Assert.Single(run.Calls, c => c.Name == "Log");
        Assert.Equal(ValueTag.String, log.Arguments[0].Tag);
        Assert.Equal("hello", log.Arguments[0].StringValue);
    }

    [Fact]
    public void StFunction_CallsCSharpMethod_AndResultIsObservable()
    {
        var run = StRunner.Run("""
            FUNCTION Twice : DINT
            VAR_INPUT
                x : DINT;
            END_VAR
                Twice := x + x;
            END_FUNCTION

            PROGRAM Main
            VAR
                s : DINT;
            END_VAR
                s := Twice(21);
                Log(s);
            END_PROGRAM
            """, host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(42, Assert.Single(run.Calls).Arguments[0].IntValue);
    }
}
