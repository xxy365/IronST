using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using IronST.IDE.Messengers;
using IronST.StEditor;

namespace IronST.IDE.Models;

// ═══════════════════════════════════════════════════════
//  SolutionNode
// ═══════════════════════════════════════════════════════

public partial class SolutionNode : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    // 以下两个在原始代码里就是普通属性，没有 INPC，保持原样
    public string FilePath { get; set; } = string.Empty;
    public bool IsFolder { get; set; }

    // 依赖 IsFolder，但 IsFolder 构造后不会变，无需通知
    public string Icon => IsFolder ? "\u25B6" : "\u25A0";

    public ObservableCollection<SolutionNode> Children { get; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }
}

// ═══════════════════════════════════════════════════════
//  EditorViewModel
// ═══════════════════════════════════════════════════════

public partial class EditorViewModel : ObservableRecipient, IRecipient<ThemeChangedMessenger>
{
    public EditorViewModel()
    {
        IsActive = true;
    }

    // 内部状态，不是属性，保留字段
    private bool _suppressDirty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    public partial string FileName { get; set; } = "Untitled.st";

    public string FilePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Code { get; set; } = string.Empty;

    // 源生成器生成的 partial 钩子 —— Code 赋值之后触发
    partial void OnCodeChanged(string value)
    {
        if (!_suppressDirty)
            IsModified = true;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    public partial bool IsModified { get; set; }

    public string DisplayName => IsModified ? $"* {FileName}" : FileName;

    private StEditorControl? _editorControl;

    /// <summary>
    /// 编辑器边栏被点击，请求切换断点。参数为 1 起行号。
    /// 由 <see cref="EditorControl"/> 的 setter 自控件转发而来。
    /// </summary>
    public event Action<int>? BreakpointToggled;

    /// <summary>
    /// 右键菜单请求格式化本文档。由 StEditorControl 的菜单转发而来，IDE 层订阅后调格式化命令。
    /// </summary>
    public event EventHandler? FormatRequested;

    /// <summary>StEditorControl instance, set via Loaded event.</summary>
    public StEditorControl? EditorControl
    {
        get => _editorControl;
        set
        {
            if (_editorControl != null)
            {
                _editorControl.BreakpointToggled -= OnControlBreakpointToggled;
                _editorControl.FormatDocumentRequested -= OnControlFormatRequested;
            }

            _editorControl = value;

            if (value != null)
            {
                value.BreakpointToggled += OnControlBreakpointToggled;
                value.FormatDocumentRequested += OnControlFormatRequested;
                // 控件晚于分析就绪时（文件刚打开、标签页刚创建），补注入之前丢掉的波浪线
                if (LastAnalysisData != null)
                    value.AnalysisData = LastAnalysisData;
            }
        }
    }

    private void OnControlBreakpointToggled(object? sender, int line) =>
        BreakpointToggled?.Invoke(line);

    private void OnControlFormatRequested(object? sender, EventArgs e) =>
        FormatRequested?.Invoke(this, e);

    /// <summary>最近一次语义分析结果。控件未就绪时暂存，Loaded 后由 EditorControl setter 补注入。</summary>
    public EditorAnalysisData? LastAnalysisData { get; set; }

    public void LoadCode(string code)
    {
        _suppressDirty = true;
        Code = code;
        IsModified = false;
        _suppressDirty = false;
    }

    /// <summary>Release editor resources.</summary>
    public void Shutdown()
    {
        EditorControl?.Shutdown();
        EditorControl = null;
    }

    [ObservableProperty]
    public partial EditorTheme EditorTheme { get; set; } =
        ((App)App.Current).CurrentTheme == "Light" ? EditorTheme.Light : EditorTheme.Dark;

    void IRecipient<ThemeChangedMessenger>.Receive(ThemeChangedMessenger message)
    {
        EditorTheme = message.Theme == "Light" ? EditorTheme.Light : EditorTheme.Dark;
    }
}

// ═══════════════════════════════════════════════════════
//  以下都是纯 POCO，没有继承 ViewModelBase，
//  也没有 INotifyPropertyChanged，不需要转换
// ═══════════════════════════════════════════════════════

public class ErrorItem
{
    public string Severity { get; set; } = "Error";
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int Line { get; set; }
}

public class WatchItem
{
    public string Name { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class CallStackEntry
{
    public string MethodName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int Line { get; set; }
}

public class BreakpointInfo
{
    public string FilePath { get; set; } = string.Empty;
    public int Line { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string Condition { get; set; } = string.Empty;
}
