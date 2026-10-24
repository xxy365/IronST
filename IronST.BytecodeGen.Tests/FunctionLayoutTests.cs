// FunctionLayoutTests.cs — 真实编译产物的函数布局 ↔ .stmod 函数索引一致性。
//
// 判据来源：BytecodeAssembler.DefFunc 在发射完 FRAME … LEAVE RET 之后登记的
// (StartPC, EndPC)。本文件用**真实编译**（FrontendPipeline → IRGenerator →
// ByteCodeGenerator）验证：
//   1) 每个函数范围的边界字节就是 FRAME / LEAVE / RET —— 不是按函数排列顺序推测；
//   2) 范围互不重叠且落在 Code 内；
//   3) 模块写出再读回后，函数索引的 CodeOffset/CodeLength 与真实边界逐字节吻合；
//   4) 多函数（含跨文件调用）时每个用户函数都能定位。
using IronST.Frontend;
using IronST.Frontend.Analysis;
using IronST.LLVMCodeGen.CodeGen;
using IronST.Runtime.Shared;
using IronST.Runtime.Shared.Modules;

namespace IronST.BytecodeGen.Tests;

public class FunctionLayoutTests
{
    private const byte Frame = 0x24;
    private const byte Leave = 0x25;
    private const byte Ret = 0x80;

    private const string TwoFunctions = """
        FUNCTION Add : DINT
        VAR_INPUT a : DINT; b : DINT; END_VAR
        Add := a + b;
        END_FUNCTION

        PROGRAM Main
        VAR x : DINT; END_VAR
            x := Add(2, 3);
            Print(x);
        END_PROGRAM
        """;

    private const string ThreeFunctions = """
        FUNCTION Add : DINT
        VAR_INPUT a : DINT; b : DINT; END_VAR
        Add := a + b;
        END_FUNCTION

        FUNCTION Mul : DINT
        VAR_INPUT a : DINT; b : DINT; END_VAR
        Mul := a * b;
        END_FUNCTION

        PROGRAM Main
        VAR x : DINT; END_VAR
            x := Add(2, 3);
            x := Mul(x, 2);
            Print(x);
        END_PROGRAM
        """;

    private static CompileResult Compile(string source)
    {
        var analysis = FrontendPipeline.Analyze(source, "test.st", [HostBuiltins.Create()]);
        var errors = analysis.Diagnostics
            .Where(d => d.Severity == Common.Diagnostics.DiagnosticSeverity.Error)
            .ToList();
        Assert.True(
            analysis.Success,
            $"Expected success but got {errors.Count} error(s):\n"
                + string.Join("\n", errors.Select(e => $"  {e}")));

        var module = new IRGenerator().Generate(analysis);
        return new ByteCodeGenerator(
                "test.st",
                hostFunctions: new Dictionary<string, ushort>(StringComparer.Ordinal)
                {
                    ["Print"] = HostCallIds.PRINT_STR,
                })
            .Generate(module, "Main");
    }

    private static IReadOnlyList<FunctionLayoutEntry> UserFunctions(CompileResult result) =>
        result.FunctionLayout.Where(f => !f.IsDeclaration).ToList();

    private static byte[] Code(CompileResult result) => result.Module.Code;

    // ══════════════════════════════════════════════════════════════
    //  1. 范围边界就是真实指令边界
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void FunctionLayout_UserFunctions_StartWithFrame()
    {
        var result = Compile(TwoFunctions);
        byte[] code = Code(result);

        var users = UserFunctions(result);
        Assert.Equal(2, users.Count);

        foreach (var f in users)
        {
            Assert.True(f.CodeLength > 0, $"{f.Name}: CodeLength 必须 > 0");
            Assert.Equal(Frame, code[f.StartPC]);
        }
    }

    [Fact]
    public void FunctionLayout_UserFunctions_EndWithLeaveRet()
    {
        var result = Compile(TwoFunctions);
        byte[] code = Code(result);

        foreach (var f in UserFunctions(result))
        {
            Assert.Equal(Leave, code[f.EndPC - 2]);
            Assert.Equal(Ret, code[f.EndPC - 1]);
        }
    }

    [Fact]
    public void FunctionLayout_Ranges_AreDisjointAndWithinCode()
    {
        var result = Compile(ThreeFunctions);
        byte[] code = Code(result);

        foreach (var f in result.FunctionLayout)
        {
            Assert.True(f.StartPC >= 0, $"{f.Name}: StartPC 为负");
            Assert.True(f.EndPC <= code.Length, $"{f.Name}: EndPC 越过 Code");
            Assert.True(f.EndPC > f.StartPC, $"{f.Name}: 空范围");
        }

        var ordered = result.FunctionLayout.OrderBy(f => f.StartPC).ToList();
        for (int i = 1; i < ordered.Count; i++)
        {
            Assert.True(
                ordered[i - 1].EndPC <= ordered[i].StartPC,
                $"{ordered[i - 1].Name} 与 {ordered[i].Name} 的字节范围重叠");
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  2. 元信息来自真实发射/IR，不是默认值
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void FunctionLayout_CarriesRealParamCountAndSignature()
    {
        var result = Compile(ThreeFunctions);

        var add = result.FunctionLayout.Single(f => f.Name == "Add");
        var main = result.FunctionLayout.Single(f => f.Name == "Main");

        Assert.Equal(2, add.ParamCount);
        Assert.Equal(0, main.ParamCount);
        Assert.Contains("i32", add.Signature);
        Assert.True(add.IsExported, "语言层 FUNCTION 应为导出的");
        Assert.True(main.IsEntryPoint, "显式入口应被标记");
        Assert.False(main.IsDeclaration);
    }

    [Fact]
    public void FunctionLayout_EntryStubPrecedesFirstFunction()
    {
        var result = Compile(TwoFunctions);

        // 入口桩（CALL + EXIT）在 PC 0，函数体在其后。
        int first = result.FunctionLayout.Min(f => f.StartPC);
        Assert.True(first > 0, "入口桩应占用 Code 起始处");
        Assert.Equal(0u, result.Module.EntryPC);
    }

    // ══════════════════════════════════════════════════════════════
    //  3. 写出 → 读回后函数索引仍指向真实指令
    // ══════════════════════════════════════════════════════════════

    private static STModModule ToModule(CompileResult result, string name)
    {
        var metadata = new Dictionary<string, STModMetadataEntry>(StringComparer.Ordinal)
        {
            [STModMetadataKeys.ModuleName] =
                STModMetadataEntry.FromString(STModMetadataKeys.ModuleName, name),
            [STModMetadataKeys.ModuleVersion] =
                STModMetadataEntry.FromString(STModMetadataKeys.ModuleVersion, "1.0.0"),
            [STModMetadataKeys.CompilerVersion] =
                STModMetadataEntry.FromString(STModMetadataKeys.CompilerVersion, "test"),
            [STModMetadataKeys.EntryPC] =
                STModMetadataEntry.FromUInt32(STModMetadataKeys.EntryPC, result.Module.EntryPC),
        };

        return new STModModule
        {
            Metadata = metadata,
            Functions = result.FunctionLayout.Select(e => new STModFunctionEntry
            {
                FunctionId = e.FunctionId,
                Flags =
                    (e.IsExported ? STModFunctionFlags.Exported : STModFunctionFlags.None)
                    | (e.IsDeclaration ? STModFunctionFlags.Declaration : STModFunctionFlags.None)
                    | (e.IsEntryPoint ? STModFunctionFlags.EntryPoint : STModFunctionFlags.None),
                Name = e.Name,
                Signature = e.Signature,
                ParamCount = (uint)e.ParamCount,
                LocalCount = (uint)e.LocalCount,
                CodeOffset = (ulong)e.StartPC,
                CodeLength = (ulong)e.CodeLength,
            }).ToList(),
            Code = result.Module.Code,
            ConstantPool = result.Module.ConstantPool,
            Sections = [],
            HasEntryPoint = true,
            EntryPC = result.Module.EntryPC,
            Relocations = result.Relocations,
        };
    }

    [Fact]
    public void ModuleRoundTrip_FunctionIndexMatchesRealBytecode()
    {
        var result = Compile(ThreeFunctions);
        byte[] written = STModWriter.Write(ToModule(result, "Tri"));
        var readBack = STModReader.Read(written);

        Assert.Equal(result.FunctionLayout.Count, readBack.Functions.Count);

        foreach (var layout in result.FunctionLayout)
        {
            var entry = readBack.Functions.Single(f => f.Name == layout.Name);

            // 索引数值逐字节等同真实布局。
            Assert.Equal((ulong)layout.StartPC, entry.CodeOffset);
            Assert.Equal((ulong)layout.CodeLength, entry.CodeLength);

            // 且指向的确实是该函数：FRAME 起始、LEAVE+RET 收尾。
            int start = (int)entry.CodeOffset;
            int end = start + (int)entry.CodeLength;
            Assert.Equal(Frame, readBack.Code[start]);
            Assert.Equal(Leave, readBack.Code[end - 2]);
            Assert.Equal(Ret, readBack.Code[end - 1]);
        }

        // 代码区段与常量池逐字节保留。
        Assert.Equal(result.Module.Code, readBack.Code);
        Assert.Equal(result.Module.ConstantPool.Length, readBack.ConstantPool.Count);
    }

    [Fact]
    public void ModuleRoundTrip_MultipleUserFunctions_EachLocatedCorrectly()
    {
        var result = Compile(ThreeFunctions);
        var readBack = STModReader.Read(STModWriter.Write(ToModule(result, "Tri")));

        var starts = new List<ulong>();
        foreach (string expected in new[] { "Main", "Add", "Mul" })
        {
            var entry = readBack.Functions.Single(f => f.Name == expected);
            Assert.True(entry.CodeLength > 0);
            Assert.Equal(Frame, readBack.Code[(int)entry.CodeOffset]);
            starts.Add(entry.CodeOffset);
        }

        Assert.Equal(starts.Count, starts.Distinct().Count());
    }

    [Fact]
    public void ModuleRoundTrip_HostDeclarations_AreFlaggedAsDeclarations()
    {
        var result = Compile(TwoFunctions);
        var readBack = STModReader.Read(STModWriter.Write(ToModule(result, "Two")));

        var print = readBack.Functions.Single(f => f.Name == "Print");
        Assert.True(
            (print.Flags & STModFunctionFlags.Declaration) != 0,
            "宿主函数声明应带 Declaration 标志");
        Assert.True(
            (print.Flags & STModFunctionFlags.Exported) == 0,
            "声明式函数不应被标为导出");
    }
}
