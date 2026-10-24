// VMControlFlowTests.cs — 分支/跳转/局部变量/递归
using IronST.Runtime.Shared;

namespace IronST.Runtime.Tests;

public class VMControlFlowTests
{
    [Fact]
    public void Bz_False_TakesBranch()
    {
        // 布局: LFALSE(2B) BZ(3B) LDC99(6B) JMP(5B) LDC42(6B) HALT
        // BZ falsy → 5+11=16 → LDC(42)；truthy → LDC(99) → JMP(22) → HALT
        long r = VMTestHelper.RunInt(b =>
        {
            b.LFALSE(4);
            b.BZ(4, 11);
            b.LDC(4, b.AddConst(99L));
            b.JMP(22);
            b.LDC(4, b.AddConst(42L));
        });
        Assert.Equal(42, r);
    }

    [Fact]
    public void Bz_True_FallsThrough()
    {
        long r = VMTestHelper.RunInt(b =>
        {
            b.LTRUE(4);
            b.BZ(4, 11);
            b.LDC(4, b.AddConst(99L));
            b.JMP(22);
            b.LDC(4, b.AddConst(42L));
        });
        Assert.Equal(99, r);
    }

    [Fact]
    public void Bnz_NonZero_TakesBranch()
    {
        // 布局: LDC1(6B) BNZ(3B) LDC99(6B) JMP(5B) LDC42(6B) HALT
        // BNZ truthy → 9+11=20 → LDC(42)；falsy → LDC(99) → JMP(26) → HALT
        long r = VMTestHelper.RunInt(b =>
        {
            b.LDC(4, b.AddConst(1L));
            b.BNZ(4, 11);
            b.LDC(4, b.AddConst(99L));
            b.JMP(26);
            b.LDC(4, b.AddConst(42L));
        });
        Assert.Equal(42, r);
    }

    [Fact]
    public void Jmp_BackwardLoop_CountsToTen()
    {
        long r = VMTestHelper.RunInt(b =>
        {
            b.LDC(4, b.AddConst(0L));   // counter
            b.LDC(5, b.AddConst(10L));  // limit
            b.LDC(6, b.AddConst(1L));   // step
            int loop = b.PC;
            b.IADD(4, 4, 6);            // counter += 1   (4 字节)
            b.BLT(4, 5, (sbyte)(loop - (b.PC + 4)));  // counter < limit → 回跳
        });
        Assert.Equal(10, r);
    }

    [Fact]
    public void GetL_SetL_LocalRoundTrip()
    {
        long r = VMTestHelper.RunInt(b =>
        {
            b.FRAME(2);                 // 2 个局部槽
            b.LDC(4, b.AddConst(77L));
            b.SETL(4, 0);               // local[0] = 77
            b.LDC(4, b.AddConst(0L));
            b.GETL(4, 0);               // R4 = local[0]
            b.LEAVE();                  // 顶层无调用者，不能 RET（会跳回 0）
        });
        Assert.Equal(77, r);
    }

    [Fact]
    public void RecursiveFibonacci_10_Is55()
    {
        var b = new BytecodeBuilder();
        b.LDC(4, b.AddConst(10L));
        b.CALL(0);
        int callOff = b.PC - 5;
        b.HALT();

        // fib(n): FRAME(3) → local[0]=n, local[1]=LR, local[2]=fib(n-1)
        int fib = b.PC;
        b.FRAME(3);
        b.SETL(4, 0);                       // local[0] = n
        b.GETL(10, 0);
        b.LDC(11, b.AddConst(1L));
        b.BGT(10, 11, 6);                   // n > 1 → 跳递归（跳过 GETL 4B + LEAVE 1B + RET 1B）
        b.GETL(4, 0);                       // base case: return n
        b.LEAVE();
        b.RET();

        b.SETL(3, 1);                       // 保存 LR
        b.GETL(10, 0);
        b.IDEC(10);
        b.MOV(4, 10);
        b.CALL((uint)fib);                  // fib(n-1)
        b.SETL(4, 2);                       // local[2] = fib(n-1)

        b.GETL(10, 0);
        b.LDC(11, b.AddConst(2L));
        b.ISUB(10, 10, 11);
        b.MOV(4, 10);
        b.CALL((uint)fib);                  // fib(n-2)

        b.GETL(10, 2);
        b.IADD(4, 10, 4);                   // A0 = fib(n-1) + fib(n-2)
        b.GETL(3, 1);                       // 恢复 LR
        b.LEAVE();
        b.RET();

        b.PatchUInt32(callOff + 1, (uint)fib);

        var vm = VMTestHelper.Run(b.Build());
        Assert.Equal(VMState.Halted, vm.State);
        Assert.Equal(55, vm.Context.ReadReg(4).IntValue);
    }

    [Fact]
    public void VmCall_Pauses_ThenResume_Continues()
    {
        var vm = VMTestHelper.Run(VMTestHelper.Build(b =>
        {
            b.LDC(4, b.AddConst(1L));
            b.VMCALL(1);
            b.LDC(4, b.AddConst(2L));
        }));

        Assert.Equal(VMState.Paused, vm.State);
        Assert.Equal(1, vm.Context.ReadReg(4).IntValue);

        vm.Resume();
        Assert.Equal(VMState.Halted, vm.State);
        Assert.Equal(2, vm.Context.ReadReg(4).IntValue);
    }
}
