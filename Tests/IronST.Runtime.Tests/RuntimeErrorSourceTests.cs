// RuntimeErrorSourceTests.cs — 运行期错误的源位置：解析器 + VM 单点回填。
//
// 设计要点（见 RuntimeError.cs / SourceResolver.cs）：
//   · RuntimeErrorException 自带 SourceLocation，Message 里拼出「（文件:行）」；
//   · 位置由 VM 在**唯一**边界回填一次（RunLoop catch 补 PC → Run catch 查表），
//     所以 CLI / IDE / 示例宿主全都只是打印 ex.Message，不再各自查表；
//   · 组件（StructHeap / BcValue / VMContext）不持有 VM，抛的异常 PC 为 0
//     （"未知"），由指令边界补上当前指令的 PC。
using IronST.Runtime.Shared;

namespace IronST.Runtime.Tests;

public class RuntimeErrorSourceTests
{
    // ══════════════════════════════════════════════════════════
    //  SourceResolver：PC → (文件, 行)
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void FromMap_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(SourceResolver.FromMap(null));
        Assert.Null(SourceResolver.FromMap(new Dictionary<int, (string, int)>()));
    }

    [Fact]
    public void FromMap_NonEmpty_ResolvesByNearestKeyAtOrBelowPc()
    {
        var map = new Dictionary<int, (string file, int line)>
        {
            [0x10] = ("Main.st", 3),
            [0xE0] = ("Main.st", 37),
            [0x120] = ("Main.st", 40),
        };

        var locate = SourceResolver.FromMap(map)!;

        Assert.Equal(("Main.st", 3), locate(0x10));
        Assert.Equal(("Main.st", 37), locate(0xE2));   // 两条映射之间 → 取 ≤ PC 的最近一条
        Assert.Equal(("Main.st", 40), locate(0x120));
    }

    [Fact]
    public void Lookup_NoEntryAtOrBelowPc_ReturnsNull()
    {
        var map = new Dictionary<int, (string file, int line)> { [0x100] = ("Main.st", 9) };

        Assert.Null(SourceResolver.Lookup(map, 0));
        Assert.Null(SourceResolver.Lookup(map, 0xFF));
    }

    /// <summary>链接只平移指令地址、不改指令边界 ⇒ 源映射整体平移即可对齐。</summary>
    [Fact]
    public void Shift_AddsCodeBaseToEveryPc()
    {
        var shifted = SourceResolver.Shift(
            new Dictionary<int, (string file, int line)>
            {
                [0x00] = ("a.st", 1),
                [0x08] = ("a.st", 2),
            },
            codeBase: 0x40);

        Assert.Equal(("a.st", 1), shifted[0x40]);
        Assert.Equal(("a.st", 2), shifted[0x48]);
        Assert.Equal(2, shifted.Count);
    }

    [Fact]
    public void Merge_ShiftsEachPartByItsOwnCodeBase()
    {
        var merged = SourceResolver.Merge(new[]
        {
            (0x40, (IReadOnlyDictionary<int, (string file, int line)>)
                new Dictionary<int, (string file, int line)> { [0x00] = ("a.st", 1) }),
            (0x80, (IReadOnlyDictionary<int, (string file, int line)>)
                new Dictionary<int, (string file, int line)> { [0x04] = ("b.st", 7) }),
        });

        Assert.Equal(("a.st", 1), merged[0x40]);
        Assert.Equal(("b.st", 7), merged[0x84]);
    }

    // ══════════════════════════════════════════════════════════
    //  VM：位置回填
    // ══════════════════════════════════════════════════════════

    /// <summary>NOP 占 1 字节 ⇒ IDIV 落在 PC=1，走"抛出点自带 PC"这条路径。</summary>
    private static BytecodeModule DivideByZeroAtPc1() =>
        VMTestHelper.Build(b =>
        {
            b.NOP();
            b.IDIV(4, 4, 5);   // R5 未初始化 ⇒ IntValue == 0
        });

    [Fact]
    public void Vm_BakesLocationIntoMessage()
    {
        var vm = new VM(
            DivideByZeroAtPc1(),
            new VMOptions
            {
                StackSize = 4096,
                SourceLocator = SourceResolver.FromMap(
                    new Dictionary<int, (string file, int line)> { [1] = ("Main.st", 7) }),
            });

        var ex = Assert.Throws<RuntimeErrorException>(() => vm.Run());

        Assert.Equal(RuntimeErrorType.DivideByZero, ex.ErrorType);
        Assert.Equal(1u, ex.PC);
        Assert.Equal(("Main.st", 7), ex.SourceLocation);
        Assert.Contains("（Main.st:7）", ex.Message);
    }

    [Fact]
    public void Vm_BakesLocationIntoLastError_WhenHooksAttached()
    {
        var vm = new HooksVm(
            DivideByZeroAtPc1(),
            new VMOptions
            {
                StackSize = 4096,
                SourceLocator = SourceResolver.FromMap(
                    new Dictionary<int, (string file, int line)> { [1] = ("Main.st", 7) }),
            },
            new RecordingHooks());

        vm.Run();   // 有钩子 ⇒ 不抛，停在 Paused 并记 LastError

        Assert.Equal(VMState.Paused, vm.State);
        Assert.Equal(("Main.st", 7), vm.LastError!.SourceLocation);
        Assert.Contains("（Main.st:7）", vm.LastError.Message);
    }

    /// <summary>
    /// 组件（StructHeap）抛的异常 PC 为 0；指令边界必须补上当前指令 PC，否则
    /// "abc" 这类转换错误、未注册结构体错误永远没有行号。
    /// </summary>
    [Fact]
    public void Vm_AdoptsCurrentPc_WhenRaiserDoesNotKnowIt()
    {
        var module = VMTestHelper.Build(b =>
        {
            b.NOP();
            b.SNEW(4, 9, 2);   // typeId 9 未注册 → StructHeap 抛 pc=0 的异常
        });

        uint? askedAbout = null;
        var vm = new VM(
            module,
            new VMOptions
            {
                StackSize = 4096,
                SourceLocator = pc =>
                {
                    askedAbout = pc;
                    return ("lib.st", 42);
                },
            });

        var ex = Assert.Throws<RuntimeErrorException>(() => vm.Run());

        Assert.Equal(RuntimeErrorType.TypeError, ex.ErrorType);
        Assert.Equal(1u, askedAbout!.Value);   // SNEW 在 PC=1，不是 0
        Assert.Equal(1u, ex.PC);
        Assert.Contains("（lib.st:42）", ex.Message);
    }

    [Fact]
    public void WithoutLocator_MessageStaysPcOnly()
    {
        var vm = new VM(DivideByZeroAtPc1(), new VMOptions { StackSize = 4096 });

        var ex = Assert.Throws<RuntimeErrorException>(() => vm.Run());

        Assert.Null(ex.SourceLocation);
        Assert.Equal("[DivideByZero] at 0x1: Divide by zero", ex.Message);
    }

    /// <summary>空映射 = 没有可用位置 ⇒ 与"完全没注入"逐字一致，不伪造位置。</summary>
    [Fact]
    public void EmptyMap_BehavesAsNoLocator()
    {
        var vm = new VM(
            DivideByZeroAtPc1(),
            new VMOptions
            {
                StackSize = 4096,
                SourceLocator = SourceResolver.FromMap(new Dictionary<int, (string, int)>()),
            });

        var ex = Assert.Throws<RuntimeErrorException>(() => vm.Run());

        Assert.Null(ex.SourceLocation);
        Assert.Equal("[DivideByZero] at 0x1: Divide by zero", ex.Message);
    }

    private sealed class RecordingHooks : IVMDebugHooks
    {
        public bool PreStep(VMBase vm, uint pc) => true;
        public void OnError(RuntimeErrorException ex) { }
        public void OnBreak(VMBase vm, uint pc) { }
    }

    /// <summary>带钩子的 VM（等价于 Runtime.Debug 的 DebugVM，测试里不必依赖那个项目）。</summary>
    private sealed class HooksVm : VMBase
    {
        public HooksVm(BytecodeModule module, VMOptions options, IVMDebugHooks hooks)
            : base(module, options, hooks) { }
    }
}
