// ProjectCompileServiceTests.cs — IDE 阶段 VII：F5 按工程调试的编译核心（纯逻辑）。
//
// 阶段 VI 只把「分析」接上了工程上下文；「编译」还停在 DebugViewModel.CompileCurrent
// 的单文件 FrontendPipeline.Analyze —— 结果就是：右键 Run（CLI、多文件）能跑，
// F5 调试同一个工程却报 16 个假错误 / TypeError: Cannot compare Void and Void。
//
// 契约（对应 Wireless 交接单 §3.2 的测试切法）：
//   ① 多文件工程用 AnalyzeFiles 前端 → IR → 字节码，能编过（无错误诊断）；
//   ② 入口就是 .stproj 的 StartupProgram（FunctionLayout 里 IsEntryPoint 那一项）；
//   ③ SourceMap 按函数归属文件：Main 的 PC 指向 Main.st，ClampDint 的指向 Utils.st
//     （阶段 VI 已修好 IRDebugEmitter 的逐函数文件记忆，这里直接消费）；
//   ④ 两个文件里同一行号各设断点 → 各自只命中自己文件的 PC（核心回归点：
//     阶段 VI 之前的 RegisterBreakpointsForLine 只按行匹配，Main.st:29 的断点
//     会挂到 Utils.st:29）。
//   ⑤ 没有工程时的单文件路径保持可编译（RunWithoutDebug 的既有行为不变）。
//
// 被测内容模仿仓库外的 demo 工程（Main.st / Motor.st / Utils.st + StartupProgram=Main），
// 但内联在测试里，不碰磁盘上那个目录。
using IronST.BytecodeGen;
using IronST.IDE.Services;

namespace IronST.IDE.Tests;

public class ProjectCompileServiceTests
{
    // ── 内联 demo 工程 ───────────────────────────────────────

    private const string MainPath = @"C:\proj\Main.st";
    private const string MotorPath = @"C:\proj\Motor.st";
    private const string UtilsPath = @"C:\proj\Utils.st";

    private const string MainSt = """
        // Main.st — 入口（对应 .stproj <StartupProgram>Main</StartupProgram>）
        PROGRAM Main
        VAR value : DINT; rpm : DINT; info : MotorInfo; END_VAR
        value := ClampDint(150, 0, 100);
        value := SumTo(10);
        rpm := RpmAfterRamp(900, 250, 1200);
        info.Rpm := 1150;
        IF rpm > 1000 THEN
            info.Load := 0.9;
        END_IF
        Print(value);
        Print(rpm);
        END_PROGRAM
        """;

    private const string MotorSt = """
        // Motor.st — 电机模型（定义在别的文件的类型/函数，Main.st 直接引用）
        TYPE
            MotorInfo :
            STRUCT
                Rpm    : DINT;
                Load   : REAL;
                Faulty : BOOL;
            END_STRUCT;
        END_TYPE;

        FUNCTION RpmAfterRamp : DINT
        VAR_INPUT current : DINT; delta : DINT; ceiling : DINT; END_VAR
        RpmAfterRamp := ClampDint(current + delta, 0, ceiling);
        END_FUNCTION
        """;

    private const string UtilsSt = """
        // Utils.st — 通用工具
        FUNCTION ClampDint : DINT
        VAR_INPUT value : DINT; low : DINT; high : DINT; END_VAR
        IF value < low THEN
            ClampDint := low;
        ELSIF value > high THEN
            ClampDint := high;
        ELSE
            ClampDint := value;
        END_IF
        END_FUNCTION

        FUNCTION SumTo : DINT
        VAR_INPUT n : DINT; END_VAR
        VAR i : DINT; acc : DINT; END_VAR
        acc := 0;
        FOR i := 1 TO n DO
            acc := acc + i;
        END_FOR
        SumTo := acc;
        END_FUNCTION
        """;

    /// <summary>整个工程的文件列表，顺序与 .stproj 一致。</summary>
    private static (string Source, string FileName)[] DemoFiles() => new[]
    {
        (MainSt, MainPath),   // 当前标签页（编辑器实时内容）
        (MotorSt, MotorPath), // 其余从磁盘读
        (UtilsSt, UtilsPath),
    };

    private static CompileResult CompileDemo(string? entry = "Main")
    {
        var (result, errors) = ProjectCompileService.Compile(DemoFiles(), entry, MainPath);
        if (result is null || errors.Count > 0)
            throw new InvalidOperationException(
                "demo 工程本应编译通过，错误: " + string.Join("; ", errors.Select(e => $"{e.Code} {e.Message}")));
        return result;
    }

    // ── ① 多文件工程能编过 ──────────────────────────────────

    [Fact]
    public void MultiFileProject_CompilesWithoutErrors()
    {
        var (result, errors) = ProjectCompileService.Compile(DemoFiles(), "Main", MainPath);

        Assert.NotNull(result);
        Assert.Empty(errors);
    }

    // ── ② 入口 = StartupProgram ─────────────────────────────

    [Fact]
    public void Entry_IsStartupProgram()
    {
        var result = CompileDemo("Main");

        var entry = Assert.Single(result.FunctionLayout, f => f.IsEntryPoint);
        Assert.Equal("Main", entry.Name);
    }

    // ── ③ SourceMap 按函数归属文件 ──────────────────────────

    [Fact]
    public void SourceMap_FunctionsAttributedToTheirOwnFile()
    {
        var result = CompileDemo("Main");

        AssertFunctionFile(result, "Main", MainPath);
        AssertFunctionFile(result, "ClampDint", UtilsPath);
        AssertFunctionFile(result, "SumTo", UtilsPath);
        AssertFunctionFile(result, "RpmAfterRamp", MotorPath);
    }

    /// <summary>函数真实字节范围内的每一条带行号映射的指令，都必须归属该函数自己的文件。</summary>
    private static void AssertFunctionFile(CompileResult result, string function, string file)
    {
        var layout = Assert.Single(result.FunctionLayout, f => f.Name == function);

        var mapped = result.SourceMap
            .Where(kv => kv.Key >= layout.StartPC && kv.Key < layout.EndPC)
            .ToList();

        // 空映射会让断点测试很不稳健，这里要求函数体内至少有一条行号映射
        // —— 没有就说明调试链对这个函数根本没产出源码定位，先失败在这里。
        Assert.True(mapped.Count > 0, $"{function}（{layout.StartPC}..{layout.EndPC}）没有任何行号映射");

        var wrong = mapped.Where(kv => kv.Value.file != file).ToList();
        Assert.True(wrong.Count == 0,
            $"{function} 的 PC 应全部归到 {file}，实际混入: "
            + string.Join("; ", wrong.Select(kv => $"PC={kv.Key} file={kv.Value.file} line={kv.Value.line}")));
    }

    // ── ④ 同行号断点按文件隔离（阶段 VII 核心回归点）────────

    [Fact]
    public void Breakpoints_SameLineDifferentFiles_HitOwnFileOnly()
    {
        var result = CompileDemo("Main");

        // 找一个两个文件都有可断点 PC 的行号（两边的头几行都是可执行语句）
        var linesOfMain = PCsAtLine(result, MainPath).Keys.ToHashSet();
        var linesOfUtils = PCsAtLine(result, UtilsPath).Keys.ToHashSet();
        var shared = linesOfMain.Intersect(linesOfUtils).OrderBy(x => x).ToList();

        Assert.True(shared.Count > 0,
            $"Main 与 Utils 应有同行号的可断点行（Main 有 {linesOfMain.Count} 行、Utils 有 {linesOfUtils.Count} 行）");
        int line = shared[0];

        var fromMain = ProjectCompileService.BreakpointPCs(result, MainPath, line);
        var fromUtils = ProjectCompileService.BreakpointPCs(result, UtilsPath, line);

        // 各自只命中自己文件的那一摊 —— 不许串
        Assert.Equal(PCsAtLine(result, MainPath)[line], fromMain);
        Assert.Equal(PCsAtLine(result, UtilsPath)[line], fromUtils);
    }

    [Fact]
    public void Breakpoints_WrongFileOrUnknownLine_ReturnNothing()
    {
        var result = CompileDemo("Main");

        // 没有这个文件的任何 PC
        Assert.Empty(ProjectCompileService.BreakpointPCs(result, @"C:\proj\Nowhere.st", 2));
        // 文件对、行号在字节码里根本没映射
        Assert.Empty(ProjectCompileService.BreakpointPCs(result, MainPath, 9999));
    }

    private static SortedDictionary<int, List<uint>> PCsAtLine(
        CompileResult result, string file)
    {
        var map = new SortedDictionary<int, List<uint>>();
        foreach (var (pc, (f, line)) in result.SourceMap)
        {
            if (!string.Equals(f, file, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!map.TryGetValue(line, out var list))
                map[line] = list = new List<uint>();
            list.Add((uint)pc);
        }
        // 与被测输出同一序（按 PC 升序），否则 Assert.Equal 是顺序敏感的
        foreach (var list in map.Values)
            list.Sort();
        return map;
    }

    // ── ⑤ 没有工程时的单文件路径保持可编译 ───────────────

    [Fact]
    public void SingleFile_StillCompiles()
    {
        // 自包含源码：没有工程时只剩当前标签页一个文件，
        // 引用别处定义的符号应当真报错 —— 所以这里用不依赖其它文件的程序。
        var (result, errors) = ProjectCompileService.Compile(
            [("PROGRAM Main\nVAR i : DINT; END_VAR\ni := 7;\nPrint(i);\nEND_PROGRAM", MainPath)],
            null, MainPath);

        Assert.NotNull(result);
        Assert.Empty(errors);
        // 单程序自动选入口的行为不变
        Assert.Contains(result.FunctionLayout, f => f.IsEntryPoint);
    }
}
