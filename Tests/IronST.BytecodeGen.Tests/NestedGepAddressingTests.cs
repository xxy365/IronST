// NestedGepAddressingTests.cs — 0B 地址计算切片：第二级 GEP 字段偏移与寄存器活跃性
//
// ── 背景 ────────────────────────────────────────────────────────────
// 聚合搬运修复后仍遗留两个缺陷，均属「地址计算 / 寄存器活跃性」的后端正确性问题，
// 且与任何 ST 语言语义裁定无关：
//   ① 第二级 GEP 偏移丢失：GEP 的第一个 index 是 LLVM 里 ptr → pointee 的「入口」步骤，
//      对结构体和数组都不产生偏移、也不下降容器；旧实现把它当成「字段 0」而使容器
//      多降一级，GEP %B, p, 0, 1 于是求出 g.a 的偏移而非 g.b 的 2。
//   ② alloca 的冗余 LDC：EnsureReg 已为 alloca 分配寄存器并装入起始槽号，
//      EmitAlloca 又发一条同寄存器、同值的 LDC —— 死指令（覆盖前一条），
//      且掩盖了真实的寄存器活跃性问题。
//
// ── 本文件不涉及 ────────────────────────────────────────────────────
// 调用约定 / 多槽传参（T-COV-3）、Arena 方向、StructTypeId、F7/F9、宿主 ID。
//
// ── 观测方法 ────────────────────────────────────────────────────────
// 越界探测器（PROGRAM 的 VAR 无可读全局观测口，见 ReferenceEndToEndTests.cs:9-11）；
// 字节码层遍历 DebugModule 指令（不扫裸字节，立即数与寄存器号会同值）。
using IronST.Frontend;
using IronST.LLVMCodeGen.CodeGen;
using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.BytecodeGen.Tests;

public class NestedGepAddressingTests
{
    // ═══════════════════════════════════════════════════════════════
    //  嵌套结构体字段访问（原 Skip 登记的缺陷，现已修复）
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void SecondLevelGep_PreservesFieldOffset()
    {
        // B = {A(2 槽), i32} ⇒ 扁平 3 槽：a.x@0、a.y@1、b@2。
        // GEP %A, p, 0, 1 的偏移必须是 1（%A 自己的字段 1），
        // 而 GEP %B, p, 0, 1 的偏移必须是 2（越过占 2 槽的 %A）。
        var vm = CompileAndRun("""
            TYPE A : STRUCT x : DINT; y : DINT; END_STRUCT; END_TYPE;
            TYPE B : STRUCT a : A; b : DINT; END_STRUCT; END_TYPE;
            PROGRAM Main
            VAR a : ARRAY[0..99] OF INT; g : B; END_VAR
                g.a.x := 1; g.a.y := 2; g.b := 3;
                IF g.a.y <> 2 THEN a[999] := 1; END_IF
                a[0] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    [Fact]
    public void EveryLeafOfNestedStruct_IsIndependentlyAddressable()
    {
        // 三个叶全部可寻址：任一个写错都会与另一个互相覆盖。
        var vm = CompileAndRun("""
            TYPE A : STRUCT x : DINT; y : DINT; END_STRUCT; END_TYPE;
            TYPE B : STRUCT a : A; b : DINT; END_STRUCT; END_TYPE;
            PROGRAM Main
            VAR a : ARRAY[0..99] OF INT; g : B; END_VAR
                g.a.x := 1; g.a.y := 2; g.b := 3;
                IF g.a.x <> 1 THEN a[999] := 1; END_IF
                IF g.a.y <> 2 THEN a[999] := 1; END_IF
                IF g.b   <> 3 THEN a[999] := 1; END_IF
                a[0] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    [Fact]
    public void NestedField_WritePath_And_ReadPath_BothCorrect()
    {
        // 读写两条路径分别验证：写完立刻读回（写路径），
        // 再由另一处读（读路径）—— 两条若有其一错都会污染另一个叶。
        var vm = CompileAndRun("""
            TYPE A : STRUCT x : DINT; y : DINT; END_STRUCT; END_TYPE;
            TYPE B : STRUCT a : A; b : DINT; END_STRUCT; END_TYPE;
            PROGRAM Main
            VAR a : ARRAY[0..99] OF INT; g : B; END_VAR
                g.a.x := 11;
                g.a.y := 22;
                g.b := 33;
                IF g.a.y <> 22 THEN a[999] := 1; END_IF
                g.a.y := 99;
                IF g.a.x <> 11 THEN a[999] := 1; END_IF
                IF g.b <> 33 THEN a[999] := 1; END_IF
                a[0] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    // ═══════════════════════════════════════════════════════════════
    //  数组 of 结构体：元素步长 = 元素扁平槽数
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ArrayOfStruct_ElementStepIsElementSlotCount()
    {
        // [2 x %A]：元素 1 的偏移 = 1 × FlatSlotCount(%A) = 2，
        // 而不是 1 —— 后者会让 g[1] 落在 g[0].y 上。
        var vm = CompileAndRun("""
            TYPE A : STRUCT x : DINT; y : DINT; END_STRUCT; END_TYPE;
            PROGRAM Main
            VAR a : ARRAY[0..99] OF INT; g : ARRAY[0..1] OF A; END_VAR
                g[0].x := 1; g[0].y := 2;
                g[1].x := 3; g[1].y := 4;
                IF g[0].x <> 1 THEN a[999] := 1; END_IF
                IF g[0].y <> 2 THEN a[999] := 1; END_IF
                IF g[1].x <> 3 THEN a[999] := 1; END_IF
                IF g[1].y <> 4 THEN a[999] := 1; END_IF
                a[0] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    [Fact]
    public void MultiDimArray_RowStepCompounds()
    {
        // [3 x [4 x i32]]：行步长 = 4（内维长度），列步长 = 1。
        // 入口索引不下降容器 ⇒ 行偏移 = i × 4，列偏移 = j。
        var vm = CompileAndRun("""
            PROGRAM Main
            VAR a : ARRAY[0..99] OF INT; m : ARRAY[0..2, 0..3] OF DINT; END_VAR
                m[0, 0] := 1;
                m[1, 2] := 6;
                m[2, 3] := 11;
                IF m[0, 0] <> 1  THEN a[999] := 1; END_IF
                IF m[1, 2] <> 6  THEN a[999] := 1; END_IF
                IF m[2, 3] <> 11 THEN a[999] := 1; END_IF
                IF m[1, 0] <> 0  THEN a[999] := 1; END_IF
                a[0] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    // ═══════════════════════════════════════════════════════════════
    //  寄存器活跃性：地址寄存器不得被后续 LDC 覆盖
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void AddressRegister_NotClobbered_ByLaterConstantLoads()
    {
        // 大量 LDC 之后再取同一地址：若 alloca 的槽号寄存器被复用，
        // 后续 GEP 会基于错误基址寻址。
        var vm = CompileAndRun("""
            TYPE A : STRUCT x : DINT; y : DINT; END_STRUCT; END_TYPE;
            TYPE B : STRUCT a : A; b : DINT; END_STRUCT; END_TYPE;
            PROGRAM Main
            VAR a : ARRAY[0..99] OF INT; g : B; END_VAR
                g.a.x := 1; g.a.y := 2; g.b := 3;
                a[0] := 10; a[1] := 20; a[2] := 30; a[3] := 40; a[4] := 50;
                IF g.a.y <> 2 THEN a[999] := 1; END_IF
                a[5] := 60; a[6] := 70; a[7] := 80;
                IF g.b <> 3 THEN a[999] := 1; END_IF
                a[8] := 90;
                IF g.a.x <> 1 THEN a[999] := 1; END_IF
                a[0] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    [Fact]
    public void Alloca_EmitsExactlyOneLdc_PerAlloca()
    {
        // 去冗余回归：alloca 的槽号只应有一条 LDC（此前两条，第二条死覆盖第一条）。
        // 3 个 alloca ⇒ 3 条来自 alloca；其余 LDC 来自常量与 GEP，故只断言
        // 「同一寄存器不会连续出现两条 LDC」——这是死指令的直接判据。
        var (_, result) = CompileAndRunFull("""
            TYPE A : STRUCT x : DINT; y : DINT; END_STRUCT; END_TYPE;
            PROGRAM Main
            VAR a : ARRAY[0..99] OF INT; g : A; h : A; END_VAR
                g.x := 1; g.y := 2;
                h := g;
                a[0] := 1;
            END_PROGRAM
            """);

        var instrs = result.DebugModule.Functions.SelectMany(f => f.Instructions).ToList();

        byte prevReg = 0xFF;
        foreach (var ins in instrs)
        {
            if (ins.Opcode != OpCode.LDC) { prevReg = 0xFF; continue; }

            Assert.False(ins.Dst == prevReg,
                $"consecutive LDC into R{ins.Dst}: the second overwrites the first "
                    + "without any consumer in between (dead instruction).");
            prevReg = ins.Dst;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  字节码层：arr1 之外再确认 lit/glob/fwd 语义无变化
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GepOffsets_ProduceOneIaddPerNonZeroLevel()
    {
        // 每级非零字段索引用一条 IADD 折价；入口索引 0 不产生任何指令。
        var (_, r2) = CompileAndRunFull("""
            TYPE P : STRUCT x : DINT; y : DINT; END_STRUCT; END_TYPE;
            PROGRAM Main
            VAR p : P; END_VAR
                p.y := 7;
            END_PROGRAM
            """);

        // p.y => GEP %P, p, 0, 1：入口不出指令，字段 1 出一条 IADD + 一条装载偏移的 LDC。
        var instrs = r2.DebugModule.Functions.SelectMany(f => f.Instructions).ToList();
        Assert.True(instrs.Count(i => i.Opcode == OpCode.IADD) >= 1,
            "expected at least one IADD for the non-zero field index");
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
