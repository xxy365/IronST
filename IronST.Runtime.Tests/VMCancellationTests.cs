// VMCancellationTests.cs — VM 协作式取消（CancellationToken）
//
// 契约：VMOptions.Cancellation 取消后，Run() 在指令边界退出并抛
// OperationCanceledException；不取消时行为与既有版本完全一致。
using System.Diagnostics;
using IronST.Runtime.Shared;

namespace IronST.Runtime.Tests;

public class VMCancellationTests
{
    /// <summary>代码段只有一条 JMP 0 —— 永不执行到 HALT。</summary>
    private static BytecodeModule InfiniteLoopModule()
    {
        var b = new BytecodeBuilder();
        b.JMP(0);
        return b.Build();
    }

    private static BytecodeModule HaltModule()
    {
        var b = new BytecodeBuilder();
        b.NOP();
        b.HALT();
        return b.Build();
    }

    [Fact]
    public void CancelDuringInfiniteLoop_ThrowsAndStopsQuickly()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var vm = new VM(InfiniteLoopModule(), new VMOptions
        {
            StackSize = 4096,
            Cancellation = cts.Token,
        });

        var sw = Stopwatch.StartNew();
        Assert.Throws<OperationCanceledException>(() => vm.Run());
        sw.Stop();

        Assert.Equal(VMState.Halted, vm.State);
        Assert.True(
            sw.ElapsedMilliseconds < 5000,
            $"取消未生效：Run() 耗时 {sw.ElapsedMilliseconds} ms（死循环仍未被叫停）");
    }

    [Fact]
    public void AlreadyCancelledToken_ExitsWithoutExecuting()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var vm = new VM(InfiniteLoopModule(), new VMOptions
        {
            StackSize = 4096,
            Cancellation = cts.Token,
        });

        Assert.Throws<OperationCanceledException>(() => vm.Run());
        Assert.Equal(VMState.Halted, vm.State);
    }

    [Fact]
    public void NormalProgram_WithLiveToken_StillHaltsNormally()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var vm = new VM(HaltModule(), new VMOptions
        {
            StackSize = 4096,
            Cancellation = cts.Token,
        });

        vm.Run();

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }
}
