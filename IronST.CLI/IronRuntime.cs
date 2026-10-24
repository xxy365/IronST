using System.Text;
using System.Text.Json;
using IronST.BytecodeGen;
using IronST.Frontend;
using IronST.Frontend.Analysis;
using IronST.LLVMCodeGen.CodeGen;
using IronST.LLVMCodeGen.Debug;
using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.CLI;

/// <summary>
/// IronST 便利入口。封装编译 + 运行 + IO。
/// </summary>
public static class IronRuntime
{
    // ═══════════════════════════════════════════
    //  编译
    // ═══════════════════════════════════════════

    /// <summary>
    /// 编译 ST 源码为 BytecodeModule + DebugModule + SourceMap。
    /// </summary>
    public static CompileResult Compile(
        string source,
        string? sourceFileName = null,
        bool emitDebugInfo = true,
        IReadOnlyDictionary<string, ushort>? hostFunctions = null,
        IReadOnlyList<ExternalLibrary>? libraries = null      // ← 新增
    )
    {
        var analysis = FrontendPipeline.Analyze(
            source,
            sourceFileName ?? "source.st",
            libraries);                                       // ← 透传

        // 前端分析
        if (!analysis.Success) {
            string errors = string.Join("\n", analysis.Diagnostics.Select(d => $"  {d}"));
            throw new CompilationException(errors);
        }

        // AST生成IR代码。宿主已提供实现的内建函数（如字符串标准库）不内联，改发声明调用。
        // emitDebugInfo 时挂 IRDebugEmitter 收集 指令→(文件,行号)；不挂则一条行号都不记，
        // SourceMap 为空，运行期报错只能给 PC（0xE0）而给不出源行号。
        var file = sourceFileName ?? "source.st";
        IRDebugEmitter? debugEmitter = emitDebugInfo ? new IRDebugEmitter() : null;
        var irGen = new IRGenerator(debugEmitter, hostFunctions?.Keys);
        var module = irGen.Generate(analysis);

        // IR 代码 生成字节码
        var codeGen = new ByteCodeGenerator(
            sourceFileName: file,
            hostFunctions: hostFunctions
        );

        return codeGen.Generate(
            module,
            irSourceMap: debugEmitter?.BuildSourceMap(module, file));
    }

    /// <summary>
    /// 编译多个 ST 源文件（多文件编译，路线 A：前端合并为单一编译单元）。
    /// </summary>
    /// <param name="files">源文件列表（源码 + 文件名），顺序即合并顺序。</param>
    /// <param name="hostFunctions">宿主函数名 → 固定 ID 映射（HCALL）。</param>
    /// <param name="libraries">前端外部库声明（如 <c>HostBuiltins.Create()</c>）。</param>
    /// <param name="entryFunctionName">
    /// 显式入口名；为 <c>null</c> 时按"唯一可执行 PROGRAM"自动选择。
    /// 存在多个 PROGRAM 且未显式指定时抛 <see cref="CompilationException"/>。
    /// </param>
    public static CompileResult CompileFiles(
        IReadOnlyList<(string Source, string FileName)> files,
        IReadOnlyDictionary<string, ushort>? hostFunctions = null,
        IReadOnlyList<ExternalLibrary>? libraries = null,
        string? entryFunctionName = null,
        bool allowExternalCalls = false)
    {
        var analysis = FrontendPipeline.AnalyzeFiles(files, libraries);

        // 前端分析
        if (!analysis.Success) {
            string errors = string.Join("\n", analysis.Diagnostics.Select(d => $"  {d}"));
            throw new CompilationException(errors);
        }

        var entry = ResolveEntryName(analysis, entryFunctionName);

        var fallbackFile = files.Count > 0 ? files[0].FileName : "source.st";

        // AST生成IR代码。宿主已提供实现的内建函数（如字符串标准库）不内联，改发声明调用。
        // 挂 IRDebugEmitter：SourceMap 才非空，运行期报错才能给出源行号
        // （多文件时逐条指令用各自 POU 的文件名，fallback 只是缺省回退值）。
        var debugEmitter = new IRDebugEmitter();
        var irGen = new IRGenerator(debugEmitter, hostFunctions?.Keys);
        var module = irGen.Generate(analysis);

        // IR 代码 生成字节码
        var codeGen = new ByteCodeGenerator(
            sourceFileName: fallbackFile,
            hostFunctions: hostFunctions,
            allowExternalCalls: allowExternalCalls
        );

        return codeGen.Generate(
            module,
            entryFunctionName: entry,
            irSourceMap: debugEmitter.BuildSourceMap(module, fallbackFile));
    }

    /// <summary>
    /// 解析多文件编译的入口名（路线 A，确定性）。
    /// <para>显式指定优先，但必须是存在且可执行的 PROGRAM（绝不静默回退到别的函数）；
    /// 未指定时仅当恰好一个可执行 PROGRAM 才自动选择；多个 PROGRAM → 确定性失败；
    /// 零 PROGRAM → 交给既有回退。</para>
    /// </summary>
    private static string? ResolveEntryName(AnalysisResult analysis, string? requested)
    {
        var programs = analysis.POUs
            .Where(p => p.Kind == POUKind.Program && !p.IsExternal)
            .Select(p => p.Name)
            .ToList();

        if (!string.IsNullOrEmpty(requested))
        {
            // 显式入口必须存在且可执行 —— 否则后端只会发出对不存在函数的调用，
            // 在字节码装配期以 "Undefined label or function" 之类的内部错误炸掉。
            if (!programs.Contains(requested))
                throw new CompilationException(
                    $"多文件编译：找不到可执行的入口 PROGRAM '{requested}'。"
                    + (programs.Count > 0
                        ? $"可用入口：{string.Join("、", programs)}。"
                        : "当前编译单元中没有可执行的 PROGRAM。")
                );

            return requested;
        }

        return programs.Count switch
        {
            1 => programs[0],
            0 => null,
            _ => throw new CompilationException(
                $"多文件编译：存在 {programs.Count} 个可执行 PROGRAM "
                + $"（{string.Join("、", programs)}），无法确定入口；请显式指定入口名。"
            ),
        };
    }

    // ═══════════════════════════════════════════
    //  运行
    // ═══════════════════════════════════════════

    /// <summary>运行编译结果。</summary>
    /// <param name="maxInstructions">
    /// 指令预算；<c>0</c> = 不限。超限抛
    /// <see cref="RuntimeErrorType.InstructionBudgetExceeded"/>（CLI 映射为退出码 1）。
    /// </param>
    public static void Run(
        CompileResult result,
        Action<HostBridge>? configureHost = null,
        int stackSize = 8192,
        CancellationToken cancellation = default,
        long maxInstructions = 0
    )
    {
        RunModule(
            result.Module, configureHost, stackSize, result.StructRegistrations,
            sourceMap: result.SourceMap, cancellation: cancellation,
            maxInstructions: maxInstructions);
    }

    /// <summary>运行 BytecodeModule。</summary>
    /// <param name="sourceMap">
    /// 可选的 PC → (文件, 行号) 表。给了它，VM 会把运行期错误的源位置回填进异常
    /// （<see cref="RuntimeErrorException.Message"/> 于是带「（文件:行）」）；
    /// 不给（<c>null</c>）则只报 PC。字节码输入可由
    /// <see cref="LoadSourceMap"/> 从旁车 <c>.map.json</c> 读回来。
    /// </param>
    public static void RunModule(
        BytecodeModule module,
        Action<HostBridge>? configureHost = null,
        int stackSize = 8192,
        IReadOnlyList<(byte TypeId, int FieldCount)>? structTypes = null,
        uint? entryReturnPC = null,
        IReadOnlyDictionary<int, (string file, int line)>? sourceMap = null,
        CancellationToken cancellation = default,
        long maxInstructions = 0
    )
    {
        var bridge = new HostBridge();
        configureHost?.Invoke(bridge);

        var vm = new VM(module, new VMOptions
        {
            StackSize = stackSize,
            HostBridge = bridge,
            StructTypes = structTypes,
            EntryReturnPC = entryReturnPC,
            SourceLocator = SourceResolver.FromMap(sourceMap),
            Cancellation = cancellation,
            MaxInstructions = maxInstructions,
        });
        vm.Run();
    }

    /// <summary>编译并运行（一步到位）。</summary>
    public static CompileResult CompileAndRun(
        string source,
        string? sourceFileName = null,
        Action<HostBridge>? configureHost = null,
        IReadOnlyList<ExternalLibrary>? libraries = null      // ← 新增
    )
    {
        var result = Compile(source, sourceFileName, libraries: libraries);  // ← 透传
        Run(result, configureHost);
        return result;
    }

    // ═══════════════════════════════════════════
    //  保存 / 加载
    // ═══════════════════════════════════════════

    public static void Save(CompileResult result, string path) =>
        BytecodeFileHelper.Save(path, result.Module);

    public static void SaveModule(BytecodeModule module, string path) =>
        BytecodeFileHelper.Save(path, module);

    public static BytecodeModule LoadModule(string path) =>
        BytecodeModule.Load(File.ReadAllBytes(path));

    /// <summary>
    /// 把源映射写到二进制的旁车文件 <c>&lt;name&gt;.map.json</c>（空表不写）。
    /// 与 <see cref="LoadSourceMap"/> 同一个命名规则，故 <paramref name="binaryPath"/>
    /// 传的是 .isb / .stmod 的路径，不是 map 文件本身。
    /// </summary>
    public static void SaveSourceMap(CompileResult result, string binaryPath)
    {
        var dict = result.SourceMap.ToDictionary(kv => kv.Key, kv => kv.Value);
        BytecodeFileHelper.SaveSourceMap(binaryPath, dict);
    }

    /// <summary>读二进制的旁车源映射（不存在则空表）。</summary>
    public static Dictionary<int, (string file, int line)> LoadSourceMap(string binaryPath) =>
        BytecodeFileHelper.LoadSourceMap(binaryPath);

    /// <summary>
    /// 直接写一张（可能已平移的）源映射到 <paramref name="binaryPath"/> 的旁车。
    /// 链接产物用 <see cref="SourceResolver.Merge"/> 合并各输入模块的映射后走这里。
    /// </summary>
    public static void SaveSourceMap(
        string binaryPath, Dictionary<int, (string file, int line)> sourceMap) =>
        BytecodeFileHelper.SaveSourceMap(binaryPath, sourceMap);

    // ═══════════════════════════════════════════
    //  SourceMap 查询
    // ═══════════════════════════════════════════

    public static (string file, int line)? FindSource(CompileResult result, int pc) =>
        SourceResolver.Lookup(result.SourceMap, (uint)pc);

    /// <summary>按 PC 在源映射里取最近的源位置（最大 key ≤ pc）；没有则 <c>null</c>。</summary>
    /// <remarks>负数 PC 无效（直接返回 <c>null</c>）—— 强转 <c>uint</c> 会变成极大值，
    /// 那样反而会匹配到表里最后一条。</remarks>
    public static (string file, int line)? FindSource(
        IReadOnlyDictionary<int, (string file, int line)> sourceMap, int pc) =>
        pc < 0 ? null : SourceResolver.Lookup(sourceMap, (uint)pc);
}

// ═══════════════════════════════════════════════════════════════
//  CompilationException — 编译失败
// ═══════════════════════════════════════════════════════════════

public sealed class CompilationException : Exception
{
    public CompilationException(string messages)
        : base($"Compilation failed:\n{messages}") { }
}

// ═══════════════════════════════════════════════════════════════
//  BytecodeFileHelper — .irst 序列化 + SourceMap 读写
// ═══════════════════════════════════════════════════════════════

internal static class BytecodeFileHelper
{
    private const uint Magic = 0x49525354;
    private const byte VerMajor = 1;
    private const byte VerMinor = 0;
    private const byte VerPatch = 0;

    public static void Save(string path, BytecodeModule module)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        bw.Write(Magic);
        bw.Write(VerMajor);
        bw.Write(VerMinor);
        bw.Write(VerPatch);
        bw.Write((byte)0);
        bw.Write(module.EntryPC);
        bw.Write((uint)module.Code.Length);
        bw.Write(module.ConstantPool.Length);
        bw.Write(module.GlobalCount);
        bw.Write((uint)(module.DebugInfo?.Length ?? 0));

        bw.Write(module.Code);

        // 常量池编解码与 .stmod 共用 BcValueSerializer（单一事实来源，字节不变）。
        foreach (var val in module.ConstantPool)
            BcValueSerializer.Write(bw, val);

        if (module.DebugInfo is { Length: > 0 } dbg)
            bw.Write(dbg);
    }

    /// <summary>
    /// 把源映射写到二进制的**旁车**文件 <c>&lt;name&gt;.map.json</c>
    /// （与 <see cref="LoadSourceMap"/> 同一个命名规则）。空表不写 —— 免得产出
    /// 一个永远解析不出位置的废文件。
    /// </summary>
    /// <param name="binaryPath">对应二进制的路径（.isb / .irst / .stmod），不是 map 文件本身。</param>
    public static void SaveSourceMap(
        string binaryPath,
        Dictionary<int, (string file, int line)> sourceMap
    )
    {
        if (sourceMap.Count == 0)
            return;

        var json = JsonSerializer.Serialize(
            sourceMap.ToDictionary(
                kv => kv.Key,
                kv => new { file = kv.Value.file, line = kv.Value.line }
            ),
            new JsonSerializerOptions { WriteIndented = true }
        );
        File.WriteAllText(SourceMapPath(binaryPath), json);
    }

    /// <summary>读旁车源映射；文件不存在 / 解析失败时返回空表（调用方按"无行号"降级）。</summary>
    /// <param name="binaryPath">对应二进制的路径（.isb / .irst / .stmod）。</param>
    public static Dictionary<int, (string file, int line)> LoadSourceMap(string binaryPath)
    {
        var sourceMap = new Dictionary<int, (string file, int line)>();
        string mapPath = SourceMapPath(binaryPath);

        if (!File.Exists(mapPath))
            return sourceMap;

        try {
            string json = File.ReadAllText(mapPath);
            var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (raw != null)
                foreach (var kv in raw) {
                    int pc = int.Parse(kv.Key);
                    string file = kv.Value.GetProperty("file").GetString() ?? "";
                    int line = kv.Value.GetProperty("line").GetInt32();
                    sourceMap[pc] = (file, line);
                }
        } catch (Exception ex) {
            Console.Error.WriteLine($"[IronRuntime] SourceMap load failed: {ex.Message}");
        }

        return sourceMap;
    }

    /// <summary>旁车源映射路径：与二进制同名，扩展名换成 <c>.map.json</c>。</summary>
    private static string SourceMapPath(string binaryPath) =>
        Path.ChangeExtension(binaryPath, ".map.json");
}