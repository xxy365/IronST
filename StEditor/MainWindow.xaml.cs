using System.IO;
using System.Windows;

namespace StEditor;

public partial class MainWindow : Window
{
    private string? _currentFilePath;

    public MainWindow()
    {
        InitializeComponent();

        // 示例代码
        Editor.Text = @"PROGRAM Main
VAR
    counter : INT := 0;
    name    : STRING := 'Hello';
    temp    : REAL := 3.14;
END_VAR

IF counter < 10 THEN
    counter := counter + 1;
END_IF;

END_PROGRAM";
    }

    // ── 以下全是应用层逻辑，与控件无关 ──

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        { Filter = "ST 文件 (*.st)|*.st|所有文件 (*.*)|*.*" };
        if (dialog.ShowDialog() == true) {
            _currentFilePath = dialog.FileName;
            Editor.Text = File.ReadAllText(_currentFilePath);
            Title = $"ST Editor - {Path.GetFileName(_currentFilePath)}";
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFilePath == null) {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "ST 文件 (*.st)|*.st" };
            if (dialog.ShowDialog() == true) _currentFilePath = dialog.FileName;
            else return;
        }
        File.WriteAllText(_currentFilePath, Editor.Text);
    }
}
