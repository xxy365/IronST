using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace IronST.IDE.Views.Controls;
/// <summary>
/// SolutionExplorerControl.xaml 的交互逻辑
/// </summary>
public partial class SolutionExplorerControl : UserControl
{
    public SolutionExplorerControl()
    {
        InitializeComponent();
    }

    /// <summary>E4：双击 .st 文件节点 → 打开到编辑器。</summary>
    private void TreeView_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not Models.SolutionNode node)
            return;

        if (node.IsFolder || string.IsNullOrEmpty(node.FilePath))
            return;

        if (DataContext is ViewModels.MainWindowViewModel vm)
            vm.OpenFile(node.FilePath);
    }

    /// <summary>
    /// 右键点节点时先把它选中（WPF 的 TreeView 默认只有左键才改选中项），
    /// 这样上下文菜单的"操作对象"和用户看到的高亮一致。
    /// 不置 e.Handled，让右键事件继续走到 ContextMenuService 弹出菜单。
    /// </summary>
    private void TreeViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TreeViewItem item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }
}
