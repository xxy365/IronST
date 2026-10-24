using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.Integration.Tests;

public class StringIntegrationTests
{
    [Fact]
    public void StringStandardFunctions_ComposeAndReturnExpectedValues()
    {
        var run = StRunner.Run("""
            PROGRAM Main
            VAR
                s : STRING;
                n : DINT;
            END_VAR
                s := CONCAT('ab', 'cd');
                Log(s);
                n := LEN(s);
                Log(n);

                s := LEFT('abcdef', 3);
                Log(s);
                s := RIGHT('abcdef', 2);
                Log(s);
                s := MID('abcdef', 2, 3);
                Log(s);

                n := FIND('abcdef', 'cd');
                Log(n);
            END_PROGRAM
            """, host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(6, run.Calls.Count);
        Assert.Equal("abcd", run.Calls[0].Arguments[0].StringValue);
        Assert.Equal(4, run.Calls[1].Arguments[0].IntValue);
        Assert.Equal("abc", run.Calls[2].Arguments[0].StringValue);
        Assert.Equal("ef", run.Calls[3].Arguments[0].StringValue);
        Assert.Equal("cd", run.Calls[4].Arguments[0].StringValue);
        Assert.Equal(3, run.Calls[5].Arguments[0].IntValue);
    }
}
