using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.Integration.Tests;

/// <summary>
/// 宿主函数 &gt;4 参数的回归测试：
///   · 第 5 个起的参数在栈上，读取必须以当前 SP 为基准；
///   · HCALL 返回后必须回收调用方压入的参数槽，否则重复调用泄漏栈。
/// </summary>
public class HostArgumentPassingTests
{
    [Fact]
    public void EightArguments_AllArriveInDeclarationOrder()
    {
        long[]? seen = null;

        var run = StRunner.Run("""
            PROGRAM Main
            VAR x : DINT; END_VAR
                x := Sum8(1, 2, 3, 4, 5, 6, 7, 8);
                Log(x);
            END_PROGRAM
            """, host => host
                .RegisterFunction("Sum8", 8, args =>
                {
                    seen = args.ToArray().Select(a => a.IntValue).ToArray();
                    return new BcValue(0L);
                })
                .RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6, 7, 8 }, seen);
    }

    [Fact]
    public void CSharpCall_StFunction_SixArguments_ExtraArgsGoOnStack()
    {
        var run = StRunner.Call("""
            FUNCTION Sum6 : DINT
            VAR_INPUT
                a : DINT;
                b : DINT;
                c : DINT;
                d : DINT;
                e : DINT;
                f : DINT;
            END_VAR
                Sum6 := a * 100000 + b * 10000 + c * 1000 + d * 100 + e * 10 + f;
            END_FUNCTION
            """,
            "Sum6",
            configureHost: null,
            new BcValue(1L),
            new BcValue(2L),
            new BcValue(3L),
            new BcValue(4L),
            new BcValue(5L),
            new BcValue(6L));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(123456, run.ReturnValue.IntValue);
    }

    [Fact]
    public void FiveArguments_RepeatedCalls_DoNotLeakStack()
    {
        var stackPointers = new List<long>();

        var run = StRunner.Run("""
            PROGRAM Main
            VAR x : DINT; END_VAR
                x := Sum5(1, 2, 3, 4, 5);
                x := Sum5(1, 2, 3, 4, 5);
                x := Sum5(1, 2, 3, 4, 5);
                x := Sum5(1, 2, 3, 4, 5);
                x := Sum5(1, 2, 3, 4, 5);
                x := Sum5(1, 2, 3, 4, 5);
                x := Sum5(1, 2, 3, 4, 5);
                x := Sum5(1, 2, 3, 4, 5);
                x := Sum5(1, 2, 3, 4, 5);
                x := Sum5(1, 2, 3, 4, 5);
            END_PROGRAM
            """, host => host.RegisterFunction("Sum5", 5, args =>
            {
                stackPointers.Add(args.StackPointer);
                return new BcValue(0L);
            }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(10, run.Calls.Count);
        Assert.Single(stackPointers.Distinct());
    }
}
