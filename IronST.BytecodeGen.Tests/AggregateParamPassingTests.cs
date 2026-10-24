// AggregateParamPassingTests.cs — 0B 多槽聚合作 VAR_INPUT 实参（调用约定扩展）
//
// ── 背景 ────────────────────────────────────────────────────────────
// 聚合搬运切片（26fdceb）为"不静默产出错值字节码"而对多槽聚合作参数抛
// InvalidOperationException。但那把一个**完全合法的 ST 程序**变成了
// 编译期硬失败：结构体作 VAR_INPUT 参数是常规用法（本例 Dist(p1, p2)）。
//
// 本文件用测试驱动补齐调用约定的多槽通道：
//   · 参数槽分配按 FlatSlotCount（标量仍 1 槽 ⇒ 对既有程序零行为变化）
//   · 多槽聚合实参逐槽 PUSH，被调用方逐槽 POP 收取（顺序严格互逆）
//   · <4 参数位仍按原约定占用（聚合 A 位传起始槽号作占位）
//
// ── 不涉及 ──────────────────────────────────────────────────────────
// VAR_INPUT 传值 / 传引用的**语言语义裁定**（§6.3.5 #3）：
// 本切片只实现"按整个对象传副本"这一 **IR 值语义**（LLVM 的 byval 语义），
// 不断言 ST 规范应取传值还是传引用；若日后裁定为传引用，本文件的
// "是否别名"类用例需随之重新评估，但传参能力本身不受影响。
// 也不涉及 Arena / StructTypeId / F7/F9 / 宿主 ID。
//
// ── 观测方法 ────────────────────────────────────────────────────────
// 越界探测器（PROGRAM 的 VAR 无可读全局观测口，见 ReferenceEndToEndTests.cs:9-11）。
using IronST.Frontend;
using IronST.LLVMCodeGen.CodeGen;
using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.BytecodeGen.Tests;

public class AggregateParamPassingTests
{
    /// <summary>用户实际报障的场景：两个 REAL 结构体参数。</summary>
    private const string SrcDist = """
        TYPE Point : STRUCT X : REAL; Y : REAL; END_STRUCT; END_TYPE;

        FUNCTION Dist : REAL
        VAR_INPUT
            a : Point;
            b : Point;
        END_VAR
        VAR
            dx : REAL;
            dy : REAL;
        END_VAR
            dx := a.X - b.X;
            dy := a.Y - b.Y;
            Dist := dx * dx + dy * dy;
        END_FUNCTION

        PROGRAM Main
        VAR
            a : ARRAY[0..99] OF INT;
            p1 : Point;
            p2 : Point;
            distance : REAL;
        END_VAR
            p1.X := 0.0;
            p1.Y := 0.0;
            p2.X := 3.0;
            p2.Y := 4.0;
            distance := Dist(p1, p2);
            IF distance <> 25.0 THEN a[999] := 1; END_IF
            a[0] := 1;
        END_PROGRAM
        """;

    /// <summary>单个多槽结构体参数。</summary>
    private const string SrcSingleStructParam = """
        TYPE P : STRUCT x : DINT; y : DINT; END_STRUCT; END_TYPE;
        FUNCTION F : DINT
        VAR_INPUT p : P; END_VAR
            F := p.x * 10 + p.y;
        END_FUNCTION
        PROGRAM Main
        VAR a : ARRAY[0..99] OF INT; p : P; END_VAR
            p.x := 4; p.y := 2;
            IF F(p) <> 42 THEN a[999] := 1; END_IF
            a[0] := 1;
        END_PROGRAM
        """;

    /// <summary>聚合与标量参数混合（聚合在下标 1，验证 A 位与栈位不互相污染）。</summary>
    private const string SrcMixedParams = """
        TYPE P : STRUCT x : DINT; y : DINT; END_STRUCT; END_TYPE;
        FUNCTION F : DINT
        VAR_INPUT
            k : DINT;
            p : P;
        END_VAR
            F := k + p.x + p.y;
        END_FUNCTION
        PROGRAM Main
        VAR a : ARRAY[0..99] OF INT; p : P; END_VAR
            p.x := 20; p.y := 30;
            IF F(2, p) <> 52 THEN a[999] := 1; END_IF
            a[0] := 1;
        END_PROGRAM
        """;

    /// <summary>三个参数、其中两个是多槽聚合（2+1+2 = 5 槽，超出 A0–A3 预算）。</summary>
    private const string SrcThreeParams = """
        TYPE P : STRUCT x : DINT; y : DINT; END_STRUCT; END_TYPE;
        FUNCTION F : DINT
        VAR_INPUT
            u : P;
            k : DINT;
            v : P;
        END_VAR
            F := u.x * 100 + k * 10 + v.y;
        END_FUNCTION
        PROGRAM Main
        VAR a : ARRAY[0..99] OF INT; u : P; v : P; END_VAR
            u.x := 1; u.y := 2;
            v.x := 3; v.y := 7;
            IF F(u, 5, v) <> 157 THEN a[999] := 1; END_IF
            a[0] := 1;
        END_PROGRAM
        """;

    /// <summary>三个参数均为单槽聚合 + 标量（1+1+1 = 3 槽，在预算内）。</summary>
    private const string SrcThreeParamsFitsBudget = """
        TYPE Q : STRUCT x : DINT; END_STRUCT; END_TYPE;
        FUNCTION F : DINT
        VAR_INPUT
            u : Q;
            k : DINT;
            v : Q;
        END_VAR
            F := u.x * 100 + k * 10 + v.x;
        END_FUNCTION
        PROGRAM Main
        VAR a : ARRAY[0..99] OF INT; u : Q; v : Q; END_VAR
            u.x := 1;
            v.x := 7;
            IF F(u, 5, v) <> 157 THEN a[999] := 1; END_IF
            a[0] := 1;
        END_PROGRAM
        """;

    /// <summary>参数不得别名调用方局部：改被调方可见的副本不能污染实参。</summary>
    private const string SrcNoAliasing = """
        TYPE P : STRUCT x : DINT; y : DINT; END_STRUCT; END_TYPE;
        FUNCTION Bump : DINT
        VAR_INPUT p : P; END_VAR
            p.x := p.x + 1000;
            Bump := p.x;
        END_FUNCTION
        PROGRAM Main
        VAR a : ARRAY[0..99] OF INT; p : P; r : DINT; END_VAR
            p.x := 1; p.y := 2;
            r := Bump(p);
            IF r <> 1001 THEN a[999] := 1; END_IF
            IF p.x <> 1 THEN a[999] := 1; END_IF
            a[0] := 1;
        END_PROGRAM
        """;

    // ═══════════════════════════════════════════════════════════════
    //  应通过的用例（本切片验收）
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void StructByValueParam_UserReportedScenario_CompilesAndRuns()
    {
        var vm = CompileAndRun(SrcDist);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    [Fact]
    public void StructByValueParam_SingleMultiSlotStruct()
    {
        var vm = CompileAndRun(SrcSingleStructParam);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    [Fact]
    public void StructByValueParam_MixedWithScalar()
    {
        var vm = CompileAndRun(SrcMixedParams);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    [Fact]
    public void StructByValueParam_ThreeParams_MixedTypesWithinBudget()
    {
        // 三个参数混合 单槽聚合 + 标量，总槽数 3 ⇒ 在 A0–A3 预算内。
        var vm = CompileAndRun(SrcThreeParamsFitsBudget);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    [Fact]
    public void OverBudgetArgumentSlots_PassedViaFrameArgArea()
    {
        // 5 个 A 槽 > 4：现已支持 —— 前 4 槽走 A0–A3，第 5 槽走帧上参数区。
        // （本切片前的过渡态是显式拒绝，现已由栈传参取代。）
        var vm = CompileAndRun(SrcThreeParams);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    [Fact]
    public void FiveScalarParams_ProduceCorrectResult()
    {
        // 原有回归护栏：5 个标量参数原先走 PUSH/POP，而 FRAME 会覆盖压入的参数，
        // 结果是「编译通过、运行结果错」。现已改为帧上参数区，结果必须正确。
        var vm = CompileAndRun("""
            FUNCTION Sum5 : DINT
            VAR_INPUT a : DINT; b : DINT; c : DINT; d : DINT; e : DINT; END_VAR
                Sum5 := a * 10000 + b * 1000 + c * 100 + d * 10 + e;
            END_FUNCTION
            PROGRAM Main
            VAR g : ARRAY[0..99] OF INT; END_VAR
                IF Sum5(1, 2, 3, 4, 5) <> 12345 THEN g[999] := 1; END_IF
                g[0] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    [Fact]
    public void StructByValueParam_DoesNotAliasCallerLocal()
    {
        // IR 值语义：被调方改副本不得污染调用方实参。
        var vm = CompileAndRun(SrcNoAliasing);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    // ═══════════════════════════════════════════════════════════════
    //  零行为变化回归
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ScalarOnlyParams_BehaviourUnchanged()
    {
        // 标量参数的槽分配与调用路径必须与扩展前完全一致。
        var vm = CompileAndRun("""
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

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    [Fact]
    public void ScalarOnlyParams_FrameIsDeterministic_AndCoversParamSlots()
    {
        // 不硬编码帧号（F 内还有返回值 alloca 等），改为验证两件事：
        //   ① 至少容纳 3 个参数槽；② 编译确定性（同一源码两次编译帧号相同）。
        const string src = """
            FUNCTION F : DINT
            VAR_INPUT a : DINT; b : DINT; c : DINT; END_VAR
                F := a;
            END_FUNCTION
            PROGRAM Main
            VAR g : ARRAY[0..99] OF INT; END_VAR
                g[0] := 1;
            END_PROGRAM
            """;

        var (_, r1) = CompileAndRunFull(src);
        var (_, r2) = CompileAndRunFull(src);

        var f1 = r1.DebugModule.Functions.First(x => x.Name == "F");
        var f2 = r2.DebugModule.Functions.First(x => x.Name == "F");
        int frame1 = f1.Instructions.First(i => i.Opcode == OpCode.FRAME).Imm16;
        int frame2 = f2.Instructions.First(i => i.Opcode == OpCode.FRAME).Imm16;

        Assert.Equal(frame1, frame2);
        Assert.True(frame1 >= 3, $"frame {frame1} must cover the 3 scalar param slots");
    }

    [Fact]
    public void SingleSlotAggregateParam_StillWorks()
    {
        // 单字段结构体（FlatSlotCount==1）此前即可工作，不得回归。
        var vm = CompileAndRun("""
            TYPE P : STRUCT x : DINT; END_STRUCT; END_TYPE;
            FUNCTION F : DINT
            VAR_INPUT p : P; END_VAR
                F := p.x;
            END_FUNCTION
            PROGRAM Main
            VAR g : ARRAY[0..99] OF INT; p : P; END_VAR
                p.x := 9;
                IF F(p) <> 9 THEN g[999] := 1; END_IF
                g[0] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
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
