// VMStructTests.cs — 结构体 SNEW/SGET/SSET + 类型注册
using IronST.Runtime.Shared;

namespace IronST.Runtime.Tests;

public class VMStructTests
{
    [Fact]
    public void Struct_SnewSsetSget_FieldRoundTrip()
    {
        var vm = VMTestHelper.Run(
            VMTestHelper.Build(b =>
            {
                b.SNEW(4, 1, 2);        // R4 = struct(typeId=1, 2 字段)
                b.LDC(5, b.AddConst(42L));
                b.SSET(5, 4, 0);        // struct.field0 = 42
                b.SGET(6, 4, 0);        // R6 = struct.field0
                b.MOV(4, 6);
            }),
            structTypes: new List<(byte, int)> { ((byte)1, 2) }
        );

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Equal(42, vm.Context.ReadReg(4).IntValue);
    }

    [Fact]
    public void Struct_MultipleFields_Independent()
    {
        var vm = VMTestHelper.Run(
            VMTestHelper.Build(b =>
            {
                b.SNEW(4, 1, 3);
                b.LDC(5, b.AddConst(10L));
                b.LDC(6, b.AddConst(20L));
                b.SSET(5, 4, 0);
                b.SSET(6, 4, 2);
                b.SGET(7, 4, 0);        // field0 = 10
                b.SGET(8, 4, 2);        // field2 = 20
                b.IADD(4, 7, 8);        // 10 + 20
            }),
            structTypes: new List<(byte, int)> { ((byte)1, 3) }
        );

        Assert.Equal(30, vm.Context.ReadReg(4).IntValue);
    }

    [Fact]
    public void Struct_UnregisteredType_ThrowsTypeError()
    {
        var ex = Assert.Throws<RuntimeErrorException>(() =>
            VMTestHelper.Run(VMTestHelper.Build(b =>
            {
                b.SNEW(4, 9, 2);        // typeId 9 未注册
                b.LDC(5, b.AddConst(1L));
                b.SSET(5, 4, 0);
            }))
        );
        Assert.Equal(RuntimeErrorType.TypeError, ex.ErrorType);
        Assert.Contains("unregistered typeId 9", ex.Message);
    }
}
