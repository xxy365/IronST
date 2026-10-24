// ViewModels/OutputViewModel.cs
using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronST.IDE.Models;

namespace IronST.IDE.ViewModels;

public partial class OutputViewModel : ObservableObject
{
    public ObservableCollection<string> Messages { get; } = [];
    public ObservableCollection<ErrorItem> Errors { get; } = [];
    public ObservableCollection<CallStackEntry> CallStack { get; } = [];
    public ObservableCollection<BreakpointInfo> Breakpoints { get; } = [];
    public ObservableCollection<WatchItem> WatchVariables { get; } = [];

    private readonly StringBuilder _text = new();

    /// <summary>
    /// 输出面板整段文本 —— <see cref="Messages"/> 的换行拼接。
    ///
    /// 输出框是只读多行文本而非 ListBox，视图只认一个字符串，所以每次追加时
    /// 顺带把这一份拼好。两份在同一个方法里更新，不会各说各话。
    /// </summary>
    [ObservableProperty]
    private string _displayText = "";

    // 快捷方法，避免到处写 Messages.Add("[xxx] ...")
    public void Info(string msg) => Append("Info", msg);
    public void Warn(string msg) => Append("Warn", msg);
    public void Error(string msg) => Append("Error", msg);
    public void Debug(string msg) => Append("Debug", msg);

    private void Append(string level, string msg)
    {
        var line = $"[{level}] {msg}";

        Messages.Add(line);

        if (_text.Length > 0)
            _text.Append('\n');
        _text.Append(line);

        DisplayText = _text.ToString();
    }

    /// <summary>清空输出面板。右键菜单里的"清空"走这里。</summary>
    [RelayCommand]
    private void Clear()
    {
        Messages.Clear();
        _text.Clear();
        DisplayText = "";
    }
}
