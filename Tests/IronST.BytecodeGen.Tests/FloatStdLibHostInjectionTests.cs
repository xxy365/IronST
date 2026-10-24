// FloatStdLibHostInjectionTests.cs — F13：IEC 浮点标准库函数走「用户自建宿主注入」路径
// （缺陷登记簿 §3.8 · 主缺陷表 F13 行）
//
// F13 的根因：IEC 浮点标准库函数（SQRT / SIN / LN / 浮点 ABS…）在 IR 层降级为
// C extern（sqrt / sin / log / fabs…），而字节码后端要求每个 extern 在
// `hostFunctions` 里登记 —— 缺失即抛 `InvalidOperationException: External function
// 'sqrt' has no host handler` ⇒ 内置实现不可用。
//
// 2026-10-22 用户裁定：**不内置实现**，改走「用户自建宿主注入」路径。
// 本文件为该路径提供**验收证据** —— 它证明这条路径**端到端可用**，而不是断言
// 「应该可用」。
//
// 路径要通，**两个**条件缺一不可：
//   1. 前端：外部库声明里要有该函数（`IECStandardLibrary.Create()`）—— 否则
//      前端先报 `E2006 未声明的函数 'SQRT'`，根本走不到后端；
//   2. 后端：`hostFunctions` 里登记 **extern 名**（`sqrt`，不是 ST 名 `SQRT`）
//      并提供 handler —— 后端是按 extern 名查表的。
using IronST.Frontend;
using IronST.Frontend.Analysis;
using IronST.LLVMCodeGen.CodeGen;
using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.BytecodeGen.Tests;

public class FloatStdLibHostInjectionTests
{
    /// <summary>
    /// 编译并运行，返回宿主 <c>Print</c> 读到的 <c>A0</c>。
    /// </summary>
    /// <param name="source">ST 源码。</param>
    /// <param name="hostFunctions">
    /// 后端 extern 名 → 宿主 ID 的映射（**extern 名**，如 <c>sqrt</c>，不是 <c>SQRT</c>）。
    /// </param>
    /// <param name="handlers">宿主 ID → handler。</param>
    private static BcValue CompileAndRun(
        string source,
        IReadOnlyDictionary<string, ushort> hostFunctions,
        IReadOnlyDictionary<ushort, Func<VMContext, BcValue>> handlers)
    {
        var analysis = FrontendPipeline.Analyze(
            source,
            "test.st",
            [HostBuiltins.Create(), IECStandardLibrary.Create()]);

        var errors = analysis.Diagnostics
            .Where(d => d.Severity == Common.Diagnostics.DiagnosticSeverity.Error)
            .ToList();
        Assert.True(
            analysis.Success,
            $"frontend failed with {errors.Count} error(s):\n"
                + string.Join("\n", errors.Select(e => $"  {e}")));

        var module = new IRGenerator().Generate(analysis);
        var result = new ByteCodeGenerator("test.st", hostFunctions: hostFunctions).Generate(module);

        BcValue? printed = null;
        var bridge = new HostBridge();
        bridge.Register(0, ctx =>
        {
            printed = ctx.ReadReg(BytecodeBuilder.A0);
            return BcValue.Void;
        });
        foreach (var (id, handler) in handlers)
            bridge.Register(id, ctx => handler(ctx));

        var vm = new VM(result.Module, new VMOptions
        {
            StackSize = 8192,
            HostBridge = bridge,
            StructTypes = result.StructRegistrations,
        });
        vm.Run();

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
        Assert.NotNull(printed);
        return printed!.Value;
    }

    /// <summary>
    /// <c>SQRT</c> 经用户自建宿主注入后端到端可用。
    /// </summary>
    /// <remarks>
    /// 修复前（无宿主 handler）：后端抛
    /// <c>InvalidOperationException: External function 'sqrt' has no host handler</c>。
    /// </remarks>
    [Fact]
    public void Sqrt_WorksWhenUserRegistersHostHandler()
    {
        var value = CompileAndRun(
            """
            PROGRAM Main
            VAR
                r : REAL;
            END_VAR
                r := SQRT(4.0);
                Print(r);
            END_PROGRAM
            """,
            new Dictionary<string, ushort>(StringComparer.Ordinal)
            {
                ["Print"] = 0,
                ["sqrt"] = 1,
            },
            new Dictionary<ushort, Func<VMContext, BcValue>>
            {
                [1] = ctx => new BcValue(Math.Sqrt(ctx.ReadReg(BytecodeBuilder.A0).FloatValue)),
            });

        Assert.Equal(ValueTag.Float64, value.Tag);
        Assert.Equal(2.0, value.FloatValue, 6);
    }

    /// <summary>
    /// 同一路径对 <c>SIN</c> / <c>LN</c> / 浮点 <c>ABS</c> 同样成立 ——
    /// 它们降级为同名 extern（<c>sin</c> / <c>log</c> / <c>fabs</c>）。
    /// </summary>
    [Theory]
    [InlineData("SQRT(4.0)", "sqrt", 2.0)]
    [InlineData("SIN(0.0)", "sin", 0.0)]
    [InlineData("LN(1.0)", "log", 0.0)]
    [InlineData("ABS(-3.5)", "fabs", 3.5)]
    public void StdlibFloatFunction_WorksWhenUserRegistersHostHandler(
        string stExpr,
        string externName,
        double expected)
    {
        var value = CompileAndRun(
            $"""
            PROGRAM Main
            VAR
                r : REAL;
            END_VAR
                r := {stExpr};
                Print(r);
            END_PROGRAM
            """,
            new Dictionary<string, ushort>(StringComparer.Ordinal)
            {
                ["Print"] = 0,
                [externName] = 1,
            },
            new Dictionary<ushort, Func<VMContext, BcValue>>
            {
                [1] = ctx =>
                {
                    var x = ctx.ReadReg(BytecodeBuilder.A0).FloatValue;
                    return new BcValue(externName switch
                    {
                        "sqrt" => Math.Sqrt(x),
                        "sin" => Math.Sin(x),
                        "log" => Math.Log(x),
                        "fabs" => Math.Abs(x),
                        _ => throw new InvalidOperationException(externName),
                    });
                },
            });

        Assert.Equal(ValueTag.Float64, value.Tag);
        Assert.Equal(expected, value.FloatValue, 6);
    }

    /// <summary>
    /// **反例（不得回归）**：不登记宿主 handler 时，后端必须**明确报错**，
    /// 而不是静默给出错值。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 失败点在**编译期**（<c>ByteCodeGenerator.Generate</c>），不是运行期：
    /// extern 调用走「extern 名 → <c>hostFunctions</c>」查表，查不到即抛
    /// <c>InvalidOperationException</c>，根本走不到 VM。
    /// </para>
    /// <para>
    /// 这条断言的意义在于：F13 的「不可用」是**显式失败**，不是静默错误。
    /// 若将来后端改成静默兜底（例如悄悄返回 0），本例会失败并暴露出来。
    /// </para>
    /// </remarks>
    [Fact]
    public void Sqrt_WithoutHostHandler_FailsExplicitly()
    {
        var analysis = FrontendPipeline.Analyze(
            """
            PROGRAM Main
            VAR
                r : REAL;
            END_VAR
                r := SQRT(4.0);
                Print(r);
            END_PROGRAM
            """,
            "test.st",
            [HostBuiltins.Create(), IECStandardLibrary.Create()]);
        Assert.True(analysis.Success);

        var module = new IRGenerator().Generate(analysis);

        // 只登记 Print，不登记 sqrt。
        var ex = Assert.ThrowsAny<Exception>(() =>
            new ByteCodeGenerator(
                "test.st",
                hostFunctions: new Dictionary<string, ushort>(StringComparer.Ordinal) { ["Print"] = 0 }
            ).Generate(module));

        Assert.Contains("sqrt", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("host handler", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
