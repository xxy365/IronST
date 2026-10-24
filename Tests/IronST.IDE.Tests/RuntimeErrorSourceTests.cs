// RuntimeErrorSourceTests.cs — IDE 运行期错误的源行号。
//
// 位置由 VM 单点回填：DebugViewModel 建 VM 时把 CompileResult.SourceMap 交给
// VMOptions.SourceLocator，异常 Message 里就带「（文件:行）」—— 输出窗口直接打印
// ex.Message 即可，不再有第二个查表点。
//
// 覆盖：
//   ① IDE 编译路径产出的真实 SourceMap 能把 PC 解析成「文件:行」；
//   ② 端到端：编译 → 真跑 VM → 错误消息带行号（与 DebugViewModel 同款接线）；
//   ③ 反向：不注入 locator 就只有 PC —— 证明行号确实来自这次注入；
//   ④ F5 调试路径：DebugVM + VMDebugSession 报出的 RuntimeErrorEvent 同样带行号。
using IronST.BytecodeGen;
using IronST.IDE.Services;
using IronST.Runtime;
using IronST.Runtime.Debug;
using IronST.Runtime.Shared;

namespace IronST.IDE.Tests;

public class RuntimeErrorSourceTests
{
    private const string MainPath = @"C:\proj\Main.st";

    private const string Source = """
        PROGRAM Main
        VAR g : ARRAY[0..99] OF DINT; i : DINT; END_VAR
        i := 1;
        g[999] := 1;
        END_PROGRAM
        """;

    /// <summary>与 DebugViewModel 同款接线：把映射交给 VM 的 SourceLocator。</summary>
    private static VM RunWithIdeWiring(CompileResult result) =>
        new(result.Module, new VMOptions
        {
            StackSize = 8192,
            StructTypes = result.StructRegistrations,
            SourceLocator = SourceResolver.FromMap(result.SourceMap),
        });

    [Fact]
    public void IdeSourceMap_ResolvesPcToFileAndLine()
    {
        var (result, errors) = ProjectCompileService.Compile(
            new[] { (Source, MainPath) },
            entryProgram: "Main",
            fallbackFileName: MainPath);

        Assert.NotNull(result);
        Assert.Empty(errors);
        Assert.NotEmpty(result!.SourceMap);

        var (pc, loc) = result.SourceMap.OrderBy(kv => kv.Key).First();

        Assert.Equal(loc, SourceResolver.Lookup(result.SourceMap, (uint)pc));
        Assert.Equal(MainPath, loc.file);
    }

    [Fact]
    public void IdeCompileThenRun_ReportsSourceLine()
    {
        var (result, errors) = ProjectCompileService.Compile(
            new[] { (Source, MainPath) },
            entryProgram: "Main",
            fallbackFileName: MainPath);
        Assert.NotNull(result);
        Assert.Empty(errors);

        var ex = Assert.Throws<RuntimeErrorException>(() => RunWithIdeWiring(result!).Run());

        Assert.Contains("[ArrayBounds]", ex.Message);
        Assert.Contains($"{MainPath}:4", ex.Message);   // g[999] := 1; 在第 4 行
    }

    [Fact]
    public void WithoutLocator_MessageStaysPcOnly()
    {
        var (result, _) = ProjectCompileService.Compile(
            new[] { (Source, MainPath) },
            entryProgram: "Main",
            fallbackFileName: MainPath);
        Assert.NotNull(result);

        var vm = new VM(result!.Module, new VMOptions
        {
            StackSize = 8192,
            StructTypes = result.StructRegistrations,
        });

        var ex = Assert.Throws<RuntimeErrorException>(() => vm.Run());

        Assert.Contains("[ArrayBounds]", ex.Message);
        Assert.Null(ex.SourceLocation);
        Assert.DoesNotContain(MainPath, ex.Message);
    }

    /// <summary>
    /// F5 调试路径：DebugVM（带 hooks）出错时不抛异常，而是停在 Paused 并把错误
    /// 交给 VMDebugSession —— 事件里的异常也必须带行号，否则调试窗口看到的
    /// 仍然只是 "at 0x1C"。
    /// </summary>
    [Fact]
    public void DebugSession_RuntimeErrorEvent_ReportsSourceLine()
    {
        var (result, errors) = ProjectCompileService.Compile(
            new[] { (Source, MainPath) },
            entryProgram: "Main",
            fallbackFileName: MainPath);
        Assert.NotNull(result);
        Assert.Empty(errors);

        var session = new VMDebugSession();
        RuntimeErrorEvent? received = null;
        session.OnDebugEvent += ev =>
        {
            if (ev is RuntimeErrorEvent err)
                received = err;
        };

        // 与 DebugViewModel.StartDebug 同款接线。
        var vm = new DebugVM(result!.Module, new VMDevOptions
        {
            DebugSession = session,
            StackSize = 8192,
            StructTypes = result.StructRegistrations,
            SourceLocator = SourceResolver.FromMap(result.SourceMap),
        });

        vm.Run();   // 有 hooks ⇒ 不抛，停在 Paused

        Assert.Equal(VMState.Paused, vm.State);
        Assert.NotNull(received);
        Assert.Contains("[ArrayBounds]", received!.Error.Message);
        Assert.Contains($"{MainPath}:4", received.Error.Message);
        Assert.Equal((MainPath, 4), received.Error.SourceLocation);
    }
}
