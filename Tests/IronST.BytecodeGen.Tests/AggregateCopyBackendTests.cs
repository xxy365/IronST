// AggregateCopyBackendTests.cs — 0B 聚合搬运缺陷切片：后端层 T-COV-1 / T-COV-2
//
// ── 判据不与任何 ST 语言语义耦合 ────────────────────────────────────
// IR 的 load/store 聚合是「整个对象」的值读写（LLVM 语义）。"load %Point 却只搬
// 一个槽"本身就是 **IR 语义违背**，不依赖任何语言规范裁定 —— 这正是
// docs/阶段0B设计.md §1.4.2 分层表把它列为「后端层」、§6.3.4 允许其在语义
// 裁定前独立修复的原因。
//
// ── 刻意不断言的部分（待裁定项，勿据本文件反推规范）──────────────────
// • 结构体整体赋值是**值**语义还是引用语义（§6.3.5 #1）
// • `VAR_INPUT` 聚合传值 / 传引用（§6.3.5 #3）
// • FB 实例成员赋值可见性（§6.3.5 #1 行，2026-10-15 明确裁定"语义待裁定"）
//
// ── 观测方法 ────────────────────────────────────────────────────────
// 沿用 ReferenceEndToEndTests.cs:7-8 的越界探测器：语义正确 → 正常停机；
// 语义失真 → 故意 a[999] := 1 触发 ArrayBounds。（PROGRAM 的 VAR 是栈上局部
// 变量，VAR_GLOBAL 顶层不被接受，没有可读的全局观测口。）
//
// 字节码层断言走 DebugModule.Instructions（DebugModule.cs:9 / :80 / :148），
// 不扫裸 byte[] —— 后者会把指令立即数误判成操作码。
using IronST.Frontend;
using IronST.LLVMCodeGen.CodeGen;
using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.BytecodeGen.Tests;

public class AggregateCopyBackendTests
{
    // ═══════════════════════════════════════════════════════════════
    //  源码用例
    // ═══════════════════════════════════════════════════════════════

    /// <summary>2 字段结构体整体赋值，读回**两个**字段。</summary>
    private const string SrcStruct2 = """
        TYPE P : STRUCT x : DINT; y : DINT; END_STRUCT; END_TYPE;
        PROGRAM Main
        VAR a : ARRAY[0..99] OF INT; p : P; q : P; END_VAR
            p.x := 1;
            p.y := 2;
            q := p;
            IF q.x <> 1 THEN a[999] := 1; END_IF
            IF q.y <> 2 THEN a[999] := 1; END_IF
            a[0] := 1;
        END_PROGRAM
        """;

    /// <summary>3 字段结构体 —— 用于证明槽数随字段数增长（不是写死 2）。</summary>
    private const string SrcStruct3 = """
        TYPE P : STRUCT x : DINT; y : DINT; z : DINT; END_STRUCT; END_TYPE;
        PROGRAM Main
        VAR a : ARRAY[0..99] OF INT; p : P; q : P; END_VAR
            p.x := 1; p.y := 2; p.z := 3;
            q := p;
            IF q.z <> 3 THEN a[999] := 1; END_IF
            a[0] := 1;
        END_PROGRAM
        """;

    /// <summary>单字段结构体 —— 零行为变化回归（原先就走标量路径）。</summary>
    private const string SrcStruct1 = """
        TYPE P : STRUCT x : DINT; END_STRUCT; END_TYPE;
        PROGRAM Main
        VAR a : ARRAY[0..99] OF INT; p : P; q : P; END_VAR
            p.x := 5;
            q := p;
            IF q.x <> 5 THEN a[999] := 1; END_IF
            a[0] := 1;
        END_PROGRAM
        """;

    /// <summary>数组整体赋值，读回**两个**元素。</summary>
    private const string SrcArray2 = """
        PROGRAM Main
        VAR a : ARRAY[0..99] OF INT; s : ARRAY[0..1] OF INT; t : ARRAY[0..1] OF INT; END_VAR
            s[0] := 1; s[1] := 2;
            t := s;
            IF t[0] <> 1 THEN a[999] := 1; END_IF
            IF t[1] <> 2 THEN a[999] := 1; END_IF
            a[0] := 1;
        END_PROGRAM
        """;

    /// <summary>嵌套：只读 b —— 验证字段偏移越过第一个子结构体（FlatFieldOffset）。</summary>
    private const string SrcNestedFieldBeyondSubStruct = """
        TYPE A : STRUCT x : DINT; y : DINT; END_STRUCT; END_TYPE;
        TYPE B : STRUCT a : A; b : DINT; END_STRUCT; END_TYPE;
        PROGRAM Main
        VAR a : ARRAY[0..99] OF INT; g : B; h : B; END_VAR
            g.a.x := 1; g.a.y := 2; g.b := 3;
            h := g;
            IF h.b <> 3 THEN a[999] := 1; END_IF
            a[0] := 1;
        END_PROGRAM
        """;

    /// <summary>多槽结构体按值传参 —— 当前调用约定无多槽通道，应显式拒绝。</summary>
    private const string SrcParam2 = """
        TYPE P : STRUCT x : DINT; y : DINT; END_STRUCT; END_TYPE;
        FUNCTION F : DINT
        VAR_INPUT p : P; END_VAR
            F := p.x + p.y;
        END_FUNCTION
        PROGRAM Main
        VAR a : ARRAY[0..99] OF INT; p : P; END_VAR
            p.x := 3; p.y := 4;
            IF F(p) <> 7 THEN a[999] := 1; END_IF
            a[0] := 1;
        END_PROGRAM
        """;

    /// <summary>单槽结构体按值传参 —— 零行为变化回归（原先就能跑）。</summary>
    private const string SrcParam1 = """
        TYPE P : STRUCT x : DINT; END_STRUCT; END_TYPE;
        FUNCTION F : DINT
        VAR_INPUT p : P; END_VAR
            F := p.x;
        END_FUNCTION
        PROGRAM Main
        VAR a : ARRAY[0..99] OF INT; p : P; END_VAR
            p.x := 3;
            IF F(p) <> 3 THEN a[999] := 1; END_IF
            a[0] := 1;
        END_PROGRAM
        """;

    // ═══════════════════════════════════════════════════════════════
    //  端到端：聚合 load/store 必须覆盖每个叶槽
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void StructAssign_CopiesEveryLeafSlot()      // T-COV-1（后端层）
    {
        var vm = CompileAndRun(SrcStruct2);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    [Fact]
    public void ArrayAssign_CopiesEveryElementSlot()    // T-COV-2（后端层）
    {
        var vm = CompileAndRun(SrcArray2);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    [Fact]
    public void NestedStructAssign_OffsetsFieldBeyondFirstSubStruct()
    {
        // 第一层已验证：%B 的字段 1（b）扁平偏移 = %A 的槽数 = 2。
        // h.b 读回正确即证明 FlatFieldOffset 生效（修前它落在 a 的槽上）。
        // 子结构体内部各叶（h.a.x / h.a.y）的可寻址性由
        // NestedGepAddressingTests.SecondLevelGep_PreservesFieldOffset 等用例覆盖 ——
        // 第二级 GEP 的入口索引语义已在地址计算切片修正。
        var vm = CompileAndRun(SrcNestedFieldBeyondSubStruct);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    [Fact]
    public void StructAssign_DoesNotAliasTheSource()
    {
        // IR 的 load/store 是两个独立操作 ⇒ 值语义 ⇒ 写目标不得污染源。
        // 这是 **IR 语义**（不由 ST 规范裁定得出）；若日后语言层裁定为引用
        // 语义，需连同 §6.3.5 #1 一起重新评估本用例。
        var vm = CompileAndRun("""
            TYPE P : STRUCT x : DINT; y : DINT; END_STRUCT; END_TYPE;
            PROGRAM Main
            VAR a : ARRAY[0..99] OF INT; p : P; q : P; END_VAR
                p.x := 1; p.y := 2;
                q := p;
                q.x := 9;
                IF p.x <> 1 THEN a[999] := 1; END_IF
                IF q.y <> 2 THEN a[999] := 1; END_IF
                a[0] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    // ═══════════════════════════════════════════════════════════════
    //  零行为变化：单槽聚合继续走原标量路径
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void SingleSlotAggregate_BehaviourUnchanged()
    {
        // 单字段结构体一个槽就装得下 ⇒ 不新增逐槽序列，保持与修复前一致。
        var (vm, result) = CompileAndRunFull(SrcStruct1);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
        Assert.Equal(1, CountOp(result, OpCode.GETL));
        Assert.Equal(1, CountOp(result, OpCode.SETL));
    }

    [Fact]
    public void SingleSlotStructByValueParam_StillCompilesAndRuns()
    {
        var vm = CompileAndRun(SrcParam1);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    // ═══════════════════════════════════════════════════════════════
    //  字节码层：每个叶槽一对 GETL/SETL，且随叶槽数增长
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void AggregateCopy_EmitsOneGetlSetlPair_PerLeafSlot()
    {
        var (_, r2) = CompileAndRunFull(SrcStruct2);
        var (_, r3) = CompileAndRunFull(SrcStruct3);

        // 2 字段 ⇒ 2 对；3 字段 ⇒ 3 对。差值证明按叶槽数展开，而非写死常量。
        Assert.Equal(2, CountOp(r2, OpCode.GETL));
        Assert.Equal(2, CountOp(r2, OpCode.SETL));
        Assert.Equal(3, CountOp(r3, OpCode.GETL));
        Assert.Equal(3, CountOp(r3, OpCode.SETL));
    }

    [Fact]
    public void NestedAggregate_AllocaReservesAllLeafSlots_AndCopiesEachLeaf()
    {
        // 用"只读 b"的可运行版本（不掺入 h.a.y 的既有缺陷），
        // 两个维度分别断言：帧号覆盖所有叶槽；每叶一对 GETL/SETL。
        // B = {A(2), i32(1)} ⇒ 扁平 3 槽；100 + 3 + 3 = 106（修前为 104，少 2）。
        var (_, result) = CompileAndRunFull(SrcNestedFieldBeyondSubStruct);

        var instrs = result.DebugModule.Functions.SelectMany(f => f.Instructions).ToList();
        var frame = instrs.First(i => i.Opcode == OpCode.FRAME);
        Assert.Equal(106, frame.Imm16);

        Assert.Equal(3, instrs.Count(i => i.Opcode == OpCode.GETL));
        Assert.Equal(3, instrs.Count(i => i.Opcode == OpCode.SETL));
    }

    // ═══════════════════════════════════════════════════════════════
    //  跨帧缺口：多槽聚合按值传参显式拒绝（不静默产出错值字节码）
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void MultiSlotStructByValueParam_PassesWholeObject()
    {
        // 多槽聚合按值传参已由「调用约定扩展切片」支持（A0–A3 按槽分配）：
        // 本用例从「预期拒绝」改为「预期搬完整对象」——旧的拒绝行为是过渡态，
        // 依据见 AggregateParamPassingTests。
        var vm = CompileAndRun(SrcParam2);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    // ═══════════════════════════════════════════════════════════════
    //  辅助
    // ═══════════════════════════════════════════════════════════════

    private static VM CompileAndRun(string source)
        => CompileAndRunFull(source).vm;

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

    /// <summary>
    /// 统计某操作码在**指令流**中的出现次数（遍历 DebugModule 的指令列表，
    /// 不扫裸字节 —— 立即数与寄存器号都可能与操作码同值）。
    /// </summary>
    private static int CountOp(CompileResult result, OpCode op)
        => result.DebugModule.Functions
            .SelectMany(f => f.Instructions)
            .Count(i => i.Opcode == op);
}
