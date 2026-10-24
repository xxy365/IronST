// VMInstructionBudgetTests.cs — 指令预算（VMOptions.MaxInstructions，C1）
//
// 契约：
//   · MaxInstructions = 0（默认）→ 不限制，与既有行为完全一致；
//   · MaxInstructions = N → 恰好允许执行 N 条指令，第 N+1 条在指令边界抛
//     RuntimeErrorException(InstructionBudgetExceeded)，并带上该指令的 PC；
//   · 预算与取消（CancellationToken）互相独立：预算耗尽不是 OperationCanceledException。
using System.Diagnostics;
using IronST.Runtime.Shared;

namespace IronST.Runtime.Tests;

public class VMInstructionBudgetTests
{
    /// <summary>三条指令：NOP, NOP, HALT，占用 PC 0 / 1 / 2。</summary>
    private static BytecodeModule ThreeInstructionModule()
    {
        var b = new BytecodeBuilder();
        b.NOP();
        b.NOP();
        b.HALT();
        return b.Build();
    }

    /// <summary>代码段只有一条 JMP 0 —— 永不执行到 HALT。</summary>
    private static BytecodeModule InfiniteLoopModule()
    {
        var b = new BytecodeBuilder();
        b.JMP(0);
        return b.Build();
    }

    [Fact]
    public void ZeroBudget_MeansUnlimited()
    {
        var vm = new VM(ThreeInstructionModule(), new VMOptions { StackSize = 4096 });

        vm.Run();

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    [Fact]
    public void BudgetEqualToInstructionCount_RunsToCompletion()
    {
        var vm = new VM(ThreeInstructionModule(), new VMOptions
        {
            StackSize = 4096,
            MaxInstructions = 3,
        });

        vm.Run();

        Assert.Equal(VMState.Halted, vm.State);
    }

    [Fact]
    public void BudgetBelowInstructionCount_ThrowsOnFirstInstructionOverBudget()
    {
        var vm = new VM(ThreeInstructionModule(), new VMOptions
        {
            StackSize = 4096,
            MaxInstructions = 2,
        });

        var ex = Assert.Throws<RuntimeErrorException>(() => vm.Run());

        Assert.Equal(RuntimeErrorType.InstructionBudgetExceeded, ex.ErrorType);
        // 前两条（PC 0、1）执行完，第三条（PC 2，HALT）在边界被拦下。
        Assert.Equal(2u, ex.PC);
        Assert.Contains("指令预算耗尽", ex.Message);
        Assert.Contains("2", ex.Message);
        Assert.Equal(VMState.Halted, vm.State);
        Assert.Same(ex, vm.LastError);
    }

    [Fact]
    public void InfiniteLoop_IsStoppedByBudget()
    {
        var vm = new VM(InfiniteLoopModule(), new VMOptions
        {
            StackSize = 4096,
            MaxInstructions = 100_000,
        });

        var sw = Stopwatch.StartNew();
        var ex = Assert.Throws<RuntimeErrorException>(() => vm.Run());
        sw.Stop();

        Assert.Equal(RuntimeErrorType.InstructionBudgetExceeded, ex.ErrorType);
        Assert.True(
            sw.ElapsedMilliseconds < 5_000,
            $"指令预算未生效：Run() 耗时 {sw.ElapsedMilliseconds} ms（死循环仍未被叫停）");
    }

    [Fact]
    public void BudgetIsNotCancellation()
    {
        // 不传 CancellationToken：预算仍独立生效，且异常不是取消。
        var vm = new VM(InfiniteLoopModule(), new VMOptions
        {
            StackSize = 4096,
            MaxInstructions = 10,
        });

        var ex = Assert.Throws<RuntimeErrorException>(() => vm.Run());

        Assert.IsNotType<OperationCanceledException>(ex);
    }
}
