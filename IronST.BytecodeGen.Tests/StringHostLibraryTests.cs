// StringHostLibraryTests.cs — 字符串标准库「C# 宿主注入」端到端测试（路线：HCALL）。
//
// 链路：FrontendPipeline.Analyze（声明来自 IECStandardLibrary）
//       → IRGenerator（hostInjectedBuiltins：不内联，改发声明调用）
//       → ByteCodeGenerator（hostFunctions：转 HCALL）
//       → VM + HostBridge（StringStandardLibrary 的 C# 处理函数）
//
// 观测：宿主 Print 回传 A0 的 BcValue，断言 Tag / 值。
using IronST.Frontend;
using IronST.Frontend.Analysis;
using IronST.LLVMCodeGen.CodeGen;
using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.BytecodeGen.Tests;

public class StringHostLibraryTests
{
    private static (List<BcValue> printed, VM vm) Prepare(
        string source,
        bool registerStrings = true)
    {
        var libraries = new List<ExternalLibrary>
        {
            IECStandardLibrary.Create(),
            HostBuiltins.Create(),
        };

        var analysis = FrontendPipeline.Analyze(source, "test.st", libraries);
        var errors = analysis.Diagnostics
            .Where(d => d.Severity == Common.Diagnostics.DiagnosticSeverity.Error)
            .ToList();
        Assert.True(
            analysis.Success,
            $"frontend failed with {errors.Count} error(s):\n"
                + string.Join("\n", errors.Select(e => $"  {e}")));

        var hostFunctions = new Dictionary<string, ushort>(StringComparer.Ordinal)
        {
            ["Print"] = 0,
        };
        foreach (var kv in StringStandardLibrary.Functions)
            hostFunctions[kv.Key] = kv.Value;

        var module = new IRGenerator(hostInjectedBuiltins: hostFunctions.Keys)
            .Generate(analysis);
        var result = new ByteCodeGenerator("test.st", hostFunctions: hostFunctions)
            .Generate(module);

        var printed = new List<BcValue>();
        var bridge = new HostBridge();
        bridge.Register(0, ctx =>
        {
            printed.Add(ctx.ReadReg(BytecodeBuilder.A0));
            return BcValue.Void;
        });
        if (registerStrings)
            StringStandardLibrary.Register(bridge);

        var vm = new VM(result.Module, new VMOptions
        {
            StackSize = 8192,
            HostBridge = bridge,
            StructTypes = result.StructRegistrations,
        });
        return (printed, vm);
    }

    /// <summary>编译 + 运行；VM 正常停机时返回。</summary>
    private static (List<BcValue> printed, VM vm) Run(
        string source,
        bool registerStrings = true)
    {
        var (printed, vm) = Prepare(source, registerStrings);
        vm.Run();
        return (printed, vm);
    }

    private static string StringResult(List<BcValue> printed, VM vm)
    {
        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
        var v = Assert.Single(printed);
        Assert.Equal(ValueTag.String, v.Tag);
        return v.StringValue ?? "";
    }

    private static long IntResult(List<BcValue> printed, VM vm)
    {
        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
        var v = Assert.Single(printed);
        Assert.Equal(ValueTag.Int64, v.Tag);
        return v.IntValue;
    }

    // ═══════════════════════════════════════════════════════════════
    //  逐函数：返回值
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Concat_ReturnsJoinedString()
    {
        var (printed, vm) = Run("""
            PROGRAM Main
            VAR s : STRING; END_VAR
            s := CONCAT('hello', 'world');
            Print(s);
            END_PROGRAM
            """);
        Assert.Equal("helloworld", StringResult(printed, vm));
    }

    [Fact]
    public void Len_ReturnsLength()
    {
        var (printed, vm) = Run("""
            PROGRAM Main
            VAR n : INT; END_VAR
            n := LEN('hello');
            Print(n);
            END_PROGRAM
            """);
        Assert.Equal(5L, IntResult(printed, vm));
    }

    [Fact]
    public void Left_ReturnsPrefix()
    {
        var (printed, vm) = Run("""
            PROGRAM Main
            VAR s : STRING; END_VAR
            s := LEFT('hello', 3);
            Print(s);
            END_PROGRAM
            """);
        Assert.Equal("hel", StringResult(printed, vm));
    }

    [Fact]
    public void Right_ReturnsSuffix()
    {
        var (printed, vm) = Run("""
            PROGRAM Main
            VAR s : STRING; END_VAR
            s := RIGHT('hello', 3);
            Print(s);
            END_PROGRAM
            """);
        Assert.Equal("llo", StringResult(printed, vm));
    }

    [Fact]
    public void Mid_StartIsOneBased()
    {
        var (printed, vm) = Run("""
            PROGRAM Main
            VAR s : STRING; END_VAR
            s := MID('hello', 2, 2);
            Print(s);
            END_PROGRAM
            """);
        Assert.Equal("el", StringResult(printed, vm));
    }

    [Fact]
    public void Find_ReturnsOneBasedPosition()
    {
        var (printed, vm) = Run("""
            PROGRAM Main
            VAR n : INT; END_VAR
            n := FIND('hello', 'll');
            Print(n);
            END_PROGRAM
            """);
        Assert.Equal(3L, IntResult(printed, vm));
    }

    [Fact]
    public void Find_NotFound_ReturnsZero()
    {
        var (printed, vm) = Run("""
            PROGRAM Main
            VAR n : INT; END_VAR
            n := FIND('hello', 'zz');
            Print(n);
            END_PROGRAM
            """);
        Assert.Equal(0L, IntResult(printed, vm));
    }

    [Fact]
    public void Insert_AtOneBasedPosition()
    {
        var (printed, vm) = Run("""
            PROGRAM Main
            VAR s : STRING; END_VAR
            s := INSERT('hello', 'XY', 3);
            Print(s);
            END_PROGRAM
            """);
        Assert.Equal("heXYllo", StringResult(printed, vm));
    }

    [Fact]
    public void Delete_RemovesRange()
    {
        var (printed, vm) = Run("""
            PROGRAM Main
            VAR s : STRING; END_VAR
            s := DELETE('hello', 2, 2);
            Print(s);
            END_PROGRAM
            """);
        Assert.Equal("hlo", StringResult(printed, vm));
    }

    [Fact]
    public void Replace_ReplacesRange()
    {
        var (printed, vm) = Run("""
            PROGRAM Main
            VAR s : STRING; END_VAR
            s := REPLACE('hello', 'XY', 2, 2);
            Print(s);
            END_PROGRAM
            """);
        Assert.Equal("hXYlo", StringResult(printed, vm));
    }

    // ═══════════════════════════════════════════════════════════════
    //  组合：字符串结果可直接作 Print 实参（不落变量）
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void NestedCall_ResultPassedDirectlyToHost()
    {
        var (printed, vm) = Run("""
            PROGRAM Main
            VAR unused : INT; END_VAR
            Print(CONCAT('ab', 'cd'));
            END_PROGRAM
            """);
        Assert.Equal("abcd", StringResult(printed, vm));
    }

    // ═══════════════════════════════════════════════════════════════
    //  边界：越界/零长度给出确定结果（不崩溃）
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Left_OverLength_ClampsToWholeString()
    {
        var (printed, vm) = Run("""
            PROGRAM Main
            VAR s : STRING; END_VAR
            s := LEFT('hi', 10);
            Print(s);
            END_PROGRAM
            """);
        Assert.Equal("hi", StringResult(printed, vm));
    }

    [Fact]
    public void Mid_StartPastEnd_ReturnsEmpty()
    {
        var (printed, vm) = Run("""
            PROGRAM Main
            VAR s : STRING; END_VAR
            s := MID('hi', 5, 10);
            Print(s);
            END_PROGRAM
            """);
        Assert.Equal("", StringResult(printed, vm));
    }

    // ═══════════════════════════════════════════════════════════════
    //  异常路径：未注册宿主处理函数 ⇒ VM 确定性报错（非静默）
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void UnregisteredStringHandler_FailsDeterministically()
    {
        var (_, vm) = Prepare("""
            PROGRAM Main
            VAR s : STRING; END_VAR
            s := CONCAT('a', 'b');
            END_PROGRAM
            """, registerStrings: false);

        // VM 契约：未注册的 HCALL 抛 RuntimeErrorException，并记录到 LastError。
        var ex = Assert.Throws<RuntimeErrorException>(() => vm.Run());
        Assert.Contains("unregistered", ex.Message);
        Assert.NotNull(vm.LastError);
    }

    // ═══════════════════════════════════════════════════════════════
    //  回归：非字符串程序（同一 libraries/hostFunctions）不受影响
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void NonStringProgram_StillPrintsInteger()
    {
        var (printed, vm) = Run("""
            PROGRAM Main
            VAR a : INT; END_VAR
            a := 42;
            Print(a);
            END_PROGRAM
            """);
        Assert.Equal(42L, IntResult(printed, vm));
    }
}
