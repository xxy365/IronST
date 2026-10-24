using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.Integration.Tests;

public class HostInteropIntegrationTests
{
    [Fact]
    public void MultipleHostFunctions_CallsArriveInSourceOrder()
    {
        var run = StRunner.Run("""
            PROGRAM Main
            VAR
                unused : DINT;
            END_VAR
                A(1);
                B(2);
                A(3);
            END_PROGRAM
            """, host => host
                .RegisterAction("A", 1, _ => { })
                .RegisterAction("B", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(new[] { "A", "B", "A" }, run.Calls.Select(c => c.Name));
        Assert.Equal(new long[] { 1, 2, 3 }, run.Calls.Select(c => c.Arguments[0].IntValue));
    }

    [Fact]
    public void HostFunction_StatefulClosure_IsIndependentOfVmState()
    {
        int next = 0;
        var run = StRunner.Run("""
            PROGRAM Main
            VAR
                unused : DINT;
            END_VAR
                Log(Next());
                Log(Next());
                Log(Next());
            END_PROGRAM
            """, host => host
                .RegisterFunction("Next", 0, _ => new BcValue(++next))
                .RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(
            new long[] { 1, 2, 3 },
            run.Calls.Where(c => c.Name == "Log").Select(c => c.Arguments[0].IntValue));
    }

    [Fact]
    public void HostFunction_RealReturnValue_IsUsableInSt()
    {
        var run = StRunner.Run("""
            PROGRAM Main
            VAR
                r : LREAL;
            END_VAR
                r := Half(5.0);
                Log(r);
            END_PROGRAM
            """, host => host
                .RegisterFunction("Half", 1, args => new BcValue(args.Float(0) / 2.0), returnType: "LREAL")
                .RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(2.5, Assert.Single(run.Calls, c => c.Name == "Log").Arguments[0].FloatValue);
    }

    [Fact]
    public void HostFunction_BoolReturnValue_DrivesIfBranch()
    {
        var run = StRunner.Run("""
            PROGRAM Main
            VAR
                unused : DINT;
            END_VAR
                IF IsPositive(5) THEN
                    Log(1);
                ELSE
                    Log(0);
                END_IF
                IF IsPositive(-1) THEN
                    Log(1);
                ELSE
                    Log(0);
                END_IF
            END_PROGRAM
            """, host => host
                .RegisterFunction("IsPositive", 1, args => new BcValue(args.Int(0) > 0 ? 1L : 0L), returnType: "BOOL")
                .RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(
            new long[] { 1, 0 },
            run.Calls.Where(c => c.Name == "Log").Select(c => c.Arguments[0].IntValue));
    }

    [Fact]
    public void HostFunction_IsCalledOncePerLoopIteration()
    {
        var run = StRunner.Run("""
            PROGRAM Main
            VAR
                i : DINT;
            END_VAR
                FOR i := 1 TO 5 DO
                    Tick(i);
                END_FOR
            END_PROGRAM
            """, host => host.RegisterAction("Tick", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(5, run.Calls.Count);
        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, run.Calls.Select(c => c.Arguments[0].IntValue));
    }
}
