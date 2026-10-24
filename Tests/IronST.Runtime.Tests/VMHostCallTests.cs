// VMHostCallTests.cs — HCALL 宿主互操作
using IronST.Runtime.Shared;

namespace IronST.Runtime.Tests;

public class VMHostCallTests
{
    [Fact]
    public void HCall_RegisteredFunction_DoublesInput()
    {
        var bridge = new HostBridge();
        bridge.Register(0x0100, ctx => new BcValue(ctx.ReadReg(4).IntValue * 2));

        var vm = VMTestHelper.Run(
            VMTestHelper.Build(b =>
            {
                b.LDC(4, b.AddConst(21L));
                b.HCALL(0x0100);
            }),
            bridge
        );

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Equal(42, vm.Context.ReadReg(4).IntValue);
    }

    [Fact]
    public void HCall_ReadsMultipleArgs_FromRegistersAndStack()
    {
        var bridge = new HostBridge();
        bridge.Register(
            0x0101,
            ctx => new BcValue(ctx.ReadReg(4).IntValue + ctx.ReadReg(5).IntValue)
        );

        var vm = VMTestHelper.Run(
            VMTestHelper.Build(b =>
            {
                b.LDC(4, b.AddConst(30L));
                b.LDC(5, b.AddConst(12L));
                b.HCALL(0x0101);
            }),
            bridge
        );

        Assert.Equal(42, vm.Context.ReadReg(4).IntValue);
    }

    [Fact]
    public void HCall_Unregistered_ThrowsIllegalOpcode()
    {
        var ex = Assert.Throws<RuntimeErrorException>(() =>
            VMTestHelper.Run(VMTestHelper.Build(b =>
            {
                b.LDC(4, b.AddConst(1L));
                b.HCALL(0x7FFF);
            }))
        );
        Assert.Equal(RuntimeErrorType.IllegalOpcode, ex.ErrorType);
        Assert.Contains("unregistered", ex.Message);
    }
}
