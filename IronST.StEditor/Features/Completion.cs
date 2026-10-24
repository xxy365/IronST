using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using IronST.StEditor.Editor;
using System.Windows;
using System.Windows.Media;

namespace IronST.StEditor.Features;

/// <summary>
/// 补全窗口管理。
///
/// 行为：
/// - 字母/下划线输入 → 立即显示或过滤补全列表
/// - 补全窗口关闭后 → 触发 AfterInsert 回调（用于 AutoEndBlock 等后续处理）
/// - 非标识符输入 → TextEntering 中由 MainWindow 关闭窗口
/// </summary>
public class Completion : EditorFeatureBase
{
    private CompletionWindow? _window;
    private List<StCompletionData> _cached = new();
    private int _startOffset;

    // ★ 第①处：新增主题属性
    public CompletionThemeColors Theme { get; set; } = CompletionThemeColors.Dark;

    /// <summary>当前补全窗口是否打开</summary>
    public bool IsOpen => _window != null;

    /// <summary>
    /// 补全插入完成后的回调。
    /// MainWindow 注册此回调，用于触发 AutoEndBlock 等后续功能。
    /// </summary>
    public Action? AfterInsert { get; set; }

    /// <summary>外部注入的分析数据，由 StEditorControl 设置</summary>
    internal EditorAnalysisData? AnalysisData { get; set; }


    public override bool OnTextEntered(TextEditor editor, string inputText)
    {
        if (inputText.Length == 1 && (char.IsLetter(inputText[0]) || inputText[0] == '_')) {
            if (_window != null)
                Filter(editor);
            else
                Show(editor);
        }

        return false;
    }

    public void Close()
    {
        if (_window == null) return;
        _window?.Close();
    }

    // ══════════════════════════════════════════════════════════════
    //  内部实现
    // ══════════════════════════════════════════════════════════════

    private void Show(TextEditor editor)
    {
        var text = editor.Text;
        var offset = editor.CaretOffset;
        var partialStart = GetPartialWordStart(text, offset);

        var completions = StCompletionProvider.GetCompletions(text, offset, AnalysisData);
        if (completions.Count == 0) return;

        _cached = completions;
        _startOffset = partialStart;

        _window = new CompletionWindow(editor.TextArea)
        {
            StartOffset = partialStart,
            CloseAutomatically = true,
        };

        // ★ 第②处：用 Theme 属性替代硬编码颜色
        var t = Theme;
        _window.Background = t.Background;
        _window.Foreground = t.Foreground;
        _window.BorderBrush = t.Border;

        var listBox = _window.CompletionList.ListBox;
        listBox.Background = t.Background;
        listBox.Foreground = t.Foreground;
        listBox.BorderBrush = t.Border;

        // 选中项样式
        var itemStyle = new Style(typeof(System.Windows.Controls.ListBoxItem));
        itemStyle.Setters.Add(new Setter(
            System.Windows.Controls.Control.BackgroundProperty,
            Brushes.Transparent));
        itemStyle.Setters.Add(new Setter(
            System.Windows.Controls.Control.ForegroundProperty, t.Foreground));

        var selTrigger = new System.Windows.Trigger
        {
            Property = System.Windows.Controls.ListBoxItem.IsSelectedProperty,
            Value = true
        };
        selTrigger.Setters.Add(new Setter(
            System.Windows.Controls.Control.BackgroundProperty, t.SelectedBackground));
        selTrigger.Setters.Add(new Setter(
            System.Windows.Controls.Control.ForegroundProperty, t.SelectedForeground));
        itemStyle.Triggers.Add(selTrigger);

        var hoverTrigger = new System.Windows.Trigger
        {
            Property = System.Windows.Controls.ListBoxItem.IsMouseOverProperty,
            Value = true
        };
        hoverTrigger.Setters.Add(new Setter(
            System.Windows.Controls.Control.BackgroundProperty, t.HoverBackground));
        itemStyle.Triggers.Add(hoverTrigger);

        _window.Resources[typeof(System.Windows.Controls.ListBoxItem)] = itemStyle;

        foreach (var item in completions)
            _window.CompletionList.CompletionData.Add(item);

        _window.Closed += (_, _) => {
            var wasInserting = _window != null;
            _window = null;
            _cached.Clear();

            if (wasInserting)
                AfterInsert?.Invoke();
        };

        _window.Show();
    }

    private void Filter(TextEditor editor)
    {
        if (_window == null) return;

        var partial = editor.Text[_startOffset..editor.CaretOffset];

        var filtered = _cached
            .Where(c => c.Text.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (filtered.Count == 0) {
            Close();
            return;
        }

        var list = _window.CompletionList;
        list.CompletionData.Clear();
        foreach (var item in filtered)
            list.CompletionData.Add(item);
        list.SelectItem(partial);
    }

    private static int GetPartialWordStart(string text, int offset)
    {
        int start = offset - 1;
        while (start >= 0 && (char.IsLetterOrDigit(text[start]) || text[start] == '_'))
            start--;
        return start + 1;
    }
}

// ★ 第③处：新增主题数据类（放在 Completion 类后面）
public sealed class CompletionThemeColors
{
    public required SolidColorBrush Background { get; init; }
    public required SolidColorBrush Foreground { get; init; }
    public required SolidColorBrush Border { get; init; }
    public required SolidColorBrush SelectedBackground { get; init; }
    public required SolidColorBrush SelectedForeground { get; init; }
    public required SolidColorBrush HoverBackground { get; init; }

    private static SolidColorBrush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public static readonly CompletionThemeColors Dark = new()
    {
        Background = Freeze(Color.FromRgb(0x18, 0x18, 0x25)),
        Foreground = Freeze(Color.FromRgb(0xcd, 0xd6, 0xf4)),
        Border = Freeze(Color.FromRgb(0x31, 0x32, 0x44)),
        SelectedBackground = Freeze(Color.FromArgb(80, 0x89, 0xb4, 0xfa)),
        SelectedForeground = Freeze(Color.FromRgb(0xcd, 0xd6, 0xf4)),
        HoverBackground = Freeze(Color.FromArgb(40, 0xcd, 0xd6, 0xf4)),
    };

    public static readonly CompletionThemeColors Light = new()
    {
        Background = Freeze(Color.FromRgb(0xff, 0xff, 0xff)),
        Foreground = Freeze(Color.FromRgb(0x4c, 0x4f, 0x69)),
        Border = Freeze(Color.FromRgb(0xcc, 0xd0, 0xda)),
        SelectedBackground = Freeze(Color.FromArgb(80, 0x1e, 0x66, 0xf5)),
        SelectedForeground = Freeze(Color.FromRgb(0x4c, 0x4f, 0x69)),
        HoverBackground = Freeze(Color.FromArgb(40, 0x4c, 0x4f, 0x69)),
    };
}
