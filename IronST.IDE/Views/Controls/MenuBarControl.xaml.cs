using System.ComponentModel;   // 添加引用
using System.Windows;
using System.Windows.Controls;

namespace IronST.IDE.Views.Controls;

public partial class MenuBarControl : UserControl
{
    public MenuBarControl()
    {
        InitializeComponent();

        // 设计时直接退出，不执行任何业务逻辑
        if (DesignerProperties.GetIsInDesignMode(this))
            return;

        // 运行时获取当前主题
        string currentTheme = ((App)Application.Current).CurrentTheme;
        LightThemeMenuItem.IsChecked = currentTheme == "Light";
        DarkThemeMenuItem.IsChecked = currentTheme == "Dark";
    }

    private void LightTheme_Click(object sender, RoutedEventArgs e)
    {
        App.SwitchTheme("Light");
        LightThemeMenuItem.IsChecked = true;
        DarkThemeMenuItem.IsChecked = false;
    }

    private void DarkTheme_Click(object sender, RoutedEventArgs e)
    {
        App.SwitchTheme("Dark");
        DarkThemeMenuItem.IsChecked = true;
        LightThemeMenuItem.IsChecked = false;
    }
}