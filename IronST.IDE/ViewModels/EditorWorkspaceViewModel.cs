// ViewModels/EditorWorkspaceViewModel.cs
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronST.Frontend.Formatting;
using IronST.IDE.Models;
using Microsoft.Win32;

namespace IronST.IDE.ViewModels;

public partial class EditorWorkspaceViewModel : ObservableObject
{
    private readonly OutputViewModel _output;
    private readonly Action<string> _setStatus;   // 回调更新状态栏
    private int _untitledCount;

    public EditorWorkspaceViewModel(OutputViewModel output, Action<string> setStatus)
    {
        _output = output;
        _setStatus = setStatus;
    }

    // ═══ Properties ═══

    public ObservableCollection<EditorViewModel> EditorTabs { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveAsCommand))]
    [NotifyCanExecuteChangedFor(nameof(FormatDocumentCommand))]
    public partial EditorViewModel? SelectedEditor { get; set; }

    // ═══ Commands ═══

    [RelayCommand]
    private void NewFile()
    {
        _untitledCount++;
        var tab = new EditorViewModel { FileName = $"Untitled-{_untitledCount}.st" };
        EditorTabs.Add(tab);
        SelectedEditor = tab;
        _setStatus("New file created.");
    }

    [RelayCommand]
    private void OpenFileFromDialog()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "ST Files (*.st)|*.st|All Files (*.*)|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() == true)
            foreach (var file in dlg.FileNames)
                OpenFile(file);
    }

    /// <summary>
    /// 激活已打开的指定路径标签页（不新开磁盘文件）。没有则返回 null。
    /// 阶段 VII：调试线程命中断点/暂停时，把执行行所在的文件带到前台用。
    /// </summary>
    public EditorViewModel? ActivateExisting(string path)
    {
        var existing = EditorTabs.FirstOrDefault(t =>
            t.FilePath != null && string.Equals(t.FilePath, path, StringComparison.Ordinal));
        if (existing == null)
            return null;

        SelectedEditor = existing;
        _setStatus($"Activated: {path}");
        return existing;
    }

    /// <summary>供 Solution Tree 双击等外部调用</summary>
    public void OpenFile(string path)
    {
        var existing = EditorTabs.FirstOrDefault(t => t.FilePath == path);
        if (existing != null) { SelectedEditor = existing; return; }

        try
        {
            var code = File.ReadAllText(path);
            var tab = new EditorViewModel
            {
                FileName = Path.GetFileName(path),
                FilePath = path
            };
            tab.LoadCode(code);
            EditorTabs.Add(tab);
            SelectedEditor = tab;
            _setStatus($"Opened: {path}");
            _output.Info($"Opened {path}");
        }
        catch (Exception ex)
        {
            _output.Error($"Failed to open {path}: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (SelectedEditor == null) return;
        if (string.IsNullOrEmpty(SelectedEditor.FilePath)) { SaveAs(); return; }

        try
        {
            File.WriteAllText(SelectedEditor.FilePath, SelectedEditor.Code);
            SelectedEditor.IsModified = false;
            _setStatus($"Saved: {SelectedEditor.FilePath}");
        }
        catch (Exception ex) { _output.Error($"Save failed: {ex.Message}"); }
    }

    private bool CanSave() => SelectedEditor is not null;

    /// <summary>
    /// 格式化当前文档：读取文本 → StFormatter.Format → 有变化才写回编辑器
    /// （走 ApplyTextTransform 的 RunUpdate+Replace，不直接给 Code 赋值 ——
    /// 文本变更沿 TextChanged → Text DP → Code 绑定链路同步，IsModified 自然置位）。
    /// 有语法错误时不改、无变化时不动（不置脏）。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanFormat))]
    private void FormatDocument() => FormatDocument(SelectedEditor!);

    /// <summary>
    /// 对指定标签页执行格式化（Ctrl+Shift+F 命令与编辑器右键菜单共用入口）。
    /// </summary>
    public void FormatDocument(EditorViewModel? editor)
    {
        var control = editor?.EditorControl;
        if (editor == null || control == null)
            return;

        try
        {
            var result = StFormatter.Format(editor.Code);

            if (result.HasErrors)
            {
                _output.Error("Format skipped: source has syntax errors.");
                _setStatus("Format skipped: source has syntax errors.");
                return;
            }

            if (!result.Changed)
            {
                _setStatus("Document already formatted.");
                return;
            }

            control.ApplyTextTransform(result.Text, restoreCaret: null);
            _setStatus("Document formatted.");
        }
        catch (Exception ex)
        {
            _output.Error($"Format failed: {ex.Message}");
            _setStatus("Format failed.");
        }
    }

    private bool CanFormat() => SelectedEditor?.EditorControl is not null;

    [RelayCommand]
    private void SaveAll()
    {
        var saved = 0;
        foreach (var tab in EditorTabs.Where(t => t.IsModified))
        {
            if (string.IsNullOrEmpty(tab.FilePath)) continue;
            try
            {
                File.WriteAllText(tab.FilePath, tab.Code);
                tab.IsModified = false;
                saved++;
            }
            catch (Exception ex) { _output.Error($"Save failed for {tab.FileName}: {ex.Message}"); }
        }
        _setStatus(saved > 0 ? $"Saved {saved} file(s)." : "Nothing to save.");
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void SaveAs()
    {
        if (SelectedEditor == null) return;
        var dlg = new SaveFileDialog
        {
            Filter = "ST Files (*.st)|*.st|All Files (*.*)|*.*",
            FileName = SelectedEditor.FileName
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            File.WriteAllText(dlg.FileName, SelectedEditor.Code);
            SelectedEditor.FilePath = dlg.FileName;
            SelectedEditor.FileName = Path.GetFileName(dlg.FileName);
            SelectedEditor.IsModified = false;
            _setStatus($"Saved as: {dlg.FileName}");
        }
        catch (Exception ex) { _output.Error($"Save failed: {ex.Message}"); }
    }

    [RelayCommand]
    private void CloseTab(EditorViewModel? tab)
    {
        tab ??= SelectedEditor;
        if (tab == null) return;

        if (tab.IsModified)
        {
            var result = System.Windows.MessageBox.Show(
                $"Save changes to {tab.FileName}?",
                "IronST.IDE",
                System.Windows.MessageBoxButton.YesNoCancel,
                System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Cancel) return;
            if (result == System.Windows.MessageBoxResult.Yes)
            {
                SelectedEditor = tab;
                Save();
            }
        }

        tab.Shutdown();
        var index = EditorTabs.IndexOf(tab);
        EditorTabs.Remove(tab);

        if (SelectedEditor == tab)
            SelectedEditor = EditorTabs.Count > 0
                ? EditorTabs[Math.Min(index, EditorTabs.Count - 1)]
                : null;

        _setStatus($"Closed: {tab.FileName}");
    }
}
