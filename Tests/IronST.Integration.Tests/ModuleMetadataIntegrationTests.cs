using IronST.BytecodeGen;
using IronST.Frontend;
using IronST.Frontend.Analysis;
using IronST.LLVMCodeGen.CodeGen;
using IronST.Runtime;
using IronST.Runtime.Shared;
using IronST.Runtime.Shared.Modules;

namespace IronST.Integration.Tests;

/// <summary>
/// `.stmod` 的端到端覆盖：真实 ST 编译产物 → 写自定义元数据 → 读写往返 →
/// 按函数名装载 → C# 设置参数执行。
/// </summary>
public class ModuleMetadataIntegrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "ironst-integration-tests",
        Guid.NewGuid().ToString("N"));

    public ModuleMetadataIntegrationTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // 临时目录清理失败不影响测试结果。
        }
    }

    [Fact]
    public void CustomMetadata_RoundTrips_AndFunctionCanBeInvokedByName()
    {
        const string source = """
            FUNCTION Add : DINT
            VAR_INPUT
                a : DINT;
                b : DINT;
            END_VAR
                Add := a + b;
            END_FUNCTION
            """;

        STModModule compiled = CompileModule(source, entryName: "Add", moduleName: "math");

        // 模拟调用方在编译产物上追加自定义元数据。
        var metadata = new Dictionary<string, STModMetadataEntry>(compiled.Metadata)
        {
            [STModMetadataKeys.ModuleAuthor] = STModMetadataEntry.FromString(
                STModMetadataKeys.ModuleAuthor, "integration-test"),
            ["mykey"] = STModMetadataEntry.FromString("mykey", "myvalue"),
        };

        var withMetadata = CopyWithMetadata(compiled, metadata);
        string path = Path.Combine(_dir, "math.stmod");
        STModWriter.Save(path, withMetadata);

        // 从磁盘重新读取，确认元数据和函数索引都保留。
        STModModule readBack = STModReader.Load(path);

        Assert.Equal("integration-test",
            readBack.Metadata[STModMetadataKeys.ModuleAuthor].AsString());
        Assert.Equal("myvalue", readBack.Metadata["mykey"].AsString());

        STModFunctionEntry add = Assert.Single(readBack.Functions, f => f.Name == "Add");
        Assert.Equal(2u, add.ParamCount);
        Assert.False(add.IsDeclaration);
        Assert.Contains("i32", add.Signature);

        // C# 按名调用：前两个标量参数写 A0/A1，返回值从 A0 读取。
        STModInvocationResult result = STModInvoker.Invoke(
            readBack, "Add", new BcValue(2L), new BcValue(3L));

        Assert.Equal(VMState.Halted, result.State);
        Assert.Null(result.Error);
        Assert.Equal(5, result.ReturnValue.IntValue);
    }

    [Fact]
    public void Invoker_PassesExtraScalarArgumentsOnStack()
    {
        const string source = """
            FUNCTION Sum6 : DINT
            VAR_INPUT
                a : DINT;
                b : DINT;
                c : DINT;
                d : DINT;
                e : DINT;
                f : DINT;
            END_VAR
                Sum6 := a * 100000 + b * 10000 + c * 1000 + d * 100 + e * 10 + f;
            END_FUNCTION
            """;

        STModModule module = CompileModule(source, entryName: "Sum6", moduleName: "sum");

        STModInvocationResult result = STModInvoker.Invoke(
            module,
            "Sum6",
            new BcValue(1L),
            new BcValue(2L),
            new BcValue(3L),
            new BcValue(4L),
            new BcValue(5L),
            new BcValue(6L));

        Assert.Equal(VMState.Halted, result.State);
        Assert.Null(result.Error);
        Assert.Equal(123456, result.ReturnValue.IntValue);
    }

    [Fact]
    public void Invoker_IntegerMod_UsesBuiltinMathHandler()
    {
        const string source = """
            FUNCTION Remainder : DINT
            VAR_INPUT
                dividend : DINT;
                divisor : DINT;
            END_VAR
                Remainder := dividend MOD divisor;
            END_FUNCTION
            """;

        STModModule module = CompileModule(source, entryName: "Remainder", moduleName: "mod");

        STModInvocationResult result = STModInvoker.Invoke(
            module,
            "Remainder",
            new BcValue(-17L),
            new BcValue(5L));

        Assert.Equal(VMState.Halted, result.State);
        Assert.Null(result.Error);
        Assert.Equal(-2, result.ReturnValue.IntValue);
    }

    private static STModModule CompileModule(
        string source,
        string entryName,
        string moduleName)
    {
        var analysis = FrontendPipeline.Analyze(
            source,
            $"{moduleName}.st",
            [HostBuiltins.Create(), IECStandardLibrary.CreateStringFunctions()]);

        if (!analysis.Success) {
            throw new InvalidOperationException(
                "frontend failed:\n"
                + string.Join("\n", analysis.Diagnostics.Select(d => $"  {d}")));
        }

        var ir = new IRGenerator().Generate(analysis);
        CompileResult compiled = new ByteCodeGenerator($"{moduleName}.st")
            .Generate(ir, entryFunctionName: entryName);

        var functions = compiled.FunctionLayout
            .Select(e => new STModFunctionEntry
            {
                FunctionId = e.FunctionId,
                Flags =
                    (e.IsExported ? STModFunctionFlags.Exported : STModFunctionFlags.None)
                    | (e.IsDeclaration ? STModFunctionFlags.Declaration : STModFunctionFlags.None)
                    | (e.IsEntryPoint ? STModFunctionFlags.EntryPoint : STModFunctionFlags.None),
                Name = e.Name,
                Signature = e.Signature,
                ParamCount = (uint)Math.Max(0, e.ParamCount),
                LocalCount = (uint)Math.Max(0, e.LocalCount),
                MaxStack = 0,
                CodeOffset = (ulong)e.StartPC,
                CodeLength = (ulong)e.CodeLength,
            })
            .ToList();

        var exports = compiled.FunctionLayout
            .Where(e => e.IsExported && !e.IsDeclaration)
            .Select(e => new STModSymbol
            {
                Name = e.Name,
                Signature = e.Signature,
                Kind = STModSymbolKind.Function,
                Flags = STModSymbolFlags.Exported,
                FunctionId = e.FunctionId,
            })
            .ToList();

        var metadata = new Dictionary<string, STModMetadataEntry>(StringComparer.Ordinal)
        {
            [STModMetadataKeys.ModuleName] = STModMetadataEntry.FromString(
                STModMetadataKeys.ModuleName, moduleName),
            [STModMetadataKeys.ModuleVersion] = STModMetadataEntry.FromString(
                STModMetadataKeys.ModuleVersion, "1.0.0"),
            [STModMetadataKeys.CompilerVersion] = STModMetadataEntry.FromString(
                STModMetadataKeys.CompilerVersion, "integration-test"),
            [STModMetadataKeys.EntryPC] = STModMetadataEntry.FromUInt32(
                STModMetadataKeys.EntryPC, compiled.Module.EntryPC),
            [STModMetadataKeys.EntryFunction] = STModMetadataEntry.FromString(
                STModMetadataKeys.EntryFunction, entryName),
            [STModMetadataKeys.ModuleLinked] = STModMetadataEntry.FromBoolean(
                STModMetadataKeys.ModuleLinked, true),
            [STModMetadataKeys.GlobalCount] = STModMetadataEntry.FromUInt32(
                STModMetadataKeys.GlobalCount, (uint)Math.Max(0, compiled.Module.GlobalCount)),
        };

        return new STModModule
        {
            Metadata = metadata,
            Functions = functions,
            Code = compiled.Module.Code,
            ConstantPool = compiled.Module.ConstantPool,
            Sections = [],
            HasEntryPoint = compiled.Module.Code.Length > 0,
            EntryPC = compiled.Module.EntryPC,
            Relocations = compiled.Relocations,
            Imports = [],
            Exports = exports,
            IsLinked = true,
            GlobalCount = compiled.Module.GlobalCount,
        };
    }

    private static STModModule CopyWithMetadata(
        STModModule module,
        IReadOnlyDictionary<string, STModMetadataEntry> metadata)
        => new()
        {
            Metadata = metadata,
            Functions = module.Functions,
            Code = module.Code,
            ConstantPool = module.ConstantPool,
            Sections = module.Sections,
            HasEntryPoint = module.HasEntryPoint,
            EntryPC = module.EntryPC,
            Relocations = module.Relocations,
            Imports = module.Imports,
            Exports = module.Exports,
            IsLinked = module.IsLinked,
            GlobalCount = module.GlobalCount,
            FormatVersionMajor = module.FormatVersionMajor,
            FormatVersionMinor = module.FormatVersionMinor,
        };
}
