using System;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using IronST.IDE.Messengers;

namespace IronST.IDE;

public partial class App : Application
{
    public string CurrentTheme { get; private set; } = "Light";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. 总是先加载公共样式（常驻，不再移除）
        var commonDict = new ResourceDictionary
        {
            Source = new Uri("/Themes/CommonStyles.xaml", UriKind.Relative),
        };
        Resources.MergedDictionaries.Add(commonDict);

        // 2. 加载保存的主题颜色
        string savedTheme = IronST.IDE.Properties.Settings.Default.Theme;
        if (string.IsNullOrEmpty(savedTheme))
            savedTheme = "Light";

        ApplyTheme(savedTheme);
    }

    public void ApplyTheme(string mode)
    {
        try
        {
            CurrentTheme = mode;
            // 准备新颜色字典
            string colorFile = mode.Equals("Dark", StringComparison.OrdinalIgnoreCase)
                ? "/Themes/DarkColors.xaml"
                : "/Themes/LightColors.xaml";

            var newColorDict = new ResourceDictionary
            {
                Source = new Uri(colorFile, UriKind.Relative),
            };

            // 插入到最前面（索引0），以便 DynamicResource 优先查找新颜色
            Resources.MergedDictionaries.Insert(0, newColorDict);

            // 移除旧的同类型颜色字典（但不要删 CommonStyles）
            var dicts = Resources.MergedDictionaries;
            for (int i = dicts.Count - 1; i >= 0; i--)
            {
                var source = dicts[i].Source?.ToString();
                if (
                    source != null
                    && (source.Contains("LightColors.xaml") || source.Contains("DarkColors.xaml"))
                    && dicts[i] != newColorDict
                )
                {
                    dicts.RemoveAt(i);
                }
            }

            // 持久化
            IronST.IDE.Properties.Settings.Default.Theme = mode;
            IronST.IDE.Properties.Settings.Default.Save();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"主题切换失败：{ex.Message}",
                "错误",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
        }

#if NET9_0_OR_GREATER
        try
        {
            if (mode.Equals("Dark", StringComparison.OrdinalIgnoreCase))
#pragma warning disable WPF0001 // 类型仅用于评估，在将来的更新中可能会被更改或删除。取消此诊断以继续。
                ThemeMode = ThemeMode.Dark;
            else
                ThemeMode = ThemeMode.Light;
#pragma warning restore WPF0001 // 类型仅用于评估，在将来的更新中可能会被更改或删除。取消此诊断以继续。
        }
        catch
        { /* 忽略可能的实验性API异常 */
        }
#endif
    }

    public static void SwitchTheme(string mode)
    {
        if (Current is App app)
        {
            app.ApplyTheme(mode);
            WeakReferenceMessenger.Default.Send(new ThemeChangedMessenger(mode));
        }
    }
}
