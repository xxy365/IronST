// VariableInitializerIntegrationTests.cs — F3：显式初始值丢失
// （缺陷登记簿 §3.1 · 增量编译契约 §7 L1）
//
// 缺陷形态：语法与 AST **确实解析了** `:= 5`，但语义层没有承载初始值的字段，
// 于是 IR 层一律零初始化、字节码层没有任何全局初始化发射器 ——
// `VAR_GLOBAL x : INT := 5;` 静默变成 0，**无任何诊断**。
//
// 这里把整条链跑完（前端 → IR → 字节码 → 发布 VM），用宿主 `Log` 观察实际值。
// 观测通道与 NumericConversionEndToEndTests 同路：宿主读 A0 的 BcValue。
using IronST.Runtime.Shared;

namespace IronST.Integration.Tests;

public class VariableInitializerIntegrationTests
{
    /// <summary>取宿主 <c>Log</c> 唯一一次调用的第一个实参。</summary>
    private static BcValue SingleLogArg(StRunResult run)
    {
        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        var call = Assert.Single(run.Calls);
        Assert.Equal("Log", call.Name);
        return call.Arguments[0];
    }

    // ═══════════════════════════════════════════════════════════════
    //  全局变量：显式初始值必须落到全局槽
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>VAR_GLOBAL g : DINT := 42;</c> ⇒ 全局槽初值是 42。
    /// 修复前是 0（全局一律零初始化）。
    /// </summary>
    [Fact]
    public void GlobalInt_WithExplicitInitializer_KeepsItsValue()
    {
        var run = StRunner.Run(
            """
            PROGRAM Main
            VAR_GLOBAL
                g : DINT := 42;
            END_VAR
                Log(g);
            END_PROGRAM
            """,
            host => host.RegisterAction("Log", 1, _ => { }));

        var arg = SingleLogArg(run);
        Assert.Equal(ValueTag.Int64, arg.Tag);
        Assert.Equal(42, arg.IntValue);
    }

    /// <summary>实数全局的初值必须是实数（不是整数位型 reinterpret）。</summary>
    [Fact]
    public void GlobalReal_WithExplicitInitializer_KeepsItsValue()
    {
        var run = StRunner.Run(
            """
            PROGRAM Main
            VAR_GLOBAL
                g : REAL := 2.5;
            END_VAR
                Log(g);
            END_PROGRAM
            """,
            host => host.RegisterAction("Log", 1, _ => { }));

        var arg = SingleLogArg(run);
        Assert.Equal(ValueTag.Float64, arg.Tag);
        Assert.Equal(2.5, arg.FloatValue, 6);
    }

    /// <summary>布尔全局：<c>:= TRUE</c> ⇒ 1。</summary>
    [Fact]
    public void GlobalBool_WithExplicitInitializer_KeepsItsValue()
    {
        var run = StRunner.Run(
            """
            PROGRAM Main
            VAR_GLOBAL
                g : BOOL := TRUE;
            END_VAR
                Log(g);
            END_PROGRAM
            """,
            host => host.RegisterAction("Log", 1, _ => { }));

        var arg = SingleLogArg(run);
        Assert.Equal(ValueTag.Int64, arg.Tag);
        Assert.Equal(1, arg.IntValue);
    }

    /// <summary>
    /// 全局初值必须在**入口函数体执行之前**就位 ——
    /// 函数第一行读到的就是 42，而不是 0。
    /// </summary>
    [Fact]
    public void GlobalInitializer_IsInPlaceBeforeEntryBodyRuns()
    {
        var run = StRunner.Run(
            """
            PROGRAM Main
            VAR_GLOBAL
                g : DINT := 42;
            END_VAR
                ReadGlobal();
            END_PROGRAM

            FUNCTION ReadGlobal : DINT
            VAR_GLOBAL
                g : DINT := 42;
            END_VAR
                Log(g);
                ReadGlobal := g;
            END_FUNCTION
            """,
            host => host.RegisterAction("Log", 1, _ => { }));

        var arg = SingleLogArg(run);
        Assert.Equal(ValueTag.Int64, arg.Tag);
        Assert.Equal(42, arg.IntValue);
    }

    /// <summary>负数初值按位模式保留（不是 0）。</summary>
    [Fact]
    public void GlobalInt_NegativeInitializer_KeepsItsValue()
    {
        var run = StRunner.Run(
            """
            PROGRAM Main
            VAR_GLOBAL
                g : DINT := -7;
            END_VAR
                Log(g);
            END_PROGRAM
            """,
            host => host.RegisterAction("Log", 1, _ => { }));

        var arg = SingleLogArg(run);
        Assert.Equal(ValueTag.Int64, arg.Tag);
        Assert.Equal(-7, arg.IntValue);
    }

    // ═══════════════════════════════════════════════════════════════
    //  局部变量：显式初始值必须落到栈帧槽
    // ═══════════════════════════════════════════════════════════════

    /// <summary><c>VAR x : DINT := 7;</c> ⇒ 函数入口处 x 是 7。</summary>
    [Fact]
    public void LocalInt_WithExplicitInitializer_KeepsItsValue()
    {
        var run = StRunner.Run(
            """
            PROGRAM Main
            VAR
                x : DINT := 7;
            END_VAR
                Log(x);
            END_PROGRAM
            """,
            host => host.RegisterAction("Log", 1, _ => { }));

        var arg = SingleLogArg(run);
        Assert.Equal(ValueTag.Int64, arg.Tag);
        Assert.Equal(7, arg.IntValue);
    }

    /// <summary>实数局部初值。</summary>
    [Fact]
    public void LocalReal_WithExplicitInitializer_KeepsItsValue()
    {
        var run = StRunner.Run(
            """
            PROGRAM Main
            VAR
                r : REAL := 1.5;
            END_VAR
                Log(r);
            END_PROGRAM
            """,
            host => host.RegisterAction("Log", 1, _ => { }));

        var arg = SingleLogArg(run);
        Assert.Equal(ValueTag.Float64, arg.Tag);
        Assert.Equal(1.5, arg.FloatValue, 6);
    }

    /// <summary>
    /// 局部初值在**每次调用**时重新生效（序言里发射，不是编译期一次性）。
    /// 函数被调两次，两次都读到 7。
    /// </summary>
    [Fact]
    public void LocalInitializer_IsReappliedOnEveryCall()
    {
        var run = StRunner.Run(
            """
            PROGRAM Main
                ReadLocal();
                ReadLocal();
            END_PROGRAM

            FUNCTION ReadLocal : DINT
            VAR
                x : DINT := 7;
            END_VAR
                Log(x);
                ReadLocal := x;
            END_FUNCTION
            """,
            host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(2, run.Calls.Count);
        Assert.All(
            run.Calls,
            c =>
            {
                Assert.Equal(ValueTag.Int64, c.Arguments[0].Tag);
                Assert.Equal(7, c.Arguments[0].IntValue);
            });
    }

    // ═══════════════════════════════════════════════════════════════
    //  FB 成员：初值在**实例创建处**写一次
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// FB 的 <c>VAR v : DINT := 5;</c> ⇒ 实例创建后成员是 5。
    /// </summary>
    /// <remarks>
    /// 初值落在**实例创建处**（调用方），不是 FB 的序言 ——
    /// FB 的 VAR 成员是实例状态，每次调用重写会把跨调用的状态冲掉。
    /// </remarks>
    [Fact]
    public void FbMember_WithExplicitInitializer_KeepsItsValue()
    {
        var run = StRunner.Run(
            """
            PROGRAM Main
            VAR
                inst : Counter;
            END_VAR
                Log(inst.v);
            END_PROGRAM

            FUNCTION_BLOCK Counter
            VAR
                v : DINT := 5;
            END_VAR
            END_FUNCTION_BLOCK
            """,
            host => host.RegisterAction("Log", 1, _ => { }));

        var arg = SingleLogArg(run);
        Assert.Equal(ValueTag.Int64, arg.Tag);
        Assert.Equal(5, arg.IntValue);
    }

    /// <summary>
    /// FB 成员的初值只在**实例创建时**写一次 —— 之后调用 FB 改写成员，
    /// 再次调用时读到的是**改写后的值**，不是初值。
    /// </summary>
    [Fact]
    public void FbMember_ExplicitInitializer_DoesNotResetStateBetweenCalls()
    {
        var run = StRunner.Run(
            """
            FUNCTION_BLOCK Counter
            VAR_OUTPUT
                count : DINT;
            END_VAR
            VAR
                n : DINT := 10;
            END_VAR
                n := n + 1;
                count := n;
            END_FUNCTION_BLOCK

            PROGRAM Main
            VAR
                c : Counter;
                v : DINT;
            END_VAR
                c(count := v);
                Log(v);
                c(count := v);
                Log(v);
            END_PROGRAM
            """,
            host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        // 10（初值，只写一次）+ 1 = 11；第二次调用是 11 + 1 = 12 —— 不是 11。
        Assert.Equal(new long[] { 11, 12 }, run.Calls.Select(c => c.Arguments[0].IntValue));
    }

    // ═══════════════════════════════════════════════════════════════
    //  对照组：没有显式初始值 ⇒ 仍然是 0，不得回归
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 无显式初值的全局仍读作 0。
    /// </summary>
    /// <remarks>
    /// 槽的 <b>Tag</b> 是 <see cref="ValueTag.Void"/> —— VM 的全局区/帧槽默认是
    /// <c>BcValue</c> 的默认值（Void），读出的数值是 0。
    /// 「未初始化槽的 Tag 该是什么」是另一条独立缺口（类型默认值未落地），
    /// 不属于 F3；本用例只锁定**数值**语义，不断言 Tag。
    /// </remarks>
    [Fact]
    public void GlobalWithoutInitializer_StaysZero()
    {
        var run = StRunner.Run(
            """
            PROGRAM Main
            VAR_GLOBAL
                g : DINT;
            END_VAR
                Log(g);
            END_PROGRAM
            """,
            host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(0, SingleLogArg(run).IntValue);
    }

    /// <summary>无显式初值的局部仍读作 0（理由同上：不锁 Tag）。</summary>
    [Fact]
    public void LocalWithoutInitializer_StaysZero()
    {
        var run = StRunner.Run(
            """
            PROGRAM Main
            VAR
                x : DINT;
            END_VAR
                Log(x);
            END_PROGRAM
            """,
            host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(0, SingleLogArg(run).IntValue);
    }

    /// <summary>
    /// 显式初值随后被普通赋值覆盖 —— 初值不是常量折叠，运行时语义不变。
    /// </summary>
    [Fact]
    public void ExplicitInitializer_IsOverwrittenByLaterAssignment()
    {
        var run = StRunner.Run(
            """
            PROGRAM Main
            VAR
                x : DINT := 7;
            END_VAR
                x := 99;
                Log(x);
            END_PROGRAM
            """,
            host => host.RegisterAction("Log", 1, _ => { }));

        var arg = SingleLogArg(run);
        Assert.Equal(ValueTag.Int64, arg.Tag);
        Assert.Equal(99, arg.IntValue);
    }
}
