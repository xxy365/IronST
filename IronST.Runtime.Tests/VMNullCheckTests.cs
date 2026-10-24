// VMNullCheckTests.cs — NULLCK 引用判空指令（A2）
//
// 契约：
//   · 引用在字节码里是帧槽号，槽 0 也合法，因此只能按 BcValue.Tag 判空：
//     Void（零初始化 / 从未绑定）= 空引用；Int64（含数值 0）= 合法指针；
//   · 空引用解引用抛 RuntimeErrorException(NullReference)，带指令 PC，
//     有 SourceLocator 时消息带「文件:行」；
//   · 非空引用的行为不变。
using IronST.Runtime.Shared;

namespace IronST.Runtime.Tests;

public class VMNullCheckTests
{
    /// <summary>LNIL(2B) + NULLCK(2B) + HALT(1B)：NULLCK 位于 PC 2。</summary>
    private static BytecodeModule NullCheckModule(bool nullReference)
    {
        var b = new BytecodeBuilder();
        if (nullReference)
        {
            b.LNIL(8);              // 零初始化引用：Tag = Void
        }
        else
        {
            b.LTRUE(8);             // 1
            b.LTRUE(9);             // 1
            b.ISUB(8, 8, 9);        // Int64 0 —— 槽 0，合法指针
        }
        b.NULLCK(8);
        b.HALT();
        return b.Build();
    }

    [Fact]
    public void VoidReference_ThrowsNullReferenceAtInstructionPC()
    {
        var vm = new VM(NullCheckModule(nullReference: true), new VMOptions { StackSize = 4096 });

        var ex = Assert.Throws<RuntimeErrorException>(() => vm.Run());

        Assert.Equal(RuntimeErrorType.NullReference, ex.ErrorType);
        Assert.Equal(2u, ex.PC);
        Assert.Contains("空引用解引用", ex.Message);
        Assert.Equal(VMState.Halted, vm.State);
        Assert.Same(ex, vm.LastError);
    }

    [Fact]
    public void IntegerZero_IsNotTreatedAsNull()
    {
        // 槽 0 是合法槽：把「数值 0」当空引用会误伤合法引用。
        var vm = new VM(NullCheckModule(nullReference: false), new VMOptions { StackSize = 4096 });

        vm.Run();

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    [Fact]
    public void NullReferenceError_CarriesSourceLocation()
    {
        var vm = new VM(NullCheckModule(nullReference: true), new VMOptions
        {
            StackSize = 4096,
            SourceLocator = pc => pc == 2 ? ("null.st", 12) : null,
        });

        var ex = Assert.Throws<RuntimeErrorException>(() => vm.Run());

        Assert.Equal(("null.st", 12), ex.SourceLocation);
        Assert.Contains("null.st:12", ex.Message);
    }
}
