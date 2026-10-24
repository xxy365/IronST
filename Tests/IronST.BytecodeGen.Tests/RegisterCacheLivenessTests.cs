// RegisterCacheLivenessTests.cs — 跨基本块 `_valReg` 缓存失效缺陷（独立切片）
//
// ── 缺陷 ────────────────────────────────────────────────────────────
// FunctionEmitter 用 `_valReg` 把「可重物化」的值缓存进寄存器
// （alloca 基址 → LDC 槽号；常量 → LDC；参数 → GETL 参数槽）。
// 该缓存**不在基本块边界失效**，复用时不检查支配关系：
//   · 调用返回后 `InvalidateRegistersAcrossCall` 丢弃这些条目；
//   · 之后**首次使用**该值的位置决定它被物化在**哪一块**——
//     若落在**条件块**内，寄存器只在"条件成立"的路径上被写入；
//   · 后续块（合流块 / 兄弟块 / 循环体）沿**未执行该物化**的路径进入时，
//     直接复用该寄存器 ⇒ 读到陈旧值，并把它当**槽号 / 地址**使用
//     ⇒ **静默写错位置**（不越界、不报错）。
//
// 与「跨帧引用重定基」（§6.3.7）**无关**：那里是跨帧槽号基准不同；
// 这里是同帧内**寄存器生命周期**问题，无指针实参的程序同样出现。
//
// ── 为什么必须在执行级复现（而非只看静态形状）────────────────────────
// 仅仅"某寄存器在条件块里被写、在后面的块里被读"不足以断定错误：
// 还要看真实执行路径是否**绕过了**那次物化。故本文件用**差分对照**：
// 同一程序，只把数组基址的首次物化从"条件块内"改为"入口块内（无条件）"，
// 其余不变；对照组无错、实验组出错 ⇒ 直接证明因果。
//
// ── 观测方法 ────────────────────────────────────────────────────────
// PROGRAM 的 VAR 无可读全局观测口（见 ReferenceEndToEndTests.cs:9-11），
// 仍用越界探测器；但把**被破坏的目标设为标量 `marker`**（声明在数组之前 ⇒ 占 slot 0）：
// 陈旧基址为 0（干净 VM 的 R8+ 初值）时，`g[0] := x` 的地址 = 0
// ⇒ 正好写进 `marker`，随后 `IF marker <> 7 THEN g[999] := 1` 触发 `ArrayBounds`。
// 若陈旧值不是 0，地址多半越界，同样报 `ArrayBounds` / `SlotOverflow`。
//
// ── 不涉及 ──────────────────────────────────────────────────────────
// · 不改 `GETR`/`SETR`、不改 ISA / VM / 操作码表、不动 Arena；不改变语言语义。
// · 本切片只处理**可重物化**条目（常量 / 参数 / alloca 基址）—— 它们能重新发射装载指令。
//   后续的跨调用 spill 切片把“有稳定帧槽的指令结果”也纳入同一恢复口径：
//   定义处写回槽，块边界 / 调用后按需 GETL；无槽的指令结果仍受 SSA 支配关系保护。
using IronST.Frontend;
using IronST.LLVMCodeGen.CodeGen;
using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.BytecodeGen.Tests;

public class RegisterCacheLivenessTests
{
    /// <summary>无副作用函数：唯一的用处是**触发一次 CALL**。</summary>
    private const string NoopDecl = """
        FUNCTION Noop : DINT
        VAR_INPUT a : DINT; END_VAR
            Noop := a;
        END_FUNCTION
        """;

    // ═══════════════════════════════════════════════════════════════
    //  条件块内首次物化 → 合流块复用
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 前置一次调用（丢弃 `_valReg` 里 alloca 基址的条目）→ `g` 的基址只在
    /// `IF` 的 then 块内被物化 → 条件为假时 then 块不执行，合流块却复用了
    /// then 块的寄存器 ⇒ `g[0] := 1` 的地址落到 slot 0（`marker`）。
    /// </summary>
    [Fact]
    public void ConditionalFirstMaterialization_MergeBlockReuse_KeepsOtherLocals()
    {
        var vm = CompileAndRun(NoopDecl + """

            PROGRAM Main
            VAR marker : DINT; g : ARRAY[0..19] OF INT; v : DINT; END_VAR
                marker := 7;
                v := Noop(0);
                IF v <> 0 THEN
                    g[0] := 1;
                END_IF
                g[0] := 1;
                IF marker <> 7 THEN g[999] := 1; END_IF
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    /// <summary>
    /// 同一形状的**差分对照**：只把 `g` 基址的首次物化挪到**入口块内、无条件**
    /// （`v := Noop(0)` 之后的 `g[0] := 0`），其余一字不变。
    /// 物化点支配后续所有使用 ⇒ 不允许出错。
    /// </summary>
    [Fact]
    public void UnconditionalFirstMaterialization_MergeBlockReuse_IsUnaffected()
    {
        var vm = CompileAndRun(NoopDecl + """

            PROGRAM Main
            VAR marker : DINT; g : ARRAY[0..19] OF INT; v : DINT; END_VAR
                marker := 7;
                v := Noop(0);
                g[0] := 0;
                IF v <> 0 THEN
                    g[0] := 1;
                END_IF
                g[0] := 1;
                IF marker <> 7 THEN g[999] := 1; END_IF
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    // ═══════════════════════════════════════════════════════════════
    //  兄弟分支复用：另一条分支从未写过该寄存器
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// `IF` / `ELSE` 两分支都写 `g`，但只有**先被发射的**那一支物化了基址寄存器
    /// （另一支直接复用同一缓存条目）。条件为假 ⇒ 走 else 分支，
    /// 而该寄存器在 else 路径上**从未被写入** ⇒ 陈旧值。
    /// 这是比"条件块被跳过"更直接的情形：物化定义在**兄弟块**里。
    /// </summary>
    [Fact]
    public void MutualBranches_ElseSiblingReusesThenBlockRegister_KeepsOtherLocals()
    {
        var vm = CompileAndRun(NoopDecl + """

            PROGRAM Main
            VAR marker : DINT; g : ARRAY[0..19] OF INT; v : DINT; END_VAR
                marker := 7;
                v := Noop(0);
                IF v <> 0 THEN
                    g[0] := 1;
                ELSE
                    g[0] := 2;
                END_IF
                g[0] := 3;
                IF marker <> 7 THEN g[999] := 1; END_IF
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    // ═══════════════════════════════════════════════════════════════
    //  循环体复用条件块物化的基址
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 条件块（`IF i = 9`，恒假）里物化 `g` 基址；随后**循环体**（另一块）
    /// 复用该寄存器写 `g[0]` ⇒ 陈旧值。覆盖"循环体"这一类后续块。
    /// </summary>
    /// <remarks>
    /// 这里把条件块放在循环**之前**：把条件块放在循环**内部**的形状会先撞上
    /// **另一处**独立缺陷（汇编器 8 位分支偏移越界，见交付说明），
    /// 不是本切片的目标，故不在此处引入。
    /// </remarks>
    [Fact]
    public void LoopBodyReuse_OfConditionallyMaterializedBase_KeepsOtherLocals()
    {
        var vm = CompileAndRun(NoopDecl + """

            PROGRAM Main
            VAR marker : DINT; g : ARRAY[0..4] OF INT; i : DINT; END_VAR
                marker := 7;
                i := Noop(0);
                IF i = 9 THEN g[0] := 1; END_IF
                WHILE i < 1 DO
                    g[0] := 2;
                    i := i + 1;
                END_WHILE
                IF marker <> 7 THEN g[99] := 1; END_IF
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    // ═══════════════════════════════════════════════════════════════
    //  回边重入：沿用**本块内**物化的寄存器是安全的（反向对照）
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 反向对照：循环体里物化 `g` 基址，第二轮从回边进入同一块时沿用该寄存器。
    /// 这**本就不该出错**：物化定义就在该块内，而一个函数的寄存器号线性递增、
    /// 每个号只被写入一次，故回边重入时寄存器里仍是有效值。
    /// 真正的危险面是"物化定义落在**被绕过的**路径上"（见本文件前三条用例）。
    /// </summary>
    /// <remarks>
    /// 本用例修前修后都通过，作用是**钉住**这一点：修复不得为了保守而破坏
    /// 回边/同块复用，也不得让循环体反向出错。
    /// </remarks>
    [Fact]
    public void LoopBackEdge_ReentryReuse_DoesNotCorruptOtherLocals()
    {
        var vm = CompileAndRun(NoopDecl + """

            PROGRAM Main
            VAR marker : DINT; g : ARRAY[0..4] OF INT; i : DINT; k : DINT; END_VAR
                marker := 7;
                i := Noop(0);
                WHILE i < 1 DO
                    k := Noop(1);
                    g[0] := 2;
                    i := i + 1;
                END_WHILE
                IF marker <> 7 THEN g[99] := 1; END_IF
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    // ═══════════════════════════════════════════════════════════════
    //  与跨帧重定基（§6.3.7）的组合：本次修复后不再需要规避
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 组合用例：<b>跨帧引用写穿透</b>（`VAR_IN_OUT`，§6.3.7 的重定基通道）与
    /// <b>本缺陷</b>（调用后基址首次物化落在条件块内）同时出现。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 上一片（`585e88d`）的递归用例为绕开本缺陷，不得不在调用后**无条件**触碰一次
    /// 观测数组（`g[r] := 1;`），让基址落在入口块。本用例**去掉该规避**：
    /// 调用后第一次用到观测数组就在条件块里（`IF r = 0 THEN g[0] := 1;`），
    /// 合流块紧随其后复用。
    /// </para>
    /// <para>
    /// 断言同时覆盖两条通道：`v` 被 `VAR_IN_OUT` 写穿（跨帧寻址正确）且
    /// `marker` 未被陈旧基址破坏（缓存失效正确）。修前会因后者触发
    /// `ArrayBounds`（`g[0] := 2` 的地址落到 slot 0）。
    /// </para>
    /// <para>
    /// 三处探测器用**互不相同**的越界下标（999 / 998 / 997），使失败信息能直接指出
    /// 是哪一条断言破了。已实测：修前报 `index 999`（`marker`），修后干净退出。
    /// </para>
    /// <para>
    /// 被调方只用**一个** `VAR_IN_OUT` 形参 —— 与 §6.3.7 已验收用例同形。
    /// （若写成"`VAR_INPUT n` + `VAR_IN_OUT x`"再单实参调用，实参按位绑到第一个形参，
    /// 与本缺陷无关，会让用例测不到想测的东西。）
    /// </para>
    /// </remarks>
    [Fact]
    public void CrossFrameRebase_WithConditionalFirstMaterialization_Composes()
    {
        var vm = CompileAndRun("""
            FUNCTION Bump : DINT
            VAR_IN_OUT x : DINT; END_VAR
                x := x + 1;
                Bump := x;
            END_FUNCTION
            PROGRAM Main
            VAR marker : DINT; g : ARRAY[0..19] OF INT; v : DINT; r : DINT; END_VAR
                marker := 7;
                v := 0;
                r := Bump(v);
                IF r = 0 THEN
                    g[0] := 1;
                END_IF
                g[0] := 2;
                IF marker <> 7 THEN g[999] := 1; END_IF
                IF v <> 1 THEN g[998] := 2; END_IF
                IF r <> 1 THEN g[997] := 3; END_IF
                g[0] := 3;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    // ═══════════════════════════════════════════════════════════════
    //  辅助
    // ═══════════════════════════════════════════════════════════════

    private static VM CompileAndRun(string source)
    {
        var analysis = FrontendPipeline.Analyze(source, "test.st");
        Assert.True(analysis.Success, "frontend failed");

        var module = new IRGenerator().Generate(analysis);
        var result = new ByteCodeGenerator("test.st").Generate(module);

        var vm = new VM(result.Module, new VMOptions
        {
            StackSize = 8192,
            StructTypes = result.StructRegistrations,
        });
        vm.Run();
        return vm;
    }
}
