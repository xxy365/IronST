using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.Integration.Tests;

public class FunctionBlockIntegrationTests
{
    [Fact]
    public void FunctionBlock_InternalState_PersistsAcrossCalls()
    {
        var run = StRunner.Run("""
            FUNCTION_BLOCK Counter
            VAR_OUTPUT
                count : DINT;
            END_VAR
            VAR
                n : DINT;
            END_VAR
                n := n + 1;
                count := n;
            END_FUNCTION_BLOCK

            PROGRAM Main
            VAR
                c : Counter;
                v : DINT;
            END_VAR
                c(count := v);
                Log(v);
                c(count := v);
                Log(v);
                c(count := v);
                Log(v);
            END_PROGRAM
            """, host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(new long[] { 1, 2, 3 }, run.Calls.Select(c => c.Arguments[0].IntValue));
    }

    [Fact]
    public void TwoFunctionBlockInstances_KeepIndependentState()
    {
        var run = StRunner.Run("""
            FUNCTION_BLOCK Acc
            VAR_INPUT
                inc : DINT;
            END_VAR
            VAR_OUTPUT
                total : DINT;
            END_VAR
            VAR
                n : DINT;
            END_VAR
                n := n + inc;
                total := n;
            END_FUNCTION_BLOCK

            PROGRAM Main
            VAR
                a : Acc;
                b : Acc;
                v : DINT;
            END_VAR
                a(inc := 3, total := v);
                Log(v);
                b(inc := 10, total := v);
                Log(v);
                a(inc := 4, total := v);
                Log(v);
                b(inc := 20, total := v);
                Log(v);
            END_PROGRAM
            """, host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(new long[] { 3, 10, 7, 30 }, run.Calls.Select(c => c.Arguments[0].IntValue));
    }
}
