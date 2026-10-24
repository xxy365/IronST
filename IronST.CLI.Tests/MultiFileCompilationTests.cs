// MultiFileCompilationTests.cs — 多文件编译（路线 A）最小入口的端到端与回归测试。
//
// 被测 API：IronST.CLI.IronRuntime.CompileFiles
// 管线：FrontendPipeline.AnalyzeFiles → IRGenerator.Generate
//       → ByteCodeGenerator.Generate → BytecodeModule → IronST.Runtime.VM
//
// 观测通道：宿主 `Print`（HostBuiltins 前端声明 + hostFunctions{Print=0}）把表达式值
// 回传到 A0，宿主处理函数读 A0 —— 与 IronST.BytecodeGen.Tests.FBTestSupport 同路。
using IronST.Frontend.Analysis;
using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.CLI.Tests;

public class MultiFileCompilationTests
{
    // ── 源码片段 ──────────────────────────────────────────────
    private const string AddFunction = """
        FUNCTION Add : DINT
        VAR_INPUT a : DINT; b : DINT; END_VAR
        Add := a + b;
        END_FUNCTION
        """;

    private const string MainCallsAdd = """
        PROGRAM Main
        VAR x : DINT; END_VAR
        x := Add(40, 2);
        Print(x);
        END_PROGRAM
        """;

    private const string PointType = """
        TYPE Point : STRUCT x : DINT; y : DINT; END_STRUCT; END_TYPE;
        """;

    private const string MainUsesPoint = """
        PROGRAM Main
        VAR p : Point; END_VAR
        p.x := 3;
        p.y := 4;
        Print(p.x);
        END_PROGRAM
        """;

    private const string ProgramAlpha = """
        PROGRAM Alpha
        VAR a : DINT; END_VAR
        a := 1; Print(a);
        END_PROGRAM
        """;

    private const string ProgramBeta = """
        PROGRAM Beta
        VAR b : DINT; END_VAR
        b := 2; Print(b);
        END_PROGRAM
        """;

    private const string SingleMainPrints7 = """
        PROGRAM Main
        VAR x : DINT; END_VAR
        x := 7; Print(x);
        END_PROGRAM
        """;

    private const string SingleMainPrints5 = """
        PROGRAM Main
        VAR x : DINT; END_VAR
        x := 5; Print(x);
        END_PROGRAM
        """;

    // ── 公共辅助 ──────────────────────────────────────────────

    private static readonly IReadOnlyDictionary<string, ushort> HostMap =
        new Dictionary<string, ushort>(StringComparer.Ordinal) { ["Print"] = 0 };

    private static (List<long> printed, VM vm) CompileAndRun(
        (string Source, string FileName)[] files,
        string? entry = null)
    {
        var result = IronRuntime.CompileFiles(
            files,
            hostFunctions: HostMap,
            libraries: [HostBuiltins.Create()],
            entryFunctionName: entry);

        var printed = new List<long>();
        var bridge = new HostBridge();
        bridge.Register(0, ctx =>
        {
            printed.Add(ctx.ReadReg(BytecodeBuilder.A0).IntValue);
            return BcValue.Void;
        });

        var vm = new VM(result.Module, new VMOptions
        {
            StackSize = 8192,
            HostBridge = bridge,
            StructTypes = result.StructRegistrations,
        });
        vm.Run();
        return (printed, vm);
    }

    // ── 跨文件函数调用 ────────────────────────────────────────

    [Fact]
    public void CrossFileFunctionCall_CompilesAndRuns()
    {
        var (printed, vm) = CompileAndRun(
            [(AddFunction, "add.st"), (MainCallsAdd, "main.st")]);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
        Assert.Equal(new long[] { 42 }, printed);
    }

    // ── 跨文件类型引用 ────────────────────────────────────────

    [Fact]
    public void CrossFileTypeReference_CompilesAndRuns()
    {
        var (printed, vm) = CompileAndRun(
            [(PointType, "types.st"), (MainUsesPoint, "main.st")]);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
        Assert.Equal(new long[] { 3 }, printed);
    }

    // ── 入口选择 ──────────────────────────────────────────────

    [Fact]
    public void ExplicitEntry_SelectsNamedProgram()
    {
        var (printed, vm) = CompileAndRun(
            [(ProgramAlpha, "alpha.st"), (ProgramBeta, "beta.st")],
            entry: "Beta");

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Equal(new long[] { 2 }, printed);
    }

    [Fact]
    public void SingleProgram_WithoutExplicitEntry_AutoSelectsIt()
    {
        var (printed, vm) = CompileAndRun([(SingleMainPrints7, "main.st")]);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Equal(new long[] { 7 }, printed);
    }

    [Fact]
    public void MultiplePrograms_WithoutExplicitEntry_FailsDeterministically()
    {
        var ex = Assert.Throws<CompilationException>(() =>
            IronRuntime.CompileFiles(
                [(ProgramAlpha, "alpha.st"), (ProgramBeta, "beta.st")],
                hostFunctions: HostMap,
                libraries: [HostBuiltins.Create()]));

        Assert.Contains("Alpha", ex.Message);
        Assert.Contains("Beta", ex.Message);
        Assert.Contains("入口", ex.Message);
    }

    // ── 单文件兼容（回归）──────────────────────────────────────

    [Fact]
    public void SingleFile_ViaCompileFiles_Runs()
    {
        var (printed, vm) = CompileAndRun([(SingleMainPrints5, "one.st")]);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Equal(new long[] { 5 }, printed);
    }

    [Fact]
    public void SingleFile_ExistingCompile_StillRuns()
    {
        var result = IronRuntime.Compile(
            SingleMainPrints5,
            "one.st",
            hostFunctions: HostMap,
            libraries: [HostBuiltins.Create()]);

        var printed = new List<long>();
        var bridge = new HostBridge();
        bridge.Register(0, ctx =>
        {
            printed.Add(ctx.ReadReg(BytecodeBuilder.A0).IntValue);
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
        Assert.Equal(new long[] { 5 }, printed);
    }
}
