using IronST.IDE.ViewModels;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace IronST.IDE;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = (MainWindowViewModel)DataContext;
        RestoreWindowState();
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        foreach (var f in files) {
            if (f.EndsWith(".st", StringComparison.OrdinalIgnoreCase))
                _vm.OpenFile(f);
        }
    }

    private static string StateDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IronST");
    private static string StateFile => Path.Combine(StateDir, "window-state.json");

    private void RestoreWindowState()
    {
        try {
            if (!File.Exists(StateFile)) return;
            var json = File.ReadAllText(StateFile);
            var s = JsonSerializer.Deserialize<WindowStateDto>(json);
            if (s == null) return;

            Left = s.X;
            Top = s.Y;
            Width = s.W;
            Height = s.H;
            if (s.Maximized) WindowState = WindowState.Maximized;
        } catch { }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        try {
            Directory.CreateDirectory(StateDir);
            var s = new WindowStateDto
            {
                X = (int)Left,
                Y = (int)Top,
                W = (int)Width,
                H = (int)Height,
                Maximized = WindowState == WindowState.Maximized
            };
            File.WriteAllText(StateFile, JsonSerializer.Serialize(s));
        } catch { }

        _vm.ShutdownAllEditors();
        base.OnClosing(e);
    }

    private record WindowStateDto
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int W { get; set; }
        public int H { get; set; }
        public bool Maximized { get; set; }
    }
}