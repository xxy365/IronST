using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.Integration.Tests;

public class GlobalVariableIntegrationTests
{
    [Fact]
    public void GlobalScalar_IsSharedAcrossFunctions()
    {
        // 前端按 POU 作用域解析 VAR_GLOBAL：两个 POU 各声明同名全局，
        // IR 层按名字去重，最终落到同一个全局槽位。
        var run = StRunner.Run("""
            PROGRAM Main
            VAR_GLOBAL
                g : DINT;
            END_VAR
            VAR
                unused : DINT;
            END_VAR
                Inc();
                Inc();
                Inc();
                Log(g);
            END_PROGRAM

            FUNCTION Inc : DINT
            VAR_GLOBAL
                g : DINT;
            END_VAR
                g := g + 1;
                Inc := g;
            END_FUNCTION
            """, host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(3, Assert.Single(run.Calls).Arguments[0].IntValue);
    }

    [Fact]
    public void GlobalArray_ElementWriteAndRead_AreVisible()
    {
        var run = StRunner.Run("""
            PROGRAM Main
            VAR_GLOBAL
                a : ARRAY[0..4] OF DINT;
            END_VAR
            VAR
                unused : DINT;
            END_VAR
                a[2] := 7;
                a[4] := 9;
                Log(a[2]);
                Log(a[4]);
            END_PROGRAM
            """, host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(new long[] { 7, 9 }, run.Calls.Select(c => c.Arguments[0].IntValue));
    }
}
