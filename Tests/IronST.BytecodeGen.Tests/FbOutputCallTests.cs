// FbOutputCallTests.cs — FB 带 VAR_OUTPUT 且被调用（Samples/debug-test.st 源③ 回归）
//
// ── 历史缺陷（源③ / Samples/demo.st 已知问题①）──────────────────────
// FB **有** VAR_OUTPUT 且**被调用**时，后端抛异常（demo.st 记为
// `FunctionEmitter.EnsureReg` 里 `key == null` 的 ArgumentNullException）。
// demo.st 记载的边界：
//   · FB **无** VAR_OUTPUT ⇒ 正常；
//   · FB **有** VAR_OUTPUT 但**从不调用** ⇒ 正常；
//   · FB 有 VAR_OUTPUT 且被调用 ⇒ 崩溃（历史）。
//
// ── 修复（0498733 主，deed3d6 守卫）────────────────────────────────
// `ExpressionEmitter.EmitFBInstanceCall` / `EmitFBArgument`：`VAR_INPUT` 直写实例字段，
// **只有显式提供的 OUT / IN_OUT 才进实参**；省略的 OUT 不再在实参表留下空槽
// （原崩溃要求空槽到达 `EnsureReg`）。`deed3d6` 为空实参保留显式后端守卫。
//
// ── 本切片的验收范围（只到这里）──────────────────────────────────
// · 显式**命名**输出：写穿调用方变量。
// · 显式**位置**输出：写穿调用方变量。
// · **省略**输出（历史复现形状）：只断言「能编译 + 运行不崩溃」，**不**断言输出值
//   —— 省略 VAR_OUTPUT 的取值语义属 `docs/阶段0B设计.md` §6.3.10 未裁定项。
// · 声明了 VAR_OUTPUT 但从不调用：不崩溃。
//
// 不涉及：VAR_OUTPUT 省略语义裁定、VAR_IN_OUT、聚合。
//
// 观测通道（宿主 `Print` → A0）见 FBTestSupport。
using IronST.Runtime.Shared;

namespace IronST.BytecodeGen.Tests;

public class FbOutputCallTests
{
    /// <summary>
    /// 显式**命名**输出：FB 经 OUT 指针写穿调用方变量。
    /// 先置 <c>v := -1</c>，以区分「写穿」与「未写入」。
    /// </summary>
    [Fact]
    public void ExplicitNamedOutput_WritesThroughToCaller()
    {
        var (printed, vm) = FBTestSupport.Run("""
            FUNCTION_BLOCK Counter
            VAR_INPUT inc : DINT; END_VAR
            VAR_OUTPUT count : DINT; END_VAR
                count := inc;
            END_FUNCTION_BLOCK

            PROGRAM Main
            VAR ctr : Counter; v : DINT; END_VAR
                v := -1;
                ctr(inc := 5, count := v);
                Print(v);
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Equal(new long[] { 5 }, printed);
    }

    /// <summary>
    /// 显式**位置**输出：按 FB 成员声明序（IN 在前、OUT 在后）映射实参。
    /// </summary>
    [Fact]
    public void ExplicitPositionalOutput_WritesThroughToCaller()
    {
        var (printed, vm) = FBTestSupport.Run("""
            FUNCTION_BLOCK Counter
            VAR_INPUT inc : DINT; END_VAR
            VAR_OUTPUT count : DINT; END_VAR
                count := inc;
            END_FUNCTION_BLOCK

            PROGRAM Main
            VAR ctr : Counter; v : DINT; END_VAR
                v := -1;
                ctr(5, v);
                Print(v);
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Equal(new long[] { 5 }, printed);
    }

    /// <summary>
    /// **省略**输出（源③ 的历史复现形状）：只要求「能编译 + 运行不崩溃」。
    /// 不断言 <c>count</c> 的取值 —— 省略 VAR_OUTPUT 的语义未裁定（§6.3.10）。
    /// FB 体打印 <c>inc</c> 只为证明调用确实执行。
    /// </summary>
    [Fact]
    public void OmittedOutput_CallDoesNotCrash()
    {
        var (printed, vm) = FBTestSupport.Run("""
            FUNCTION_BLOCK Counter
            VAR_INPUT inc : DINT; END_VAR
            VAR_OUTPUT count : DINT; END_VAR
                count := inc;
                Print(inc);
            END_FUNCTION_BLOCK

            PROGRAM Main
            VAR ctr : Counter; END_VAR
                ctr(inc := 5);
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
        Assert.Equal(new long[] { 5 }, printed); // 证明确实执行了调用
    }

    /// <summary>
    /// 声明了 VAR_OUTPUT 但**从不调用**（demo.st 记载的另一条边界）⇒ 正常。
    /// </summary>
    [Fact]
    public void DeclaredOutput_NeverCalled_IsFine()
    {
        var (printed, vm) = FBTestSupport.Run("""
            FUNCTION_BLOCK Counter
            VAR_INPUT inc : DINT; END_VAR
            VAR_OUTPUT count : DINT; END_VAR
                count := inc;
            END_FUNCTION_BLOCK

            PROGRAM Main
            VAR ctr : Counter; END_VAR
                Print(1);
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Equal(new long[] { 1 }, printed);
    }
}
