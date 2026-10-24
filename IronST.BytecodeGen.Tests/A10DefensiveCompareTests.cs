using IronST.Runtime.Shared;

namespace IronST.BytecodeGen.Tests;

/// <summary>A10 层 2 运行时验证：BcValue.Compare 在实际运行时对 Void 处理正确。</summary>
public class A10DefensiveCompareTests
{
    [Fact]
    public void A10_VoidVsVoid_CompareReturnsZero()
    {
        // Void vs Void → 相等（Compare 返回 0）
        var a = BcValue.Void;
        var b = BcValue.Void;
        Assert.Equal(0, BcValue.Compare(a, b));
    }

    [Fact]
    public void A10_VoidVsInt_CompareTreatsVoidAsZero()
    {
        // Void 视为 0，Int(5) > 0 → Compare(Void, Int5) 应返回负数（Void < Int5）
        var voidVal = BcValue.Void;
        var intVal = new BcValue(5);
        int cmp = BcValue.Compare(voidVal, intVal);
        Assert.True(cmp < 0, $"Expected Void < Int(5), got {cmp}");
    }

    [Fact]
    public void A10_IntVsVoid_CompareTreatsVoidAsZero()
    {
        // Int(5) vs Void(0) → 5 > 0 → 正数
        var intVal = new BcValue(5);
        var voidVal = BcValue.Void;
        int cmp = BcValue.Compare(intVal, voidVal);
        Assert.True(cmp > 0, $"Expected Int(5) > Void, got {cmp}");
    }

    [Fact]
    public void A10_VoidVsFloat_CompareTreatsVoidAsZeroPointZero()
    {
        var voidVal = BcValue.Void;
        var floatVal = new BcValue(3.14);
        Assert.True(BcValue.Compare(voidVal, floatVal) < 0);
    }

    [Fact]
    public void A10_VoidVsString_CompareThrowsTypeError()
    {
        var voidVal = BcValue.Void;
        var strVal = new BcValue("hello");
        Assert.Throws<RuntimeErrorException>(() => BcValue.Compare(voidVal, strVal));
    }
}
