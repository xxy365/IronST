// VMBoundsTests.cs — BOUNDS 数组边界检查指令（C1）
using IronST.Runtime.Shared;

namespace IronST.Runtime.Tests;

public class VMBoundsTests
{
    [Fact]
    public void Bounds_InRange_Passes()
    {
        var vm = VMTestHelper.Run(VMTestHelper.Build(b =>
        {
            b.LDC(4, b.AddConst(3L));   // index = 3
            b.LDC(5, b.AddConst(10L));  // max = 10
            b.BOUNDS(4, 5);
        }));

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    [Fact]
    public void Bounds_ZeroIndex_IsValid()
    {
        var vm = VMTestHelper.Run(VMTestHelper.Build(b =>
        {
            b.LDC(4, b.AddConst(0L));
            b.LDC(5, b.AddConst(10L));
            b.BOUNDS(4, 5);
        }));

        Assert.Equal(VMState.Halted, vm.State);
    }

    [Fact]
    public void Bounds_OutOfRange_ThrowsArrayBounds()
    {
        var ex = Assert.Throws<RuntimeErrorException>(() =>
            VMTestHelper.Run(VMTestHelper.Build(b =>
            {
                b.LDC(4, b.AddConst(10L));  // index = max → 越界
                b.LDC(5, b.AddConst(10L));
                b.BOUNDS(4, 5);
            }))
        );
        Assert.Equal(RuntimeErrorType.ArrayBounds, ex.ErrorType);
    }

    [Fact]
    public void Bounds_NegativeIndex_ThrowsArrayBounds()
    {
        var ex = Assert.Throws<RuntimeErrorException>(() =>
            VMTestHelper.Run(VMTestHelper.Build(b =>
            {
                b.LDC(4, b.AddConst(-1L));
                b.LDC(5, b.AddConst(10L));
                b.BOUNDS(4, 5);
            }))
        );
        Assert.Equal(RuntimeErrorType.ArrayBounds, ex.ErrorType);
    }
}
