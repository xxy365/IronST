// CaseRangeTests.cs — F12：`CASE ... OF` 区间标签用**范围判定**实现（不再逐值展开）
//
// ── 缺陷（登记 §3.7 / 主缺陷表 F12）──────────────────────────────
// 区间标签在 IR 层被**逐值展开**为多个 switch case
// （`StatementEmitter.EmitCase` 原 `.RangeStart..RangeEnd` 循环）；
// `FunctionEmitter.EmitSwitch` 又把 switch 降为**逐 case 的线性 `BEQ` 链**。
// 大区间（如 `0..200`）⇒ 约 200 条 `BEQ` ⇒ 分支目标距离超过 `Off8`（8-bit）范围
// ⇒ `BytecodeAssembler.ApplyPatches` 抛 `InvalidOperationException`，构造不可用。
//
// ── 本切片 ──────────────────────────────────────────────────────
// 区间标签改用范围判定 `lo <= x <= hi`（`Sge` + `Sle` + `And`，多区间再 `Or`）；
// 单值 / 枚举标签仍走 switch（字节码不变）。
// · 不改 ISA、不改 VM、不改字节码头部；
// · 不改一般 switch 的编码策略（`FunctionEmitter.EmitSwitch` 原样保留）；
// · 保留 F2 的 `Off8` 越界守卫。
//
// ── 边界（不得扩大宣称）────────────────────────────────────────
// 仍以**线性比较链**判定：若单条 CASE 的**单值** case 数量本身极大，仍可能触达
// `Off8` 上限 —— 那属另一范围（未登记），不在本切片。
//
// 观测通道见 FBTestSupport（宿主 `Print` → A0）。
using IronST.Runtime.Shared;

namespace IronST.BytecodeGen.Tests;

public class CaseRangeTests
{
    /// <summary>构造 `t := <value>` 的 CASE 程序；无匹配分支走 ELSE（`level := 7`）。</summary>
    private static string CaseSource(int t, string branches) =>
        "PROGRAM Main\n"
        + "VAR t : INT; level : INT; END_VAR\n"
        + "    level := 0;\n"
        + $"    t := {t};\n"
        + "    CASE t OF\n"
        + branches
        + "    ELSE\n"
        + "        level := 7;\n"
        + "    END_CASE\n"
        + "    Print(level);\n"
        + "END_PROGRAM\n";

    // 大区间：历史最小复现形状（跨度 200）。修复前：后端 Off8 越界异常。
    private const string ThreeRanges =
        "        0..50   : level := 1;\n"
        + "        51..80  : level := 2;\n"
        + "        81..200 : level := 3;\n";

    /// <summary>
    /// 大区间 + 边界值 + 未匹配值：`temperature := 75` 必须命中 `51..80` ⇒ `2`。
    /// 修复前本组全部在**编译期**抛 `InvalidOperationException`（Off8 越界）。
    /// </summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(50, 1)]   // 下边界末值
    [InlineData(51, 2)]   // 上区间首值
    [InlineData(75, 2)]   // 历史最小复现取值
    [InlineData(80, 2)]   // 区间末值
    [InlineData(81, 3)]   // 下一区间首值
    [InlineData(200, 3)]  // 上边界
    [InlineData(201, 7)]  // 紧邻越界 → ELSE
    [InlineData(-1, 7)]   // 负值 → ELSE
    public void LargeRange_SelectsCorrectBranch(int t, int expected)
    {
        var (printed, vm) = FBTestSupport.Run(CaseSource(t, ThreeRanges));

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Equal(new long[] { expected }, printed);
    }

    /// <summary>小区间：修复前后都通过（作为「小区间不回归」对照）。</summary>
    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(3, 20)]
    [InlineData(4, 7)]
    public void SmallRange_SelectsCorrectBranch(int t, int expected)
    {
        var branches =
            "        0..1 : level := 10;\n"
            + "        2..3 : level := 20;\n";

        var (printed, vm) = FBTestSupport.Run(CaseSource(t, branches));

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Equal(new long[] { expected }, printed);
    }

    /// <summary>区间分支与普通单值分支共存（含逗号分隔的单值列表）。</summary>
    [Theory]
    [InlineData(5, 100)]   // 单值
    [InlineData(10, 200)]  // 区间首值
    [InlineData(20, 200)]  // 区间末值
    [InlineData(30, 300)]  // 单值列表第 1 个
    [InlineData(31, 300)]  // 单值列表第 2 个
    [InlineData(25, 7)]    // 未覆盖 → ELSE
    public void RangeAndSingleLabels_Coexist(int t, int expected)
    {
        var branches =
            "        5      : level := 100;\n"
            + "        10..20 : level := 200;\n"
            + "        30, 31 : level := 300;\n";

        var (printed, vm) = FBTestSupport.Run(CaseSource(t, branches));

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Equal(new long[] { expected }, printed);
    }
}
