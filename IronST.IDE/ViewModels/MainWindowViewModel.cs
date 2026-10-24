// ViewModels/MainWindowViewModel.cs
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronST.IDE.Models;

namespace IronST.IDE.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel()
    {
        Output = new OutputViewModel();
        EditorWorkspace = new EditorWorkspaceViewModel(Output, s => StatusText = s);
        // 阶段 VII：F5 按工程调试 —— DebugViewModel 需要（① 当前工程
        // .stproj，取 StartupProgram 当入口；② 按路径激活已开标签页，
        // 执行行落在别的文件时把那个标签页带到前台）。
        Debug = new DebugViewModel(Output, s => StatusText = s,
                                             () => EditorWorkspace.SelectedEditor,
                                             () => Project.Project,
                                             path => EditorWorkspace.ActivateExisting(path));

        // E1/E2：编辑器文本 → 前端语义分析 → 诊断波浪线 + 错误列表
        Analysis = new Services.EditorAnalysisService(EditorWorkspace, Output,
                                                     () => Project.Project);

        // 断点边栏点击 → 调试器（每个标签页都要接，点击发生在哪个就用哪个）
        foreach (var tab in EditorWorkspace.EditorTabs)
            HookBreakpointToggle(tab);
        EditorWorkspace.EditorTabs.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (var item in e.NewItems)
                    if (item is EditorViewModel tab)
                        HookBreakpointToggle(tab);
        };

        // SelectedEditor 变化 → 转发通知 + 刷新 WindowTitle + 该文件的断点红点
        EditorWorkspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorWorkspace.SelectedEditor))
            {
                // 本类的 SelectedEditor 只是转发生属性、自己不发通知，而 TabControl 的
                // SelectedItem 正绑在本类上 —— 不转发的话，源端选中新标签（打开/新建/
                // 关闭文件）视图永远收不到，标签不会跟着切过去。点标签之所以还能用，
                // 是因为那是 TwoWay 回写，方向相反。
                OnPropertyChanged(nameof(SelectedEditor));
                OnPropertyChanged(nameof(WindowTitle));
                Debug.RefreshBreakpointIndicatorsForSelected();
                FocusSelectedEditor();
            }
        };

        // Debug 状态变化 → 刷新转发属性
        Debug.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DebugViewModel.IsDebugging)
                                  or nameof(DebugViewModel.IsPaused)
                                  or nameof(DebugViewModel.DebugStatus))
            {
                OnPropertyChanged(nameof(IsDebugging));
                OnPropertyChanged(nameof(IsPaused));
                OnPropertyChanged(nameof(DebugStatusColor));
                OnPropertyChanged(nameof(DebugStatus));
            }
        };

        // 输出面板文本变化 → 转发通知（本类的 OutputDisplayText 只是转发生属性，
        // 而输出框的 Text 绑在本类上）
        Output.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OutputViewModel.DisplayText))
                OnPropertyChanged(nameof(OutputDisplayText));
        };

        LoadSampleData();
    }

    /// <summary>
    /// 选中标签页后把键盘焦点交给里面的编辑器，这样打开文件就能直接接着输入。
    ///
    /// WPF 的 TabControl 切换选中项并不会把焦点送进内容区（焦点仍留在标签头或菜单上），
    /// 而内容是 ContentTemplate 懒创建的 —— 通知到达那一刻控件还没生成，所以丢给
    /// Dispatcher 等这一帧渲染完再去聚焦。
    /// </summary>
    private void FocusSelectedEditor()
    {
        var tab = EditorWorkspace.SelectedEditor;
        if (tab == null) return;

        // 别在排程时判 EditorControl：内容是懒创建的，此刻它多半还是 null。
        // 等到回调跑（Loaded 优先级，排在布局之后）它才有值。
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(() =>
            {
                // 回调执行时若选中项已经换人（连续开多个文件、或用户点了别处），
                // 就别再抢焦点了
                if (EditorWorkspace.SelectedEditor == tab)
                    tab.EditorControl?.GetEditor().Focus();
            }));
    }

    // ═══════════════════════════════════════════
    //  子 ViewModel（内部使用）
    // ═══════════════════════════════════════════

    public OutputViewModel Output { get; }
    public EditorWorkspaceViewModel EditorWorkspace { get; }
    public DebugViewModel Debug { get; }

    /// <summary>编辑器 ↔ 语义分析桥接服务（E1/E2）。</summary>
    public Services.EditorAnalysisService Analysis { get; }

    // ═══════════════════════════════════════════
    //  转发：编辑器命令
    // ═══════════════════════════════════════════

    public IRelayCommand NewFileCommand => EditorWorkspace.NewFileCommand;
    public IRelayCommand OpenFileCommand => EditorWorkspace.OpenFileFromDialogCommand;
    public IRelayCommand SaveCommand => EditorWorkspace.SaveCommand;
    public IRelayCommand SaveAllCommand => EditorWorkspace.SaveAllCommand;
    public IRelayCommand SaveAsCommand => EditorWorkspace.SaveAsCommand;
    public IRelayCommand CloseTabCommand => EditorWorkspace.CloseTabCommand;
    public IRelayCommand FormatDocumentCommand => EditorWorkspace.FormatDocumentCommand;
    public IRelayCommand ClearOutputCommand => Output.ClearCommand;

    // ═══════════════════════════════════════════
    //  转发：调试命令
    // ═══════════════════════════════════════════

    public IRelayCommand DebugToggleCommand => Debug.DebugToggleCommand;

    /// <summary>
    /// Ctrl+F5 / 调试菜单的「开始执行（不调试）」：有打开的工程就跑整个工程，
    /// 否则保持既有行为（跑当前标签页）。
    /// </summary>
    public IRelayCommand RunWithoutDebugCommand => RunStartupCommand;

    public IRelayCommand StopDebugCommand => Debug.StopDebugCommand;
    public IRelayCommand StepIntoCommand => Debug.StepIntoCommand;
    public IRelayCommand StepOverCommand => Debug.StepOverCommand;
    public IRelayCommand StepOutCommand => Debug.StepOutCommand;
    public IRelayCommand ToggleBreakpointCommand => Debug.ToggleBreakpointCommand;

    // ═══════════════════════════════════════════
    //  转发：编辑器属性
    // ═══════════════════════════════════════════

    public ObservableCollection<EditorViewModel> EditorTabs => EditorWorkspace.EditorTabs;

    public EditorViewModel? SelectedEditor
    {
        get => EditorWorkspace.SelectedEditor;
        set => EditorWorkspace.SelectedEditor = value;
    }

    // ═══════════════════════════════════════════
    //  转发：调试属性
    // ═══════════════════════════════════════════

    public bool IsDebugging => Debug.IsDebugging;
    public bool IsPaused => Debug.IsPaused;
    public string DebugStatus => Debug.DebugStatus;
    public System.Windows.Media.Brush DebugStatusColor => Debug.DebugStatusColor;

    // ═══════════════════════════════════════════
    //  转发：输出属性
    // ═══════════════════════════════════════════

    /// <summary>输出面板整段文本（<see cref="OutputViewModel.DisplayText"/>）。</summary>
    public string OutputDisplayText => Output.DisplayText;
    public ObservableCollection<ErrorItem> Errors => Output.Errors;
    public ObservableCollection<CallStackEntry> CallStack => Output.CallStack;
    public ObservableCollection<BreakpointInfo> Breakpoints => Output.Breakpoints;
    public ObservableCollection<WatchItem> WatchVariables => Output.WatchVariables;

    // ═══════════════════════════════════════════
    //  自身属性
    // ═══════════════════════════════════════════

    [ObservableProperty] public partial bool ShowSolutionExplorer { get; set; } = true;
    [ObservableProperty] public partial bool ShowPropertiesPanel { get; set; } = true;
    [ObservableProperty] public partial bool ShowBottomPanel { get; set; } = true;
    [ObservableProperty] public partial string StatusText { get; set; } = "Ready";
    [ObservableProperty] public partial string CursorPosition { get; set; } = "Ln 1, Col 1";

    public ObservableCollection<SolutionNode> SolutionNodes { get; } = [];

    public string WindowTitle =>
        $"{(EditorWorkspace.SelectedEditor?.DisplayName ?? "IronST.IDE")} - IronST.IDE";

    // ═══════════════════════════════════════════
    //  自身命令
    // ═══════════════════════════════════════════

    [RelayCommand]
    private void Exit() => System.Windows.Application.Current.Shutdown();

    // ═══════════════════════════════════════════
    //  E4：解决方案树（真实文件夹扫描）
    // ═══════════════════════════════════════════

    [RelayCommand]
    private void OpenFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择包含 .st 文件的文件夹",
            Multiselect = false,
        };
        if (dlg.ShowDialog() != true)
            return;

        string selectedPath = dlg.FolderName;
        SolutionNodes.Clear();
        SolutionNodes.Add(BuildNode(Path.GetFileName(selectedPath), selectedPath));
        StatusText = $"已打开文件夹: {selectedPath}";
        Output.Info($"已打开文件夹: {selectedPath}");
        Output.Info($"找到 {CountStFiles(selectedPath)} 个 .st 文件");
    }

    private static SolutionNode BuildNode(string name, string path)
    {
        var node = new SolutionNode
        {
            Name = name,
            FilePath = path,
            IsFolder = Directory.Exists(path),
            IsExpanded = true,
        };

        if (!Directory.Exists(path))
            return node;

        foreach (var dir in Directory.GetDirectories(path))
            node.Children.Add(BuildNode(Path.GetFileName(dir), dir));
        foreach (var file in Directory.GetFiles(path, "*.st"))
            node.Children.Add(new SolutionNode { Name = Path.GetFileName(file), FilePath = file });

        return node;
    }

    private static int CountStFiles(string path) =>
        Directory.EnumerateFiles(path, "*.st", SearchOption.AllDirectories).Count();

    // ═══════════════════════════════════════════
    //  工程（.stproj）：新建 / 打开
    // ═══════════════════════════════════════════

    /// <summary>当前工程工作区（逻辑在 <see cref="Services.ProjectWorkspace"/>，可单测）。</summary>
    public Services.ProjectWorkspace Project { get; } = new();

    public bool HasProject => Project.HasProject;

    public string CurrentProjectName => Project.ProjectName;

    [RelayCommand]
    private void NewProject()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "新建 IronST 工程",
            Filter = "IronST 工程 (*.stproj)|*.stproj",
            DefaultExt = ".stproj",
            FileName = "MyApp.stproj",
            AddExtension = true,
            // 我们从不覆盖已有工程（STProjectWriter 会拒绝），别让对话框假装能覆盖
            OverwritePrompt = false,
        };

        if (dlg.ShowDialog() != true)
            return;

        try
        {
            string created = Project.Create(dlg.FileName);
            RefreshProjectTree();

            Output.Info($"已新建工程: {created}");
            StatusText = $"已新建工程: {Project.ProjectName}";

            // 顺手把生成的入口源文件打开，用户可以立刻编辑 / F5
            if (Project.Project?.SourceFiles.FirstOrDefault() is { } first)
                OpenFile(first);

            // 工程上下文变了：已开着的标签页要按工程重算一遍诊断
            Analysis.AnalyzeAllOpenTabs();
        }
        catch (IronST.CLI.ProjectFileException ex)
        {
            ReportProjectFailure("新建工程失败", ex.Message);
        }
    }

    [RelayCommand]
    private void OpenProject()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "打开 IronST 工程",
            Filter = "IronST 工程 (*.stproj)|*.stproj",
            CheckFileExists = true,
        };

        if (dlg.ShowDialog() != true)
            return;

        try
        {
            Project.Open(dlg.FileName);
            RefreshProjectTree();

            Output.Info($"已打开工程: {Project.ProjectPath}");
            StatusText = $"已打开工程: {Project.ProjectName}";

            foreach (string source in Project.Project?.SourceFiles ?? [])
                OpenFile(source);

            // 工程上下文变了：已开着的标签页要按工程重算一遍诊断
            // （否则 Main.st 还挂着单文件分析时的"未声明/未知类型"）
            Analysis.AnalyzeAllOpenTabs();
        }
        catch (IronST.CLI.ProjectFileException ex)
        {
            ReportProjectFailure("打开工程失败", ex.Message);
        }
    }

    private void RefreshProjectTree()
    {
        SolutionNodes.Clear();
        foreach (var node in Project.BuildTree())
            SolutionNodes.Add(node);

        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(CurrentProjectName));
    }

    private void ReportProjectFailure(string title, string message)
    {
        Output.Error($"{title}: {message}");
        StatusText = title;
        System.Windows.MessageBox.Show(
            message, title,
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning);
    }

    // ── 往工程里加文件 ──

    /// <summary>工程 → 添加新建项：在工程目录里造一个 <c>.st</c> 并加入工程。</summary>
    [RelayCommand]
    private void AddNewItem()
    {
        if (!RequireProject("添加新建项"))
            return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "添加新建项",
            Filter = "ST 源文件 (*.st)|*.st",
            DefaultExt = ".st",
            FileName = "NewPOU.st",
            InitialDirectory = Project.ProjectDirectory,
            AddExtension = true,
            // 我们从不覆盖已有文件（CreateSourceFile 会拒绝），别让对话框假装能覆盖
            OverwritePrompt = false,
        };

        if (dlg.ShowDialog() != true)
            return;

        try
        {
            string created = Project.AddNewSource(dlg.FileName);
            RefreshProjectTree();

            Output.Info($"已添加源文件: {created}");
            StatusText = $"已添加: {Path.GetFileName(created)}";
            OpenFile(created);
        }
        catch (IronST.CLI.ProjectFileException ex)
        {
            ReportProjectFailure("添加新建项失败", ex.Message);
        }
    }

    /// <summary>工程 → 添加现有项：<c>.st</c> 加为源文件，<c>.stmod</c> 加为模块引用。</summary>
    [RelayCommand]
    private void AddExistingItem()
    {
        if (!RequireProject("添加现有项"))
            return;

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "添加现有项",
            Filter = "ST 源文件或模块 (*.st;*.stmod)|*.st;*.stmod"
                   + "|ST 源文件 (*.st)|*.st"
                   + "|IronST 模块 (*.stmod)|*.stmod",
            InitialDirectory = Project.ProjectDirectory,
            Multiselect = true,
            CheckFileExists = true,
        };

        if (dlg.ShowDialog() != true)
            return;

        foreach (string file in dlg.FileNames)
        {
            try
            {
                string added = Project.AddExistingFile(file);
                RefreshProjectTree();

                Output.Info($"已添加: {added}");
                StatusText = $"已添加: {Path.GetFileName(added)}";

                if (added.EndsWith(".st", StringComparison.OrdinalIgnoreCase))
                    OpenFile(added);
            }
            catch (IronST.CLI.ProjectFileException ex)
            {
                ReportProjectFailure("添加现有项失败", ex.Message);
            }
        }
    }

    private bool RequireProject(string title)
    {
        if (Project.HasProject)
            return true;

        ReportProjectFailure(title, "没有打开的工程，请先用 文件 → 新建工程 / 打开工程。");
        return false;
    }

    // ── 生成 / 运行工程 ──

    /// <summary>工程 → 生成工程：编译整个 <c>.stproj</c> 并写出 <c>.isb</c>。</summary>
    [RelayCommand]
    private void BuildProject() => ExecuteProjectCommand("生成工程", () => Project.Build());

    /// <summary>工程 → 运行工程：编译并执行整个 <c>.stproj</c>（不调试）。</summary>
    [RelayCommand]
    private void RunProject() => ExecuteProjectCommand("运行工程", () => Project.Run());

    /// <summary>Ctrl+F5：有工程跑工程，没工程跑当前标签页（保持既有行为）。</summary>
    [RelayCommand]
    private void RunStartup()
    {
        if (Project.HasProject)
            RunProject();
        else
            Debug.RunWithoutDebugCommand.Execute(null);
    }

    /// <summary>
    /// 跑一条工程级命令（build / run），把 CLI 的 stdout/stderr 原样倒进输出面板。
    /// 编译与执行都复用 CLI 的实现（见 <see cref="Services.ProjectWorkspace"/>）。
    /// </summary>
    private void ExecuteProjectCommand(string title, Func<Services.ProjectCommandResult> action)
    {
        if (!Project.HasProject)
        {
            ReportProjectFailure(title, "没有打开的工程，请先用 文件 → 新建工程 / 打开工程。");
            return;
        }

        StatusText = $"{title}...";
        try
        {
            var result = action();

            foreach (var line in SplitLines(result.Output))
                Output.Info(line);

            foreach (var line in SplitLines(result.Error))
                Output.Error(line);

            StatusText = result.Succeeded
                ? $"{title}成功"
                : $"{title}失败（退出码 {result.ExitCode}）";
        }
        catch (IronST.CLI.ProjectFileException ex)
        {
            ReportProjectFailure(title, ex.Message);
        }
    }

    private static IEnumerable<string> SplitLines(string text) =>
        text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);

    [RelayCommand]
    private void About()
    {
        System.Windows.MessageBox.Show(
            "IronST.IDE\nPLC Structured Text (IEC 61131-3) IDE\n\n" +
            "Powered by StEditorControl + IronST.Backend",
            "About IronST.IDE",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    // 外部调用入口
    public void OpenFile(string path) => EditorWorkspace.OpenFile(path);

    /// <summary>把某标签页编辑器的边栏点击接到调试器上。</summary>
    private void HookBreakpointToggle(EditorViewModel tab) =>
        tab.BreakpointToggled += line => Debug.ToggleBreakpointAt(tab, line);

    public void ShutdownAllEditors()
    {
        foreach (var tab in EditorWorkspace.EditorTabs)
            tab.Shutdown();
    }

    // ═══════════════════════════════════════════
    //  初始化（E4 前：不加载假数据）
    // ═══════════════════════════════════════════

    private void LoadSampleData()
    {
        Output.Info("IronST.IDE ready.");
        Output.Info("使用 文件 → 打开 加载 .st 文件；编辑器会自动进行语义分析（诊断波浪线 + 错误列表）。");
    }
}
