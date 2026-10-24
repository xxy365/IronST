using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.Integration.Tests;

public class FunctionIntegrationTests
{
    [Fact]
    public void RecursiveFunction_AccumulatesThroughVarInOut()
    {
        var run = StRunner.Run("""
            FUNCTION SumDown : DINT
            VAR_INPUT
                n : DINT;
            END_VAR
            VAR_IN_OUT
                acc : DINT;
            END_VAR
                IF n > 0 THEN
                    acc := acc + n;
                    SumDown := SumDown(n - 1, acc);
                ELSE
                    SumDown := acc;
                END_IF
            END_FUNCTION

            PROGRAM Main
            VAR
                v : DINT;
                result : DINT;
            END_VAR
                v := 0;
                result := SumDown(5, v);
                Log(result);
                Log(v);
            END_PROGRAM
            """, host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(
            new long[] { 15, 15 },
            run.Calls.Select(c => c.Arguments[0].IntValue));
    }

    [Fact]
    public void VarInOut_ModifiesCallerVariable()
    {
        var run = StRunner.Run("""
            FUNCTION Bump : DINT
            VAR_IN_OUT
                x : DINT;
            END_VAR
                x := x + 1;
                Bump := x;
            END_FUNCTION

            PROGRAM Main
            VAR
                v : DINT;
            END_VAR
                v := 41;
                Bump(v);
                Log(v);
            END_PROGRAM
            """, host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(42, Assert.Single(run.Calls).Arguments[0].IntValue);
    }

    [Fact]
    public void VarOutput_WritesThroughToCaller()
    {
        var run = StRunner.Run("""
            FUNCTION DivMod : DINT
            VAR_INPUT
                a : DINT;
                b : DINT;
            END_VAR
            VAR_OUTPUT
                q : DINT;
                r : DINT;
            END_VAR
                q := a / b;
                r := a - q * b;
                DivMod := 0;
            END_FUNCTION

            PROGRAM Main
            VAR
                q : DINT;
                r : DINT;
            END_VAR
                DivMod(17, 5, q, r);
                Log(q);
                Log(r);
            END_PROGRAM
            """, host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(new long[] { 3, 2 }, run.Calls.Select(c => c.Arguments[0].IntValue));
    }

    [Fact]
    public void HostFunction_CalledFromWithinStFunction_ReachesHost()
    {
        var run = StRunner.Run("""
            FUNCTION Bump : DINT
            VAR_INPUT
                x : DINT;
            END_VAR
                Tick(x);
                Bump := x + 1;
            END_FUNCTION

            PROGRAM Main
            VAR
                result : DINT;
            END_VAR
                result := Bump(7);
                Log(result);
            END_PROGRAM
            """, host => host
                .RegisterAction("Tick", 1, _ => { })
                .RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(new[] { "Tick", "Log" }, run.Calls.Select(c => c.Name));
        Assert.Equal(new long[] { 7, 8 }, run.Calls.Select(c => c.Arguments[0].IntValue));
    }

    [Fact]
    public void DirectCall_ZeroArguments_ReturnsConstant()
    {
        var run = StRunner.Call("""
            FUNCTION Answer : DINT
                Answer := 42;
            END_FUNCTION
            """, "Answer");

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(42, run.ReturnValue.IntValue);
    }

    [Fact]
    public void RecursiveFunction_ReturnsFactorial()
    {
        var run = StRunner.Run("""
            FUNCTION Fact : DINT
            VAR_INPUT
                n : DINT;
            END_VAR
                IF n <= 1 THEN
                    Fact := 1;
                ELSE
                    Fact := n * Fact(n - 1);
                END_IF
            END_FUNCTION

            PROGRAM Main
            VAR
                result : DINT;
            END_VAR
                result := Fact(5);
                Log(result);
            END_PROGRAM
            """, host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(120, Assert.Single(run.Calls).Arguments[0].IntValue);
    }

    [Fact]
    public void FunctionChain_ComposesReturnValues()
    {
        var run = StRunner.Run("""
            FUNCTION Double : DINT
            VAR_INPUT
                x : DINT;
            END_VAR
                Double := x + x;
            END_FUNCTION

            FUNCTION AddOne : DINT
            VAR_INPUT
                x : DINT;
            END_VAR
                AddOne := x + 1;
            END_FUNCTION

            PROGRAM Main
            VAR
                result : DINT;
            END_VAR
                result := AddOne(Double(Double(10)));
                Log(result);
            END_PROGRAM
            """, host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(41, Assert.Single(run.Calls).Arguments[0].IntValue);
    }

    [Fact]
    public void RecursiveFunction_WithStackArgument_PreservesCallerValues()
    {
        var run = StRunner.Run("""
            FUNCTION FactAcc : DINT
            VAR_INPUT
                n : DINT;
                acc : DINT;
                a : DINT;
                b : DINT;
                c : DINT;
            END_VAR
                IF n <= 1 THEN
                    FactAcc := acc;
                ELSE
                    FactAcc := FactAcc(n - 1, acc * n, a, b, c);
                END_IF
            END_FUNCTION

            PROGRAM Main
            VAR
                result : DINT;
            END_VAR
                result := FactAcc(5, 1, 0, 0, 0);
                Log(result);
            END_PROGRAM
            """, host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(120, Assert.Single(run.Calls).Arguments[0].IntValue);
    }

    [Fact]
    public void LiveValueAcrossCall_WithVarInOut_RebasesPointer()
    {
        var run = StRunner.Run("""
            FUNCTION Bump : DINT
            VAR_IN_OUT
                x : DINT;
            END_VAR
            VAR_INPUT
                n : DINT;
            END_VAR
                x := x + n;
                Bump := x;
            END_FUNCTION

            PROGRAM Main
            VAR
                a : DINT;
                b : DINT;
                c : DINT;
            END_VAR
                a := 10;
                b := a + a;
                c := Bump(a, 5);
                b := b + Bump(a, 2);
                Log(a);
                Log(b);
                Log(c);
            END_PROGRAM
            """, host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(
            new long[] { 17, 37, 15 },
            run.Calls.Where(c => c.Name == "Log")
                .Select(c => c.Arguments[0].IntValue)
                .ToArray());
    }

    [Fact]
    public void LiveValueAcrossCall_IsPreserved()
    {
        var run = StRunner.Run("""
            FUNCTION Inc : DINT
            VAR_INPUT
                x : DINT;
            END_VAR
                Inc := x + 1;
            END_FUNCTION

            PROGRAM Main
            VAR
                a : DINT;
                result : DINT;
            END_VAR
                a := 5;
                result := a + Inc(1);
                Log(result);
            END_PROGRAM
            """, host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(7, Assert.Single(run.Calls).Arguments[0].IntValue);
    }

    [Fact]
    public void LiveValueAcrossHostCall_IsPreserved()
    {
        var run = StRunner.Run("""
            PROGRAM Main
            VAR
                a : DINT;
                result : DINT;
            END_VAR
                a := 5;
                result := a + HostInc(1);
                Log(result);
            END_PROGRAM
            """, host => host
                .RegisterFunction("HostInc", 1, args => new BcValue(args.Int(0) + 1))
                .RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(
            7,
            Assert.Single(run.Calls, c => c.Name == "Log").Arguments[0].IntValue);
    }

    [Fact]
    public void LiveValueAcrossConditionalCall_MergeReloadsSpill()
    {
        var run = StRunner.Run("""
            FUNCTION Inc : DINT
            VAR_INPUT
                x : DINT;
            END_VAR
                Inc := x + 1;
            END_FUNCTION

            PROGRAM Main
            VAR
                a : DINT;
                b : DINT;
                result : DINT;
            END_VAR
                a := 10;
                b := a + a;
                IF b > 0 THEN
                    result := Inc(1);
                ELSE
                    result := 0;
                END_IF
                result := b + result;
                Log(result);
            END_PROGRAM
            """, host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(22, Assert.Single(run.Calls).Arguments[0].IntValue);
    }

    [Fact]
    public void MixedScalarAndStructArguments_FillFourSlotsAndStack()
    {
        var run = StRunner.Run("""
            TYPE P : STRUCT
                x : DINT;
                y : DINT;
            END_STRUCT; END_TYPE;

            FUNCTION Mix : DINT
            VAR_INPUT
                a : DINT;
                p : P;
                b : DINT;
                q : P;
            END_VAR
                Mix := a * 1000 + p.x * 100 + b * 10 + q.y;
            END_FUNCTION

            PROGRAM Main
            VAR
                p : P;
                q : P;
                result : DINT;
            END_VAR
                p.x := 3;
                p.y := 4;
                q.x := 5;
                q.y := 6;
                result := Mix(7, p, 8, q);
                Log(result);
            END_PROGRAM
            """, host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(7386, Assert.Single(run.Calls).Arguments[0].IntValue);
    }
}
