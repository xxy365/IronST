// ProjectCompileService.cs — IDE 阶段 VII：F5 按工程调试的「编译」核心（纯逻辑）。
//
// 阶段 VI 把「分析」接上了工程上下文（CompilationSources + DiagnosticFilter），
// 但「编译」还埋在 DebugViewModel.CompileCurrent 里：只喂当前标签页一个文件。
// 结果就是右键 Run（CLI、整工程）能跑，F5 调试同一工程却拿到的入口退化成
// 第一个函数（三个形参全是 Void → TypeError: Cannot compare Void and Void），
// 或者单文件 16 个假错误直接编不过。
//
// 这里把编译抽成可单测的纯函数，与 EditorAnalysisService 共用同一个文件口径：
//
//   · 调用方先用 CompilationSources.Build(...) 算出该喂哪些文件（无工程 =
//     只有当前标签页；有工程 = 整个工程）；
//   · 多文件走 FrontendPipeline.AnalyzeFiles（共享符号表），单文件保持 Analyze
//     （语义与既有逐字节行为完全一致，EditorAnalysisService 同款分流）；
//   · 入口用 .stproj 的 StartupProgram（ByteCodeGenerator.Generate 的
//     entryFunctionName 参数），没有工程时传 null 走自动选择；
//   · irSourceMap 的缺省文件名只是回退值 —— 阶段 VI 起 IRDebugEmitter 逐条
//     记录了指令所属 POU 的文件，多文件时行号映射各自归各自的文件。
//
// DebugViewModel 只做接线：报错、建 VM、注册断点、切标签页。
using IronST.BytecodeGen;
using IronST.Common.Diagnostics;
using IronST.Frontend;
using IronST.Frontend.Analysis;
using IronST.LLVMCodeGen.CodeGen;
using IronST.LLVMCodeGen.Debug;
using IronST.Runtime.Shared;

namespace IronST.IDE.Services;

public static class ProjectCompileService
{
    /// <summary>DebugViewModel 用的宿主函数表：Print（DebugHostBridge 里有 HCALL 处理函数）。</summary>
    /// <remarks>
    /// 只注入宿主桥里存在的函数 —— IECStandardLibrary 的数学函数在 IR 层降级成
    /// C extern 名，ByteCodeGenerator 找不到登记会抛 InvalidOperationException。
    /// </remarks>
    public static IReadOnlyDictionary<string, ushort> DebugHostFunctions =>
        DebugHostBridge.HostFunctions;

    /// <summary>
    /// 编译 St 源文件集合 → 字节码。
    /// </summary>
    /// <param name="files">该喂给前端的文件（来源/文件名），见 <see cref="CompilationSources.Build"/>。</param>
    /// <param name="entryProgram">入口 PROGRAM 名（.stproj 的 StartupProgram）；null = 单程序自动选择。</param>
    /// <param name="fallbackFileName">
    /// SourceMap 里没有逐函数文件名的指令的回退文件名；单文件时就是那个文件，
    /// 多文件时 IRDebugEmitter 自己记住的文件名优先。
    /// </param>
    /// <param name="hostFunctions">字节码层宿主函数登记（名称 → HCALL id），缺省 Print。</param>
    /// <returns>编译成功时的 <see cref="CompileResult"/>；任何编译错误时为 null，同时带上错误诊断。</returns>
    public static (CompileResult? Result, IReadOnlyList<Diagnostic> Errors) Compile(
        IReadOnlyList<(string Source, string FileName)> files,
        string? entryProgram,
        string fallbackFileName,
        IReadOnlyDictionary<string, ushort>? hostFunctions = null)
    {
        hostFunctions ??= DebugHostFunctions;

        try
        {
            // 单文件保持 Analyze（与既有行为逐字节一致）；多文件 AnalyzeFiles ——
            // 合并类型声明 + POU 后统一跑三遍语义分析，跨文件符号全部可见。
            var analysis = files.Count == 1
                ? FrontendPipeline.Analyze(files[0].Source, files[0].FileName, [HostBuiltins.Create()])
                : FrontendPipeline.AnalyzeFiles(files, [HostBuiltins.Create()]);

            var errors = analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();
            if (errors.Count > 0)
                return (null, errors);

            // 行号映射：IRGenerator 默认走 NullDebugEmitter，一条行号都不记 →
            // SourceMap 为空 → 断点注册不进去，程序照跑不停。IRDebugEmitter
            // 收集 指令→(文件,行号)，再按 EmitBody 同序整理成 irSourceMap。
            var debugEmitter = new IRDebugEmitter();
            var module = new IRGenerator(debugEmitter).Generate(analysis);
            var irSourceMap = debugEmitter.BuildSourceMap(module, fallbackFileName);

            var result = new ByteCodeGenerator(
                sourceFileName: fallbackFileName,
                hostFunctions: hostFunctions)
                .Generate(
                    module,
                    entryFunctionName: entryProgram,
                    irSourceMap: irSourceMap);

            return (result, errors);
        }
        catch (Exception ex)
        {
            // 编译器内部错误（越界补丁、未登记 extern ……）收成一条结构性诊断，
            // 让调用方按错误展示，而不是把 IDE 崩掉。
            return (null, new Diagnostic[]
            {
                new()
                {
                    Severity = DiagnosticSeverity.Error,
                    Code = ErrorCode.E1000,
                    Message = $"编译器内部错误: {ex.Message}",
                    Span = SourceSpan.Unknown,
                },
            });
        }
    }

    /// <summary>
    /// 某个（文件, 行）的断点 PC 集合 —— 只在 <paramref name="file"/> 里找。
    /// </summary>
    /// <remarks>
    /// 这是 DebugViewModel.RegisterBreakpointsForLine 的核心回归点：旧实现
    /// <c>SourceMap.Where(kv =&gt; kv.Value.line == line)</c> 不带文件，Main.st:29
    /// 的断点会同时挂到 Utils.st:29 的指令上；<c>_lineToBreakpoints</c> 只按行做
    /// 键，两个文件同名行还会互相
    /// 被同时注册/摘除。返回的是「会话里 AddAddressBreakpoint 的地址」，
    /// 必须与 <see cref="IronST.Runtime.Debug.SourceMap"/> 里的文件索引同源。
    /// </remarks>
    public static IReadOnlyList<uint> BreakpointPCs(
        CompileResult result, string file, int line)
    {
        var pcs = new List<uint>();
        foreach (var (pc, (sourceFile, sourceLine)) in result.SourceMap)
        {
            if (sourceLine != line)
                continue;
            if (!string.Equals(sourceFile, file, StringComparison.OrdinalIgnoreCase))
                continue;
            pcs.Add((uint)pc);
        }

        // 确定性输出：UI 报告、单测断言都依赖稳定顺序
        pcs.Sort();
        return pcs;
    }

    /// <summary>编译错误（或内部异常）的展示文本：<c>文件:行 [E 编号] 消息</c>。</summary>
    public static string FormatError(Diagnostic d)
    {
        string file = d.Span?.FileName ?? ""; // Span 文件名可能为空（全局诊断）
        int line = d.Span?.Line ?? 0;
        return $"{file}:{(line > 0 ? line : 0)} [{d.Code}] {d.Message}";
    }
}
