using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;
using IronST.StEditor.Editor;
using System.Windows.Threading;

namespace IronST.StEditor.Features;

/// <summary>
/// 块注释状态重建 + 代码折叠。
/// 文本变化后延迟 200ms 执行（防抖）。
/// </summary>
public class StateRebuilder
{
    private readonly DispatcherTimer _timer;
    private readonly StSyntaxHighlightingTransformer _highlighter;
    private readonly StFoldingStrategy _foldingStrategy;
    private readonly FoldingManager _foldingManager;
    private readonly Action _invalidateVisual;

    /// <summary>
    /// 初始化状态重建器。
    /// </summary>
    /// <param name="highlighter">语法高亮器（管理块注释行状态）</param>
    /// <param name="foldingStrategy">折叠策略</param>
    /// <param name="foldingManager">AvalonEdit 折叠管理器</param>
    /// <param name="invalidateVisual">触发 AvalonEdit 重绘的回调</param>
    public StateRebuilder(
        StSyntaxHighlightingTransformer highlighter,
        StFoldingStrategy foldingStrategy,
        FoldingManager foldingManager,
        Action invalidateVisual)
    {
        _highlighter = highlighter;
        _foldingStrategy = foldingStrategy;
        _foldingManager = foldingManager;
        _invalidateVisual = invalidateVisual;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += OnTimerTick;
    }

    private TextDocument? _pendingDocument;

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        if (_pendingDocument != null) {
            _highlighter.RebuildLineStates(_pendingDocument);
            _foldingStrategy.UpdateFoldings(_foldingManager, _pendingDocument);
            _invalidateVisual();
            _pendingDocument = null;
        }
    }

    /// <summary>
    /// 文本变化时调用：启动 200ms 防抖定时器
    /// </summary>
    public void OnTextChanged(TextDocument document)
    {
        _pendingDocument = document;
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>
    /// 立即重建（初始加载时使用，不等待定时器）
    /// </summary>
    public void RebuildImmediate(TextDocument document)
    {
        _highlighter.RebuildLineStates(document);
        _foldingStrategy.UpdateFoldings(_foldingManager, document);
    }

    public void Stop()
    {
        _timer.Stop();
    }
}
