// VMErrorTests.cs — 运行时异常路径
using IronST.Runtime.Shared;

namespace IronST.Runtime.Tests;

public class VMErrorTests
{
    [Fact]
    public void IDiv_ByZero_ThrowsDivideByZero()
    {
        var ex = Assert.Throws<RuntimeErrorException>(() =>
            VMTestHelper.Run(VMTestHelper.Build(b =>
            {
                b.LDC(4, b.AddConst(1L));
                b.LDC(5, b.AddConst(0L));
                b.IDIV(4, 4, 5);
            }))
        );
        Assert.Equal(RuntimeErrorType.DivideByZero, ex.ErrorType);
    }

    [Fact]
    public void FDiv_ByZero_ThrowsDivideByZero()
    {
        var ex = Assert.Throws<RuntimeErrorException>(() =>
            VMTestHelper.Run(VMTestHelper.Build(b =>
            {
                b.LDC(4, b.AddConst(1.0));
                b.LDC(5, b.AddConst(0.0));
                b.FDIV(4, 4, 5);
            }))
        );
        Assert.Equal(RuntimeErrorType.DivideByZero, ex.ErrorType);
    }

    [Fact]
    public void UndefinedOpcode_ThrowsIllegalOpcode()
    {
        // BytecodeBuilder.Build 会在校验阶段拦截非法指令，
        // 直接用 FromBuilder 构造模块绕过校验，测试 VM 运行时的处理。
        var module = BytecodeModule.FromBuilder(
            0,
            new byte[] { 0xFF, 0, 0 },
            Array.Empty<BcValue>(),
            0,
            debugInfo: null
        );

        var ex = Assert.Throws<RuntimeErrorException>(() => VMTestHelper.Run(module));
        Assert.Equal(RuntimeErrorType.IllegalOpcode, ex.ErrorType);
    }

    [Fact]
    public void SlotOverflow_GlobalIndexOutOfRange_Throws()
    {
        var ex = Assert.Throws<RuntimeErrorException>(() =>
            VMTestHelper.Run(VMTestHelper.Build(b =>
            {
                b.LDC(4, b.AddConst(1L));
                b.SETG(4, 5);           // 只有 0 个全局槽
            }))
        );
        Assert.Equal(RuntimeErrorType.SlotOverflow, ex.ErrorType);
    }
}
