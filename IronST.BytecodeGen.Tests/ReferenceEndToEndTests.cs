// ReferenceEndToEndTests.cs — REFERENCE TO 解引用端到端（① 的验收链路）
//
// 语义分析通过 ≠ 功能可用。这里把 ST 源码整条跑完：
//   FrontendPipeline → IRGenerator → ExpressionEmitter → ByteCodeGenerator → 发布 VM
// 以确认「赋值 / 读取 / 运行时发射」三段都真的接上了。
//
// 判别方式：值正确 → 正常停机；值偏差 → 故意写 a[999] 越界。
// 这样断言落在「正常停机」上，任何一处语义失真都会变成 ArrayBounds 异常而失败。
// 之所以不直接读变量值：PROGRAM 内的 VAR 是栈上局部变量，而 VAR_GLOBAL 在语法层
// 就不被支持（解析器只接受 EOF/PROGRAM/FUNCTION/FUNCTION_BLOCK/CONFIGURATION），
// 没有可 ReadGlobal 的观测口。
using IronST.LLVMCodeGen.CodeGen;
using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.BytecodeGen.Tests;

public class ReferenceEndToEndTests
{
    private static VM CompileAndRun(string source)
    {
        var analysis = FrontendTestHelper.Analyze(source);
        Assert.True(analysis.Success);

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

    /// <summary>
    /// 写穿透：<c>r REF= i</c> 绑定后 <c>r^ := 99</c> 必须让 <c>i</c> 变成 99。
    /// 若写穿透没生效 <c>i</c> 仍是 0，走到 <c>a[999]</c> 即抛 ArrayBounds。
    /// </summary>
    [Fact]
    public void ReferenceWriteThrough_UpdatesTarget()
    {
        var vm = CompileAndRun("""
            PROGRAM Main
            VAR
                a : ARRAY[0..99] OF INT;
                i : INT;
                r : REFERENCE TO INT;
            END_VAR
                i := 0;
                r REF= i;
                r^ := 99;
                IF i <> 99 THEN
                    a[999] := 1;
                END_IF
                a[i] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    /// <summary>
    /// 读穿透：<c>out := r^</c> 必须读到 <c>i</c> 的 5。
    /// 读到别的值（含未解引用时的地址）都会走进 <c>a[999]</c>。
    /// </summary>
    [Fact]
    public void ReferenceReadThrough_ReadsTargetValue()
    {
        var vm = CompileAndRun("""
            PROGRAM Main
            VAR
                a   : ARRAY[0..99] OF INT;
                i   : INT;
                r   : REFERENCE TO INT;
                out : DINT;
            END_VAR
                i := 5;
                r REF= i;
                out := r^;
                IF out <> 5 THEN
                    a[999] := 1;
                END_IF
                a[out] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    /// <summary>
    /// 读-改-写走完整链路：经引用改回原值后再读出来，两步任一失真都会越界。
    /// </summary>
    [Fact]
    public void ReferenceReadWriteRoundTrip()
    {
        var vm = CompileAndRun("""
            PROGRAM Main
            VAR
                a   : ARRAY[0..99] OF INT;
                i   : INT;
                r   : REFERENCE TO INT;
                out : DINT;
            END_VAR
                i := 7;
                r REF= i;
                r^ := 70;
                out := r^;
                IF (i <> 70) OR (out <> 70) THEN
                    a[999] := 1;
                END_IF
                a[out] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    /// <summary>
    /// <b>前向引用</b>的 <c>REFERENCE TO</c> 走完整链路：<c>Holder</c> 声明在
    /// <c>Target</c> 之前，其 <c>REFERENCE TO Target</c> 字段在类型布局的不动点
    /// 收敛前就被解析（引用边不阻塞）—— 目标类型必须回填成真布局，
    /// 否则 <c>h.p^.v</c> 拿不到字段偏移，整条链路失真。
    ///
    /// <para>
    /// 判别方式同前：值正确 → 正常停机；值偏差 → <c>a[999]</c> 越界。
    /// </para>
    /// </summary>
    [Fact]
    public void ForwardReferenceToStruct_WorksEndToEnd()
    {
        var vm = CompileAndRun("""
            TYPE Holder : STRUCT p : REFERENCE TO Target; END_STRUCT; END_TYPE
            TYPE Target : STRUCT v : INT; END_STRUCT; END_TYPE

            PROGRAM Main
            VAR
                a   : ARRAY[0..99] OF INT;
                t   : Target;
                h   : Holder;
                got : DINT;
            END_VAR
                t.v := 7;
                h.p REF= t;
                got := h.p^.v;
                IF got <> 7 THEN
                    a[999] := 1;
                END_IF
                a[got] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }
}
