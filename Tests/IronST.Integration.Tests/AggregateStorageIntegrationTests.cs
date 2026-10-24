// AggregateStorageIntegrationTests.cs — A8 / A9
//
// A8：全局聚合（结构体 / 数组）按 FlatSlotCount 展开为连续的全局槽；
//     字段/元素读写走 GETGI/SETGI，整体赋值与比较不再落进错误的栈槽。
// A9：聚合整体写入/读出数组元素（源/目标是 GEP 运行时地址）按 FlatSlotCount 深拷贝。
//
// 每个用例都用 Log 观测结果，避免依赖 Print 的格式化。
using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.Integration.Tests;

public class AggregateStorageIntegrationTests
{
    private static StRunResult Run(string source) =>
        StRunner.Run(source, host => host.RegisterAction("Log", 1, _ => { }));

    private static long[] Results(StRunResult run) =>
        run.Calls.Select(c => c.Arguments[0].IntValue).ToArray();

    private const string Types = """
        TYPE Inner : STRUCT i : DINT; r : REAL; END_STRUCT; END_TYPE;
        TYPE P : STRUCT inner : Inner; n : DINT; b : BOOL; END_STRUCT; END_TYPE;
        """;

    // ── A8：全局聚合 ──────────────────────────────────────────────

    [Fact]
    public void GlobalStruct_FieldWriteWholeAssignment_AreStored()
    {
        var run = Run($$"""
            {{Types}}

            PROGRAM Main
            VAR_GLOBAL
                x : P;
                y : P;
            END_VAR
            VAR
                unused : DINT;
            END_VAR
                x.inner.i := 41;
                x.inner.r := 1.5;
                x.n := 7;
                x.b := TRUE;

                y := x;

                Log(x.inner.i);
                Log(y.inner.i);
                Log(y.n);
                Log(y.b);
                Log(y.inner.r);
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal([41L, 41L, 7L, 1L], Results(run)[..4]);
        Assert.Equal(1.5, run.Calls[^1].Arguments[0].FloatValue);
    }

    [Fact]
    public void GlobalStruct_ComparesByValue()
    {
        var run = Run($$"""
            {{Types}}

            PROGRAM Main
            VAR_GLOBAL
                x : P;
                y : P;
            END_VAR
            VAR
                l : P;
            END_VAR
                x.inner.i := 11;
                x.inner.r := 1.5;
                x.n := 3;
                x.b := TRUE;

                y := x;
                Log(x = y);
                Log(x <> y);

                y.n := 4;
                Log(x = y);
                Log(x <> y);

                l := x;
                Log(x = l);
                Log(y = l);
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal([1L, 0L, 0L, 1L, 1L, 0L], Results(run));
    }

    [Fact]
    public void GlobalStruct_IsVisibleAcrossPous()
    {
        var run = Run($$"""
            {{Types}}

            FUNCTION Bump : DINT
            VAR_EXTERNAL
                g : P;
            END_VAR
                g.n := g.n + 1;
                g.inner.i := 99;
                Bump := g.n;
            END_FUNCTION

            PROGRAM Main
            VAR_GLOBAL
                g : P;
            END_VAR
            VAR
                k : DINT;
            END_VAR
                g.n := 5;
                g.inner.i := 1;

                k := Bump();
                Bump();

                Log(k);
                Log(g.n);
                Log(g.inner.i);
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal([6L, 7L, 99L], Results(run));
    }

    [Fact]
    public void GlobalArrayOfStructs_DynamicIndex_ReadsAndWrites()
    {
        var run = Run($$"""
            {{Types}}

            PROGRAM Main
            VAR_GLOBAL
                xs : ARRAY[0..2] OF P;
                nums : ARRAY[0..2] OF DINT;
            END_VAR
            VAR
                i : DINT;
            END_VAR
                i := 1;
                xs[i].inner.i := 21;
                xs[i].n := 33;
                nums[i] := 44;

                Log(xs[1].inner.i);
                Log(xs[i].n);
                Log(nums[i]);

                xs[2] := xs[i];
                Log(xs[2].inner.i);
                Log(xs[2].n);
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal([21L, 33L, 44L, 21L, 33L], Results(run));
    }

    [Fact]
    public void LocalCopyOfGlobalAggregate_DoesNotAlias()
    {
        var run = Run($$"""
            {{Types}}

            PROGRAM Main
            VAR_GLOBAL
                g : P;
            END_VAR
            VAR
                l : P;
            END_VAR
                g.inner.i := 3;
                g.n := 4;

                l := g;
                l.inner.i := 100;
                l.n := 200;

                Log(g.inner.i);
                Log(g.n);
                Log(l.inner.i);
                Log(l.n);
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal([3L, 4L, 100L, 200L], Results(run));
    }

    // ── A9：数组元素的聚合搬运 ────────────────────────────────────

    [Fact]
    public void AggregateStoreToArrayElement_CopiesEveryField()
    {
        var run = Run($$"""
            {{Types}}

            PROGRAM Main
            VAR
                x : P;
                xs : ARRAY[0..1] OF P;
            END_VAR
                x.inner.i := 7;
                x.inner.r := 2.5;
                x.n := 9;
                x.b := TRUE;

                xs[0] := x;

                Log(xs[0].inner.i);
                Log(xs[0].n);
                Log(xs[0].b);
                Log(xs[0].inner.r);
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal([7L, 9L, 1L], Results(run)[..3]);
        Assert.Equal(2.5, run.Calls[^1].Arguments[0].FloatValue);
    }

    [Fact]
    public void AggregateCopyBetweenArrayElements_AndDynamicIndex()
    {
        var run = Run($$"""
            {{Types}}

            PROGRAM Main
            VAR
                xs : ARRAY[0..2] OF P;
                i : DINT;
            END_VAR
                xs[0].inner.i := 5;
                xs[0].n := 6;

                xs[1] := xs[0];
                Log(xs[1].inner.i);
                Log(xs[1].n);

                i := 2;
                xs[i] := xs[1];
                Log(xs[2].inner.i);
                Log(xs[2].n);
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal([5L, 6L, 5L, 6L], Results(run));
    }

    [Fact]
    public void AggregateSelfAssignmentAndOverlap_AreStable()
    {
        var run = Run($$"""
            {{Types}}

            PROGRAM Main
            VAR
                xs : ARRAY[0..1] OF P;
            END_VAR
                xs[0].inner.i := 12;
                xs[0].n := 13;

                xs[0] := xs[0];
                Log(xs[0].inner.i);
                Log(xs[0].n);

                xs[1] := xs[0];
                xs[0] := xs[1];
                Log(xs[0].inner.i);
                Log(xs[1].n);
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal([12L, 13L, 12L, 13L], Results(run));
    }

    [Fact]
    public void NestedStructField_WholeAssignment()
    {
        var run = Run($$"""
            {{Types}}

            PROGRAM Main
            VAR
                a : P;
                b : P;
            END_VAR
                a.inner.i := 5;
                a.inner.r := 1.25;

                b.inner := a.inner;

                Log(b.inner.i);
                Log(b.inner.r);
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(5, run.Calls[0].Arguments[0].IntValue);
        Assert.Equal(1.25, run.Calls[1].Arguments[0].FloatValue);
    }

    [Fact]
    public void AggregatePassedByValue_FromArrayElement()
    {
        var run = Run($$"""
            {{Types}}

            FUNCTION Sum : DINT
            VAR_INPUT p : P; END_VAR
                Sum := p.inner.i + p.n;
            END_FUNCTION

            PROGRAM Main
            VAR
                xs : ARRAY[0..1] OF P;
            END_VAR
                xs[0].inner.i := 3;
                xs[0].n := 4;
                Log(Sum(xs[0]));
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(7, Assert.Single(run.Calls).Arguments[0].IntValue);
    }
}
