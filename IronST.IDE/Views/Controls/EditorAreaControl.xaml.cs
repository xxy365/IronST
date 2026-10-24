using IronST.IDE.Models;
using IronST.IDE.ViewModels;
using IronST.StEditor;
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
/// EditorAreaControl.xaml 的交互逻辑
/// </summary>
public partial class EditorAreaControl : UserControl
{
    public EditorAreaControl()
    {
        InitializeComponent();
    }
    private void StEditor_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not StEditorControl ctrl) return;

        // WPF 的 ContentPresenter 在模板与内容类型都不变时会复用同一个控件、只换
        // DataContext —— 于是 Loaded 只在第一个标签页触发一次，后续标签页的
        // EditorControl 永远是 null。而 EditorControl 同时是断点边栏与波浪线的挂载点，
        // 不跟着 DataContext 走，断点在第二个及之后的标签页上就是死的。
        ctrl.DataContextChanged -= StEditor_DataContextChanged;
        ctrl.DataContextChanged += StEditor_DataContextChanged;

        Attach(ctrl, oldVm: null);
    }

    private void StEditor_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        => Attach((StEditorControl)sender, e.OldValue);

    private void Attach(StEditorControl ctrl, object? oldVm)
    {
        // 解绑上一个标签页：否则同一次边栏点击会同时喂给两个标签。
        // EditorControl 的 setter 负责退订 BreakpointToggled。
        // 右键菜单的"格式化文档"同理会指到旧标签，这里一并退订。
        if (oldVm is EditorViewModel old && !ReferenceEquals(old, ctrl.DataContext))
        {
            old.EditorControl = null;
            old.FormatRequested -= OnFormatRequested;
        }

        if (ctrl.DataContext is EditorViewModel vm)
        {
            vm.EditorControl = ctrl;

            // 防止同一标签页被重复 Attach（Loaded + DataContextChanged 各触发一次）。
            vm.FormatRequested -= OnFormatRequested;
            vm.FormatRequested += OnFormatRequested;
        }
    }

    private void OnFormatRequested(object? sender, EventArgs e)
    {
        if (sender is EditorViewModel vm
            && DataContext is MainWindowViewModel main)
            main.EditorWorkspace.FormatDocument(vm);
    }

}
