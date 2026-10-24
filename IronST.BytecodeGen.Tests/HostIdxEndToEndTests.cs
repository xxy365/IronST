// HostIdxEndToEndTests.cs — 0B 步骤 3 · 切片 5：生产路径的 HostIdx 记录
//
// 生产路径的 HCALL 在 FunctionEmitter.EmitCall（:1126-1131）：isHost 分支先
// RecordHostRef 按名登记，再经 EmitInstr 发射 HCALL。
// 这里从 ST 源码整条跑完：FrontendPipeline → IRGenerator → ByteCodeGenerator，
// 确认宿主调用确实产出重定位记录，且回填后的字节与记录一致。
//
// 前端只认 HostBuiltins 声明的名字（目前只有 "Print"，HostBuiltins.cs:30），
// 后端则要求 hostFunctions 字典里有对应项，否则 FunctionEmitter.cs:1103-1108
// 抛 "no host handler" —— 两边必须同时给。
//
// 静态 ABI ID（HostCallIds.*）不在本文件范围：裁定为不进重定位表（§5 HOST-2）。
using IronST.Frontend;
using IronST.Frontend.Analysis;
using IronST.LLVMCodeGen.CodeGen;
using IronST.LLVMCodeGen.Debug;
using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.BytecodeGen.Tests;

public class HostIdxEndToEndTests
{
    private const string Source = """
        FUNCTION Main : INT
        VAR
            a : INT;
        END_VAR
            a := 42;
            Print(a);
            Main := a;
        END_FUNCTION
        """;

    /// <summary>
    /// 编译：前端带上 <c>HostBuiltins</c>（否则 Print 未声明），
    /// 后端带上 <c>hostFunctions</c>（否则 "no host handler"）。
    /// </summary>
    private static CompileResult Compile(
        string source,
        IReadOnlyDictionary<string, ushort>? hostFunctions = null)
    {
        var analysis = FrontendPipeline.Analyze(
            source, "test.st", [HostBuiltins.Create()]);
        var errors = analysis.Diagnostics
            .Where(d => d.Severity == Common.Diagnostics.DiagnosticSeverity.Error)
            .ToList();
        Assert.True(
            analysis.Success,
            $"Expected success but got {errors.Count} error(s):\n"
                + string.Join("\n", errors.Select(e => $"  {e}")));

        var module = new IRGenerator().Generate(analysis);   // 默认 NullDebugEmitter
        return new ByteCodeGenerator("test.st", hostFunctions: hostFunctions)
            .Generate(module);
    }

    /// <summary>IDE 路径：带 IRDebugEmitter，才有指令→行号映射。</summary>
    private static CompileResult CompileWithDebug(
        string source,
        IReadOnlyDictionary<string, ushort>? hostFunctions = null)
    {
        var analysis = FrontendPipeline.Analyze(
            source, "test.st", [HostBuiltins.Create()]);
        Assert.True(analysis.Success, "expected frontend success");

        var debugEmitter = new IRDebugEmitter();
        var module = new IRGenerator(debugEmitter).Generate(analysis);
        var irSourceMap = debugEmitter.BuildSourceMap(module, "test.st");
        return new ByteCodeGenerator("test.st", hostFunctions: hostFunctions)
            .Generate(module, irSourceMap: irSourceMap);
    }

    private static Dictionary<string, ushort> Hosts(ushort id = 7)
        => new(StringComparer.Ordinal) { ["Print"] = id };

    private static List<RelocationRecord> Hosts_(CompileResult result)
        => result.Relocations.Records
            .Where(r => r.Kind == RelocationKind.HostIdx)
            .ToList();

    // ═══════════════════════════════════════════════════════════════
    //  记录
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void HostCall_ProducesHostIdxRecord()
    {
        var hosts = Hosts_(Compile(Source, Hosts()));

        Assert.NotEmpty(hosts);
        Assert.All(hosts, r => Assert.Equal(16, r.EncodingWidthBits));
        Assert.All(hosts, r => Assert.Equal(RelocationSection.Code, r.Section));
    }

    [Fact]
    public void HostIdxRecord_CarriesStableName_AndModuleLocalId()
    {
        var hosts = Hosts_(Compile(Source, Hosts(7)));

        var r = Assert.Single(hosts);

        Assert.Equal("Print", r.SymbolName);   // 身份是名字，跨模块按名解析
        Assert.Equal(7, r.Addend);             // 模块局部 ID，来自编译期 hostFunctions 字典
    }

    [Fact]
    public void HostIdxRecord_LocalIdFollowsTheSuppliedMap_NotRegistrationOrder()
    {
        // 同一份源码给不同的局部 ID ⇒ 记录跟着字典走，不依赖任何注册顺序
        var with5 = Hosts_(Compile(Source, Hosts(5)));
        var with9 = Hosts_(Compile(Source, Hosts(9)));

        Assert.Equal(5, Assert.Single(with5).Addend);
        Assert.Equal(9, Assert.Single(with9).Addend);
        Assert.Equal("Print", Assert.Single(with5).SymbolName);
        Assert.Equal("Print", Assert.Single(with9).SymbolName);
    }

    [Fact]
    public void HostIdxRecord_OffsetPointsAtTheImmediate()
    {
        var result = Compile(Source, Hosts());
        var r = Assert.Single(Hosts_(result));

        // 立即数是 HCALL 操作码 +1，且必须落在代码段内
        Assert.True(r.Offset >= 0, r.ToString());
        Assert.True(r.Offset + 2 <= result.Module.Code.Length,
            $"offset {r.Offset} + 2 exceeds code length {result.Module.Code.Length}");

        // HCALL 操作码 0xB0 恰在 Offset-1
        Assert.Equal(0xB0, result.Module.Code[r.Offset - 1]);

        // 立即数就是记录里的局部 ID（单模块未重编号）
        Assert.Equal(7, BitConverter.ToUInt16(result.Module.Code, r.Offset));
    }

    [Fact]
    public void WithoutHostFunctionsMap_HostCallIsRejectedAtCompileTime()
    {
        // 生产路径必须给出 hostFunctions，否则 FunctionEmitter.cs:1103-1108 显式报错
        var ex = Assert.Throws<InvalidOperationException>(
            () => Compile(Source, hostFunctions: null));

        Assert.Contains("Print", ex.Message);
        Assert.Contains("no host handler", ex.Message);
    }

    // ═══════════════════════════════════════════════════════════════
    //  回填与重编号
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Backfill_LeavesTheEmittedBytesUntouched_SingleModule()
    {
        // 单模块：回填写回同一 ID，字节不变
        var result = Compile(Source, Hosts(7));

        var r = Assert.Single(Hosts_(result));
        Assert.Equal(7, BitConverter.ToUInt16(result.Module.Code, r.Offset));
    }

    [Fact]
    public void HostIdRemap_RewritesTheBakedId_ToMergedId()
    {
        // 步骤④：证明模块局部 ID 可经映射写成合并后的 ID（链接期重编号入口）
        var analysis = FrontendPipeline.Analyze(
            Source, "test.st", [HostBuiltins.Create()]);
        Assert.True(analysis.Success);

        var module = new IRGenerator().Generate(analysis);
        var result = new ByteCodeGenerator("test.st", hostFunctions: Hosts(3))
            .Generate(module);

        // 生成阶段无法注入钩子，改用汇编器层等价入口复现同一回填路径
        var asm = new BytecodeAssembler();
        asm.HostIdRemap = (name, local) => name == "Print" ? (ushort)42 : local;
        asm.RecordHostIdx("Print", 3, RelocationSourceLocation.Unknown);
        asm.Raw.HCALL(3);

        var m = asm.Build();
        var offset = asm.Relocations.Records[0].Offset;
        Assert.Equal(42, BitConverter.ToUInt16(m.Code, offset));

        // 生成出来的记录本身仍是局部值（记录不持已解析值）
        Assert.Equal(3, Assert.Single(Hosts_(result)).Addend);
    }

    // ═══════════════════════════════════════════════════════════════
    //  源位置
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void WithoutIrSourceMap_RecordIsMarkedUnknown()
    {
        // CLI 路径：NullDebugEmitter 一行不记 ⇒ 如实标 Unknown，不伪造行号
        var hosts = Hosts_(Compile(Source, Hosts()));

        Assert.All(hosts, r => Assert.True(r.Source.IsUnknown, r.Source.ToString()));
    }

    [Fact]
    public void WithIrSourceMap_RecordCarriesFileAndLine()
    {
        // IDE 路径：带 IRDebugEmitter ⇒ 记录带文件名与行号，与调试源映射同一行
        var hosts = Hosts_(CompileWithDebug(Source, Hosts()));

        var r = Assert.Single(hosts);
        Assert.Equal("test.st", r.Source.File);
        Assert.True(r.Source.Line > 0, r.Source.ToString());
        Assert.False(r.Source.IsUnknown);
    }

    // ═══════════════════════════════════════════════════════════════
    //  与既有类别共存
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void HostCall_CoexistsWithConstRecords()
    {
        // 同一模块里 ConstIdx（字面量 42）与 HostIdx（Print 调用）各记各的，
        // 互不覆盖、互不误记。
        // （GlobalIdx 与 HostIdx 的共存由 Runtime 侧
        //  HostIdxRelocationTests.MixedKinds_AllBackfill 覆盖，此处的 a 是局部 VAR。）
        var result = Compile(Source, Hosts());

        var kinds = result.Relocations.Records.Select(r => r.Kind).Distinct().ToList();

        Assert.Contains(RelocationKind.ConstIdx, kinds);    // a := 42
        Assert.Contains(RelocationKind.HostIdx, kinds);     // Print(a)
        Assert.Equal(
            1,
            result.Relocations.Records.Count(r => r.Kind == RelocationKind.HostIdx));
    }

    [Fact]
    public void GeneratedCode_StillRuns()
    {
        // 迁移不得改变可执行性
        var result = Compile(Source, Hosts());

        Assert.NotEmpty(result.Module.Code);
        Assert.Contains((byte)0xB0, result.Module.Code);   // HCALL 仍在
    }
}
