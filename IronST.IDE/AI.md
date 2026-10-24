
## IronST.IDE 项目结构

- **项目类型**：WPF (.NET 9) IDE 编辑器  
- **命名空间**：`IronST.IDE`  
- **启动窗口**：`Views/MainWindow.xaml`

### 目录结构
```
IronST.IDE/
├── App.xaml / App.xaml.cs        ← 应用程序入口，动态加载主题
├── Views/
│   ├── MainWindow.xaml / .cs     ← 主窗口，只负责布局，包含所有子控件
│   └── Controls/
│       ├── MenuBarControl.xaml / .cs
│       ├── ToolBarControl.xaml / .cs
│       ├── SolutionExplorerControl.xaml / .cs
│       ├── EditorAreaControl.xaml / .cs
│       ├── PropertiesPanelControl.xaml / .cs
│       ├── BottomPanelControl.xaml / .cs
│       └── StatusBarControl.xaml / .cs
├── Themes/
│   ├── CommonStyles.xaml         ← 所有控件样式、模板、字体、转换器（无颜色定义）
│   ├── LightColors.xaml          ← 仅浅色 Color + Brush
│   └── DarkColors.xaml           ← 仅深色 Color + Brush
├── Converters/                   ← 值转换器（BoolToVis, SeverityToBrush 等）
├── Services/                     ← 编辑器分析桥、宿主桥、工程工作区（ProjectWorkspace）
├── ViewModels/                   ← MainWindowViewModel 等
└── Models/                       ← EditorViewModel, SolutionNode 等
```

### 工程（.stproj）支持

- **新建 / 打开**：`文件 → New Project... / Open Project...`（`Ctrl+Shift+N` / `Ctrl+Shift+O`）。
  新建用 `SaveFileDialog` 一次选定目录 + 工程名，落盘由 `IronST.CLI.STProjectWriter.CreateProject`
  完成（复用同一套 `.stproj` 格式，绝不覆盖已有文件）；打开用 `STProjectFile.Load`。
- **生成 / 运行**：`工程 → Build Project / Run Project`（`Ctrl+Shift+B` / `Ctrl+F5`）。
  二者都是**在进程内调 `CliRouter`**（等价 `build <proj>` / `run <proj>`），保证与命令行
  同一套编译与执行；宿主输出与诊断经 Console 捕获后倒进输出面板。
  `Ctrl+F5` 在没有打开工程时仍是"跑当前标签页"（既有行为不变）。
- **添加文件**：`工程 → Add New Item... / Add Existing Item...`（`Ctrl+Shift+A` / `Shift+Alt+A`）。
  新增项 = 在工程目录里造一个可编译的 `.st` 模板；添加现有项按扩展名分流：
  `.st` → `<STCompile>`、`.stmod` → `<STModuleReference>`；
  工程目录**之外**的文件会先复制进工程目录（保持工程自足）。
  写 `.stproj` 是 **XML 级追加**（`STProjectWriter.AddSource/AddModuleReference`），
  只增不改 —— 工程文件里的注释、未知元素、PropertyGroup 顺序都原样保留。
- **右键菜单**：解决方案资源管理器里右键（任意位置）也能用同一组命令
  （Add New / Add Existing / Build / Run），未打开工程时整组禁用。
  右键会先选中被点的节点（`SolutionExplorerControl.xaml.cs` 的
  `TreeViewItem_PreviewMouseRightButtonDown`），不置 `e.Handled` 以保证菜单照常弹出。
- **逻辑位置**：`Services/ProjectWorkspace.cs`（不依赖 WPF，可单测 —— 见 `IronST.IDE.Tests/`）。
- **工程上下文分析**：打开工程后，语义分析按**整个工程**做（`FrontendPipeline.AnalyzeFiles`
  多文件、共享符号表），再把诊断按 `SourceSpan.FileName` 过滤出当前标签页那一份。
  没有这一步，`Main.st` 会把别的文件里的类型/函数全报成"未声明/未知类型"
  （实测 demo 工程 16 条假错误），而同一个工程 `build` 起来是 0 错误。
  - `Services/CompilationSources.cs`（纯逻辑）：算出前端要吃的文件列表 ——
    无工程 / 当前文件不属于工程 ⇒ 只有当前标签页；否则 ⇒ 整个工程，
    **当前文件用编辑器实时内容**（可能未保存），其余从磁盘读，顺序 = `.stproj` 顺序。
  - `Services/DiagnosticFilter.cs`（纯逻辑）：挑出属于某个文件的诊断；
    `Span.FileName` 为空的全局诊断按当前文件处理（宁可多报也不吞错误）。
  - `EditorAnalysisService.AnalyzeAllOpenTabs()`：打开/新建工程后必须调一次，
    否则已经开着的标签页还挂着"没有工程上下文"时算出来的假错误。
- **工程断点调试（IDE 阶段 VII 起）**：F5 不再只编译当前标签页 —— 改为编译**整个工程**
  （`Services/ProjectCompileService.cs` → 多文件走 `AnalyzeFiles`、单文件保持 `Analyze`；
  入口取 `.stproj` 的 `StartupProgram`），断点按 `(文件, 行)` 命中（`BreakpointPCs`），
  并高亮当前文件。
- **已知限制**：错误列表只显示**当前文件**的诊断。别的工程文件有错，
  要打开那个文件才看得到。

### 主题系统
- 三文件拆分：`CommonStyles.xaml`（模板） + `LightColors.xaml` / `DarkColors.xaml`（颜色）。
- 所有样式模板内的颜色/画刷引用均为 `DynamicResource`（字体和转换器为 `StaticResource`）。
- 运行时由 `App.xaml.cs` 动态加载：`OnStartup` 先加 `CommonStyles.xaml`，再根据 `Properties.Settings.Default.Theme` 加载对应颜色文件。
- 主题切换通过 `App.SwitchTheme("Light"/"Dark")` 实现，只替换颜色字典，公共样式常驻。
- 设计时资源文件：`Properties/DesignTimeResources.xaml` 合并了 `CommonStyles.xaml` + `LightColors.xaml`，供 Visual Studio 设计器预览。
- 窗口标题栏颜色同步：在 `App.ApplyTheme` 中通过 `DwmSetWindowAttribute` 设置（或 .NET 9 的 `ThemeMode`，使用 `#pragma warning disable WPF0001` 消除实验性 API 警告）。

### 关键细节
- `MainWindow.xaml` 使用 `Grid` 布局，第 0 列（解决方案资源管理器）和第 4 列（属性面板）列宽为固定初始值（如 `Width="260"`），面板自身不再设置固定宽度，通过 `HorizontalAlignment="Stretch"` 自动填满列宽，配合 `GridSplitter` 可调整大小。
- `MenuBarControl` 构造函数中有设计时检测 `DesignerProperties.GetIsInDesignMode(this)`，避免在设计器中将 `Application.Current` 强制转换为 `App` 导致异常。
- 所有子控件不单独设置 `DataContext`，继承主窗口的 `MainWindowViewModel`。

---

如果后续需要调整，只需告诉我修改哪个文件或哪个功能即可。