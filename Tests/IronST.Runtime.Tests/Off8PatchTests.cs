// Off8PatchTests.cs — F2：条件跳转的 8 位相对偏移越界必须可见
using IronST.Runtime.Shared;

namespace IronST.Runtime.Tests;

/// <summary>
/// F2：<c>PatchKind.Off8</c> 此前写的是 <c>(byte)(sbyte)(targetPC - (offset + 1))</c> ——
/// 差值超出 -128..127 时，<c>(sbyte)</c> 在 unchecked 下静默回绕，分支直接跳到完全
/// 无关的 PC，编译全程零提示。宁可拒绝，也不产出错的字节码。
///
/// <para>
/// 指令布局：<c>BZ = [op][reg][off8]</c> 共 3 字节，<c>patchOffset = PC + 2</c>，
/// 故 <c>rel = targetPC - (patchOffset + 1)</c>。
/// </para>
/// </summary>
public class Off8PatchTests
{
    // ═══════════════════════════════════════════════════════════════
    //  向前跳（正偏移）
    // ═══════════════════════════════════════════════════════════════

    /// <summary>rel 恰好等于 +127（上限）：应正常构建并编码成 0x7F。</summary>
    [Fact]
    public void Forward_MaxInRange127_BuildsAndEncodes()
    {
        var asm = new BytecodeAssembler();
        asm.Label("start");
        asm.Bz(0, "far"); // patchOffset = 0 + 2 = 2
        for (int i = 0; i < 127; i++)
            asm.Raw.BRK(); // targetPC = 3 + 127 = 130 → rel = 130 - 3 = 127
        asm.Label("far");

        var m = asm.Build();

        Assert.Equal(127, (sbyte)m.Code[2]);
    }

    /// <summary>
    /// rel = 128：越界一格。此前 <c>(sbyte)128</c> 回绕成 -128，分支会往<b>回</b>跳。
    /// </summary>
    [Fact]
    public void Forward_128_Throws()
    {
        var asm = new BytecodeAssembler();
        asm.Label("start");
        asm.Bz(0, "far");
        for (int i = 0; i < 128; i++)
            asm.Raw.BRK(); // rel = 128
        asm.Label("far");

        var ex = Assert.Throws<InvalidOperationException>(() => asm.Build());
        Assert.Contains("127", ex.Message);
    }

    // ═══════════════════════════════════════════════════════════════
    //  向后跳（负偏移）
    // ═══════════════════════════════════════════════════════════════

    /// <summary>rel 恰好等于 -128（下限）：应正常构建并编码成 0x80。</summary>
    [Fact]
    public void Backward_MaxInRangeNeg128_BuildsAndEncodes()
    {
        var asm = new BytecodeAssembler();
        asm.Label("back");
        for (int i = 0; i < 125; i++)
            asm.Raw.BRK();
        asm.Bz(0, "back"); // patchOffset = 125 + 2 = 127 → rel = 0 - 128 = -128

        var m = asm.Build();

        Assert.Equal(-128, (sbyte)m.Code[127]);
    }

    /// <summary>
    /// rel = -129：越界一格。此前 <c>(sbyte)-129</c> 回绕成 +127，分支会往<b>前</b>跳。
    /// </summary>
    [Fact]
    public void Backward_Neg129_Throws()
    {
        var asm = new BytecodeAssembler();
        asm.Label("back");
        for (int i = 0; i < 126; i++)
            asm.Raw.BRK();
        asm.Bz(0, "back"); // patchOffset = 126 + 2 = 128 → rel = 0 - 129 = -129

        var ex = Assert.Throws<InvalidOperationException>(() => asm.Build());
        Assert.Contains("127", ex.Message);
    }
}
