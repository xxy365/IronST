// ConstIdxEndToEndTests.cs — 0B 步骤 3 · 切片 4：生产路径的 ConstIdx 记录
//
// 生产路径的 LDC 在 FunctionEmitter.EmitToAssembler 的 LDC 分支（:397-405）：
// 先 AddString / AddConst 追加常量池，再经 BytecodeAssembler.EmitLdc 统一收口发射。
// 这里从 ST 源码整条跑完：FrontendPipeline → IRGenerator → ByteCodeGenerator，
// 确认字面量确实产出重定位记录，且回填后的字节与记录一致。
//
// 源位置分两条路径验证（与 GlobalIdx 切片同一口径）：
//   • CLI 路径：IRGenerator 默认 NullDebugEmitter，一行不记 → 记录如实标 Unknown
//   • IDE 路径：IRDebugEmitter + BuildSourceMap 交给 Generate → 记录带文件名与行号
using IronST.LLVMCodeGen.CodeGen;
using IronST.LLVMCodeGen.Debug;
using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.BytecodeGen.Tests;

public class ConstIdxEndToEndTests
{
    private const string Source = """
        FUNCTION Main : INT
        VAR
            a : INT;
            b : REAL;
            c : INT;
        END_VAR
            a := 42;
            b := 3.5;
            c := 42;
            Main := a + c;
        END_FUNCTION
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

    private static List<RelocationRecord> Consts(CompileResult result)
        => result.Relocations.Records
            .Where(r => r.Kind == RelocationKind.ConstIdx)
            .ToList();

    [Fact]
    public void LiteralReferences_ProduceConstIdxRecords()
    {
        var consts = Consts(Compile(Source));

        Assert.NotEmpty(consts);
        // 字面量 42 出现两次、3.5 一次；池不去重，故 42 占两个局部索引
        Assert.True(consts.Count >= 3,
            $"expected at least 3 const refs, got {consts.Count}");
        Assert.All(consts, r => Assert.Equal(32, r.EncodingWidthBits));
    }

    [Fact]
    public void ConstIdxRecords_CarryLocalIndex_AndNoSymbolName()
    {
        var consts = Consts(Compile(Source));

        Assert.All(consts, r =>
        {
            Assert.Null(r.SymbolName);          // 常量无符号身份
            Assert.True(r.Addend >= 0, r.ToString());
        });
    }

    [Fact]
    public void ConstIdxRecords_BackfilledBytesMatchRecord()
    {
        // 每条记录的 Addend 必须等于回填后该偏移处的 LE32
        var result = Compile(Source);
        var consts = Consts(result);

        Assert.NotEmpty(consts);
        foreach (var r in consts)
        {
            Assert.InRange(r.Offset, 0, result.Module.Code.Length - 4);
            Assert.Equal(r.Addend, BitConverter.ToInt32(result.Module.Code, r.Offset));
        }
    }

    [Fact]
    public void ConstIdxRecords_WithinOwnConstantPool()
    {
        // 单模块回填不设重映射，索引必然落在本模块池内
        var result = Compile(Source);
        var consts = Consts(result);

        Assert.NotEmpty(consts);
        Assert.All(consts, r => Assert.True(
            r.Addend < result.Module.ConstantPool.Length,
            $"index {r.Addend} vs pool {result.Module.ConstantPool.Length}"));
    }

    [Fact]
    public void WithIrSourceMap_RecordsCarrySourceLines()
    {
        // IDE 路径：函数体内的字面量必须把源行带出来。
        //
        // 注意：并非所有 LDC 都有源行 —— 序言/地址建立期发出的常量
        // （FunctionEmitter.cs:105 显式清空 _currentSrc）不属于任何源行，
        // 如实记为 Unknown，不伪造。故这里断言的是"有源的必须对"，
        // 而非"全部都有源"。
        var result = CompileWithDebug(Source, "test.st");
        var consts = Consts(result);

        var withSource = consts.Where(r => !r.Source.IsUnknown).ToList();

        Assert.True(withSource.Count >= 3,
            $"expected the 3 body literals to carry source, got {withSource.Count}");
        Assert.All(withSource, r =>
        {
            Assert.Equal("test.st", r.Source.File);
            Assert.True(r.Source.Line > 0, $"expected a real line, got {r.Source.Line}");
        });

        // 三个字面量 42 / 3.5 / 42 分别落在源码第 7、8、9 行
        Assert.Contains(withSource, r => r.Source.Line == 7);
        Assert.Contains(withSource, r => r.Source.Line == 8);
        Assert.Contains(withSource, r => r.Source.Line == 9);
    }

    [Fact]
    public void WithoutIrSourceMap_RecordsAreMarkedUnknown_EvenForBodyLiterals()
    {
        // CLI 路径：无 IR 源映射时连函数体字面量也没有源行 —— 不伪造
        var consts = Consts(Compile(Source));

        Assert.NotEmpty(consts);
        Assert.All(consts, r => Assert.True(r.Source.IsUnknown, r.ToString()));
    }

    [Fact]
    public void RepeatedLiteral_GetsDistinctIndices_NotDeduplicated()
    {
        // §3.3 REL-4 已冻结：首版追加合并、不去重。
        // 源码里 42 出现两次（第 7 行与第 9 行），必须占两个局部索引。
        var result = CompileWithDebug(Source, "test.st");

        var fortyTwo = result.Relocations.Records
            .Where(r => r.Kind == RelocationKind.ConstIdx)
            .Where(r => r.Addend < result.Module.ConstantPool.Length
                && result.Module.ConstantPool[r.Addend].AsInt64() == 42L)
            .ToList();

        Assert.Equal(2, fortyTwo.Count);
        Assert.Equal(2, fortyTwo.Select(r => r.Addend).Distinct().Count());
        Assert.Equal(new[] { 7, 9 }, fortyTwo.Select(r => r.Source.Line).OrderBy(l => l).ToArray());
    }

    [Fact]
    public void GeneratedCode_StillRuns()
    {
        // 收口改造不得改变运行时行为：LDC 仍从常量池取到正确条目
        var analysis = FrontendTestHelper.ExpectSuccess("""
            FUNCTION Main : INT
            VAR
                a : INT;
            END_VAR
                a := 42;
                Main := a;
            END_FUNCTION
            """);
        var module = new IRGenerator().Generate(analysis);
        var result = new ByteCodeGenerator("test.st").Generate(module);

        // 字面量确实走了 ConstIdx 收口
        Assert.Contains(result.Relocations.Records,
            r => r.Kind == RelocationKind.ConstIdx);

        var vm = new VM(result.Module, new VMOptions
        {
            StackSize = 8192,
            StructTypes = result.StructRegistrations,
        });
        vm.Run();

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }
}
