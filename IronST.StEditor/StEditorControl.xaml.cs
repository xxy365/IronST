using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Rendering;
using IronST.StEditor.Editor;
using IronST.StEditor.Features;

namespace IronST.StEditor;

public partial class StEditorControl : UserControl
{
    // ══════════════════════════════════════════════════════════════
    //  功能模块
    // ══════════════════════════════════════════════════════════════

    private AutoAssign _autoAssign = null!;
    private AutoUppercase _autoUppercase = null!;
    private AutoEndBlock _autoEndBlock = null!;
    private Completion _completion = null!;
    private StateRebuilder _stateRebuilder = null!;
    private StSyntaxHighlightingTransformer _highlighter = null!;
    private IEditorFeature[] _enteredChain = null!;
    private bool _isSyncingText;
    private SquigglyUnderlineRenderer? _squigglyRenderer;
    private BreakpointMargin? _breakpointMargin;
    private ExecutionLineHighlighter? _executionRenderer;

    /// <summary>
    /// 一次性功能（语法高亮、折叠、输入链、TextArea 事件订阅）是否已装好。
    /// 切标签页时 Loaded/Unloaded 会反复触发，这些东西只能装一次。
    /// </summary>
    private bool _featuresInstalled;

    // ══════════════════════════════════════════════════════════════
    //  依赖属性
    // ══════════════════════════════════════════════════════════════

    // ── Text ──

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(StEditorControl),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnTextPropertyChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private static void OnTextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StEditorControl control && !control._isSyncingText)
            control.Editor.Text = e.NewValue as string ?? string.Empty;
    }

    // ── BaseFontSize ──

    public static readonly DependencyProperty BaseFontSizeProperty =
        DependencyProperty.Register(
            nameof(BaseFontSize), typeof(double), typeof(StEditorControl),
            new FrameworkPropertyMetadata(14.0, OnBaseFontSizeChanged));

    public double BaseFontSize
    {
        get => (double)GetValue(BaseFontSizeProperty);
        set => SetValue(BaseFontSizeProperty, value);
    }

    private static void OnBaseFontSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StEditorControl control) control.ApplyZoom();
    }

    // ── ZoomLevel ──

    public static readonly DependencyProperty ZoomLevelProperty =
        DependencyProperty.Register(
            nameof(ZoomLevel), typeof(int), typeof(StEditorControl),
            new FrameworkPropertyMetadata(100, OnZoomLevelChanged));

    public int ZoomLevel
    {
        get => (int)GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }

    private static void OnZoomLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StEditorControl control) control.ApplyZoom();
    }

    // ── ShowLineNumbers ──

    public static readonly DependencyProperty ShowLineNumbersProperty =
        DependencyProperty.Register(
            nameof(ShowLineNumbers), typeof(bool), typeof(StEditorControl),
            new FrameworkPropertyMetadata(true));

    public bool ShowLineNumbers
    {
        get => (bool)GetValue(ShowLineNumbersProperty);
        set => SetValue(ShowLineNumbersProperty, value);
    }

    // ── IsReadOnly ──

    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(
            nameof(IsReadOnly), typeof(bool), typeof(StEditorControl),
            new FrameworkPropertyMetadata(false, OnIsReadOnlyChanged));

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    private static void OnIsReadOnlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StEditorControl control)
            control.Editor.IsReadOnly = (bool)e.NewValue;
    }

    // ── EditorTheme ──

    public static readonly DependencyProperty EditorThemeProperty =
        DependencyProperty.Register(
            nameof(EditorTheme), typeof(EditorTheme), typeof(StEditorControl),
            new FrameworkPropertyMetadata(EditorTheme.Dark, OnEditorThemeChanged));

    public EditorTheme EditorTheme
    {
        get => (EditorTheme)GetValue(EditorThemeProperty);
        set => SetValue(EditorThemeProperty, value);
    }

    private static void OnEditorThemeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StEditorControl control)
            control.ApplyTheme((EditorTheme)e.NewValue);
    }

    // ── NavigateToRequest ──

    public static readonly DependencyProperty NavigateToRequestProperty =
        DependencyProperty.Register(
            nameof(NavigateToRequest),
            typeof(NavigationRequest),
            typeof(StEditorControl),
            new FrameworkPropertyMetadata(null, OnNavigateToRequestChanged));

    public NavigationRequest? NavigateToRequest
    {
        get => (NavigationRequest?)GetValue(NavigateToRequestProperty);
        set => SetValue(NavigateToRequestProperty, value);
    }

    private static void OnNavigateToRequestChanged(
        DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StEditorControl ctrl && e.NewValue is NavigationRequest req)
            ctrl.GoToLocation(req);
    }

    // ── UnderlineRanges ──

    public static readonly DependencyProperty UnderlineRangesProperty =
        DependencyProperty.Register(
            nameof(UnderlineRanges),
            typeof(IList<TextUnderlineRange>),
            typeof(StEditorControl),
            new FrameworkPropertyMetadata(null, OnUnderlineRangesChanged));

    public IList<TextUnderlineRange>? UnderlineRanges
    {
        get => (IList<TextUnderlineRange>?)GetValue(UnderlineRangesProperty);
        set => SetValue(UnderlineRangesProperty, value);
    }

    private static void OnUnderlineRangesChanged(
        DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StEditorControl ctrl)
            ctrl._squigglyRenderer?.UpdateRanges(
                e.NewValue as IList<TextUnderlineRange>);
    }

    // ── AnalysisData ──

    public static readonly DependencyProperty AnalysisDataProperty =
        DependencyProperty.Register(
            nameof(AnalysisData),
            typeof(EditorAnalysisData),
            typeof(StEditorControl),
            new FrameworkPropertyMetadata(null, OnAnalysisDataChanged));

    public EditorAnalysisData? AnalysisData
    {
        get => (EditorAnalysisData?)GetValue(AnalysisDataProperty);
        set => SetValue(AnalysisDataProperty, value);
    }

    private static void OnAnalysisDataChanged(
        DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StEditorControl control)
            control.ApplyAnalysisData(e.NewValue as EditorAnalysisData);
    }

    // ══════════════════════════════════════════════════════════════
    //  断点 / 执行行（调试可视反馈）
    //
    //  行号一律 1 起，与 AvalonEdit 的 TextLocation.Line、
    //  诊断 SourceSpan.Line、SourceMap 的 line 保持同一基准。
    // ══════════════════════════════════════════════════════════════

    /// <summary>有断点的行号（1 起）。边栏据此画红点。</summary>
    public static readonly DependencyProperty BreakpointLinesProperty =
        DependencyProperty.Register(
            nameof(BreakpointLines),
            typeof(IList<int>),
            typeof(StEditorControl),
            new FrameworkPropertyMetadata(
                null, OnBreakpointLinesChanged));

    public IList<int>? BreakpointLines
    {
        get => (IList<int>?)GetValue(BreakpointLinesProperty);
        set => SetValue(BreakpointLinesProperty, value);
    }

    private static void OnBreakpointLinesChanged(
        DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StEditorControl control)
            control._breakpointMargin?.UpdateBreakpoints(e.NewValue as IList<int>);
    }

    /// <summary>当前执行行（1 起，0 = 无）。暂停时整行高亮。</summary>
    public static readonly DependencyProperty ExecutionLineProperty =
        DependencyProperty.Register(
            nameof(ExecutionLine),
            typeof(int),
            typeof(StEditorControl),
            new FrameworkPropertyMetadata(0, OnExecutionLineChanged));

    public int ExecutionLine
    {
        get => (int)GetValue(ExecutionLineProperty);
        set => SetValue(ExecutionLineProperty, value);
    }

    private static void OnExecutionLineChanged(
        DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not StEditorControl control)
            return;

        control._executionRenderer?.SetExecutionLine((int)e.NewValue);

        // 单步时当前行会一路往下走。只"滚到可见"的话，往下几行就顶到屏幕
        // 边缘，再走下去还得自己拖滚动条 —— 直接对齐到视口垂直中线。
        // 0 是清除高亮（停止调试 / 尚未暂停），不滚动。
        if ((int)e.NewValue > 0)
            control.CenterExecutionLine((int)e.NewValue);
    }

    /// <summary>
    /// 把指定行滚到视野正中。
    ///
    /// AvalonEdit 是懒渲染的，视野外的行根本没有 VisualLine，所以先用
    /// ScrollToLine 保证它被渲染，再等这一帧布局完成、量准它在哪之后
    /// 才去调 VerticalOffset —— 否则拿到的 VisualLines 里还没有它。
    /// </summary>
    private void CenterExecutionLine(int lineNum)
    {
        if (lineNum <= 0 || lineNum > Editor.Document.LineCount)
            return;

        Editor.ScrollToLine(lineNum);

        Editor.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                var tv = Editor.TextArea.TextView;
                if (!tv.VisualLinesValid || tv.VisualLines.Count == 0)
                    return;

                foreach (var vl in tv.VisualLines)
                {
                    if (vl.FirstDocumentLine.LineNumber != lineNum)
                        continue;

                    double viewport = tv.RenderSize.Height;
                    if (viewport <= 0)
                        return;

                    // 目标偏移 = 该行顶端 - 半个视口，再夹回合法范围
                    double target = vl.VisualTop - (viewport - vl.Height) / 2;
                    double max = Math.Max(0, tv.DocumentHeight - viewport);
                    target = Math.Max(0, Math.Min(target, max));

                    // TextView.VerticalOffset 只读，得借道承载它的 ScrollViewer。
                    // 用偏移量相等来确认找的就是这个，别误伤别的滚动容器。
                    var sv = FindScrollViewer(tv);
                    if (sv is null || Math.Abs(sv.VerticalOffset - tv.VerticalOffset) > 0.5)
                        return;

                    sv.ScrollToVerticalOffset(target);
                    return;
                }
            }));
    }

    /// <summary>先往上找再往下找，定位承载 TextView 的那个 ScrollViewer。</summary>
    private static ScrollViewer? FindScrollViewer(DependencyObject node)
    {
        for (DependencyObject? p = node; p != null; p = VisualTreeHelper.GetParent(p))
            if (p is ScrollViewer up)
                return up;

        return FindScrollViewerDown(node);
    }

    private static ScrollViewer? FindScrollViewerDown(DependencyObject node)
    {
        if (node is ScrollViewer sv)
            return sv;

        int n = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < n; i++)
        {
            var found = FindScrollViewerDown(VisualTreeHelper.GetChild(node, i));
            if (found != null)
                return found;
        }
        return null;
    }

    // ══════════════════════════════════════════════════════════════
    //  事件
    // ══════════════════════════════════════════════════════════════

    public event EventHandler? TextContentChanged;

    /// <summary>边栏被点击，请求切换某行断点。参数为 1 起行号。</summary>
    public event EventHandler<int>? BreakpointToggled;

    // ══════════════════════════════════════════════════════════════
    //  构造 & 初始化
    // ══════════════════════════════════════════════════════════════

    public StEditorControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Editor.TextArea.SelectionCornerRadius = 2;
        Editor.Options.AllowScrollBelowDocument = true;

        // ── 可视层：OnUnloaded 会拆掉，切标签页回来必须重建 ──
        if (_squigglyRenderer == null)
        {
            _squigglyRenderer = new SquigglyUnderlineRenderer(Editor.TextArea.TextView);
            Editor.TextArea.TextView.BackgroundRenderers.Add(_squigglyRenderer);
            if (UnderlineRanges is { } existing)
                _squigglyRenderer.UpdateRanges(existing);
        }

        // ── 断点边栏（行号左侧，点击切换断点）──
        if (_breakpointMargin == null)
        {
            _breakpointMargin = new BreakpointMargin(Editor.TextArea.TextView);
            _breakpointMargin.BreakpointToggled += OnMarginBreakpointToggled;
            Editor.TextArea.LeftMargins.Insert(0, _breakpointMargin);
        }
        _breakpointMargin.UpdateBreakpoints(BreakpointLines);

        // ── 当前执行行高亮（调试暂停时）──
        if (_executionRenderer == null)
        {
            _executionRenderer = new ExecutionLineHighlighter(Editor.TextArea.TextView);
            Editor.TextArea.TextView.BackgroundRenderers.Add(_executionRenderer);
        }
        _executionRenderer.SetExecutionLine(ExecutionLine);

        // ── 一次性初始化：切标签页回来时绝不能重来一遍，否则
        //    LineTransformers 里会堆出第二个高亮器、FoldingManager 装两份、
        //    TextArea 上的事件订阅全部重复。──
        if (_featuresInstalled)
        {
            _stateRebuilder?.RebuildImmediate(Editor.Document);
            return;
        }
        _featuresInstalled = true;

        // ── 初始化功能模块 ──
        _highlighter = new StSyntaxHighlightingTransformer();   // ← 先创建
        Editor.TextArea.TextView.LineTransformers.Add(_highlighter);

        var foldingManager = FoldingManager.Install(Editor.TextArea);
        var foldingStrategy = new StFoldingStrategy();

        _autoAssign = new AutoAssign();
        _autoUppercase = new AutoUppercase();
        _autoEndBlock = new AutoEndBlock();
        _completion = new Completion();
        _stateRebuilder = new StateRebuilder(
            _highlighter, foldingStrategy, foldingManager,
            () => Editor.TextArea.TextView.InvalidateVisual());

        _completion.AfterInsert = () => _autoEndBlock.TryInsert(Editor);

        _enteredChain = new IEditorFeature[]
        {
        _completion, _autoAssign, _autoUppercase, _autoEndBlock,
        };

        // ── 注册事件 ──
        Editor.TextChanged += OnEditorTextChanged;
        Editor.TextArea.TextEntered += (_, e2) => DispatchTextEntered(e2.Text);
        Editor.TextArea.TextEntering += OnTextEntering;
        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateStatus();
        Editor.TextArea.SelectionChanged += (_, _) => UpdateStatus();

        // ── 右键菜单 ──
        InstallContextMenu();

        // ── 同步依赖属性 ──
        Editor.Text = Text ?? string.Empty;
        Editor.IsReadOnly = IsReadOnly;
        ApplyZoom();
        ApplyAnalysisData(AnalysisData);

        // ── 初始构建 ──
        _stateRebuilder.RebuildImmediate(Editor.Document);

        // ── 主题必须最后应用（_highlighter 已就绪）──
        ApplyTheme(EditorTheme);

        UpdateStatus();
    }

    /// <summary>
    /// 只做"暂停"，不做"拆除"。
    ///
    /// WPF 的 TabControl 切走标签页时会把控件移出可视树（触发 Unloaded），
    /// 切回来再触发 Loaded。所以这里拆掉的可视层，必须能在 OnLoaded 里原样
    /// 重建；而那些拆不干净的一次性东西（匿名 lambda 订阅、FoldingManager）
    /// 一律不动，靠 _featuresInstalled 保证只装一次。
    ///
    /// 早先这里还写了 Loaded -= OnLoaded / Unloaded -= OnUnloaded，结果是
    /// 切一次标签页之后边栏、语法高亮、波浪线全部永久消失，再也装不回来。
    /// </summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_squigglyRenderer != null)
        {
            Editor.TextArea.TextView.BackgroundRenderers.Remove(_squigglyRenderer);
            _squigglyRenderer = null;   // 置空，OnLoaded 才会重建
        }

        if (_executionRenderer != null)
        {
            Editor.TextArea.TextView.BackgroundRenderers.Remove(_executionRenderer);
            _executionRenderer = null;
        }

        if (_breakpointMargin != null)
        {
            _breakpointMargin.BreakpointToggled -= OnMarginBreakpointToggled;
            Editor.TextArea.LeftMargins.Remove(_breakpointMargin);
            _breakpointMargin = null;
        }

        _stateRebuilder?.Stop();
        _completion?.Close();
    }

    private void OnMarginBreakpointToggled(object? sender, int line) =>
        BreakpointToggled?.Invoke(this, line);

    // ══════════════════════════════════════════════════════════════
    //  主题
    // ══════════════════════════════════════════════════════════════

    private void ApplyTheme(EditorTheme theme)
    {
        var colors = theme == EditorTheme.Light
            ? EditorThemes.Light
            : EditorThemes.Dark;

        // ① XAML DynamicResource
        colors.ApplyTo(this.Resources);

        // ② AvalonEdit 属性
        Editor.TextArea.SelectionBrush = new SolidColorBrush(colors.SelectionBrush);
        Editor.TextArea.TextView.CurrentLineBackground =
            new SolidColorBrush(colors.CurrentLineBackground);
        Editor.TextArea.TextView.CurrentLineBorder =
            new Pen(new SolidColorBrush(colors.CurrentLineBorder), 1);

        // ③ 语法高亮颜色
        _highlighter?.UpdateSyntaxColors(colors.Syntax);

        // ④ 补全窗口主题
        if (_completion is not null)
            _completion.Theme = theme == EditorTheme.Light
                ? CompletionThemeColors.Light
                : CompletionThemeColors.Dark;

        // ⑤ 强制重绘
        Editor.TextArea.TextView.Redraw();
        Editor.TextArea.TextView.InvalidateVisual();

        // ⑥ 状态栏
        UpdateStatus();
    }

    private void ApplyAnalysisData(EditorAnalysisData? data)
    {
        if (_completion is not null)
            _completion.AnalysisData = data;

        if (_squigglyRenderer is null) return;

        if (data?.Diagnostics is { Count: > 0 } diags) {
            var ranges = new List<TextUnderlineRange>();
            foreach (var d in diags) {
                var color = d.Severity == DiagnosticSeverity.Error
                    ? Colors.OrangeRed
                    : Colors.Goldenrod;
                ranges.Add(new TextUnderlineRange(d.Offset, d.Length, color));
            }
            _squigglyRenderer.UpdateRanges(ranges);
        } else {
            _squigglyRenderer.UpdateRanges(null);
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  文本同步
    // ══════════════════════════════════════════════════════════════

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        _isSyncingText = true;
        Text = Editor.Text;
        _isSyncingText = false;

        _stateRebuilder.OnTextChanged(Editor.Document);
        UpdateStatus();
        TextContentChanged?.Invoke(this, EventArgs.Empty);
    }

    // ══════════════════════════════════════════════════════════════
    //  事件分发
    // ══════════════════════════════════════════════════════════════

    private void DispatchTextEntered(string text)
    {
        foreach (var feature in _enteredChain)
            feature.OnTextEntered(Editor, text);
    }

    private void OnTextEntering(object? sender, TextCompositionEventArgs e)
    {
        if (e.Text.Length > 0 && _completion.IsOpen) {
            if (!char.IsLetterOrDigit(e.Text[0]) && e.Text[0] != '_')
                _completion.Close();
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  缩放
    // ══════════════════════════════════════════════════════════════

    private void Editor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        ZoomLevel = e.Delta > 0
            ? Math.Min(ZoomLevel + 10, 500)
            : Math.Max(ZoomLevel - 10, 10);
        e.Handled = true;
    }

    private void ZoomText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ZoomLevel = 100;
    }

    private void ApplyZoom()
    {
        Editor.FontSize = BaseFontSize * ZoomLevel / 100.0;
        ZoomText.Text = $"{ZoomLevel}%";
    }

    // ══════════════════════════════════════════════════════════════
    //  状态栏
    // ══════════════════════════════════════════════════════════════

    private void UpdateStatus()
    {
        UpdateCursorPosition();
        UpdateStats();
    }

    private void UpdateCursorPosition()
    {
        CursorPositionText.Text =
            $"行 {Editor.TextArea.Caret.Line}, 列 {Editor.TextArea.Caret.Column}";
    }

    private void UpdateStats()
    {
        var selLength = Editor.SelectionLength;
        var colors = EditorTheme == EditorTheme.Light
            ? EditorThemes.Light
            : EditorThemes.Dark;

        if (selLength == 0) {
            StatsText.Text = $"{Editor.Text.Length} 个字符  {Editor.LineCount} 行";
            StatsText.Foreground = new SolidColorBrush(colors.StatsNormal);
        } else {
            var selectedText = Editor.SelectedText;
            int lineCount = 1;
            foreach (char c in selectedText)
                if (c == '\n') lineCount++;

            StatsText.Text = $"({selLength} 字符  {lineCount} 行)";
            StatsText.Foreground = new SolidColorBrush(colors.StatsHighlight);
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  跳转（内部实现）
    // ══════════════════════════════════════════════════════════════

    private void GoToLocation(NavigationRequest req)
    {
        var doc = Editor.Document;
        if (doc == null) return;

        int lineNum = Math.Max(1, Math.Min(req.Line, doc.LineCount));
        var line = doc.GetLineByNumber(lineNum);

        int offset = req.Column > 0
            ? Math.Min(line.Offset + req.Column - 1, line.EndOffset)
            : line.Offset;

        Editor.CaretOffset = offset;
        Editor.Select(offset, 0);
        Editor.ScrollToLine(lineNum);
        Editor.Focus();
    }

    // ══════════════════════════════════════════════════════════════
    //  右键菜单（编辑器通用操作）
    //
    //  撤销/重做/剪贴板/全选直接调编辑器 API；格式化由 IDE 层 completion ——
    //  本控件只发 FormatDocumentRequested 事件，不依赖 IronST.Frontend。
    //  仅在首次 Loaded 的一次性安装区调用，切标签页不会重复订阅。
    // ══════════════════════════════════════════════════════════════

    /// <summary>右键菜单点击“格式化文档”。由 IDE 层订阅转发到格式化命令。</summary>
    public event EventHandler? FormatDocumentRequested;

    private MenuItem? _menuUndo, _menuRedo, _menuCut, _menuCopy, _menuPaste, _menuDelete, _menuSelectAll, _menuFormat;

    private void InstallContextMenu()
    {
        var menu = new ContextMenu();

        _menuUndo = AddContextMenuItem(menu, "撤销(_U)", "Ctrl+Z", () => Editor.Undo());
        _menuRedo = AddContextMenuItem(menu, "重做(_R)", "Ctrl+Y", () => Editor.Redo());
        menu.Items.Add(new Separator());
        _menuCut = AddContextMenuItem(menu, "剪切(_T)", "Ctrl+X", () => Editor.Cut());
        _menuCopy = AddContextMenuItem(menu, "复制(_C)", "Ctrl+C", () => Editor.Copy());
        _menuPaste = AddContextMenuItem(menu, "粘贴(_P)", "Ctrl+V", () => Editor.Paste());
        _menuDelete = AddContextMenuItem(menu, "删除(_D)", null, () => Editor.Delete());
        menu.Items.Add(new Separator());
        _menuSelectAll = AddContextMenuItem(menu, "全选(_A)", "Ctrl+A", () => Editor.SelectAll());
        _menuFormat = AddContextMenuItem(menu, "格式化文档(_F)", "Ctrl+Shift+F",
            () => FormatDocumentRequested?.Invoke(this, EventArgs.Empty));

        Editor.ContextMenu = menu;
        Editor.ContextMenuOpening += (_, _) => RefreshContextMenuStates();
    }

    private static MenuItem AddContextMenuItem(ContextMenu menu, string header, string? gesture, Action action)
    {
        var item = new MenuItem { Header = header, InputGestureText = gesture ?? string.Empty };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
        return item;
    }

    /// <summary>菜单弹出前刷新各项可用状态（只读/无选中/剪贴板无文本等）。</summary>
    private void RefreshContextMenuStates()
    {
        var hasSelection = Editor.SelectionLength > 0;
        var readOnly = Editor.IsReadOnly;
        var hasText = Editor.Text.Length > 0;
        var undoStack = Editor.Document.UndoStack;

        if (_menuUndo is { } undo) undo.IsEnabled = undoStack.CanUndo;
        if (_menuRedo is { } redo) redo.IsEnabled = undoStack.CanRedo;
        if (_menuCut is { } cut) cut.IsEnabled = !readOnly && hasSelection;
        if (_menuCopy is { } copy) copy.IsEnabled = hasSelection;
        if (_menuPaste is { } paste) paste.IsEnabled = !readOnly && Clipboard.ContainsText();
        if (_menuDelete is { } delete) delete.IsEnabled = !readOnly && hasSelection;
        if (_menuSelectAll is { } selectAll) selectAll.IsEnabled = hasText;
        if (_menuFormat is { } format) format.IsEnabled = !readOnly && hasText;
    }

    // ══════════════════════════════════════════════════════════════
    //  公共 API
    // ══════════════════════════════════════════════════════════════

    public ICSharpCode.AvalonEdit.TextEditor GetEditor() => Editor;

    /// <summary>
    /// 把整篇文本一次性替换成 <paramref name="newText"/>（格式化器写回专用）。
    ///
    /// 用 Document.RunUpdate() + Document.Replace() 里层替换，保证折叠成单个
    /// 撤销操作，并且不碰 Editor.Text 属性 —— 文本变更沿正常 TextChanged →
    /// Text DP 路径同步，光标 / 选区由这里恢复（简单夹到新文本范围内）。
    ///
    /// 内容与 newText 相同时直接返回：不产生撤销记录、不把文档置脏。
    /// </summary>
    /// <param name="newText">替换后的全文。</param>
    /// <param name="restoreCaret">
    /// 可选回调；替换完成后收到恢复后的选中范围（文本变短时已夹到合法范围）。
    /// 注意 AvalonEdit 没有 TextRange 类型，用 TextSegment 表示选区。
    /// </param>
    public void ApplyTextTransform(string newText, Action<TextSegment>? restoreCaret)
    {
        var document = Editor.Document;
        if (document == null || newText is null)
            return;

        if (string.Equals(document.Text, newText, StringComparison.Ordinal))
            return;

        var oldCaret = Editor.CaretOffset;
        var oldSelStart = Editor.SelectionStart;
        var oldSelLength = Editor.SelectionLength;

        using (document.RunUpdate())
        {
            document.Replace(0, document.TextLength, newText);
        }

        var newLength = document.TextLength;
        var caret = Math.Min(oldCaret, newLength);
        var selStart = Math.Min(oldSelStart, newLength);
        var selLength = Math.Min(oldSelLength, newLength - selStart);

        if (selLength > 0)
            Editor.Select(selStart, selLength);
        else
            Editor.CaretOffset = caret;

        restoreCaret?.Invoke(new TextSegment { StartOffset = caret, EndOffset = caret + selLength });
    }

    public void GoToLine(int lineNumber)
    {
        Editor.ScrollToLine(lineNumber);
        var line = Editor.Document.GetLineByNumber(lineNumber);
        Editor.CaretOffset = line.Offset;
        Editor.Focus();
    }

    public void Select(int start, int length) => Editor.Select(start, length);

    public void Shutdown()
    {
        _stateRebuilder?.Stop();
        _completion?.Close();
        if (_squigglyRenderer != null)
            Editor.TextArea.TextView.BackgroundRenderers.Remove(_squigglyRenderer);
    }
}

// ══════════════════════════════════════════════════════════════════
//  枚举
// ══════════════════════════════════════════════════════════════════

public enum EditorTheme { Dark, Light }

// ══════════════════════════════════════════════════════════════════
//  模型类
// ══════════════════════════════════════════════════════════════════

public sealed class NavigationRequest
{
    public int Line { get; }
    public int Column { get; }

    public NavigationRequest(int line, int column = 0)
    {
        Line = Math.Max(1, line);
        Column = Math.Max(0, column);
    }
}

public sealed class TextUnderlineRange
{
    public int Offset { get; }
    public int Length { get; }
    public Color Color { get; }

    public TextUnderlineRange(int offset, int length, Color? color = null)
    {
        Offset = offset;
        Length = Math.Max(0, length);
        Color = color ?? Colors.OrangeRed;
    }
}

// ══════════════════════════════════════════════════════════════════
//  主题配色
// ══════════════════════════════════════════════════════════════════

public sealed class SyntaxHighlightColors
{
    public Color Comment { get; init; }
    public Color String { get; init; }
    public Color Number { get; init; }
    public Color Keyword { get; init; }
    public Color Type { get; init; }
    public Color Operator { get; init; }
    public Color DirectAddr { get; init; }
}

internal sealed class ThemeColors
{
    // ── 控件颜色 ──
    public Color ControlBackground { get; init; }
    public Color EditorForeground { get; init; }
    public Color LineNumbersForeground { get; init; }
    public Color StatusBarBackground { get; init; }
    public Color StatusBarForeground { get; init; }
    public Color SeparatorColor { get; init; }
    public Color StatsNormal { get; init; }
    public Color StatsHighlight { get; init; }
    public Color SelectionBrush { get; init; }
    public Color CurrentLineBackground { get; init; }
    public Color CurrentLineBorder { get; init; }

    // ── 语法高亮颜色 ──
    public required SyntaxHighlightColors Syntax { get; init; }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public void ApplyTo(ResourceDictionary resources)
    {
        resources["ControlBackground"] = Frozen(ControlBackground);
        resources["EditorForeground"] = Frozen(EditorForeground);
        resources["LineNumbersForeground"] = Frozen(LineNumbersForeground);
        resources["StatusBarBackground"] = Frozen(StatusBarBackground);
        resources["StatusBarForeground"] = Frozen(StatusBarForeground);
        resources["SeparatorColor"] = Frozen(SeparatorColor);
        resources["StatsNormal"] = Frozen(StatsNormal);
        resources["StatsHighlight"] = Frozen(StatsHighlight);
    }
}

internal static class EditorThemes
{
    // ── Catppuccin Mocha（深色）──
    public static readonly ThemeColors Dark = new()
    {
        ControlBackground = Color.FromRgb(0x1e, 0x1e, 0x2e),
        EditorForeground = Color.FromRgb(0xcd, 0xd6, 0xf4),
        LineNumbersForeground = Color.FromRgb(0x6c, 0x70, 0x86),
        StatusBarBackground = Color.FromRgb(0x18, 0x18, 0x25),
        StatusBarForeground = Color.FromRgb(0xa6, 0xad, 0xc8),
        SeparatorColor = Color.FromRgb(0x31, 0x32, 0x44),
        StatsNormal = Color.FromRgb(0xa6, 0xad, 0xc8),
        StatsHighlight = Color.FromRgb(0xf9, 0xe2, 0xaf),
        SelectionBrush = Color.FromArgb(120, 0x45, 0x47, 0x5a),
        CurrentLineBackground = Color.FromArgb(30, 0x89, 0xb4, 0xfa),
        CurrentLineBorder = Color.FromArgb(60, 0x89, 0xb4, 0xfa),
        Syntax = new SyntaxHighlightColors
        {
            Comment = Color.FromRgb(108, 112, 134),
            String = Color.FromRgb(166, 227, 161),
            Number = Color.FromRgb(250, 179, 135),
            Keyword = Color.FromRgb(203, 166, 247),
            Type = Color.FromRgb(137, 180, 250),
            Operator = Color.FromRgb(249, 226, 175),
            DirectAddr = Color.FromRgb(137, 220, 235),
        },
    };

    // ── Catppuccin Latte（浅色）──
    public static readonly ThemeColors Light = new()
    {
        ControlBackground = Color.FromRgb(0xef, 0xf1, 0xf5),
        EditorForeground = Color.FromRgb(0x4c, 0x4f, 0x69),
        LineNumbersForeground = Color.FromRgb(0x9c, 0xa0, 0xb0),
        StatusBarBackground = Color.FromRgb(0xe6, 0xe9, 0xef),
        StatusBarForeground = Color.FromRgb(0x5c, 0x5f, 0x77),
        SeparatorColor = Color.FromRgb(0xcc, 0xd0, 0xda),
        StatsNormal = Color.FromRgb(0x5c, 0x5f, 0x77),
        StatsHighlight = Color.FromRgb(0xdf, 0x8e, 0x1d),
        SelectionBrush = Color.FromArgb(100, 0x72, 0x87, 0xfd),
        CurrentLineBackground = Color.FromArgb(30, 0x1e, 0x66, 0xf5),
        CurrentLineBorder = Color.FromArgb(60, 0x1e, 0x66, 0xf5),
        Syntax = new SyntaxHighlightColors
        {
            Comment = Color.FromRgb(156, 160, 176),
            String = Color.FromRgb(64, 160, 43),
            Number = Color.FromRgb(254, 100, 11),
            Keyword = Color.FromRgb(136, 57, 239),
            Type = Color.FromRgb(30, 102, 245),
            Operator = Color.FromRgb(223, 142, 29),
            DirectAddr = Color.FromRgb(23, 146, 153),
        },
    };
}

// ══════════════════════════════════════════════════════════════════
//  波浪线渲染器
// ══════════════════════════════════════════════════════════════════

internal sealed class SquigglyUnderlineRenderer : IBackgroundRenderer
{
    private readonly TextView _textView;
    private IList<TextUnderlineRange> _ranges = Array.Empty<TextUnderlineRange>();
    private readonly Dictionary<Color, Pen> _penCache = new();

    public KnownLayer Layer => KnownLayer.Selection;

    public SquigglyUnderlineRenderer(TextView textView)
    {
        _textView = textView ?? throw new ArgumentNullException(nameof(textView));
    }

    public void UpdateRanges(IList<TextUnderlineRange>? ranges)
    {
        _ranges = ranges ?? Array.Empty<TextUnderlineRange>();
        _textView.InvalidateLayer(Layer);
    }

    public void Draw(TextView textView, DrawingContext dc)
    {
        if (!textView.VisualLinesValid || _ranges.Count == 0)
            return;

        foreach (var range in _ranges) {
            if (range.Length <= 0) continue;
            DrawSingleRange(textView, dc, range);
        }
    }

    private void DrawSingleRange(TextView textView, DrawingContext dc, TextUnderlineRange range)
    {
        int docLen = textView.Document.TextLength;
        int rangeEnd = Math.Min(range.Offset + range.Length, docLen);
        if (range.Offset >= rangeEnd) return;

        foreach (var vl in textView.VisualLines) {
            int vlStart = vl.FirstDocumentLine.Offset;
            int vlEnd = vl.LastDocumentLine.EndOffset;

            if (rangeEnd <= vlStart || range.Offset >= vlEnd)
                continue;

            int segStart = Math.Max(range.Offset, vlStart);
            int segEnd = Math.Min(rangeEnd, vlEnd);

            var segment = new TextSegment { StartOffset = segStart, Length = segEnd - segStart };

            var builder = new BackgroundGeometryBuilder
            {
                AlignToWholePixels = true
            };
            builder.AddSegment(textView, segment);
            var geo = builder.CreateGeometry();
            if (geo == null) continue;

            double bottom = geo.Bounds.Bottom + 1;
            var squiggle = BuildWavyLine(geo.Bounds.Left, geo.Bounds.Right, bottom);
            dc.DrawGeometry(null, GetPen(range.Color), squiggle);
        }
    }

    private Pen GetPen(Color color)
    {
        if (!_penCache.TryGetValue(color, out var pen)) {
            pen = new Pen(new SolidColorBrush(color), 1.0);
            pen.Freeze();
            _penCache[color] = pen;
        }
        return pen;
    }

    private static Geometry BuildWavyLine(double x1, double x2, double y)
    {
        var sg = new StreamGeometry();
        using (var ctx = sg.Open()) {
            ctx.BeginFigure(new Point(x1, y), false, false);

            const double step = 4.0;
            const double amplitude = 2.0;
            double x = x1;
            bool up = true;

            while (x < x2) {
                double midX = x + step / 2;
                double nextX = Math.Min(x + step, x2);
                double ctrlY = up ? y - amplitude : y + amplitude;

                ctx.QuadraticBezierTo(
                    new Point(midX, ctrlY),
                    new Point(nextX, y),
                    isStroked: true,
                    isSmoothJoin: false);

                x = nextX;
                up = !up;
            }
        }
        sg.Freeze();
        return sg;
    }
}

// ══════════════════════════════════════════════════════════════════
//  断点边栏
//
//  挂在 TextArea.LeftMargins 上，位于行号区外侧。画红点并在点击时
//  触发 BreakpointToggled。行号 1 起。
//
//  坐标换算：边栏与 TextView 纵向同源，故 margin 内的 Y 加上
//  TextView.VerticalOffset 即得 GetVisualLineFromVisualTop 所需的
//  文档空间 visualTop。
// ══════════════════════════════════════════════════════════════════

internal sealed class BreakpointMargin : AbstractMargin
{
    /// <summary>
    /// 边栏宽度（px）。16px 太窄，实际用下来跟留白没区别，用户不会意识到
    /// 这条能点；22px 配合右侧分隔线能看出它是一块独立区域。
    /// </summary>
    public const double DefaultWidth = 22;

    // 断点红在深浅主题下观感一致（VS Code 同样不随主题变色），故不接主题。
    private static readonly Brush DotBrush = MakeFrozen(0xE5, 0x39, 0x35);
    private static readonly Pen HoverPen = MakeFrozenPen(0x88, 0x50, 0x4E);
    private static readonly Pen BorderPen = MakeFrozenPen(0x58, 0x5B, 0x70);

    // 底色有双重作用，缺一不可：
    //  ① 命中测试 —— WPF 只对"真正绘制出来的几何"做命中测试。之前空闲态
    //     什么都不画，鼠标会直接穿过边栏打到底下的 TextView，边栏收不到
    //     任何点击（表现为"只能先定位光标、再按 F9"）。
    //  ② 视觉 —— 比编辑区略深，让这 22px 读起来是一块独立区域而非留白，
    //     用户才知道那里能点。
    private static readonly Brush GutterBrush = MakeFrozen(0x18, 0x18, 0x25);
    private static readonly Brush HoverBgBrush = MakeFrozen(0x2A, 0x2A, 0x3D);

    private IList<int> _breakpoints = Array.Empty<int>();
    private int _hoverLine = -1;

    /// <summary>请求切换断点，参数为 1 起行号。</summary>
    public event EventHandler<int>? BreakpointToggled;

    public BreakpointMargin(TextView textView)
    {
        TextView = textView;
        Cursor = Cursors.Hand;
        IsHitTestVisible = true;
        // 光有窄条不够，得让人知道它是干什么的。悬停时浮出来说明用法。
        ToolTip = "点击切换断点";
    }

    /// <summary>刷新红点集合。</summary>
    public void UpdateBreakpoints(IList<int>? lines)
    {
        _breakpoints = lines ?? Array.Empty<int>();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(DefaultWidth, 0);

    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        base.OnTextViewChanged(oldTextView, newTextView);
        // 滚动/重绘时 VisualLine 的 Y 会变，必须重画，否则红点会"粘"在原地。
        if (oldTextView != null)
            oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
        if (newTextView != null)
            newTextView.VisualLinesChanged += OnVisualLinesChanged;
    }

    private void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        // 必须无条件先铺底，且要放在下面那个 early return 之前 —— 空文档、
        // 尚未排版时同样要有可命中的几何，否则边栏在那些状态下又是死区。
        dc.DrawRectangle(GutterBrush, null, new Rect(RenderSize));

        var tv = TextView;
        if (tv == null || !tv.VisualLinesValid || tv.VisualLines.Count == 0)
            return;

        // 与行号区之间的分隔线
        double borderX = RenderSize.Width - 0.5;
        dc.DrawLine(BorderPen, new Point(borderX, 0), new Point(borderX, RenderSize.Height));

        foreach (var vl in tv.VisualLines)
        {
            int line = vl.FirstDocumentLine.LineNumber;
            double top = vl.VisualTop - tv.VerticalOffset;
            double cy = top + vl.Height / 2;

            double cx = RenderSize.Width / 2;

            if (_breakpoints.Contains(line))
            {
                dc.DrawEllipse(DotBrush, null, new Point(cx, cy), 5, 5);
            }
            else if (line == _hoverLine)
            {
                // 悬停：整行铺提示底色 + 空心圈（VS Code 的"幽灵断点"同款）。
                // 早先只画实心暗红点，反而像是已经断上了，容易误读。
                dc.DrawRectangle(HoverBgBrush, null, new Rect(0, top, RenderSize.Width, vl.Height));
                dc.DrawEllipse(null, HoverPen, new Point(cx, cy), 5, 5);
            }
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        int? line = HitTestLine(e);
        if (line == null)
            return;

        BreakpointToggled?.Invoke(this, line.Value);
        e.Handled = true;   // 别把点击传给编辑器，否则光标会跳到那一行
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int? line = HitTestLine(e);
        int newHover = line ?? -1;
        if (newHover != _hoverLine)
        {
            _hoverLine = newHover;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverLine != -1)
        {
            _hoverLine = -1;
            InvalidateVisual();
        }
    }

    /// <summary>鼠标位置 → 1 起文档行号。落在可视行之外时返回 null。</summary>
    private int? HitTestLine(MouseEventArgs e)
    {
        var tv = TextView;
        if (tv == null || !tv.VisualLinesValid || tv.VisualLines.Count == 0)
            return null;

        double visualTop = e.GetPosition(tv).Y + tv.VerticalOffset;
        var vl = tv.GetVisualLineFromVisualTop(visualTop);
        return vl?.FirstDocumentLine.LineNumber;
    }

    private static Brush MakeFrozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Pen MakeFrozenPen(byte r, byte g, byte b)
    {
        var pen = new Pen(MakeFrozen(r, g, b), 1);
        pen.Freeze();
        return pen;
    }
}

// ══════════════════════════════════════════════════════════════════
//  当前执行行高亮
//
//  调试暂停时把当前行整行铺一层底色 + 上下边框，比状态栏里那行
//  "已暂停 @ PC=…" 直观得多。
// ══════════════════════════════════════════════════════════════════

internal sealed class ExecutionLineHighlighter : IBackgroundRenderer
{
    private readonly TextView _textView;
    private readonly Brush _fill;
    private readonly Pen _border;

    private int _line;      // 1 起，0 = 不高亮

    public KnownLayer Layer => KnownLayer.Background;

    public ExecutionLineHighlighter(TextView textView)
    {
        _textView = textView;

        var fill = new SolidColorBrush(Color.FromArgb(60, 0x89, 0xB4, 0xFA));
        fill.Freeze();
        _fill = fill;

        var borderPen = new Pen(new SolidColorBrush(Color.FromArgb(160, 0x89, 0xB4, 0xFA)), 1);
        borderPen.Freeze();
        _border = borderPen;
    }

    /// <summary>设置当前执行行（1 起），0 取消高亮。</summary>
    public void SetExecutionLine(int line)
    {
        _line = line;
        _textView.InvalidateLayer(Layer);
    }

    public void Draw(TextView textView, DrawingContext dc)
    {
        if (_line <= 0 || !textView.VisualLinesValid || textView.VisualLines.Count == 0)
            return;

        foreach (var vl in textView.VisualLines)
        {
            if (vl.FirstDocumentLine.LineNumber != _line)
                continue;

            double top = vl.VisualTop - textView.VerticalOffset;
            var rect = new Rect(0, top, textView.RenderSize.Width, vl.Height);
            dc.DrawRectangle(_fill, _border, rect);
            return;     // 一行只会有一个视觉行
        }
    }
}
