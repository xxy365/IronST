// FBTestSupport.cs — FB 实例寻址 / VAR_INPUT 保留 端到端测试的共享辅助。
//
// 观测通道与取证探针 `%TEMP%\opencode\fbsem\` 同路：
// · 宿主函数 `Print`（HostBuiltins + hostFunctions id 0）把表达式值回传到 A0；
// · 调用方可见性另用越界探测器 `g[999] := n`（命中即触发 ArrayBounds ⇒ Run 抛异常）。
using IronST.Frontend;
using IronST.Frontend.Analysis;
using IronST.LLVMCodeGen.CodeGen;
using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.BytecodeGen.Tests;

internal static class FBTestSupport
{
    /// <summary>编译并以宿主 `Print` 回传 A0（与探针 harness 同路）。</summary>
    public static (List<long> printed, VM vm) Run(string source)
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

    /// <summary>运行并返回 VM；未触发任何 trap 时 <c>State == Halted</c> 且 <c>LastError == null</c>。</summary>
    public static VM RunHalted(string source)
    {
        var (_, vm) = Run(source);
        Assert.Null(vm.LastError);
        return vm;
    }
}
