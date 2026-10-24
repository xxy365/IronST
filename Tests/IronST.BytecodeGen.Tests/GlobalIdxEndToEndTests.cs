// GlobalIdxEndToEndTests.cs — 0B 步骤 3 · 切片 3：生产路径的 GlobalIdx 记录
//
// 生产路径不走 BytecodeAssembler.GetGlobal/SetGlobal 的便捷 API：
// FunctionEmitter 直接发射 DebugInstr.Slot(OpCode.GETG/SETG, …)（:900 / :953）。
// 这里从 ST 源码整条跑完：FrontendPipeline → IRGenerator → ByteCodeGenerator，
// 确认发射点确实产出了重定位记录，且回填后的字节与记录一致。
//
// 源位置分两条路径验证（DebugViewModel.cs:550-561 的真实用法）：
//   • CLI 路径：IRGenerator 默认 NullDebugEmitter，一行不记 → 记录如实标 Unknown
//   • IDE 路径：IRDebugEmitter + BuildSourceMap 交给 Generate → 记录带文件名与行号
using IronST.LLVMCodeGen.CodeGen;
using IronST.LLVMCodeGen.Debug;
using IronST.Runtime.Shared;

namespace IronST.BytecodeGen.Tests;

public class GlobalIdxEndToEndTests
{
    private const string Source = """
        PROGRAM Main
        VAR_GLOBAL
            counter : INT;
            total : INT;
        END_VAR
        VAR
            i : INT;
        END_VAR
            counter := 1;
            total := counter + 2;
            i := counter + total;
        END_PROGRAM
        """;

    private static CompileResult Compile(string source)
    {
        var analysis = FrontendTestHelper.ExpectSuccess(source);
        var module = new IRGenerator().Generate(analysis);   // 默认 NullDebugEmitter
        return new ByteCodeGenerator("test.st").Generate(module);
    }

    /// <summary>IDE 路径：带 IRDebugEmitter，才有指令→行号映射。</summary>
    private static CompileResult CompileWithDebug(string source, string file)
    {
        var analysis = FrontendTestHelper.ExpectSuccess(source);
        var debugEmitter = new IRDebugEmitter();
        var module = new IRGenerator(debugEmitter).Generate(analysis);
        var irSourceMap = debugEmitter.BuildSourceMap(module, file);
        return new ByteCodeGenerator(file).Generate(module, irSourceMap: irSourceMap);
    }

    private static List<RelocationRecord> Globals(CompileResult result)
        => result.Relocations.Records
            .Where(r => r.Kind == RelocationKind.GlobalIdx)
            .ToList();

    [Fact]
    public void GlobalReferences_ProduceGlobalIdxRecords()
    {
        var globals = Globals(Compile(Source));

        Assert.NotEmpty(globals);
        Assert.Contains(globals, r => r.SymbolName == "counter");
        Assert.Contains(globals, r => r.SymbolName == "total");
        Assert.All(globals, r => Assert.Equal(16, r.EncodingWidthBits));
    }

    [Fact]
    public void GlobalIdxRecords_CarryDeclaredSlotInAddend()
    {
        var globals = Globals(Compile(Source));

        // 声明顺序决定局部槽位：counter = 0，total = 1
        Assert.All(globals, r => Assert.Equal(
            r.SymbolName == "counter" ? 0 : 1, r.Addend));
    }

    [Fact]
    public void GlobalIdxRecords_BackfilledBytesMatchRecord()
    {
        // 每条记录的 Addend（模块局部槽位）必须等于回填后该偏移处的 LE16
        var result = Compile(Source);
        var globals = Globals(result);

        Assert.NotEmpty(globals);
        foreach (var r in globals)
        {
            Assert.InRange(r.Offset, 0, result.Module.Code.Length - 2);
            Assert.Equal(r.Addend, BitConverter.ToInt16(result.Module.Code, r.Offset));
        }
    }

    [Fact]
    public void WithoutIrSourceMap_RecordsAreMarkedUnknown()
    {
        // CLI 路径的事实：IRGenerator 默认走 NullDebugEmitter，IR 指令不带行号，
        // 记录如实标 Unknown —— 不伪造位置。引用来源退化为代码段内偏移
        // （ApplyGlobalIdx 的诊断仍带 code offset）。
        var globals = Globals(Compile(Source));

        Assert.NotEmpty(globals);
        Assert.All(globals, r => Assert.True(r.Source.IsUnknown, r.ToString()));
    }

    [Fact]
    public void WithIrSourceMap_RecordsCarrySourceLines()
    {
        // IDE 路径：有行信息时，记录必须把它带出来（DG-1 的引用来源）
        var globals = Globals(CompileWithDebug(Source, "test.st"));

        Assert.NotEmpty(globals);
        Assert.All(globals, r =>
        {
            Assert.False(r.Source.IsUnknown);
            Assert.Equal("test.st", r.Source.File);
            Assert.True(r.Source.Line > 0, $"expected a real line, got {r.Source.Line}");
        });
    }

    [Fact]
    public void ProgramWithoutGlobals_ProducesNoGlobalIdxRecords()
    {
        var result = Compile("""
            FUNCTION Main : INT
            VAR
                i : INT;
            END_VAR
                i := 1;
                Main := i;
            END_FUNCTION
            """);

        Assert.DoesNotContain(result.Relocations.Records,
            r => r.Kind == RelocationKind.GlobalIdx);
    }
}
