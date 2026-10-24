// StructComparisonIntegrationTests.cs — A1/F5：结构体按值比较的端到端行为
//
// 每个用例都刻意保持“小”：
//   字节码后端的 _nextReg 只增不减，一条语句里的临时值不会回收，
//   太大的程序会先撞上 242 个寄存器的上限（独立缺陷，不在 A1 内修复）。
//   这里用 Log 观测比较结果（BOOL 的 IntValue 为 0/1）。
//
// 已知边界：`xs[0] := x` 这类“整体写入数组元素”的聚合搬运是独立缺陷
// （字段会保留 0/nil），所以数组元素用例直接逐字段赋值，不依赖该路径。
using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.Integration.Tests;

public class StructComparisonIntegrationTests
{
    private static StRunResult Run(string source) =>
        StRunner.Run(source, host => host.RegisterAction("Log", 1, _ => { }));

    private static long[] Results(StRunResult run) =>
        run.Calls.Select(c => c.Arguments[0].IntValue).ToArray();

    [Fact]
    public void FlatStruct_DirectVariables_NumericBoolRealEnum()
    {
        var run = Run("""
            TYPE Color : (Red, Green, Blue); END_TYPE;
            TYPE P : STRUCT
                a : DINT;
                b : BOOL;
                r : REAL;
                c : Color;
            END_STRUCT; END_TYPE;

            PROGRAM Main
            VAR x : P; y : P; END_VAR
                x.a := 10;
                x.b := TRUE;
                x.r := 1.5;
                x.c := Green;
                y := x;

                Log(x = y);
                Log(x <> y);

                y.a := 11;
                Log(x = y);
                Log(x <> y);

                y := x;
                y.b := FALSE;
                Log(x = y);
                Log(x <> y);

                y := x;
                y.r := 2.5;
                Log(x = y);
                Log(x <> y);

                y := x;
                y.c := Blue;
                Log(x = y);
                Log(x <> y);
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal([1L, 0L, 0L, 1L, 0L, 1L, 0L, 1L, 0L, 1L], Results(run));
    }

    [Fact]
    public void StructWithNestedAndArrayFields_ComparesEveryLeaf()
    {
        var run = Run("""
            TYPE Inner : STRUCT i : DINT; r : REAL; END_STRUCT; END_TYPE;
            TYPE P : STRUCT
                v : DINT;
                inner : Inner;
                a : ARRAY[0..2] OF INT;
                m : ARRAY[0..1, 0..1] OF DINT;
            END_STRUCT; END_TYPE;

            PROGRAM Main
            VAR x : P; y : P; END_VAR
                x.v := 1;
                x.inner.i := 2;
                x.inner.r := 3.5;
                x.a[0] := 4;
                x.a[1] := 5;
                x.a[2] := 6;
                x.m[0,0] := 7;
                x.m[0,1] := 8;
                x.m[1,0] := 9;
                x.m[1,1] := 10;
                y := x;

                Log(x = y);
                Log(x <> y);

                y.inner.r := 9.5;
                Log(x = y);
                Log(x <> y);

                y := x;
                y.a[1] := 99;
                Log(x = y);
                Log(x <> y);

                y := x;
                y.m[1,0] := 66;
                Log(x = y);
                Log(x <> y);
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal([1L, 0L, 0L, 1L, 0L, 1L, 0L, 1L], Results(run));
    }

    [Fact]
    public void SingleFieldAndAllBoolStructs_AreSupported()
    {
        var run = Run("""
            TYPE One : STRUCT v : DINT; END_STRUCT; END_TYPE;
            TYPE OnlyBool : STRUCT b : BOOL; END_STRUCT; END_TYPE;

            PROGRAM Main
            VAR p : One; q : One; b1 : OnlyBool; b2 : OnlyBool; END_VAR
                p.v := 5;
                q := p;
                Log(p = q);
                Log(p <> q);
                q.v := 6;
                Log(p = q);
                Log(p <> q);

                b1.b := TRUE;
                b2 := b1;
                Log(b1 = b2);
                b2.b := FALSE;
                Log(b1 = b2);
                Log(b1 <> b2);
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal([1L, 0L, 0L, 1L, 1L, 0L, 1L], Results(run));
    }

    [Fact]
    public void StructArrayElements_AreComparableWhenFieldsAreSetDirectly()
    {
        var run = Run("""
            TYPE Inner : STRUCT i : DINT; r : REAL; END_STRUCT; END_TYPE;
            TYPE P : STRUCT a : DINT; inner : Inner; END_STRUCT; END_TYPE;

            PROGRAM Main
            VAR xs : ARRAY[0..1] OF P; END_VAR
                xs[0].a := 1;
                xs[0].inner.i := 2;
                xs[0].inner.r := 3.5;
                xs[1].a := 1;
                xs[1].inner.i := 2;
                xs[1].inner.r := 3.5;

                Log(xs[0] = xs[1]);
                Log(xs[0] <> xs[1]);

                xs[1].inner.r := 9.5;
                Log(xs[0] = xs[1]);
                Log(xs[0] <> xs[1]);
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal([1L, 0L, 0L, 1L], Results(run));
    }

    [Fact]
    public void NestedStructFieldOperands_AreComparable()
    {
        var run = Run("""
            TYPE Inner : STRUCT i : DINT; r : REAL; f : BOOL; END_STRUCT; END_TYPE;
            TYPE P : STRUCT a : DINT; inner : Inner; END_STRUCT; END_TYPE;

            PROGRAM Main
            VAR x : P; y : P; END_VAR
                x.a := 1;
                x.inner.i := 2;
                x.inner.r := 3.5;
                x.inner.f := TRUE;
                y := x;

                Log(x.inner = y.inner);
                Log(x.inner <> y.inner);

                y.inner.f := FALSE;
                Log(x.inner = y.inner);
                Log(x.inner <> y.inner);
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal([1L, 0L, 0L, 1L], Results(run));
    }

    [Fact]
    public void DeeplyNestedStruct_ComparesRecursively()
    {
        var run = Run("""
            TYPE Color : (Red, Green, Blue); END_TYPE;
            TYPE A : STRUCT v : DINT; END_STRUCT; END_TYPE;
            TYPE B : STRUCT a : A; r : REAL; END_STRUCT; END_TYPE;
            TYPE C : STRUCT b : B; c : Color; END_STRUCT; END_TYPE;

            PROGRAM Main
            VAR x : C; y : C; END_VAR
                x.b.a.v := 7;
                x.b.r := 1.25;
                x.c := Blue;
                y := x;

                Log(x = y);
                Log(x <> y);

                y.b.a.v := 8;
                Log(x = y);
                Log(x <> y);
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal([1L, 0L, 0L, 1L], Results(run));
    }
}
