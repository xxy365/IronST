// VMBuiltinMathGuardTests.cs — 内置 VM 的数学能力边界守卫
//
// 缺陷登记簿 §3.8（F13）· 待办 P1「F13：IEC 浮点标准库函数不可用」
//
// ── 约束 ────────────────────────────────────────────────────────
// 「内置 VM 不加入 IEC 数学库函数」。
//
// 内置 VM（VMBase 构造时）只安装 `MathStandardLibrary`，而它当前只含
// **整数 MOD 运算符**的实现（`MATH_IMOD` / `MATH_UMOD`）：
//
//   · MOD 是**语言内建运算符**，不是 IEC 标准库函数；
//   · 若每个宿主各自注册，`MOD` 会在 CLI / IDE / STModInvoker / 测试宿主
//     之间出现「能编译、不能运行」的差异 —— 故内建（2026-10-21 修复）。
//
// 而 IEC **浮点标准库函数**（SQRT / SIN / LN / 浮点 ABS…）走
// 「用户自建宿主注入」路径（2026-10-22 用户裁定）：**内置 VM 不提供实现**。
// 静态 `HostCallIds.MATH_SQRT`…`MATH_ABS`（0x33–0x38）与 F13 保留区
// （0x0200–0x0203）在本 VM 里**必须没有 handler**。
//
// ── 本守卫的防线 ────────────────────────────────────────────────
// 反射枚举 `HostCallIds` 里**所有** `MATH_*` 常量，逐个断言内置 VM 没有
// handler。用反射而非硬编码列表，是为了让「将来有人新增 `MATH_POW` 之类
// 常量并顺手注册进内置 VM」这种改动**当场失败**，而不是静默通过。
using System.Reflection;
using IronST.Runtime.Shared;

namespace IronST.Runtime.Tests;

public class VMBuiltinMathGuardTests
{
    /// <summary>F13 保留区函数 ID（docs/F13保留区函数ID分配提案.md §2.1，DP-1 已批准）。</summary>
    private static readonly ushort[] F13ReservedRegionIds = [0x0200, 0x0201, 0x0202, 0x0203];

    /// <summary>
    /// 内置 VM 允许自带的数学 handler：**只有**整数 MOD 运算符的两个 ID。
    /// </summary>
    private static readonly ushort[] AllowedBuiltinMathIds =
        [HostCallIds.MATH_IMOD, HostCallIds.MATH_UMOD];

    /// <summary>
    /// 反射取出 <see cref="HostCallIds"/> 里全部 <c>MATH_*</c> 常量。
    /// </summary>
    private static IReadOnlyList<(string Name, ushort Id)> MathHostCallIds() =>
        typeof(HostCallIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral
                        && f.FieldType == typeof(ushort)
                        && f.Name.StartsWith("MATH_", StringComparison.Ordinal))
            .Select(f => (Name: f.Name, Id: (ushort)f.GetRawConstantValue()!))
            .OrderBy(x => x.Id)
            .ToList();

    /// <summary>
    /// 内置 VM（宿主不注册任何 handler）对该 HCALL ID **没有**处理器。
    /// </summary>
    /// <remarks>
    /// 走真实执行路径：默认 <see cref="HostBridge"/> 由 VMBase 构造并安装
    /// 内建 handler；随后 HCALL 未注册的 ID ⇒ 抛 <c>IllegalOpcode</c>。
    /// </remarks>
    private static void AssertNoBuiltinHandler(ushort id)
    {
        var ex = Assert.Throws<RuntimeErrorException>(() =>
            VMTestHelper.Run(VMTestHelper.Build(b =>
            {
                b.LDC(VMTestHelper.A0, b.AddConst(1L));
                b.HCALL(id);
            })));

        Assert.Equal(RuntimeErrorType.IllegalOpcode, ex.ErrorType);
        Assert.Contains("unregistered", ex.Message);
    }

    /// <summary>
    /// 内置 VM **不提供**任何 IEC 数学库函数实现。
    /// </summary>
    /// <remarks>
    /// 覆盖 <see cref="HostCallIds"/> 里全部 <c>MATH_*</c> 常量（除整数 MOD 的两个）。
    /// 若将来有人往 <see cref="MathStandardLibrary"/> 里加 <c>MATH_SQRT</c> 之类的
    /// handler，本例会失败 —— 这正是它存在的意义。
    /// </remarks>
    [Fact]
    public void BuiltinVm_RegistersNoIecMathLibraryFunction()
    {
        var mathIds = MathHostCallIds();
        Assert.NotEmpty(mathIds);

        foreach (var (name, id) in mathIds)
        {
            if (AllowedBuiltinMathIds.Contains(id))
                continue;

            AssertNoBuiltinHandler(id);
        }
    }

    /// <summary>
    /// F13 保留区 ID（0x0200–0x0203）在内置 VM 里**也**没有 handler。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 保留区是 F13「用户自建宿主注入」路径的**已批准**发射口径（DP-1「单一发射路径」
    /// / DP-4「一致性责任单一」，见
    /// <c>docs/F13保留区函数ID分配提案.md</c> §2.1）—— 保留区数值
    /// <c>0x0200–0x0203</c> 已正式分配，但该提案的**实现授权 = 部分**（仅设计侧准备，
    /// 不写实现），故后端目前**尚未**把保留区值接进 <c>hostFunctions</c>。
    /// </para>
    /// <para>
    /// 无论保留区何时落地，内置 VM 都**不得**抢先注册它们 —— 那会让「不内置实现」的裁定
    /// 出现第二个发射口径，直接违反 DP-1。故本例作为**前向守卫**常驻。
    /// </para>
    /// </remarks>
    [Fact]
    public void BuiltinVm_RegistersNoF13ReservedRegionHandler()
    {
        foreach (var id in F13ReservedRegionIds)
            AssertNoBuiltinHandler(id);
    }

    /// <summary>
    /// 正面锚点：整数 <c>MOD</c> 运算符的实现**由内置 VM 提供**。
    /// </summary>
    /// <remarks>
    /// 这条与上面两条构成完整边界 —— 内置 VM 有数学能力，但**只限语言内建运算符**，
    /// 不含任何 IEC 库函数。若将来 MOD 也改走宿主注入，本例会失败，提示同步
    /// 缺陷登记簿 §13.1（MOD-HCALL）与待办「已完成」条目。
    /// </remarks>
    [Fact]
    public void BuiltinVm_StillProvidesIntegerModOperator()
    {
        long r = VMTestHelper.RunInt(b =>
        {
            b.LDC(VMTestHelper.A0, b.AddConst(17L));
            b.LDC(BytecodeBuilder.A1, b.AddConst(5L));
            b.HCALL(HostCallIds.MATH_IMOD);
        });

        Assert.Equal(2, r);
    }
}
