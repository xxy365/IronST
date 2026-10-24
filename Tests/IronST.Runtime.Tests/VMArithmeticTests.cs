// VMArithmeticTests.cs — 整数/浮点/位运算指令
using IronST.Runtime.Shared;

namespace IronST.Runtime.Tests;

public class VMArithmeticTests
{
    [Fact]
    public void IAdd_2Plus3_Is5()
    {
        long r = VMTestHelper.RunInt(b =>
        {
            b.LDC(4, b.AddConst(2L));
            b.LDC(5, b.AddConst(3L));
            b.IADD(4, 4, 5);
        });
        Assert.Equal(5, r);
    }

    [Fact]
    public void ISub_10Minus4_Is6()
    {
        long r = VMTestHelper.RunInt(b =>
        {
            b.LDC(4, b.AddConst(10L));
            b.LDC(5, b.AddConst(4L));
            b.ISUB(4, 4, 5);
        });
        Assert.Equal(6, r);
    }

    [Fact]
    public void IMul_6Times7_Is42()
    {
        long r = VMTestHelper.RunInt(b =>
        {
            b.LDC(4, b.AddConst(6L));
            b.LDC(5, b.AddConst(7L));
            b.IMUL(4, 4, 5);
        });
        Assert.Equal(42, r);
    }

    [Fact]
    public void IDiv_42Div6_Is7()
    {
        long r = VMTestHelper.RunInt(b =>
        {
            b.LDC(4, b.AddConst(42L));
            b.LDC(5, b.AddConst(6L));
            b.IDIV(4, 4, 5);
        });
        Assert.Equal(7, r);
    }

    [Fact]
    public void FAdd_1_5_Plus_2_5_Is4()
    {
        double r = VMTestHelper.RunFloat(b =>
        {
            b.LDC(4, b.AddConst(1.5));
            b.LDC(5, b.AddConst(2.5));
            b.FADD(4, 4, 5);
        });
        Assert.Equal(4.0, r);
    }

    [Fact]
    public void FDiv_7_5_Div_2_5_Is3()
    {
        double r = VMTestHelper.RunFloat(b =>
        {
            b.LDC(4, b.AddConst(7.5));
            b.LDC(5, b.AddConst(2.5));
            b.FDIV(4, 4, 5);
        });
        Assert.Equal(3.0, r);
    }

    [Fact]
    public void INeg_7_IsNeg7()
    {
        long r = VMTestHelper.RunInt(b =>
        {
            b.LDC(4, b.AddConst(7L));
            b.INEG(4, 4);
        });
        Assert.Equal(-7, r);
    }

    [Fact]
    public void IInc_Twice_From1_Is3()
    {
        long r = VMTestHelper.RunInt(b =>
        {
            b.LDC(4, b.AddConst(1L));
            b.IINC(4);
            b.IINC(4);
        });
        Assert.Equal(3, r);
    }

    [Fact]
    public void IDec_Once_From5_Is4()
    {
        long r = VMTestHelper.RunInt(b =>
        {
            b.LDC(4, b.AddConst(5L));
            b.IDEC(4);
        });
        Assert.Equal(4, r);
    }

    [Fact]
    public void And_0b1100_And_0b1010_Is8()
    {
        long r = VMTestHelper.RunInt(b =>
        {
            b.LDC(4, b.AddConst(0b1100L));
            b.LDC(5, b.AddConst(0b1010L));
            b.AND(4, 4, 5);
        });
        Assert.Equal(0b1000, r);
    }

    [Fact]
    public void Or_0b1100_Or_0b1010_Is14()
    {
        long r = VMTestHelper.RunInt(b =>
        {
            b.LDC(4, b.AddConst(0b1100L));
            b.LDC(5, b.AddConst(0b1010L));
            b.OR(4, 4, 5);
        });
        Assert.Equal(0b1110, r);
    }

    [Fact]
    public void Shl_1_Shl_3_Is8()
    {
        long r = VMTestHelper.RunInt(b =>
        {
            b.LDC(4, b.AddConst(1L));
            b.LDC(5, b.AddConst(3L));
            b.SHL(4, 4, 5);
        });
        Assert.Equal(8, r);
    }

    [Fact]
    public void Shr_16_Shr_2_Is4()
    {
        long r = VMTestHelper.RunInt(b =>
        {
            b.LDC(4, b.AddConst(16L));
            b.LDC(5, b.AddConst(2L));
            b.SHR(4, 4, 5);
        });
        Assert.Equal(4, r);
    }

    [Fact]
    public void IEq_5_Eq_5_Is1()
    {
        long r = VMTestHelper.RunInt(b =>
        {
            b.LDC(4, b.AddConst(5L));
            b.LDC(5, b.AddConst(5L));
            b.IEQ(4, 4, 5);
        });
        Assert.Equal(1, r);
    }

    [Fact]
    public void INe_5_Ne_6_Is1()
    {
        long r = VMTestHelper.RunInt(b =>
        {
            b.LDC(4, b.AddConst(5L));
            b.LDC(5, b.AddConst(6L));
            b.INE(4, 4, 5);
        });
        Assert.Equal(1, r);
    }

    // ── 语言内建整数取余 HCALL ─────────────────────────────
    // 这些 ID 由 FunctionEmitter 直接发出，不应依赖宿主环境注册。

    [Fact]
    public void HCallMathIMod_PositiveOperands_Is2()
    {
        long r = VMTestHelper.RunInt(b =>
        {
            b.LDC(VMTestHelper.A0, b.AddConst(17L));
            b.LDC(BytecodeBuilder.A1, b.AddConst(5L));
            b.HCALL(HostCallIds.MATH_IMOD);
        });

        Assert.Equal(2, r);
    }

    [Fact]
    public void HCallMathIMod_NegativeDividend_UsesDividendSign()
    {
        long r = VMTestHelper.RunInt(b =>
        {
            b.LDC(VMTestHelper.A0, b.AddConst(-17L));
            b.LDC(BytecodeBuilder.A1, b.AddConst(5L));
            b.HCALL(HostCallIds.MATH_IMOD);
        });

        Assert.Equal(-2, r);
    }

    [Fact]
    public void HCallMathUMod_HighBitDividend_UsesUnsignedSemantics()
    {
        long r = VMTestHelper.RunInt(b =>
        {
            // 0xFFFF...FFFF 按无符号解释是 ulong.MaxValue；对 2 取余应为 1。
            b.LDC(VMTestHelper.A0, b.AddConst(-1L));
            b.LDC(BytecodeBuilder.A1, b.AddConst(2L));
            b.HCALL(HostCallIds.MATH_UMOD);
        });

        Assert.Equal(1, r);
    }

    [Fact]
    public void HCallMathIMod_ZeroDivisor_ThrowsDivideByZero()
    {
        var ex = Assert.Throws<RuntimeErrorException>(() =>
            VMTestHelper.Run(VMTestHelper.Build(b =>
            {
                b.LDC(VMTestHelper.A0, b.AddConst(17L));
                b.LDC(BytecodeBuilder.A1, b.AddConst(0L));
                b.HCALL(HostCallIds.MATH_IMOD);
            })));

        Assert.Equal(RuntimeErrorType.DivideByZero, ex.ErrorType);
    }
}
