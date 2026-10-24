// FBInstanceAddressingTests.cs — FB-INSTANCE-ADDR：FB 实例成员寻址落到**调用方实例**。
//
// ── 缺陷（登记 §6.3.11）──────────────────────────────────────────
// `__instance` 由调用方传入的是**调用方槽号**，FB 却以**本帧 FP** 解引用
// （GETR/SETR：addr = FP - 1 - slot）⇒ 成员读写落到 **FB 自己的帧**。后果：
//   · 调用方读 `f.n` 恒为 0（nil）—— FB 从不写调用方的 `f`（P0）；
//   · VAR 的「持久」是偶然：撞上序言槽则被覆写（P17：1,1,1），
//     未撞上则「碰巧持久」（P18：1,2,3）。
//
// ── 修复（本文件验收）────────────────────────────────────────────
// 把 `__instance` 纳入 §6.3.7 的**调用点静态重定基**（与 OUT/IN_OUT 指针同一 K）：
// 传入值 = callerSlot - K；FB 侧 `GEP(__instance,0,idx)` 后 GETR/SETR 恰回落到
// 调用方 `%f` 的第 idx 字段 ⇒ 成员读写双向可见、状态真正持久。
//
// 该修复是 `FB-PARTIAL-INPUT` 的**前置条件**：输入字段的存储也在实例里。
// 本文件不含 `VAR_INPUT` 保留语义（见 FBInputRetentionTests）。
//
// 观测通道见 FBTestSupport。
using IronST.Runtime.Shared;

namespace IronST.BytecodeGen.Tests;

public class FBInstanceAddressingTests
{
    /// <summary>
    /// FB 在体内写成员 `n`，**调用方**随后读 `f.n` 必须看到该值。
    /// 基线：FB 把 `n` 写进**自己的帧**，调用方读到的 `f.n` 恒为 0 ⇒ 触发 trap。
    /// </summary>
    [Fact]
    public void FbMember_WrittenByFb_VisibleToCaller()
    {
        var vm = FBTestSupport.RunHalted("""
            FUNCTION_BLOCK Fb
            VAR_INPUT a : DINT; END_VAR
            VAR n : DINT; END_VAR
                n := a;
            END_FUNCTION_BLOCK

            PROGRAM Main
            VAR g : ARRAY[0..99] OF INT; f : Fb; END_VAR
                f(5);
                IF f.n <> 5 THEN g[999] := 1; END_IF
                g[0] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
    }

    /// <summary>
    /// 两个同类型实例互不影响：`f(1)` / `h(2)` 后，各自的成员读到各自的值。
    /// 基线：调用方读 `f.n` / `h.n` 均为 0 ⇒ 触发 trap。
    /// </summary>
    [Fact]
    public void TwoInstances_AreIsolated()
    {
        var vm = FBTestSupport.RunHalted("""
            FUNCTION_BLOCK Fb
            VAR_INPUT a : DINT; END_VAR
            VAR n : DINT; END_VAR
                n := a;
            END_FUNCTION_BLOCK

            PROGRAM Main
            VAR g : ARRAY[0..99] OF INT; f : Fb; h : Fb; END_VAR
                f(1);
                h(2);
                IF f.n <> 1 THEN g[999] := 1; END_IF
                IF h.n <> 2 THEN g[999] := 2; END_IF
                g[0] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
    }

    /// <summary>
    /// 函数内的 FB 实例（P17 形状，`s` 落在槽 0）：三次 `s(1)` 后 `n` 累积为 1、2、3。
    /// 基线：成员地址撞上 FB 序言槽 ⇒ 打印 1、1、1（P17）。
    /// </summary>
    [Fact]
    public void FunctionLocalInstance_PersistsAcrossCalls()
    {
        var (printed, vm) = FBTestSupport.Run("""
            FUNCTION_BLOCK Fb
            VAR_INPUT a : DINT; END_VAR
            VAR n : DINT; END_VAR
                n := n + a;
                Print(n);
            END_FUNCTION_BLOCK

            FUNCTION RunThree : DINT
            VAR s : Fb; END_VAR
                s(1);
                s(1);
                s(1);
                RunThree := 0;
            END_FUNCTION

            PROGRAM Main
            VAR g : ARRAY[0..99] OF INT; END_VAR
                RunThree();
                g[0] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Equal(new long[] { 1, 2, 3 }, printed);
    }

    /// <summary>
    /// 跨帧组合：`VAR_IN_OUT` 指针写调用方变量、FB 成员写实例，**同时**正确。
    /// 基线：`f.out` 读不到（实例寻址错）⇒ 触发 trap。
    /// </summary>
    [Fact]
    public void InOut_And_InstanceMember_BothCorrect()
    {
        var vm = FBTestSupport.RunHalted("""
            FUNCTION_BLOCK Fb
            VAR_INPUT a : DINT; END_VAR
            VAR_IN_OUT acc : DINT; END_VAR
            VAR out : DINT; END_VAR
                acc := acc + a;
                out := acc;
            END_FUNCTION_BLOCK

            PROGRAM Main
            VAR g : ARRAY[0..99] OF INT; f : Fb; v : DINT; END_VAR
                v := 10;
                f(1, v);
                IF v <> 11 THEN g[999] := 1; END_IF
                IF f.out <> 11 THEN g[999] := 2; END_IF
                f(2, v);
                IF v <> 13 THEN g[999] := 3; END_IF
                IF f.out <> 13 THEN g[999] := 4; END_IF
                g[0] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
    }
}
