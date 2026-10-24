using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.Integration.Tests;

public class ArrayIntegrationTests
{
    [Fact]
    public void OneDimArray_ConstantAndVariableIndex_ReadBackCorrectly()
    {
        var run = StRunner.Run("""
            PROGRAM Main
            VAR
                a : ARRAY[0..9] OF DINT;
                i : DINT;
            END_VAR
                a[0] := 10;
                i := 3;
                a[i] := 30;
                a[9] := 90;
                Log(a[0]);
                Log(a[3]);
                Log(a[9]);
            END_PROGRAM
            """, host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(new long[] { 10, 30, 90 }, run.Calls.Select(c => c.Arguments[0].IntValue));
    }

    [Fact]
    public void TwoDimArray_CornersAndMiddle_ReadBackCorrectly()
    {
        var run = StRunner.Run("""
            PROGRAM Main
            VAR
                m : ARRAY[0..2, 0..3] OF DINT;
            END_VAR
                m[0, 0] := 1;
                m[0, 3] := 2;
                m[2, 0] := 3;
                m[2, 3] := 4;
                m[1, 2] := 5;
                Log(m[0, 0]);
                Log(m[0, 3]);
                Log(m[2, 0]);
                Log(m[2, 3]);
                Log(m[1, 2]);
            END_PROGRAM
            """, host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(
            new long[] { 1, 2, 3, 4, 5 },
            run.Calls.Select(c => c.Arguments[0].IntValue));
    }

    [Fact]
    public void ArrayOutOfBounds_IsReportedAsArrayBoundsError()
    {
        var run = StRunner.RunExpectingError("""
            PROGRAM Main
            VAR
                a : ARRAY[0..9] OF DINT;
                i : DINT;
            END_VAR
                i := 10;
                a[i] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, run.State);
        Assert.NotNull(run.Error);
        Assert.Equal(RuntimeErrorType.ArrayBounds, run.Error!.ErrorType);
    }

    [Fact]
    public void ArrayNegativeIndex_IsReportedAsArrayBoundsError()
    {
        var run = StRunner.RunExpectingError("""
            PROGRAM Main
            VAR
                a : ARRAY[0..9] OF DINT;
                i : DINT;
            END_VAR
                i := -1;
                a[i] := 1;
            END_PROGRAM
            """);

        Assert.NotNull(run.Error);
        Assert.Equal(RuntimeErrorType.ArrayBounds, run.Error!.ErrorType);
    }
}
