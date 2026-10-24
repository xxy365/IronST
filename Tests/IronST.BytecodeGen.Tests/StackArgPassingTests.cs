// StackArgPassingTests.cs — 0B 调用约定扩展：>4 槽参数经栈传递
//
// ── 背景 ────────────────────────────────────────────────────────────
// 「多槽聚合参数」切片（c73c4ec）让 ≤4 个 A 槽的参数可传，但 >4 槽一律显式拒绝，
// 原因是既有的 PUSH/POP 栈传参路径**是坏的**：
//   PUSH 写入 --SP，而 FRAME 也从同一段 SP 分配（先压 LR/FP，再把 SP 降到 FP-n），
//   于是被调用方的帧直接覆盖调用方刚压入的参数；LEAVE 后 SP 复原，
//   被调用方 POP 也只读到帧内数据。
//
// ── 本切片的方案 ────────────────────────────────────────────────────
// 不复用 PUSH/POP，改用**帧上参数区**（参数位于 FP 之上，与 I32 调用约定的
// 帧布局一致，且与 LR/FP 保存槽不重叠）：
//   调用方：PUSHN k 预留 k 槽 → 逐槽写入参数区 → CALL
///   被调用方：把参数区逐槽读进自己的局部槽（即 _paramSlots）→ POPN k 释放
//   调用方：CALL 返回后 POPN k 回收
//
// 所需原语 PUSHN/POPN 已在 ISA 中且 VM / 指令长度表 / 反汇编器 / DebugInstr
// 全链路就绪（此前无任何发射方使用），故本切片**不改 ISA、不改 VM**。
//
// ── 不涉及 ──────────────────────────────────────────────────────────
// VAR_INPUT 传值 / 传引用的语言语义裁定（§6.3.5 #3）：本切片只实现「按整个对象
// 传副本」的 IR byval 语义，不断言 ST 规范应取哪一种。
// 也不涉及 Arena / StructTypeId / F7 / F9 / 宿主 ID。
//
// ── 观测方法 ────────────────────────────────────────────────────────
// 越界探测器（PROGRAM 的 VAR 无可读全局观测口，见 ReferenceEndToEndTests.cs:9-11）。
using IronST.Frontend;
using IronST.LLVMCodeGen.CodeGen;
using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.BytecodeGen.Tests;

public class StackArgPassingTests
{
    /// <summary>5 个标量参数 —— 既有缺陷的最小复现（修前静默出错）。</summary>
    private const string SrcFiveScalars = """
        FUNCTION F : DINT
        VAR_INPUT a : DINT; b : DINT; c : DINT; d : DINT; e : DINT; END_VAR
            F := a * 10000 + b * 1000 + c * 100 + d * 10 + e;
        END_FUNCTION
        PROGRAM Main
        VAR g : ARRAY[0..99] OF INT; END_VAR
            IF F(1, 2, 3, 4, 5) <> 12345 THEN g[999] := 1; END_IF
            g[0] := 1;
        END_PROGRAM
        """;

    /// <summary>6 个标量参数（两个栈槽）。函数名避开参数名（前端 OrdinalIgnoreCase）。</summary>
    private const string SrcSixScalars = """
        FUNCTION Sum6 : DINT
        VAR_INPUT a : DINT; b : DINT; c : DINT; d : DINT; e : DINT; f : DINT; END_VAR
            Sum6 := a * 100000 + b * 10000 + c * 1000 + d * 100 + e * 10 + f;
        END_FUNCTION
        PROGRAM Main
        VAR g : ARRAY[0..99] OF INT; END_VAR
            IF Sum6(1, 2, 3, 4, 5, 6) <> 123456 THEN g[999] := 1; END_IF
            g[0] := 1;
        END_PROGRAM
        """;

    /// <summary>3 个多槽结构体 = 6 槽（超出 A0–A3，须走栈）。</summary>
    private const string SrcThreeStructs = """
        TYPE P : STRUCT x : DINT; y : DINT; END_STRUCT; END_TYPE;
        FUNCTION F : DINT
        VAR_INPUT u : P; v : P; w : P; END_VAR
            F := u.x * 100 + v.y * 10 + w.x;
        END_FUNCTION
        PROGRAM Main
        VAR g : ARRAY[0..99] OF INT; u : P; v : P; w : P; END_VAR
            u.x := 1; u.y := 2;
            v.x := 3; v.y := 4;
            w.x := 5; w.y := 6;
            IF F(u, v, w) <> 145 THEN g[999] := 1; END_IF
            g[0] := 1;
        END_PROGRAM
        """;

    /// <summary>前 4 槽走 A 位、其余走栈 —— 混合路径。</summary>
    private const string SrcMixedAAndStack = """
        TYPE P : STRUCT x : DINT; y : DINT; END_STRUCT; END_TYPE;
        FUNCTION F : DINT
        VAR_INPUT
            a : DINT;
            p : P;
            b : DINT;
            q : P;
        END_VAR
            F := a * 1000 + p.x * 100 + b * 10 + q.y;
        END_FUNCTION
        PROGRAM Main
        VAR g : ARRAY[0..99] OF INT; p : P; q : P; END_VAR
            p.x := 3; p.y := 4;
            q.x := 5; q.y := 6;
            IF F(7, p, 8, q) <> 7386 THEN g[999] := 1; END_IF
            g[0] := 1;
        END_PROGRAM
        """;

    // ═══════════════════════════════════════════════════════════════
    //  端到端：栈传参必须搬对每个槽
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void FiveScalarParams_PassedOnStack_CorrectValues()
    {
        var vm = CompileAndRun(SrcFiveScalars);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    [Fact]
    public void SixScalarParams_TwoStackSlots_CorrectValues()
    {
        var vm = CompileAndRun(SrcSixScalars);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    [Fact]
    public void ThreeMultiSlotStructs_AllOnStack_CorrectValues()
    {
        var vm = CompileAndRun(SrcThreeStructs);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    [Fact]
    public void MixedARegistersAndStackSlots_CorrectValues()
    {
        // a(1) + p(2) + b(1) + q(2) = 6 槽：前 4 走 A0–A3，后 2 走栈。
        var vm = CompileAndRun(SrcMixedAAndStack);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    // ═══════════════════════════════════════════════════════════════
    //  栈传参不破坏既有行为
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void CallerLocals_SurviveStackArgCall()
    {
        // 栈传参自己也要动栈：调用方的局部变量不得被参数区踩坏。
        var vm = CompileAndRun("""
            TYPE P : STRUCT x : DINT; y : DINT; END_STRUCT; END_TYPE;
            FUNCTION F : DINT
            VAR_INPUT a : DINT; b : DINT; c : DINT; d : DINT; e : DINT; END_VAR
                F := a + b + c + d + e;
            END_FUNCTION
            PROGRAM Main
            VAR g : ARRAY[0..99] OF INT; p : P; s : DINT; END_VAR
                p.x := 11; p.y := 22; s := 33;
                IF F(1, 2, 3, 4, 5) <> 15 THEN g[999] := 1; END_IF
                IF p.x <> 11 THEN g[999] := 1; END_IF
                IF p.y <> 22 THEN g[999] := 1; END_IF
                IF s <> 33 THEN g[999] := 1; END_IF
                g[0] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    [Fact]
    public void StackArgCall_InsideLoop_RepeatedCallsStayCorrect()
    {
        // 反复调用：参数区必须每次正确预留/回收，不能泄漏或错位。
        // 结果写进数组（按 i 索引）而非循环携带的累加器；
        // 循环携带标量的累加由 LoopCarriedAccumulator_IsPreserved 单独覆盖。
        var vm = CompileAndRun("""
            FUNCTION Sum5 : DINT
            VAR_INPUT a : DINT; b : DINT; c : DINT; d : DINT; e : DINT; END_VAR
                Sum5 := a * 10000 + b * 1000 + c * 100 + d * 10 + e;
            END_FUNCTION
            PROGRAM Main
            VAR
                g : ARRAY[0..99] OF INT;
                i : INT;
            END_VAR
                i := 0;
                WHILE i < 3 DO
                    i := i + 1;
                    g[i] := Sum5(i, i, i, i, i);
                END_WHILE
                IF g[1] <> 11111 THEN g[999] := 1; END_IF
                IF g[2] <> 22222 THEN g[999] := 1; END_IF
                IF g[3] <> 33333 THEN g[999] := 1; END_IF
                g[0] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    /// <summary>
    /// 回归：WHILE 内“循环携带标量 + 函数调用”累加必须正确。
    /// </summary>
    /// <remarks>
    /// 该形状曾在基线 <c>c73c4ec</c> 上失败（纯 A 位路径，与栈传参无关）。
    /// 根因是调用边界没有保存 caller-saved 寄存器，而不是 PHI 回填；
    /// 现已由跨调用 liveness + 定义处 spill 修复。
    /// </remarks>
    [Fact]
    public void LoopCarriedAccumulator_IsPreserved()
    {
        var vm = CompileAndRun("""
            FUNCTION Sum4 : DINT
            VAR_INPUT a : DINT; b : DINT; c : DINT; d : DINT; END_VAR
                Sum4 := a * 1000 + b * 100 + c * 10 + d;
            END_FUNCTION
            PROGRAM Main
            VAR g : ARRAY[0..99] OF INT; i : INT; r : DINT; END_VAR
                i := 0; r := 0;
                WHILE i < 3 DO
                    i := i + 1;
                    r := r + Sum4(i, i, i, i);
                END_WHILE
                IF r <> 1111 + 2222 + 3333 THEN g[999] := 1; END_IF
                g[0] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
    }

    [Fact]
    public void NestedStackArgCalls_InnerCallDoesNotCorruptOuterArgs()
    {
        // 嵌套调用：内层调用的参数区不得覆盖外层尚未读取的参数。
        var vm = CompileAndRun("""
            FUNCTION Inner : DINT
            VAR_INPUT a : DINT; b : DINT; c : DINT; d : DINT; e : DINT; END_VAR
                Inner := a + b + c + d + e;
            END_FUNCTION
            FUNCTION Outer : DINT
            VAR_INPUT a : DINT; b : DINT; c : DINT; d : DINT; e : DINT; END_VAR
                Outer := Inner(a, b, c, d, e) + a * 10000 + e;
            END_FUNCTION
            PROGRAM Main
            VAR g : ARRAY[0..99] OF INT; END_VAR
                IF Outer(1, 2, 3, 4, 5) <> 15 + 10005 THEN g[999] := 1; END_IF
                g[0] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    // ═══════════════════════════════════════════════════════════════
    //  零行为变化：≤4 槽仍走 A 位，不碰栈
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void FourOrFewerSlots_DoNotTouchTheStack()
    {
        // 4 槽（2 个多槽结构体）仍走 A0–A3，且不得出现 PUSHN/POPN。
        var (_, result) = CompileAndRunFull("""
            TYPE P : STRUCT x : DINT; y : DINT; END_STRUCT; END_TYPE;
            FUNCTION F : DINT
            VAR_INPUT u : P; v : P; END_VAR
                F := u.x * 10 + v.y;
            END_FUNCTION
            PROGRAM Main
            VAR g : ARRAY[0..99] OF INT; u : P; v : P; END_VAR
                u.x := 4; v.y := 5;
                IF F(u, v) <> 45 THEN g[999] := 1; END_IF
                g[0] := 1;
            END_PROGRAM
            """);

        var instrs = result.DebugModule.Functions
            .SelectMany(f => f.Instructions)
            .ToList();

        Assert.DoesNotContain(OpCode.PUSHN, instrs.Select(i => i.Opcode));
        Assert.DoesNotContain(OpCode.POPN, instrs.Select(i => i.Opcode));
    }

    [Fact]
    public void FiveScalars_EmitPushAndPopn()
    {
        // 5 槽 ⇒ 前 4 走 A 位、第 5 槽走帧上参数区：调用方 PUSH（逐值压栈）+ POPN（回收）。
        // 刻意不用 PUSHN 传值：它只改 SP 不写内存，而 ISA 里没有"按 SP 偏移写"的指令。
        var (_, result) = CompileAndRunFull(SrcFiveScalars);

        var instrs = result.DebugModule.Functions
            .SelectMany(f => f.Instructions)
            .ToList();

        Assert.Contains(OpCode.PUSH, instrs.Select(i => i.Opcode));
        Assert.Contains(OpCode.POPN, instrs.Select(i => i.Opcode));
        Assert.DoesNotContain(OpCode.PUSHN, instrs.Select(i => i.Opcode));
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
