// CrossFrameReferenceTests.cs — 跨帧引用寻址（§6.3.6 缺陷的后端闭环 · 修复记录见 §6.3.7）
//
// ── 缺陷（已登记 §6.3.6）────────────────────────────────────────────
// 字节码后端的「指针」= **帧相对槽号**：
//   · EnsureReg：alloca → LDC 槽号；指针参数 → GETL 参数槽
//   · GETR/SETR：addr = R.FP - 1 - R[rs]
// 于是调用方把「自己帧里的槽号」交给被调方后，被调方用**自己的 FP** 解引用
// ⇒ 落到被调方帧内、不越界、不报错、**静默错值**。
//
// ── 本切片的方案（零 ISA 改动）──────────────────────────────────────
// FRAME 先压 LR 再压 FP 两槽，故：被调方 FP = 调用方 FP - (调用方帧大小 + 该点已压栈槽数 + 2)。
// 调用方在**传指针实参**时把槽号减去这个**调用点编译期常数**；被调方照常 GETR/SETR，
// 地址即回落到调用方的同一槽。跨多帧逐级重定基 ⇒ 自动复合。
//
// ── 不涉及 ──────────────────────────────────────────────────────────
// · 不启动 Arena、不改 ISA、不改 VM（GETR/SETR 语义原样保留）。
// · FB 实例指针（`__instance`）**不改**：FB 成员赋值可见性仍是语言层待裁定项
//   （§6.3.5 首行），本切片不得由实现反推该语义。
// · 同帧 `REFERENCE TO`（`r REF= i; r^`）本就可用，本切片不改其行为。
// · 另有一处**既有**的「跨基本块 `_valReg` 缓存」隐患（条件块内物化 alloca 基址、
//   后续块复用、无支配检查）—— 与跨帧寻址无关，本切片未修；用例形状当时已避开它，
//   详见 §6.3.7 末尾与 `VarInOut_Recursion_EachLevelRebases` 的注释。
//   **该隐患已于 2026-10-16 独立修复**（§6.3.8，切 `_valReg` 跨基本块缓存）。
//   本文件用例的形状与预期**不因该修复而改变**（`585e88d` 起即全部通过）。
//
// ── 观测方法 ────────────────────────────────────────────────────────
// 越界探测器（PROGRAM 的 VAR 无可读全局观测口，见 ReferenceEndToEndTests.cs:9-11）。
using IronST.Frontend;
using IronST.LLVMCodeGen.CodeGen;
using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.BytecodeGen.Tests;

public class CrossFrameReferenceTests
{
    // ═══════════════════════════════════════════════════════════════
    //  VAR_IN_OUT：标量
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void VarInOut_Scalar_ModificationVisibleToCaller_AndReturnCorrect()
    {
        var vm = CompileAndRun("""
            FUNCTION Bump : DINT
            VAR_IN_OUT x : DINT; END_VAR
                x := x + 1;
                Bump := x;
            END_FUNCTION
            PROGRAM Main
            VAR g : ARRAY[0..99] OF INT; v : DINT; END_VAR
                v := 41;
                IF Bump(v) <> 42 THEN g[999] := 1; END_IF
                IF v <> 42 THEN g[999] := 2; END_IF
                g[0] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    [Fact]
    public void VarInOut_TwoDistinctTargets_BothUpdated()
    {
        var vm = CompileAndRun("""
            FUNCTION SwapAB : DINT
            VAR_IN_OUT a : DINT; b : DINT; END_VAR
            VAR t : DINT; END_VAR
                t := a;
                a := b;
                b := t;
                SwapAB := 0;
            END_FUNCTION
            PROGRAM Main
            VAR g : ARRAY[0..99] OF INT; p : DINT; q : DINT; END_VAR
                p := 3; q := 9;
                SwapAB(p, q);
                IF p <> 9 THEN g[999] := 1; END_IF
                IF q <> 3 THEN g[999] := 2; END_IF
                g[0] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    [Fact]
    public void VarInOut_DoesNotDisturbOtherCallerLocals()
    {
        var vm = CompileAndRun("""
            FUNCTION Bump : DINT
            VAR_IN_OUT x : DINT; END_VAR
                x := x + 1;
                Bump := 0;
            END_FUNCTION
            PROGRAM Main
            VAR g : ARRAY[0..99] OF INT; a : DINT; b : DINT; c : DINT; END_VAR
                a := 10; b := 20; c := 30;
                Bump(b);
                IF a <> 10 THEN g[999] := 1; END_IF
                IF b <> 21 THEN g[999] := 2; END_IF
                IF c <> 30 THEN g[999] := 3; END_IF
                g[0] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    // ═══════════════════════════════════════════════════════════════
    //  VAR_OUTPUT：标量
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void VarOutput_Scalar_WrittenByCalleeVisibleToCaller()
    {
        var vm = CompileAndRun("""
            FUNCTION Set7 : DINT
            VAR_OUTPUT y : DINT; END_VAR
                y := 7;
                Set7 := 0;
            END_FUNCTION
            PROGRAM Main
            VAR g : ARRAY[0..99] OF INT; v : DINT; END_VAR
                v := 0;
                Set7(v);
                IF v <> 7 THEN g[999] := 1; END_IF
                g[0] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    // ═══════════════════════════════════════════════════════════════
    //  VAR_IN_OUT：多槽聚合（字段就地写穿）
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void VarInOut_Struct_FieldsWrittenThrough()
    {
        var vm = CompileAndRun("""
            TYPE P : STRUCT x : DINT; y : DINT; END_STRUCT; END_TYPE;
            FUNCTION Bump : DINT
            VAR_IN_OUT p : P; END_VAR
                p.x := p.x + 10;
                Bump := p.x;
            END_FUNCTION
            PROGRAM Main
            VAR g : ARRAY[0..99] OF INT; q : P; END_VAR
                q.x := 5; q.y := 6;
                IF Bump(q) <> 15 THEN g[999] := 1; END_IF
                IF q.x <> 15 THEN g[999] := 2; END_IF
                IF q.y <> 6 THEN g[999] := 3; END_IF
                g[0] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    // ═══════════════════════════════════════════════════════════════
    //  逐帧复合：调用方 → Outer → Inner，重定基必须累加
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void VarInOut_ForwardedThroughTwoFrames_Composes()
    {
        var vm = CompileAndRun("""
            FUNCTION Inner : DINT
            VAR_IN_OUT x : DINT; END_VAR
                x := x + 1;
                Inner := 0;
            END_FUNCTION
            FUNCTION Outer : DINT
            VAR_IN_OUT y : DINT; END_VAR
                Outer := Inner(y);
            END_FUNCTION
            PROGRAM Main
            VAR g : ARRAY[0..99] OF INT; v : DINT; END_VAR
                v := 10;
                Outer(v);
                IF v <> 11 THEN g[999] := 1; END_IF
                g[0] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    // ═══════════════════════════════════════════════════════════════
    //  递归：每层各自重定基
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 递归 4 层（同一函数反复进入）：每层都要按**自己**的帧重新平移指针，
    /// 说明重定基不是"父帧特例"而能逐层复合。
    /// </summary>
    /// <remarks>
    /// <b>观测通道形状是有意为之</b>：入口块里 <c>g[r] := 1</c> 让观测数组的基址槽号
    /// 在**调用之后、无条件**地被物化一次，后面的条件 trap 复用它。这样该用例只测
    /// 跨帧寻址，不会与**既有**的「跨基本块 <c>_valReg</c> 缓存（条件块内物化、后续块
    /// 复用、无支配检查）」缺陷纠缠 —— 那是另一处独立问题（见交付说明），
    /// 与跨帧寻址无关：无指针程序同样呈现"条件块内 <c>LDC</c> 基址 + 末块复用同一寄存器"
    /// 的字节码形状。
    /// <para>
    /// **状态更新（2026-10-16）**：该独立问题**已修复**（§6.3.8）。上面那句
    /// <c>g[r] := 1;</c> 是**当时**的规避手段，保留原样以免改动已验收基线；
    /// "去掉规避"的组合验证放在 `RegisterCacheLivenessTests` 的
    /// <c>CrossFrameRebase_WithConditionalFirstMaterialization_Composes</c>。
    /// </para>
    /// </remarks>
    [Fact]
    public void VarInOut_Recursion_EachLevelRebases()
    {
        var vm = CompileAndRun("""
            FUNCTION AddDown : DINT
            VAR_INPUT n : DINT; END_VAR
            VAR_IN_OUT acc : DINT; END_VAR
                IF n > 0 THEN
                    acc := acc + n;
                    AddDown := AddDown(n - 1, acc);
                ELSE
                    AddDown := acc;
                END_IF
            END_FUNCTION
            PROGRAM Main
            VAR g : ARRAY[0..19] OF INT; v : DINT; r : DINT; END_VAR
                v := 0;
                r := AddDown(4, v);
                g[r] := 1;
                IF r <> 10 THEN g[999] := 1; END_IF
                IF v <> 10 THEN g[999] := 2; END_IF
                g[0] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    // ═══════════════════════════════════════════════════════════════
    //  REFERENCE TO 作为实参传入被调方（同一机制；同帧用法不得回归）
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ReferenceTo_PassedToFunction_DereferencesCrossFrame()
    {
        var vm = CompileAndRun("""
            FUNCTION ReadRef : DINT
            VAR_INPUT r : REFERENCE TO INT; END_VAR
                ReadRef := r^;
            END_FUNCTION
            PROGRAM Main
            VAR g : ARRAY[0..99] OF INT; i : INT; r : REFERENCE TO INT; END_VAR
                i := 5;
                r REF= i;
                IF ReadRef(r) <> 5 THEN g[999] := 1; END_IF
                g[0] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    // ═══════════════════════════════════════════════════════════════
    //  零行为变化护栏：无指针实参的调用不得新增任何指令
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ScalarOnlyArgs_EmitNoRebaseInstructions()
    {
        // 纯标量实参：不得出现任何 ISUB（重定基指令）。
        var (_, result) = CompileAndRunFull("""
            FUNCTION F : DINT
            VAR_INPUT a : DINT; b : DINT; c : DINT; END_VAR
                F := a * 100 + b * 10 + c;
            END_FUNCTION
            PROGRAM Main
            VAR g : ARRAY[0..99] OF INT; END_VAR
                IF F(1, 2, 3) <> 123 THEN g[999] := 1; END_IF
                g[0] := 1;
            END_PROGRAM
            """);

        var instrs = result.DebugModule.Functions
            .SelectMany(f => f.Instructions)
            .Select(i => i.Opcode)
            .ToList();

        Assert.DoesNotContain(OpCode.ISUB, instrs);
    }

    [Fact]
    public void VarInOutCall_EmitsRebaseSubtract()
    {
        // 有指针实参：调用方必须发射重定基减法（ISUB）。
        var (_, result) = CompileAndRunFull("""
            FUNCTION Bump : DINT
            VAR_IN_OUT x : DINT; END_VAR
                x := x + 1;
                Bump := 0;
            END_FUNCTION
            PROGRAM Main
            VAR g : ARRAY[0..99] OF INT; v : DINT; END_VAR
                v := 1;
                Bump(v);
                g[0] := 1;
            END_PROGRAM
            """);

        var instrs = result.DebugModule.Functions
            .SelectMany(f => f.Instructions)
            .Select(i => i.Opcode)
            .ToList();

        Assert.Contains(OpCode.ISUB, instrs);
    }

    // ═══════════════════════════════════════════════════════════════
    //  辅助
    // ═══════════════════════════════════════════════════════════════

    private static VM CompileAndRun(string source) => CompileAndRunFull(source).vm;

    private static (VM vm, CompileResult result) CompileAndRunFull(string source)
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
        return (vm, result);
    }
}
