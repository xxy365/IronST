// FBInputRetentionTests.cs — FB-PARTIAL-INPUT：`VAR_INPUT` 是实例状态（省略 ⇒ 保留）。
//
// ── 缺陷（登记 §6.3.10）──────────────────────────────────────────
// `VAR_INPUT` **无实例存储**（每次调用的临时 alloca，序言从形参 store）。
// 省略实参时，被调方读到 **A 寄存器残值**，而非「上次值」：
//   · 首次调用前省略 ⇒ 读到残值（P-D2：BOOL 读到 TRUE）；
//   · 连续部分更新 ⇒ 省略字段被中间调用改写（P14：`b` 读成 90）。
//
// ── 裁定与修复（本文件验收）──────────────────────────────────────
// · 省略 `VAR_INPUT` ⇒ **保留该实例上一次调用传入的值**；
// · **首次调用前**省略 ⇒ 读到**类型默认值**（DINT→0、BOOL→FALSE）；
// · 实例成员读写双向可见（依赖 FB-INSTANCE-ADDR 的重定基，见 FBInstanceAddressingTests）。
// 实现：`VAR_INPUT` 纳入实例结构体；FB 签名去 `IN` 形参；调用方直写实例字段；
// 实例 alloca 处**只初始化 `IN`** 为类型默认值。
//
// 不在本切片：`VAR_OUTPUT` / `VAR_IN_OUT` 的省略语义（本轮不裁定）。
//
// 观测通道见 FBTestSupport。
using IronST.Runtime.Shared;

namespace IronST.BytecodeGen.Tests;

public class FBInputRetentionTests
{
    /// <summary>
    /// 首次调用即省略 `IN` ⇒ 读到**类型默认值** 0。
    /// 先经 `Clobber(70, 80)` 在实参寄存器留下非零残值，以区分「类型默认值」与「寄存器残值」。
    /// 基线：FB 无实例存储，序言从形参槽取到 R5=80 ⇒ 打印 80。
    /// </summary>
    [Fact]
    public void FirstCall_OmittedInput_ReadsDintDefault()
    {
        var (printed, vm) = FBTestSupport.Run("""
            FUNCTION Clobber : DINT
            VAR_INPUT x : DINT; y : DINT; END_VAR
                Clobber := x + y;
            END_FUNCTION

            FUNCTION_BLOCK Fb
            VAR_INPUT a : DINT; END_VAR
            VAR out : DINT; END_VAR
                out := a;
                Print(a);
            END_FUNCTION_BLOCK

            PROGRAM Main
            VAR f : Fb; END_VAR
                Clobber(70, 80);
                f();
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Equal(new long[] { 0 }, printed);
    }

    /// <summary>
    /// 首次调用省略 `BOOL` 输入 ⇒ 读到 `FALSE`（0），而非实参残值 TRUE（1）。
    /// 基线（探针 D2）：打印 1。
    /// </summary>
    [Fact]
    public void FirstCall_OmittedInput_ReadsBoolDefault()
    {
        var (printed, vm) = FBTestSupport.Run("""
            FUNCTION Clobber : BOOL
            VAR_INPUT x : BOOL; y : BOOL; END_VAR
                Clobber := x OR y;
            END_FUNCTION

            FUNCTION_BLOCK Fb
            VAR_INPUT b : BOOL; END_VAR
            VAR out : BOOL; END_VAR
                out := b;
                IF b THEN Print(1); ELSE Print(0); END_IF
            END_FUNCTION_BLOCK

            PROGRAM Main
            VAR f : Fb; END_VAR
                Clobber(TRUE, TRUE);
                f();
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Equal(new long[] { 0 }, printed);
    }

    /// <summary>
    /// 连续部分更新（P14 形状）：`f(1,2,3)` → 中间调用改写实参寄存器 → `f(4)`。
    /// 省略的 `b`、`c` 必须**保留**上次值 2、3。
    /// 基线：`b` 读到中间调用的残值 90 ⇒ 打印序列 [1,2,3, 4,90,3]。
    /// </summary>
    [Fact]
    public void ConsecutivePartialInput_OmittedFieldsRetain()
    {
        var (printed, vm) = FBTestSupport.Run("""
            FUNCTION_BLOCK Fb
            VAR_INPUT a : DINT; b : DINT; c : DINT; END_VAR
            VAR sink : DINT; END_VAR
                sink := a + b + c;
                Print(a);
                Print(b);
                Print(c);
            END_FUNCTION_BLOCK

            FUNCTION Foo3 : DINT
            VAR_INPUT p : DINT; q : DINT; r : DINT; END_VAR
                Foo3 := p + q + r;
            END_FUNCTION

            PROGRAM Main
            VAR f : Fb; z : DINT; END_VAR
                f(1, 2, 3);
                z := Foo3(70, 80, 90);
                f(4);
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Equal(new long[] { 1, 2, 3, 4, 2, 3 }, printed);
    }

    /// <summary>
    /// 省略 `IN` ⇒ 调用方读到的成员保持上次值（`f(7)` → `f()` ⇒ `f.out` 仍为 7）。
    /// 基线：调用方读 `f.out` 恒为 0 ⇒ 触发 trap。
    /// </summary>
    [Fact]
    public void OmittedInput_RetainsValue_VisibleToCaller()
    {
        var vm = FBTestSupport.RunHalted("""
            FUNCTION_BLOCK Fb
            VAR_INPUT a : DINT; END_VAR
            VAR out : DINT; END_VAR
                out := a;
            END_FUNCTION_BLOCK

            PROGRAM Main
            VAR g : ARRAY[0..99] OF INT; f : Fb; END_VAR
                f(7);
                f();
                IF f.out <> 7 THEN g[999] := 1; END_IF
                g[0] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
    }
}
