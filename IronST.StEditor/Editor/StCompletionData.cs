using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;

namespace IronST.StEditor.Editor;

public class StCompletionData : ICompletionData
{
    public StCompletionData(string text, string description = "", CompletionKind kind = CompletionKind.Keyword)
    {
        Text = text;
        _description = string.IsNullOrEmpty(description) ? null : description;
        Kind = kind;
        // Content 默认等于 Text，显示在补全列表中
        Content = text;
    }

    public string Text { get; }

    // ── ICompletionData.Content：补全列表中显示的内容 ──
    public object Content { get; }

    // ── ICompletionData.Description：悬停时的描述文本 ──
    private readonly object? _description;
    public object Description => _description ?? string.Empty;

    public CompletionKind Kind { get; }

    public ImageSource? Image => Kind switch
    {
        CompletionKind.Keyword => CreateColorIcon(203, 166, 247),
        CompletionKind.Type => CreateColorIcon(137, 180, 250),
        CompletionKind.Variable => CreateColorIcon(166, 227, 161),
        _ => null,
    };

    public double Priority => Kind switch
    {
        CompletionKind.Keyword => 3,
        CompletionKind.Type => 2,
        CompletionKind.Variable => 1,
        _ => 0,
    };

    public void Complete(
        TextArea textArea,
        ISegment completionSegment,
        EventArgs insertionRequestEventArgs)
    {
        textArea.Document.Replace(completionSegment, Text);
    }

    private static ImageSource? CreateColorIcon(byte r, byte g, byte b)
    {
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen()) {
            ctx.DrawRectangle(
                new SolidColorBrush(Color.FromRgb(r, g, b)),
                null,
                new System.Windows.Rect(2, 2, 12, 12));
        }
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            16, 16, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
