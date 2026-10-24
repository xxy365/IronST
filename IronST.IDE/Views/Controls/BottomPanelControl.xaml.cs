using System.Windows;
using System.Windows.Controls;

namespace IronST.IDE.Views.Controls;
/// <summary>
/// BottomPanelControl.xaml 的交互逻辑
/// </summary>
public partial class BottomPanelControl : UserControl
{
    public BottomPanelControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 输出框是否贴着底部。贴着才跟随新输出，别把正在上翻阅读的人拽回去。
    ///
    /// 只认人为滚动来翻转：内容变长本身也会引发 ScrollChanged（偏移与范围同时变），
    /// 若把它也算作滚动，追加一条文本就会把"贴着底部"冲掉，第二条起再也不跟随。
    /// </summary>
    private bool _stickToBottom = true;

    private void OnOutputTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_stickToBottom && sender is TextBox tb)
            tb.ScrollToEnd();
    }

    private void OnOutputScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // 内容变长导致的偏移变化不算人为滚动
        if (e.ExtentHeightChange != 0 || e.VerticalChange == 0)
            return;

        _stickToBottom = e.VerticalOffset >= e.ExtentHeight - e.ViewportHeight - 1;
    }

    /// <summary>
    /// 右键菜单挂上当前 DataContext。
    ///
    /// ContextMenu 派生自 Popup，不在 TextBox 的可视树里，WPF 的 DataContext 继承
    /// 管不到它 —— 实测即使菜单已打开仍是 null，菜单项的命令会一直绑不上。改绑
    /// PlacementTarget.DataContext 也不行：PlacementTarget 要等 popup 真正挂上宿主
    /// 才被赋值，绑定评估时是 null。故在加载与数据上下文变化时显式设一次。
    /// </summary>
    private void OnOutputLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb)
            return;

        SyncContextMenuDataContext(tb);

        // 主 VM 是窗口级 DataContext，中途换掉时菜单也要跟上
        tb.DataContextChanged += (_, _) => SyncContextMenuDataContext(tb);
    }

    private static void SyncContextMenuDataContext(TextBox tb)
    {
        if (tb.ContextMenu != null)
            tb.ContextMenu.DataContext = tb.DataContext;
    }
}
