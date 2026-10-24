// VMTestHelper.cs — VM 测试公共辅助
// 用 BytecodeBuilder 手工构建字节码 → 发布版 VM 执行。
using IronST.Runtime.Shared;

namespace IronST.Runtime.Tests;

internal static class VMTestHelper
{
    /// <summary>A0 寄存器（结果约定）。</summary>
    public const byte A0 = BytecodeBuilder.A0;

    /// <summary>构建模块：emit 后自动补 HALT。</summary>
    public static BytecodeModule Build(Action<BytecodeBuilder> emit, int globals = 0)
    {
        var b = new BytecodeBuilder();
        b.Globals(globals);
        emit(b);
        b.HALT();
        return b.Build();
    }

    public static VM Run(
        BytecodeModule m,
        HostBridge? bridge = null,
        IReadOnlyList<(byte, int)>? structTypes = null
    )
    {
        var vm = new VM(m, new VMOptions
        {
            StackSize = 4096,
            HostBridge = bridge,
            StructTypes = structTypes,
        });
        vm.Run();
        return vm;
    }

    /// <summary>执行并断言正常停机，返回 A0 的整数值。</summary>
    public static long RunInt(Action<BytecodeBuilder> emit, int globals = 0)
    {
        var vm = Run(Build(emit, globals));
        Assert.Equal(VMState.Halted, vm.State);
        return vm.Context.ReadReg(A0).IntValue;
    }

    /// <summary>执行并断言正常停机，返回 A0 的浮点值。</summary>
    public static double RunFloat(Action<BytecodeBuilder> emit)
    {
        var vm = Run(Build(emit));
        Assert.Equal(VMState.Halted, vm.State);
        return vm.Context.ReadReg(A0).FloatValue;
    }
}
