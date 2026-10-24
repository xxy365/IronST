// VMGlobalTests.cs — 全局变量 GETG/SETG
using IronST.Runtime.Shared;

namespace IronST.Runtime.Tests;

public class VMGlobalTests
{
    [Fact]
    public void Global_SetAndGet_RoundTrips()
    {
        var vm = VMTestHelper.Run(
            VMTestHelper.Build(
                b =>
                {
                    b.LDC(4, b.AddConst(7L));
                    b.SETG(4, 0);
                    b.GETG(4, 0);
                },
                globals: 1
            )
        );

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Equal(7, vm.Context.ReadReg(4).IntValue);
        Assert.Equal(7, vm.Context.ReadGlobal(0).IntValue);
    }

    [Fact]
    public void Global_IsSharedAcrossRegisters()
    {
        var vm = VMTestHelper.Run(
            VMTestHelper.Build(
                b =>
                {
                    b.LDC(5, b.AddConst(99L));
                    b.SETG(5, 0);       // globals[0] = 99
                    b.GETG(4, 0);       // R4 = globals[0]
                },
                globals: 1
            )
        );

        Assert.Equal(99, vm.Context.ReadReg(4).IntValue);
    }
}
