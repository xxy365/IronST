// MultiFileSourceMapTests.cs — 多文件编译的「指令 → (文件, 行)」映射。
//
// 这是 IDE 多文件调试的底座：断点靠它把「第 N 行」换算成 PC，执行行高亮也靠它。
//
// 契约：
//   · 每个 POU 的指令必须标到**该 POU 自己所在的文件**，不能全按传进
//     BuildSourceMap 的那一个文件名一刀切；
//   · 调用点属于调用者所在文件，被调函数体内的指令属于被调者所在文件。
//
// 现状缺陷：IRGenerator 已经在逐函数传正确的文件名（IRGenerator.cs:473
// `_debug.EnterFunction(func, fn.Name, fn.Span.FileName ?? "", …)`），
// 但 IRDebugEmitter.EnterFunction 把它丢掉了，BuildSourceMap 给所有映射
// 统一写参数里的那个文件名。多文件时 Utils.st 的指令会被标成 Main.st ——
// 断点挂到同名的错文件、执行行高亮错文件。
using IronST.BytecodeGen;
using IronST.Frontend;
using IronST.Frontend.Analysis;
using IronST.LLVMCodeGen.CodeGen;
using IronST.LLVMCodeGen.Debug;

namespace IronST.BytecodeGen.Tests;

public class MultiFileSourceMapTests
{
    private const string MainFile = "Main.st";
    private const string UtilsFile = "Utils.st";

    private const string MainSource = """
        PROGRAM Main
        VAR
            v : DINT;
        END_VAR
            v := ClampDint(150, 0, 100);
            Print(v);
        END_PROGRAM
        """;

    private const string UtilsSource = """
        FUNCTION ClampDint : DINT
        VAR_INPUT
            value : DINT;
            low   : DINT;
            high  : DINT;
        END_VAR
            IF value < low THEN
                ClampDint := low;
            ELSE
                ClampDint := value;
            END_IF
        END_FUNCTION
        """;

    private static Dictionary<string, ushort> Hosts =>
        new(StringComparer.Ordinal) { ["Print"] = 7 };

    /// <summary>按 IDE 的多文件路径编译：AnalyzeFiles → IRDebugEmitter → Generate。</summary>
    private static CompileResult CompileBothFiles()
    {
        IReadOnlyList<(string Source, string FileName)> files =
        [
            (MainSource, MainFile),
            (UtilsSource, UtilsFile),
        ];

        var analysis = FrontendPipeline.AnalyzeFiles(files, [HostBuiltins.Create()]);
        Assert.True(
            analysis.Success,
            "前端应当通过，实际诊断: "
                + string.Join(" | ", analysis.Diagnostics.Select(d => d.ToString())));

        var debugEmitter = new IRDebugEmitter();
        var module = new IRGenerator(debugEmitter).Generate(analysis);
        var irSourceMap = debugEmitter.BuildSourceMap(module, MainFile);

        return new ByteCodeGenerator(MainFile, hostFunctions: Hosts)
            .Generate(module, entryFunctionName: "Main", irSourceMap: irSourceMap);
    }

    /// <summary>某个函数 PC 区间（真实布局，不靠猜）里出现过的文件名集合。</summary>
    private static HashSet<string> FilesOfFunction(CompileResult result, string functionName)
    {
        var entry = result.FunctionLayout.Single(f => f.Name == functionName);

        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in result.SourceMap)
            if (kv.Key >= entry.StartPC && kv.Key < entry.EndPC)
                files.Add(kv.Value.file);

        return files;
    }

    [Fact]
    public void SourceMap_CoversBothFiles()
    {
        var result = CompileBothFiles();

        var files = result.SourceMap.Values
            .Select(v => v.file)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Assert.Equal([MainFile, UtilsFile], files);
    }

    [Fact]
    public void SourceMap_AttributesEachPouToItsOwnFile()
    {
        var result = CompileBothFiles();

        Assert.Equal([MainFile], FilesOfFunction(result, "Main"));
        Assert.Equal([UtilsFile], FilesOfFunction(result, "ClampDint"));
    }

    [Fact]
    public void SourceMap_LinesStayInsideTheirOwnFile()
    {
        var result = CompileBothFiles();

        int mainLines = MainSource.Split('\n').Length;
        int utilsLines = UtilsSource.Split('\n').Length;

        foreach (var kv in result.SourceMap)
        {
            var (file, line) = kv.Value;

            Assert.True(
                file is MainFile or UtilsFile,
                $"映射里的文件名必须是工程里的真实文件，实际 '{file}'");

            Assert.InRange(line, 1, file == MainFile ? mainLines : utilsLines);
        }
    }
}
