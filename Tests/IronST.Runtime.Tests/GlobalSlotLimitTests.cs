// GlobalSlotLimitTests.cs — F6：BytecodeAssembler 全局槽位上限
using IronST.Runtime.Shared;

namespace IronST.Runtime.Tests;

/// <summary>
/// F6：<c>BytecodeAssembler._globalCount</c> 也是 <c>short</c>。第 32768 次声明时
/// <c>_globalCount++</c> 会把 32767 回绕成 -32768，<see cref="BytecodeAssembler.Build"/>
/// 把这个负数写进头部，VM 随后 <c>new BcValue[负数]</c> 直接崩溃。
/// </summary>
public class GlobalSlotLimitTests
{
    private static BytecodeAssembler AssemblerWithMaxGlobals()
    {
        var asm = new BytecodeAssembler();
        for (int i = 0; i < BytecodeAssembler.MaxGlobals; i++)
            asm.DeclareGlobal($"g{i}");
        return asm;
    }

    /// <summary>
    /// 恰好 32767 个：构建后头部的 GlobalCount 必须仍是 32767（正数）。
    /// 这条断言同时反证了 short 计数器没有回绕 —— 回绕后该值会是 -32768。
    /// </summary>
    [Fact]
    public void Build_AtLimit_CounterDoesNotWrap()
    {
        var asm = AssemblerWithMaxGlobals();
        asm.Exit();

        Assert.Equal(BytecodeAssembler.MaxGlobals, asm.Build().GlobalCount);
    }

    /// <summary>第 32768 个必须在回绕前被拒绝。</summary>
    [Fact]
    public void DeclareGlobal_OnePastLimit_Throws()
    {
        var asm = AssemblerWithMaxGlobals();

        var ex = Assert.Throws<InvalidOperationException>(
            () => asm.DeclareGlobal("overflow")
        );

        Assert.Contains(BytecodeAssembler.MaxGlobals.ToString(), ex.Message);
    }

    /// <summary>
    /// 端到端验证「最大有效索引为 32766」：把值写进最后一个槽位并让 VM 读回来。
    /// 槽位按 LE16 编码，若发生回绕这里会写到负索引、被 VM 判为 SlotOverflow。
    /// </summary>
    [Fact]
    public void LastSlot_Index32766_IsAddressableEndToEnd()
    {
        var asm = AssemblerWithMaxGlobals();

        byte r = asm.Load(12345L);
        asm.SetGlobal(r, $"g{BytecodeAssembler.MaxGlobals - 1}");
        asm.Exit();

        var module = asm.Build();
        Assert.Equal(BytecodeAssembler.MaxGlobals, module.GlobalCount);

        var vm = new VM(module, new VMOptions { StackSize = 4096 });
        vm.Run();

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Equal(12345, vm.Context.ReadGlobal(BytecodeAssembler.MaxGlobals - 1).IntValue);
    }
}
