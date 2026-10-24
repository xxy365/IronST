// StringEndToEndTests.cs — F11：字符串字面量 / 字符串变量在字节码后端可用（Print 读路径）
//
// 语义分析通过 ≠ 功能可用。这里把 ST 源码整条跑完：
//   FrontendPipeline（HostBuiltins）→ IRGenerator → ByteCodeGenerator
//   （hostFunctions = { ["Print"] = 0 }）→ 发布 VM（HostBridge 捕获 A0）。
//
// 观测通道：宿主 Print 读 A0 的 BcValue，断言 Tag / StringValue。
// 修复前实测（docs/缺陷登记簿.md §3.6、docs/F11字符串后端实现设计.md §2）：
//   Print('world')            => A0 = nil
//   s := 'hello'; Print(s)    => A0 = 0（整型零值）
// 两条路径在修复前都不报错、不异常（STAGE=clean-halt），故断言必须落在「值」上。
using IronST.Frontend;
using IronST.Frontend.Analysis;
using IronST.LLVMCodeGen.CodeGen;
using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.BytecodeGen.Tests;

public class StringEndToEndTests
{
    /// <summary>
    /// 编译并运行，返回宿主 <c>Print</c> 每次调用读到的 <c>A0</c>（按调用顺序）。
    /// 前端需带 <c>HostBuiltins</c>（否则 Print 未声明），后端需带 <c>hostFunctions</c>。
    /// </summary>
    private static List<BcValue> CompileAndRun(string source)
    {
        var analysis = FrontendPipeline.Analyze(source, "test.st", [HostBuiltins.Create()]);
        var errors = analysis.Diagnostics
            .Where(d => d.Severity == Common.Diagnostics.DiagnosticSeverity.Error)
            .ToList();
        Assert.True(
            analysis.Success,
            $"frontend failed with {errors.Count} error(s):\n"
                + string.Join("\n", errors.Select(e => $"  {e}")));

        var module = new IRGenerator().Generate(analysis);
        var result = new ByteCodeGenerator(
            "test.st",
            hostFunctions: new Dictionary<string, ushort>(StringComparer.Ordinal) { ["Print"] = 0 }
        ).Generate(module);

        var printed = new List<BcValue>();
        var bridge = new HostBridge();
        bridge.Register(0, ctx =>
        {
            printed.Add(ctx.ReadReg(BytecodeBuilder.A0));
            return BcValue.Void;
        });

        var vm = new VM(result.Module, new VMOptions
        {
            StackSize = 8192,
            HostBridge = bridge,
            StructTypes = result.StructRegistrations,
        });
        vm.Run();

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
        return printed;
    }

    private static void AssertIsString(string expected, BcValue actual)
    {
        Assert.Equal(ValueTag.String, actual.Tag);
        Assert.Equal(expected, actual.StringValue);
    }

    // ═══════════════════════════════════════════════════════════════
    //  字面量
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void StringLiteral_Print_ReachesHostAsString()
    {
        var printed = CompileAndRun("""
            PROGRAM Main
            VAR
                unused : INT;
            END_VAR
                Print('world');
            END_PROGRAM
            """);

        AssertIsString("world", Assert.Single(printed));
    }

    [Fact]
    public void MultipleStringLiterals_PrintEach()
    {
        var printed = CompileAndRun("""
            PROGRAM Main
            VAR
                unused : INT;
            END_VAR
                Print('AB');
                Print('CD');
            END_PROGRAM
            """);

        Assert.Equal(2, printed.Count);
        AssertIsString("AB", printed[0]);
        AssertIsString("CD", printed[1]);
    }

    // ═══════════════════════════════════════════════════════════════
    //  变量
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void StringVariable_AssignedLiteral_Print_ReachesHostAsString()
    {
        var printed = CompileAndRun("""
            PROGRAM Main
            VAR
                s : STRING;
            END_VAR
                s := 'hello';
                Print(s);
            END_PROGRAM
            """);

        AssertIsString("hello", Assert.Single(printed));
    }

    [Fact]
    public void TwoStringVariables_PrintEachDistinctValue()
    {
        var printed = CompileAndRun("""
            PROGRAM Main
            VAR
                s1 : STRING;
                s2 : STRING;
            END_VAR
                s1 := 'alpha';
                s2 := 'beta';
                Print(s1);
                Print(s2);
            END_PROGRAM
            """);

        Assert.Equal(2, printed.Count);
        AssertIsString("alpha", printed[0]);
        AssertIsString("beta", printed[1]);
    }

    // ═══════════════════════════════════════════════════════════════
    //  回归：非字符串程序不受影响
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void NonStringProgram_StillPrintsInteger()
    {
        var printed = CompileAndRun("""
            PROGRAM Main
            VAR
                a : INT;
            END_VAR
                a := 42;
                Print(a);
            END_PROGRAM
            """);

        var v = Assert.Single(printed);
        Assert.Equal(ValueTag.Int64, v.Tag);
        Assert.Equal(42L, v.IntValue);
    }
}
