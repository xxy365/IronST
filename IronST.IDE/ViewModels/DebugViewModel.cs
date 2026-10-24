// ViewModels/DebugViewModel.cs — E3：真实调试（DebugVM + VMDebugSession）
//
// 模型：专用后台调试线程运行 VM；断点/单步/暂停通过事件 + 信号量驱动。
// 所有 UI 更新通过 Dispatcher 回到主线程。
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronST.BytecodeGen;
using IronST.CLI;
using IronST.IDE.Models;
using IronST.IDE.Services;
using IronST.Runtime;
using IronST.Runtime.Debug;
using IronST.Runtime.Shared;

namespace IronST.IDE.ViewModels;

public partial class DebugViewModel : ObservableObject
{
    private readonly OutputViewModel _output;
    private readonly Action<string> _setStatus;
    private readonly Func<EditorViewModel?> _getSelectedEditor;

    // 阶段 VII：F5 按工程调试。编译取整个工程（CompilationSources.Build），
    // 入口取 .stproj 的 StartupProgram —— 右键 Run 能跑、F5 不能跑的不一致
    // 就是这两件事没对齐。
    private readonly Func<STProjectFile?> _getProject;

    // 执行行落在别的文件的标签页时，先把它带到前台，否则高亮根本看不见。
    // 注入 Func 而不是直接引用 MainWindowViewModel，怕的是循环依赖；
    // 生产传 EditorWorkspace.ActivateExisting，找不到标签页时返回 null。
    private readonly Func<string, EditorViewModel?> _activateFile;

    // ── 真实调试状态 ──
    private DebugVM? _vm;
    private VMDebugSession? _session;
    private CompileResult? _compiled;
    private Thread? _vmThread;
    private readonly AutoResetEvent _continueSignal = new(false);
    private volatile bool _debugRunning;
    // 本次会话的运行时错误是否已经通过 RuntimeErrorEvent 报过（OnError 未触发事件时不会置位）。
    // 用来保证同一条错误只在输出窗口出现一次：事件路径与 DebugLoop 收尾路径都会报。
    private bool _runtimeErrorReported;
    // 阶段 VII：从「只按行」改成「(文件, 行)」 —— 否则 Main.st:29 的断点
    // 会挂到 Utils.st:29 的指令上，两个文件同名行的断点还会互相摘除。
    private readonly Dictionary<(string File, int Line), List<int>> _lineToBreakpoints = new();

    // 断点行号（1 起），按文件分组、跨会话保留。
    // 与 _lineToBreakpoints 的分工：后者是"本次会话已注册进 VM 的断点 ID"，
    // 每次重新编译都得重建；前者是用户意图，不该因为重编译就丢。
    // 之前只有后者，且 StartDebug 会 Clear —— 结果是"先设断点再调试"永远不成立。
    private readonly Dictionary<string, HashSet<int>> _breakpointsByFile =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>取某文件的断点行集合（不存在则建）。</summary>
    /// <remarks>
    /// 分组键用 FilePath 而非 FileName —— 后者只是显示名（不含目录），
    /// 两个目录下的同名文件会串断点。未保存的 Untitled 没有 FilePath，
    /// 回落到 FileName（Untitled-N 本身就是唯一的）。
    /// </remarks>
    /// <summary>断点分组的键：有 FilePath 用 FilePath（与前端文件名同源），无则用显示名。</summary>
    private static string BreakpointKey(EditorViewModel tab) =>
        string.IsNullOrEmpty(tab.FilePath) ? tab.FileName : tab.FilePath;

    private HashSet<int> BreakpointsOf(EditorViewModel tab)
    {
        string key = BreakpointKey(tab);

        if (!_breakpointsByFile.TryGetValue(key, out var set))
        {
            set = new HashSet<int>();
            _breakpointsByFile[key] = set;
        }
        return set;
    }

    // 宿主桥：让 ST 程序能 Print(...) 把结果打到输出面板
    private readonly Services.DebugHostBridge _hostBridge;

    public DebugViewModel(
        OutputViewModel output,
        Action<string> setStatus,
        Func<EditorViewModel?> getSelectedEditor,
        Func<STProjectFile?> getProject,
        Func<string, EditorViewModel?> activateFile)
    {
        _output = output;
        _setStatus = setStatus;
        _getSelectedEditor = getSelectedEditor;
        _getProject = getProject;
        _activateFile = activateFile;
        _hostBridge = new Services.DebugHostBridge(output, OnUi);
    }

    // ═══ 状态（保留原接口） ═══

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DebugStatusColor))]
    [NotifyCanExecuteChangedFor(nameof(StopDebugCommand))]
    public partial bool IsDebugging { get; set; }

    // CanExecute 依赖这两个属性的命令（单步、停止）之前一直是灰的、永远点不动：
    // CommunityToolkit 生成的命令不会自己察觉属性变了，必须显式声明依赖关系，
    // 否则 CanExecute 只在命令刚创建时求值一次 —— 初始 false 就永远 false。
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DebugStatusColor))]
    [NotifyCanExecuteChangedFor(nameof(StepIntoCommand))]
    [NotifyCanExecuteChangedFor(nameof(StepOverCommand))]
    [NotifyCanExecuteChangedFor(nameof(StepOutCommand))]
    public partial bool IsPaused { get; set; }

    [ObservableProperty]
    public partial string DebugStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int CurrentLine { get; set; }

    public Brush DebugStatusColor =>
        IsDebugging
            ? (IsPaused ? Brushes.Yellow : Brushes.Lime)
            : Brushes.Gray;

    // ═══ 命令 ═══

    [RelayCommand]
    private void DebugToggle()
    {
        if (!IsDebugging)
            StartDebug();
        else if (IsPaused)
            Continue();
    }

    [RelayCommand]
    private void RunWithoutDebug()
    {
        var tab = _getSelectedEditor();
        if (tab == null)
        {
            _setStatus("没有打开的编辑器");
            return;
        }

        _setStatus("正在编译并运行（无调试）...");
        try
        {
            var result = CompileCurrent(tab);
            if (result == null)
                return;

            var vm = new VM(result.Module, new VMOptions
            {
                StackSize = 8192,
                StructTypes = result.StructRegistrations,
                HostBridge = _hostBridge.Create(),
                // 让 VM 把运行期错误的源位置回填进异常（Message 于是带「文件:行」）。
                SourceLocator = SourceResolver.FromMap(result.SourceMap),
            });
            _output.Info("执行开始（无调试）");
            vm.Run();
            _output.Info(vm.State == VMState.Halted
                ? "执行完成（正常停机）"
                : $"执行结束，状态: {vm.State}");
        }
        catch (RuntimeErrorException ex)
        {
            // 无调试模式没有 hooks 兜底，VMBase.Run 末尾直接把异常抛出来。
            // 源位置已由 VM 按 SourceLocator 回填，异常自带「文件:行」。
            ReportRuntimeError(ex);
        }
        catch (Exception ex)
        {
            _output.Error($"运行失败: {ex.Message}");
            _setStatus("运行失败");
        }
    }

    [RelayCommand(CanExecute = nameof(IsDebugging))]
    private void StopDebug()
    {
        _debugRunning = false;
        _continueSignal.Set();
        if (_vmThread != null && _vmThread.IsAlive)
            _vmThread.Join(500);

        _vm = null;
        _session = null;
        _compiled = null;
        IsDebugging = false;
        IsPaused = false;
        DebugStatus = string.Empty;
        CurrentLine = 0;
        SetEditorExecutionLine(0);
        _setStatus("调试已停止");
        _output.Debug("调试会话已停止");
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void StepInto()
    {
        _session?.SetSingleStep();
        Resume();
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void StepOver()
    {
        _session?.SetStepOver();
        Resume();
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void StepOut()
    {
        // 当前会话 API 未提供 StepOut，退化为 StepOver
        _output.Warn("Step Out 未实现，退化为 Step Over");
        _session?.SetStepOver();
        Resume();
    }

    [RelayCommand]
    private void ToggleBreakpoint()
    {
        var tab = _getSelectedEditor();
        if (tab?.EditorControl == null)
        {
            _setStatus("没有打开的编辑器");
            return;
        }

        ToggleBreakpointAt(tab, tab.EditorControl.GetEditor().TextArea.Caret.Line);
    }

    /// <summary>
    /// 切换某行断点（1 起）。边栏点击与 F9 都走这里。
    ///
    /// 关键改动：不再要求"先开始调试"。断点只记行号，等编译/启动时才映射成
    /// PC 注册进会话 —— "先打断点再 F5"这才成立。原实现里三种操作顺序
    /// 全走不通：F9 在编译前被拒；F5 后程序已全速跑完；再 F5 又被
    /// StartDebug 的 Clear 清空。
    /// </summary>
    public void ToggleBreakpointAt(EditorViewModel? tab, int line)
    {
        if (tab == null || line <= 0)
            return;

        var lines = BreakpointsOf(tab);
        bool added = lines.Add(line);
        if (!added)
            lines.Remove(line);

        // 会话进行中就即时注册/摘除，不必等下次 F5。键带文件 —— 不然
        // Main.st:29 的断点同时挂到 Utils.st:29 上，摘的时候还互相干扰。
        string file = BreakpointKey(tab);
        if (IsDebugging && added)
            RegisterBreakpointsForLine(file, line);
        else if (IsDebugging)
            UnregisterBreakpointsForLine(file, line);

        RefreshBreakpointIndicators(tab);
        RefreshBreakpointList(tab);
        _output.Debug($"{(added ? "已设置" : "已移除")}断点: 第 {line} 行");
    }

    /// <summary>
    /// 把（文件, 行）映射成 PC 并注册进当前会话。该行没有可断点指令时返回 false。
    /// 映射逻辑在 <see cref="ProjectCompileService.BreakpointPCs"/>（纯逻辑，有单测）。
    /// </summary>
    private bool RegisterBreakpointsForLine(string file, int line)
    {
        if (_compiled == null || _session == null)
            return false;

        var pcs = ProjectCompileService.BreakpointPCs(_compiled, file, line);

        if (pcs.Count == 0)
        {
            _output.Warn($"{Path.GetFileName(file)}:{line} 没有可断点指令");
            return false;
        }

        var ids = new List<int>();
        foreach (var pc in pcs)
            ids.Add(_session.AddAddressBreakpoint(pc).Id);

        _lineToBreakpoints[(file, line)] = ids;
        return true;
    }

    private void UnregisterBreakpointsForLine(string file, int line)
    {
        if (_lineToBreakpoints.Remove((file, line), out var ids))
        {
            foreach (var id in ids)
                _session?.RemoveBreakpoint(id);
        }
    }

    /// <summary>把断点行号推给编辑器边栏画红点。</summary>
    private void RefreshBreakpointIndicators(EditorViewModel? tab)
    {
        if (tab?.EditorControl == null)
            return;

        tab.EditorControl.BreakpointLines =
            BreakpointsOf(tab).OrderBy(x => x).ToList();
    }

    /// <summary>切换标签页后，把该文件的断点红点刷到边栏。</summary>
    public void RefreshBreakpointIndicatorsForSelected() =>
        RefreshBreakpointIndicators(_getSelectedEditor());

    // ═══ 内部：启动调试 ═══

    private void StartDebug()
    {
        var tab = _getSelectedEditor();
        if (tab == null)
        {
            _setStatus("没有打开的编辑器");
            return;
        }

        _output.Errors.Clear();
        var result = CompileCurrent(tab);
        if (result == null)
            return;

        _compiled = result;
        _session = new VMDebugSession();
        _session.OnDebugEvent += OnDebugEvent;
        _runtimeErrorReported = false;

        // 把源码映射喂给会话。单步要按源码行停，而不是一条指令一停 ——
        // 没有这份映射，会话不知道"这一行跑完了没有"，只能退化成指令级。
        // 阶段 VII：多文件时逐条指令各归各自的文件（FileIndex 不再全 0）。
        _session.SourceMap = BuildSessionSourceMap(result.SourceMap);

        _vm = new DebugVM(result.Module, new VMDevOptions
        {
            DebugSession = _session,
            StackSize = 8192,
            StructTypes = result.StructRegistrations,
            HostBridge = _hostBridge.Create(),
            // 同 RunWithoutDebug：运行期错误的源位置由 VM 回填。
            SourceLocator = SourceResolver.FromMap(result.SourceMap),
        });

        // 用新会话重建断点映射。这里清的只是"已注册 ID"这一层；
        // 行号集合是用户意图，跨会话保留，不能动。
        // 阶段 VII：编译产物已含整个工程，断点按（文件, 行）全量装载 ——
        // 不再只挂当前标签页那一摊，别的文件里的红点 F5 后照样命中。
        _lineToBreakpoints.Clear();
        var wanted = _breakpointsByFile
            .SelectMany(kv => kv.Value, (kv, line) => (file: kv.Key, line))
            .OrderBy(b => b.file, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.line)
            .ToList();
        int armed = 0;
        foreach (var (file, line) in wanted)
            if (RegisterBreakpointsForLine(file, line))
                armed++;

        RefreshBreakpointIndicators(tab);
        RefreshBreakpointList(tab);
        SetEditorExecutionLine(0);

        IsDebugging = true;
        IsPaused = false;
        DebugStatus = "Running";
        _setStatus("调试已启动...");
        _output.Debug("编译成功，调试器已启动");
        if (wanted.Count > 0)
            _output.Debug($"已装载 {armed}/{wanted.Count} 个断点");

        // 后台调试线程
        _debugRunning = true;
        _vmThread = new Thread(DebugLoop)
        {
            IsBackground = true,
            Name = "IronST.DebugVM",
        };
        _vmThread.Start();
    }

    private void DebugLoop()
    {
        // 全程用本地引用：StopDebug 会把 _vm 置空，而这个线程可能还没跑完。
        // 更麻烦的是下面那些 OnUi 回调 —— 它们是 BeginInvoke 排队的，等真正
        // 在 UI 线程执行时 _vm 早已是 null，而回调又不在本方法的 try/catch
        // 之内，一个空引用就能把整个进程带崩（表现为"点停止就闪退"）。
        // 所以：值一律在入队前算好，回调里绝不解引用 _vm。
        var vm = _vm!;

        try
        {
            vm.Run();   // 阻塞到首个断点 / 暂停 / 停机
            while (vm.State == VMState.Paused && _debugRunning)
            {
                // 错误暂停 ≠ 断点暂停：VMBase.Run 捕获运行期错误且挂了 hooks 时，
                // 会把状态置为 Paused 并调 OnError —— 它看起来和"命中断点"一样。
                // 若在这里当成断点等"继续"，用户一点继续就重跑同一条出错指令、
                // 无限重复同一个错误。所以一旦记录了 LastError 就退出循环。
                if (vm.LastError != null)
                    break;

                uint pc = vm.Context.PC;
                // PC → (文件,行) 在入队前解析：回调真正执行时 _compiled 可能
                // 已被 StopDebug 置空（本方法注释里说过的"点停止就闪退"问题）。
                var (pcFile, pcLine) = ResolveSourceLocation(pc);
                OnUi(() =>
                {
                    IsPaused = true;
                    DebugStatus = "Paused";
                    CurrentLine = pcLine;
                    SetEditorExecutionLine(pcFile, pcLine);
                    _setStatus(pcFile.Length > 0
                        ? $"已暂停 @ PC=0x{pc:X} {Path.GetFileName(pcFile)}:{pcLine}"
                        : $"已暂停 @ PC=0x{pc:X} 行 {pcLine}");
                });

                _continueSignal.WaitOne();
                if (!_debugRunning)
                    break;

                vm.Resume();   // 继续（阻塞到下次暂停）
            }

            var finalState = vm.State;
            var lastError = vm.LastError;
            // 异常自带源位置（VM 已按 SourceLocator 回填），文本与位置都在入队前取好：
            // 回调真正执行时 _compiled 可能已被 StopDebug 置空。
            var errText = lastError?.Message;
            var errLoc = lastError is null ? null : ErrorLocationText(lastError);
            OnUi(() =>
            {
                IsPaused = false;
                if (finalState == VMState.Halted)
                {
                    SetEditorExecutionLine(0);
                    DebugStatus = "Halted";
                    _setStatus("程序执行完毕（HALT）");
                    _output.Info("程序正常停机");
                }
                else if (lastError != null)
                {
                    // 出错行**保留**高亮：清成 0 等于把"错在哪一行"这条线索抹掉。
                    HighlightErrorLocation(lastError);
                    DebugStatus = "Error";
                    _setStatus(errLoc is null
                        ? $"运行时错误: {errText}"
                        : $"运行时错误: {errLoc}");
                    // 事件路径（OnError + shouldBreak）已报过就不重复报；
                    // BreakOn* 关掉时不发事件，这里兜底。
                    if (!_runtimeErrorReported)
                        _output.Error($"运行时错误: {errText}");
                }
                else
                {
                    SetEditorExecutionLine(0);
                }
            });
        }
        catch (Exception ex)
        {
            OnUi(() => _output.Error($"调试线程异常: {ex.Message}"));
        }
        finally
        {
            OnUi(() =>
            {
                IsPaused = false;
                IsDebugging = false;
                DebugStatus = string.Empty;
            });
        }
    }

    private void Continue()
    {
        IsPaused = false;
        DebugStatus = "Running";
        _setStatus("继续运行...");
        _continueSignal.Set();
    }

    private void Resume() => _continueSignal.Set();

    private void OnDebugEvent(DebugEvent ev)
    {
        // 回调是 BeginInvoke 排队的，位置在入队前解析（线程说明见 DebugLoop）
        string where = ev is BreakpointHitEvent or StepCompleteEvent
            ? DescribeLocation(ev.PC)
            : string.Empty;
        // 同理由：错误文本也在入队前取好，回调执行时 _compiled 可能已被置空。
        // 异常消息自带「文件:行」（VM 按 SourceLocator 回填过）。
        string? errText = ev is RuntimeErrorEvent errEv ? errEv.Error.Message : null;
        OnUi(() =>
        {
            switch (ev)
            {
                case BreakpointHitEvent bp:
                    _output.Debug($"断点命中: PC=0x{bp.PC:X} (bp#{bp.BreakpointId}) {where}");
                    break;
                case StepCompleteEvent st:
                    _output.Debug($"单步完成: PC=0x{st.PC:X} {where}");
                    break;
                case RuntimeErrorEvent err:
                    _runtimeErrorReported = true;
                    // 出错行高亮：位置就长在异常上，直接用（不必再查映射表）。
                    HighlightErrorLocation(err.Error);
                    _output.Error($"运行时错误: {errText}");
                    break;
            }
        });
    }

    /// <summary>供日志用的位置描述：有文件给「文件:行」，只有行号给「行 N」。</summary>
    private string DescribeLocation(uint pc)
    {
        var (file, line) = ResolveSourceLocation(pc);
        return file.Length > 0 ? $"{Path.GetFileName(file)}:{(line > 0 ? line : 0)}" : $"行 {line}";
    }

    // ═══ 运行期错误的展示 ═══

    /// <summary>
    /// 出错位置的可读文本 <c>文件:行</c>；没有位置（未开调试信息 / 无映射）时
    /// 返回 <c>null</c> —— <b>不伪造</b>位置。
    /// </summary>
    /// <remarks>
    /// 位置直接取 <see cref="RuntimeErrorException.SourceLocation"/>：VM 在错误边界
    /// （<c>VMBase.Run</c> 的 catch）已按 <see cref="VMOptions.SourceLocator"/> 回填过，
    /// 异常自带位置。于是这里不必再查映射表，也就不受
    /// 「回调执行时 <c>_compiled</c> 可能已被 StopDebug 置空」的影响。
    /// </remarks>
    private static string? ErrorLocationText(RuntimeErrorException ex) =>
        ex.SourceLocation is { } s && s.line > 0 && s.file.Length > 0
            ? $"{Path.GetFileName(s.file)}:{s.line}"
            : null;

    /// <summary>
    /// 运行期错误的统一展示：输出错误行、状态栏给出「文件:行」、并把出错行高亮出来。
    /// </summary>
    /// <remarks><b>必须在 UI 线程调用</b> —— 它会碰编辑器控件与状态栏。</remarks>
    private void ReportRuntimeError(RuntimeErrorException ex)
    {
        _output.Error($"运行时错误: {ex.Message}");
        _setStatus(ErrorLocationText(ex) is { } loc
            ? $"运行时错误: {loc}"
            : $"运行时错误: {ex.ErrorType}");
        HighlightErrorLocation(ex);
    }

    /// <summary>
    /// 把出错行高亮出来（出错文件不在当前标签页时先切过去）。
    /// </summary>
    /// <remarks>
    /// <b>必须在 UI 线程调用</b> —— 碰的是 WPF 控件。
    /// <para>
    /// 刻意<b>不</b>复用 <see cref="SetEditorExecutionLine(string, int)"/>：那个方法在
    /// 找不到对应标签页时会退化成"高亮当前编辑器"—— 对断点暂停无害（用户看得见
    /// 自己按的断点），但对错误定位是<b>误导</b>（把别的文件的同一行标成出错行）。
    /// 这里宁可不高亮，也不指错地方。
    /// </para>
    /// </remarks>
    private void HighlightErrorLocation(RuntimeErrorException ex)
    {
        if (ex.SourceLocation is not { } s || s.line <= 0 || s.file.Length == 0)
            return;

        CurrentLine = s.line;

        var editor = _activateFile(s.file) ?? _getSelectedEditor();
        if (editor?.EditorControl == null)
            return;
        if (!string.Equals(editor.FilePath, s.file, StringComparison.OrdinalIgnoreCase))
            return;   // 出错文件没打开：别去动当前编辑器

        editor.EditorControl.ExecutionLine = s.line;
    }

    private void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return;
        }
        dispatcher.BeginInvoke(DispatcherPriority.Background, action);
    }

    /// <summary>
    /// 把字节码编译产物的源码映射（按 PC 索引的字典）转成调试会话用的
    /// <see cref="IronST.Runtime.Debug.SourceMap"/>（有序表 + 二分查找）。
    /// </summary>
    /// <summary>
    /// 把字节码编译产物的源码映射（按 PC 索引的字典）转成调试会话用的
    /// <see cref="IronST.Runtime.Debug.SourceMap"/>（有序表 + 二分查找）。
    /// 阶段 VII 起是真正的多文件表：文件名收编成 FileIndex，单文件时等价于全 0。
    /// </summary>
    private static IronST.Runtime.Debug.SourceMap BuildSessionSourceMap(
        IReadOnlyDictionary<int, (string file, int line)> map)
    {
        var src = new IronST.Runtime.Debug.SourceMap();
        var fileIndex = new List<string>();

        foreach (var kv in map)
        {
            int idx = fileIndex.FindIndex(f =>
                string.Equals(f, kv.Value.file, StringComparison.OrdinalIgnoreCase));
            if (idx < 0)
            {
                idx = fileIndex.Count;
                fileIndex.Add(kv.Value.file);
            }

            src.Mappings.Add(new IronST.Runtime.Debug.SourceMap.SourceMapping(
                PC: (uint)kv.Key,
                FileIndex: idx,
                Line: kv.Value.line,
                Column: 0));
        }

        // FindByPC 走二分，Mappings 必须按 PC 升序。
        src.Mappings.Sort((a, b) => a.PC.CompareTo(b.PC));
        src.SourceFiles.AddRange(fileIndex);
        return src;
    }

    /// <summary>PC → (源文件, 行号)。没有映射时返回 ("", 0)。</summary>
    /// <remarks>
    /// 旧的 ResolveSourceLine 只返回行号 —— 多文件时，两个文件里同一行号
    /// 的指令根本分不清谁是谁，执行行高亮就挂错了文件。
    /// </remarks>
    private (string file, int line) ResolveSourceLocation(uint pc)
    {
        if (_compiled == null)
            return ("", 0);

        int best = -1;
        foreach (var key in _compiled.SourceMap.Keys)
            if (key <= pc && key > best)
                best = key;
        return best >= 0 ? _compiled.SourceMap[best] : ("", 0);
    }

    private void RefreshBreakpointList(EditorViewModel? tab)
    {
        _output.Breakpoints.Clear();
        if (tab == null)
            return;

        foreach (var line in BreakpointsOf(tab).OrderBy(x => x))
            _output.Breakpoints.Add(new BreakpointInfo
            {
                FilePath = tab.FileName,
                Line = line,
                IsEnabled = true,
            });
    }

    /// <summary>把当前执行行推给编辑器高亮（0 = 取消）。仅当前标签页视野。</summary>
    private void SetEditorExecutionLine(int line)
    {
        var control = _getSelectedEditor()?.EditorControl;
        if (control != null)
            control.ExecutionLine = line;
    }

    /// <summary>
    /// 阶段 VII 的新口径：执行行可能落在别的文件 —— 先把那个文件的标签页
    /// 带到前台，再对它高亮。找不到对应标签页（如未保存的 Untitled）就退回
    /// 当前标签页。
    /// </summary>
    private void SetEditorExecutionLine(string file, int line)
    {
        if (string.IsNullOrEmpty(file))
        {
            SetEditorExecutionLine(line);
            return;
        }

        var editor = _activateFile(file) ?? _getSelectedEditor();
        if (editor?.EditorControl != null)
            editor.EditorControl.ExecutionLine = line;
    }

    /// <summary>
    /// 编译当前调试目标。阶段 VII 起有工程就编整个工程（CompilationSources.Build
    /// 算出文件列表），入口用 .stproj 的 StartupProgram；没工程时退回单文件。
    /// 实际管线全在 <see cref="ProjectCompileService"/>（纯逻辑，有单测），这里只负责
    /// 报错和状态。
    /// </summary>
    private CompileResult? CompileCurrent(EditorViewModel tab)
    {
        var source = tab.Code ?? "";

        try
        {
            // 工程上下文与编辑器分析链完全同源（同一个 CompilationSources.Build），
            // 避免 "编辑器不报错、F5 报错"（或反过来）的两套口径。
            var project = _getProject();
            var files = CompilationSources.Build(
                project, tab.FilePath, tab.FileName, source, File.ReadAllText);
            string fileName = CompilationSources.NameFor(tab.FilePath, tab.FileName);

            var (result, errors) = ProjectCompileService.Compile(
                files,
                entryProgram: project?.StartupProgram,
                fallbackFileName: fileName);

            if (result == null)
            {
                foreach (var e in errors)
                    _output.Error(ProjectCompileService.FormatError(e));
                _setStatus($"编译失败: {errors.Count} 个错误");
                return null;
            }

            _output.Info(project is null
                ? $"编译成功: {fileName}"
                : $"编译成功: 工程 {project.Name}（{files.Count} 个文件，入口 {project.StartupProgram ?? fileName}）");
            return result;
        }
        catch (Exception ex)
        {
            // 后端抛的异常原本没人接 —— 会直接把 IDE 崩掉。这里收成错误行。
            // ProjectCompileService 内部已接一层，这里是保险丝。
            _output.Error($"编译器内部错误: {ex.Message}");
            _setStatus("编译失败（后端异常）");
            return null;
        }
    }
}
