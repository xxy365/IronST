// RegisterPressureTests.cs — C8：函数内临时寄存器只增不减（TDD 红灯用例）
//
// ── 现象 ────────────────────────────────────────────────────────────
// FunctionEmitter._nextReg 在**每个函数**内从 R8 起单调递增，语句边界不回收：
// 一条语句用完的临时值，它的寄存器在整个函数余下部分仍被占用。
// 上限是 250（寄存器号是 byte，0..255；留 5 个余量），
// 越过即抛 InvalidOperationException「Out of registers …」。
//
// 代价实测（本文件的形状）：一条「常量下标数组元素赋值」约 5~6 个寄存器
// （BOUNDS 的 idx/max、GEP 偏移、IADD 结果、右值……）。
//   · 40 条赋值：可编译（当前能力的上界）
//   · 41 条赋值：_nextReg 到达 252，编译失败
//   · 30 条赋值 + 30 条 Print：_nextReg 到达 253，编译失败
//
// ── 本文件的作用 ────────────────────────────────────────────────────
// 这是 C8 的验收测试，按 TDD 先写成红灯：
//   · 前两条钉住**现有能力与语义**（30 条赋值运行结果正确、40 条可编译），
//     修复后必须仍然通过；
//   · 后两条是**验收目标**（41 条、100+ 条普通语句可编译），当前失败。
//
// 注意：后两条在 C8 修复前会让本测试程序集变红，这是刻意的；
// 若需要中间状态保持全绿，可临时把它们标成 `[Fact(Skip = "C8 未修复")]`，
// 修复时再解除（仓库对已知缺陷用过同一手法，见 REFERENCE 前向引用）。
//
// ── 不涉及 ──────────────────────────────────────────────────────────
// 不改语言语义、不改 ISA；修复方向见 docs/生产发布检查表.md C8：
// 在语句/基本块边界回收临时寄存器，或把死值落到帧槽、按需 GETL 恢复。
using IronST.Frontend;
using IronST.LLVMCodeGen.CodeGen;
using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.BytecodeGen.Tests;

public class RegisterPressureTests
{
    // ═══════════════════════════════════════════════════════════════
    //  现有能力（修复后必须仍然通过）
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 30 条连续数组元素赋值仍能编译，且运行结果正确。
    /// 用 FOR 循环求和，避免把观测代码本身堆到寄存器上限。
    /// </summary>
    [Fact]
    public void ThirtySequentialAssignments_CompileAndRunCorrectly()
    {
        var vm = CompileAndRun(SumProgram(assignments: 30));

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);

        // 0 + 1 + … + 29 = 435；全局槽 0 = `total`
        Assert.Equal(435, vm.Context.ReadGlobal(0).IntValue);
    }

    /// <summary>
    /// 40 条连续数组元素赋值是当前仍可编译的上界 —— 修复不得让这个形状退化。
    /// </summary>
    [Fact]
    public void FortySequentialAssignments_StillCompile()
    {
        Compile(AssignmentsProgram(assignments: 40));
    }

    // ═══════════════════════════════════════════════════════════════
    //  C8 验收目标（当前为红灯）
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 41 条连续数组元素赋值：现有实现 _nextReg 到达 252，抛
    /// 「Out of registers while materializing …」。
    ///
    /// 这只是比上一条多一条**普通**语句 —— 任何“语句之间应能复用临时值”
    /// 的分配策略都应让它通过。
    /// </summary>
    [Fact]
    public void FortyOneSequentialAssignments_Compile()
    {
        Compile(AssignmentsProgram(assignments: 41));
    }

    /// <summary>
    /// 检查表 C8 的验收口径：「100+ 条普通语句可编译」。
    ///
    /// 这里用 100 条常量下标赋值（每条 5~6 个寄存器），不涉及调用 / 聚合 /
    /// 控制流 —— 排除其它因素，只测寄存器预算。
    /// </summary>
    [Fact]
    public void OneHundredOrdinaryStatements_Compile()
    {
        Compile(AssignmentsProgram(assignments: 100));
    }

    // ═══════════════════════════════════════════════════════════════
    //  辅助
    // ═══════════════════════════════════════════════════════════════

    private const int ArraySize = 100;

    private static string Assignments(int assignments) =>
        string.Join(
            "\n",
            Enumerable.Range(0, assignments).Select(i => $"    a[{i}] := {i};"));

    /// <summary>只有赋值、没有任何观测代码 —— 纯编译期预算用例。</summary>
    private static string AssignmentsProgram(int assignments) => $$"""
        PROGRAM Main
        VAR
            a : ARRAY[0..{{ArraySize - 1}}] OF DINT;
        END_VAR
        {{Assignments(assignments)}}
        END_PROGRAM
        """;

    /// <summary>带求和观测：赋值条数受控，循环体只编译一次。</summary>
    private static string SumProgram(int assignments) => $$"""
        PROGRAM Main
        VAR_GLOBAL
            total : DINT;
        END_VAR
        VAR
            a : ARRAY[0..{{ArraySize - 1}}] OF DINT;
            i : DINT;
        END_VAR
        {{Assignments(assignments)}}
            total := 0;
            FOR i := 0 TO {{assignments - 1}} DO
                total := total + a[i];
            END_FOR;
        END_PROGRAM
        """;

    private static VM CompileAndRun(string source)
    {
        var result = CompileCore(source);

        var vm = new VM(result.Module, new VMOptions
        {
            StackSize = 8192,
            StructTypes = result.StructRegistrations,
        });
        vm.Run();
        return vm;
    }

    /// <summary>只编译，不运行。寄存器压力在字节码生成阶段就会抛出。</summary>
    private static void Compile(string source) => CompileCore(source);

    private static CompileResult CompileCore(string source)
    {
        var analysis = FrontendPipeline.Analyze(source, "test.st");
        Assert.True(analysis.Success, "frontend failed: " + string.Join(
            "\n", analysis.Diagnostics.Select(d => d.ToString())));

        var module = new IRGenerator().Generate(analysis);
        return new ByteCodeGenerator("test.st").Generate(module);
    }
}
