// EditorAnalysisService.cs — E1/E2：编辑器 ↔ 前端语义分析桥接
//
// 监听编辑器文本变化（防抖 300ms）→ FrontendPipeline.Analyze / AnalyzeFiles
// → 更新 StEditorControl.AnalysisData（诊断波浪线 + 补全符号）
// → 更新 OutputViewModel.Errors（错误列表）
//
// 阶段 VI 起带**工程上下文**：打开工程时分析整个工程（当前文件用编辑器实时内容），
// 再把诊断按 SourceSpan.FileName 过滤出当前文件那一份 —— 否则跨文件的类型/函数
// 会被报成"未声明/未知类型"，而同一个工程 build 起来其实 0 错误。
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using IronST.CLI;
using IronST.Frontend;
using IronST.Frontend.Analysis;
using IronST.IDE.Models;
using IronST.IDE.ViewModels;
using IronST.StEditor;

namespace IronST.IDE.Services;

public sealed class EditorAnalysisService
{
    private readonly EditorWorkspaceViewModel _workspace;
    private readonly OutputViewModel _output;

    /// <summary>取当前打开的工程（没有则 null）。惰性求值：分析时才看工程状态。</summary>
    private readonly Func<STProjectFile?> _getProject;

    private readonly DispatcherTimer _debounce;
    private EditorViewModel? _pending;

    public EditorAnalysisService(
        EditorWorkspaceViewModel workspace,
        OutputViewModel output,
        Func<STProjectFile?> getProject)
    {
        _workspace = workspace;
        _output = output;
        _getProject = getProject;

        _debounce = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(300),
        };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            var tab = _pending;
            _pending = null;
            if (tab != null)
                Analyze(tab);
        };

        // 已存在与新增的标签页都订阅 Code 变化
        foreach (var tab in workspace.EditorTabs)
            Subscribe(tab);
        workspace.EditorTabs.CollectionChanged += OnTabsChanged;
    }

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (var item in e.NewItems)
                if (item is EditorViewModel tab)
                    Subscribe(tab);
    }

    private void Subscribe(EditorViewModel tab)
    {
        tab.PropertyChanged -= OnTabPropertyChanged;
        tab.PropertyChanged += OnTabPropertyChanged;

        // 新标签页排一次分析。LoadCode 可能发生在 Add 之前（订阅尚未建立），
        // 那次 Code 变化会错过，这里补上，覆盖新建/打开/解决方案树双击三条路径。
        _pending = tab;
        _debounce.Stop();
        _debounce.Start();
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EditorViewModel.Code) || sender is not EditorViewModel tab)
            return;

        _pending = tab;
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>立即对指定标签页做一次分析（打开文件时调用）。</summary>
    public void AnalyzeNow(EditorViewModel tab) => Analyze(tab);

    /// <summary>
    /// 对当前所有打开的标签页重跑一次分析。
    /// 打开/新建工程后必须调一次：否则已经开着的文件还挂着"没有工程上下文"
    /// 时算出来的假错误（跨文件类型/函数全报未声明），得等用户改一个字才会消失。
    /// </summary>
    public void AnalyzeAllOpenTabs()
    {
        // 马上就要把所有标签页都算一遍，挂着的单标签页待办就多余了
        // （打开工程会连开 N 个标签页，_pending 只会记住最后一个）。
        _debounce.Stop();
        _pending = null;

        foreach (var tab in _workspace.EditorTabs)
            Analyze(tab);
    }

    private void Analyze(EditorViewModel tab)
    {
        var source = tab.Code ?? "";
        string currentName = CompilationSources.NameFor(tab.FilePath, tab.FileName);

        AnalysisResult result;
        try
        {
            // 必须与另外两条链路的函数表对齐，否则会出现"编辑器报未声明、F5 却能跑"。
            // 目前先补上 Print；IECStandardLibrary 仍未注入（该库在字节码后端跑不通），
            // 所以 SQRT 之类在编辑器里依旧会报未声明 —— 已知不一致，另行处理。
            var files = CompilationSources.Build(
                _getProject(), tab.FilePath, tab.FileName, source, File.ReadAllText);

            result = files.Count == 1
                ? FrontendPipeline.Analyze(
                    source, currentName, [IronST.Frontend.Analysis.HostBuiltins.Create()])
                : FrontendPipeline.AnalyzeFiles(
                    files, [IronST.Frontend.Analysis.HostBuiltins.Create()]);
        }
        catch (Exception ex)
        {
            _output.Error($"分析异常: {ex.Message}");
            return;
        }

        // 多文件时所有文件的诊断混在一张表里，只留当前这个文件的
        // （波浪线、错误列表、状态栏都只描述当前标签页）。
        var mine = DiagnosticFilter.ForFile(result.Diagnostics, currentName);

        // ── 编辑器波浪线（行/列 → 字符偏移）──
        var diags = new List<DiagnosticInfo>();
        foreach (var d in mine)
        {
            var span = d.Span ?? Common.Diagnostics.SourceSpan.Unknown;
            var (offset, length) = ToRange(source, span);
            diags.Add(new DiagnosticInfo
            {
                Offset = offset,
                Length = Math.Max(length, 1),
                Message = $"[{d.Code}] {d.Message}",
                Severity = d.Severity == Common.Diagnostics.DiagnosticSeverity.Error
                    ? StEditor.DiagnosticSeverity.Error
                    : StEditor.DiagnosticSeverity.Warning,
            });
        }

        // 控件可能尚未 Loaded（文件刚打开），此时暂存，Loaded 后由 EditorControl setter 补注入
        tab.LastAnalysisData = new EditorAnalysisData { Diagnostics = diags };
        if (tab.EditorControl != null)
            tab.EditorControl.AnalysisData = tab.LastAnalysisData;

        // ── 错误列表 ──
        _output.Errors.Clear();
        foreach (var d in mine)
        {
            _output.Errors.Add(new ErrorItem
            {
                Severity = d.Severity.ToString(),
                Code = d.Code.ToString(),
                Description = d.Message,
                FilePath = tab.FileName,
                Line = d.Span?.Line ?? 0,
            });
        }

        int errorCount = mine.Count(
            d => d.Severity == Common.Diagnostics.DiagnosticSeverity.Error);
        if (errorCount == 0 && mine.Count > 0)
            _output.Warn($"{tab.FileName}: 通过（{mine.Count} 条警告）");
        else if (errorCount == 0)
            _output.Info($"{tab.FileName}: 分析通过，无错误");
        else
            _output.Error($"{tab.FileName}: {errorCount} 个错误");
    }

    /// <summary>行/列（1 起）→ 字符偏移。</summary>
    private static int ToOffset(string source, int line, int column)
    {
        if (line <= 1)
            return Math.Max(column - 1, 0);

        int offset = 0;
        int currentLine = 1;
        // \r 与 \n 都要计入偏移，否则跨行的下划线会系统性偏左
        // （LF 每行偏 1，CRLF 每行偏 2）。
        for (int i = 0; i < source.Length && currentLine < line; i++)
        {
            offset++;
            if (source[i] == '\n')
                currentLine++;
        }
        return offset + Math.Max(column - 1, 0);
    }

    private static (int Offset, int Length) ToRange(string source, Common.Diagnostics.SourceSpan span)
    {
        if (span.Line <= 0)
            return (0, 0);

        int start = ToOffset(source, span.Line, span.Column);
        int end = span.EndLine >= span.Line
            ? ToOffset(source, span.EndLine, span.EndColumn)
            : start + 1;
        return (start, Math.Max(end - start, 1));
    }
}
