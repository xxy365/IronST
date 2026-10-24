# IronST.StEditor 项目文档

---

## 一、项目概述

`IronST.StEditor` 是一个基于 WPF + AvalonEdit 构建的 **结构化文本（Structured Text / ST / IEC 61131-3）专用代码编辑器控件**。它以 `UserControl` 形式对外提供，可嵌入任何 WPF 宿主应用，支持语法高亮、智能补全、代码折叠、自动关键字大写、块关键字自动补全（snippet）、波浪线诊断等功能。

### 1.1 设计原则

- **编辑器是纯 UI 组件**：不依赖任何前端分析库（如 `IronST.Frontend`），所有语义数据通过 `EditorAnalysisData` 从外部注入。
- **功能模块链模式**：每个编辑行为（补全、自动大写、snippet 插入等）封装为独立的 `IEditorFeature`，通过链式顺序依次执行。
- **主题可切换**：内置 Catppuccin Mocha（深色）和 Catppuccin Latte（浅色）两套主题，支持运行时动态切换。

### 1.2 目标语言

IEC 61131-3 结构化文本（ST），覆盖关键字、数据类型、变量声明、控制结构、直接寻址（`%IX0.0`）、时间字面量（`T#1s`）、块注释（`(* ... *)`）和行注释（`//`）。

---

## 二、项目结构

```
IronST.StEditor/
├── Editor/
│   ├── StCompletionData.cs          # 单条补全项数据模型
│   ├── StCompletionProvider.cs      # 补全数据源（关键字 + 变量 + 函数 + 类型）
│   ├── StFoldingStrategy.cs         # 代码折叠策略
│   └── StSyntaxHighlightingTransformer.cs  # 语法高亮（正则 + 块注释状态机）
├── Features/
│   ├── IEditorFeature.cs            # 功能模块接口与基类
│   ├── AutoAssign.cs                # 自动输入 ":="
│   ├── AutoEndBlock.cs              # 块关键字自动 snippet（IF/FOR/WHILE...）
│   ├── AutoUppercase.cs             # 关键字自动大写
│   ├── Completion.cs                # 补全窗口管理
│   └── StateRebuilder.cs            # 块注释状态重建 + 代码折叠（防抖）
├── EditorAnalysisData.cs            # 外部注入的分析数据契约
├── StEditorControl.xaml             # 控件 XAML 布局
└── StEditorControl.xaml.cs          # 控件逻辑（依赖属性、事件分发、主题、状态栏）
```

---

## 三、架构设计

### 3.1 整体架构图

```
┌─────────────────────────────────────────────────────────┐
│                   宿主应用 (MainWindow / ViewModel)       │
│                                                          │
│  用户编辑文本 ──→ TextContentChanged 事件                 │
│  调用 FrontendPipeline.Analyze(text)                      │
│  构造 EditorAnalysisData ──→ MyEditor.AnalysisData = xxx  │
└────────────────────────────┬─────────────────────────────┘
                             │ EditorAnalysisData（唯一依赖方向）
                             ▼
┌──────────────────────────────────────────────────────────┐
│                   StEditorControl (UserControl)            │
│                                                          │
│  依赖属性：Text, ZoomLevel, EditorTheme, AnalysisData,    │
│           IsReadOnly, ShowLineNumbers, UnderlineRanges,   │
│           NavigateToRequest                               │
│                                                          │
│  ┌─────────────── 功能模块链 ───────────────┐            │
│  │                                          │            │
│  │  TextEntered 事件按顺序分发给：            │            │
│  │                                          │            │
│  │  ① Completion       (补全窗口)            │            │
│  │  ② AutoAssign       (自动 ":=")           │            │
│  │  ③ AutoUppercase    (关键字大写)           │            │
│  │  ④ AutoEndBlock     (snippet 插入)        │            │
│  │                                          │            │
│  └──────────────────────────────────────────┘            │
│                                                          │
│  ┌─── 基础服务 ────┐  ┌──── 渲染 ────────────┐          │
│  │ StateRebuilder  │  │ SyntaxHighlighting    │          │
│  │  (防抖 200ms)   │  │  (正则 + 状态机)      │          │
│  │  折叠 + 块注释   │  │                       │          │
│  └─────────────────┘  │ SquigglyUnderline     │          │
│                       │  (波浪线诊断)          │          │
│                       └───────────────────────┘          │
└──────────────────────────────────────────────────────────┘
```

### 3.2 事件流转

```
用户按键
    │
    ▼
TextEntering ──→ 若补全窗口打开且输入非标识符字符 → 关闭补全窗口
    │
    ▼
TextEntered ──→ DispatchTextEntered(text)
    │                │
    │                ├─① Completion.OnTextEntered
    │                │     字母/下划线 → 显示或过滤补全列表
    │                │
    │                ├─② AutoAssign.OnTextEntered
    │                │     ":" → 自动插入 "="
    │                │
    │                ├─③ AutoUppercase.OnTextEntered
    │                │     分隔符 → 关键字自动大写
    │                │
    │                └─④ AutoEndBlock.OnTextEntered
    │                      空格 → 块关键字 snippet
    │
    ▼
TextChanged ──→ 同步 Text 依赖属性
                StateRebuilder.OnTextChanged (200ms 防抖后重建折叠/高亮)
                触发 TextContentChanged 事件（通知宿主应用）
```

### 3.3 外部数据注入流程

```
宿主应用:
  1. 监听 MyEditor.TextContentChanged
  2. 防抖 (500ms)
  3. 调用 FrontendPipeline.Analyze(text) → 得到变量、函数、诊断等
  4. 构造 EditorAnalysisData
  5. 设置 MyEditor.AnalysisData = data

StEditorControl:
  OnAnalysisDataChanged 回调 →
    → ApplyAnalysisData(data)
      → _completion.AnalysisData = data      (供补全使用)
      → _squigglyRenderer.UpdateRanges(...)  (供波浪线使用)
```

---

## 四、核心类型 API 参考

### 4.1 `StEditorControl` — 编辑器控件

#### 依赖属性

| 属性名 | 类型 | 默认值 | 绑定模式 | 说明 |
|--------|------|--------|----------|------|
| `Text` | `string` | `""` | TwoWay | 编辑器文本内容 |
| `BaseFontSize` | `double` | `14.0` | OneWay | 基础字体大小 |
| `ZoomLevel` | `int` | `100` | OneWay | 缩放百分比（10-500） |
| `ShowLineNumbers` | `bool` | `true` | OneWay | 是否显示行号 |
| `IsReadOnly` | `bool` | `false` | OneWay | 只读模式 |
| `EditorTheme` | `EditorTheme` | `Dark` | OneWay | 主题（Dark / Light） |
| `NavigateToRequest` | `NavigationRequest?` | `null` | OneWay | 跳转请求（设置后自动跳转） |
| `UnderlineRanges` | `IList<TextUnderlineRange>?` | `null` | OneWay | 波浪线下划线区域 |
| `AnalysisData` | `EditorAnalysisData?` | `null` | OneWay | 外部注入的分析数据 |

#### 事件

| 事件名 | 类型 | 触发时机 |
|--------|------|----------|
| `TextContentChanged` | `EventHandler` | 编辑器文本内容变化时（已防抖同步 Text 属性之后） |

#### 公共方法

| 方法 | 说明 |
|------|------|
| `GetEditor()` | 返回内部的 `AvalonEdit.TextEditor` 实例 |
| `GoToLine(int lineNumber)` | 滚动到指定行并聚焦 |
| `Select(int start, int length)` | 选中指定范围的文本 |
| `Shutdown()` | 停止定时器、关闭补全窗口、移除渲染器，释放资源 |

#### XAML 用法

```xml
<Window xmlns:editor="clr-namespace:IronST.StEditor">
    <editor:StEditorControl
        x:Name="MyEditor"
        Text="{Binding Code, Mode=TwoWay}"
        EditorTheme="Dark"
        AnalysisData="{Binding EditorData}"
        TextContentChanged="MyEditor_TextContentChanged" />
</Window>
```

---

### 4.2 `EditorAnalysisData` — 外部注入数据契约

```csharp
public sealed class EditorAnalysisData
{
    public IReadOnlyList<SymbolInfo> Variables { get; init; }
    public IReadOnlyList<SymbolInfo> Functions { get; init; }
    public IReadOnlyList<SymbolInfo> TypeDefinitions { get; init; }
    public IReadOnlyList<DiagnosticInfo> Diagnostics { get; init; }
}
```

| 字段 | 用途 | 消费位置 |
|------|------|----------|
| `Variables` | 变量补全 | `StCompletionProvider.AddSymbolMatches` |
| `Functions` | 函数名补全（以 Keyword 图标显示） | `StCompletionProvider.AddSymbolMatches` |
| `TypeDefinitions` | 用户自定义类型补全（以 Type 图标显示） | `StCompletionProvider.AddSymbolMatches` |
| `Diagnostics` | 波浪线下划线 | `StEditorControl.ApplyAnalysisData` |

### 4.3 `SymbolInfo` — 符号信息

```csharp
public sealed class SymbolInfo
{
    public required string Name { get; init; }      // 名称
    public string TypeName { get; init; }           // 类型名（变量用）
    public string Description { get; init; }        // 描述（悬停显示）
    public int Line { get; init; }                  // 所在行
    public int Column { get; init; }                // 所在列
}
```

### 4.4 `DiagnosticInfo` — 诊断信息

```csharp
public sealed class DiagnosticInfo
{
    public int Offset { get; init; }                // 文档中的绝对偏移
    public int Length { get; init; }                // 长度
    public string Message { get; init; }            // 消息文本
    public DiagnosticSeverity Severity { get; init; } // Warning / Error
}
```

### 4.5 `NavigationRequest` — 跳转请求

```csharp
public sealed class NavigationRequest
{
    public int Line { get; }     // 目标行号（从 1 开始）
    public int Column { get; }   // 目标列号（从 0 开始，0 表示行首）
}
```

**用法**：设置依赖属性即可触发跳转：

```csharp
MyEditor.NavigateToRequest = new NavigationRequest(line: 42, column: 10);
```

跳转完成后控件自动聚焦并滚动到目标位置。若需再次跳转同一位置，需先设为 `null` 再重新设置（WDP 依赖属性不会对相同值重复触发回调）。

### 4.6 `TextUnderlineRange` — 波浪线区域

```csharp
public sealed class TextUnderlineRange
{
    public int Offset { get; }     // 文档绝对偏移
    public int Length { get; }     // 长度
    public Color Color { get; }    // 波浪线颜色（默认 OrangeRed）
}
```

**两种使用方式**：

1. **直接设置**（宿主自行管理偏移）：
   ```csharp
   MyEditor.UnderlineRanges = new List<TextUnderlineRange>
   {
       new TextUnderlineRange(offset: 100, length: 5),
       new TextUnderlineRange(offset: 200, length: 3, Colors.Goldenrod),
   };
   ```

2. **通过 AnalysisData 间接设置**（由 `ApplyAnalysisData` 自动转换）：
   ```csharp
   MyEditor.AnalysisData = new EditorAnalysisData
   {
       Diagnostics = new List<DiagnosticInfo>
       {
           new() { Offset = 100, Length = 5, Message = "未声明的变量", Severity = DiagnosticSeverity.Error },
       }
   };
   ```

> 注意：`UnderlineRanges` 和 `AnalysisData.Diagnostics` 同时使用时，后设置的会覆盖前者的波浪线。

### 4.7 枚举

```csharp
public enum EditorTheme { Dark, Light }

public enum CompletionContext { Default, InsideVarBlock }

public enum CompletionKind { Keyword, Type, Variable }

public enum DiagnosticSeverity { Warning, Error }
```

---

## 五、编辑器功能模块详解

### 5.1 `Completion` — 补全窗口

**触发条件**：用户输入字母或下划线时自动弹出/过滤补全列表。

**补全数据来源**：
1. 内置关键字（`PROGRAM`、`IF`、`VAR` 等控制关键字 + `BOOL`、`INT` 等数据类型）
2. 外部注入的 `AnalysisData`（变量名、函数名、自定义类型名）
3. 正则兜底解析当前文档中的变量声明

**排序规则**：精确匹配优先，其次按字母序。

**与 AutoEndBlock 的联动**：补全窗口关闭后触发 `AfterInsert` 回调，若插入的关键字是块关键字（如 `IF`、`FOR`），自动展开 snippet 模板。

**主题**：通过 `CompletionThemeColors` 支持深色/浅色切换。

### 5.2 `AutoAssign` — 自动输入 ":="

**触发条件**：用户输入 `:` 时。

**安全条件**：
- 前面不是 `:=`（避免重复）
- 后面不是 `=`（避免光标在中间时误触发）
- 不在字符串或注释内（字符扫描验证）

### 5.3 `AutoUppercase` — 关键字自动大写

**触发条件**：用户输入分隔符（空格、分号、回车、Tab）时。

**行为**：检测光标前的单词，如果是 ST 关键字且不是全大写，自动替换为大写。

**安全**：通过关键字集合查表 + `AutoAssign.IsInCodeContext` 验证不在字符串/注释内。

### 5.4 `AutoEndBlock` — 块关键字 Snippet

**触发条件**：用户输入空格时，检测光标前的单词是否是块关键字。

**支持的模板**：

| 关键字 | 生成内容 |
|--------|----------|
| `IF` | `IF condition THEN ... END_IF;` |
| `FOR` | `FOR i := 0 TO 10 BY 1 DO ... END_FOR;` |
| `WHILE` | `WHILE condition DO ... END_WHILE;` |
| `REPEAT` | `REPEAT ... UNTIL condition;` |
| `CASE` | `CASE expression OF 0: ... ELSE ... END_CASE;` |
| `PROGRAM` | `PROGRAM name ... END_PROGRAM;` |
| `FUNCTION` | `FUNCTION name : ReturnType ... END_FUNCTION;` |
| `FUNCTION_BLOCK` | `FUNCTION_BLOCK name ... END_FUNCTION_BLOCK;` |
| `ACTION` | `ACTION name ... END_ACTION;` |
| `TYPE` | `TYPE name ... END_TYPE;` |

**安全条件**：
- 关键字必须在行首（前面只有空白）
- 行尾只有空白
- 向后 300 字符内没有对应的 END 关键字（防重复）
- 占位符（如 `condition`）被选中，用户可直接输入覆盖

### 5.5 `StateRebuilder` — 状态重建器

**职责**：文本变化后延迟 200ms 重建块注释行状态和代码折叠。

**防抖机制**：通过 `DispatcherTimer` 实现，避免每次按键都重建。

**紧急刷新**：`RebuildImmediate` 在初始加载时立即执行，不等待定时器。

### 5.6 `StSyntaxHighlightingTransformer` — 语法高亮

**实现方式**：正则匹配 + 块注释状态机。

**Token 类型与颜色映射**：

| Token 类型 | 深色主题色 | 浅色主题色 |
|-----------|-----------|-----------|
| 注释 | `#6c7080` | `#9ca0b0` |
| 字符串 | `#a6e3a1` | `#40a02b` |
| 数字 | `#fab387` | `#fe640b` |
| 关键字 | `#cba6f7` | `#8839ef` |
| 数据类型 | `#89b4fa` | `#1e66f5` |
| 运算符 | `#f9e2af` | `#df8e1d` |
| 直接寻址 | `#89dceb` | `#179299` |

**块注释支持**：维护每行的块注释结束状态（`_lineEndStates`），跨行正确着色。

### 5.7 `StFoldingStrategy` — 代码折叠

**支持的折叠对**：

| 开始关键字 | 结束关键字 |
|-----------|-----------|
| `PROGRAM` | `END_PROGRAM` |
| `FUNCTION` | `END_FUNCTION` |
| `FUNCTION_BLOCK` | `END_FUNCTION_BLOCK` |
| `ACTION` | `END_ACTION` |
| `IF` | `END_IF` |
| `FOR` | `END_FOR` |
| `WHILE` | `END_WHILE` |
| `REPEAT` | `END_REPEAT` |
| `CASE` | `END_CASE` |
| `TYPE` | `END_TYPE` |
| `STRUCT` | `END_STRUCT` |
| `CONFIGURATION` | `END_CONFIGURATION` |
| `RESOURCE` | `END_RESOURCE` |

**折叠标题**：取开始行的前 60 个字符，超过则截断加 `...`。

### 5.8 `StCompletionProvider` — 补全数据源

**内置关键字库**：
- 43 个控制关键字（`PROGRAM`、`IF`、`FOR` 等）
- 23 个数据类型（`BOOL`、`INT`、`REAL`、`STRING` 等）
- 6 个额外关键字（`WITH`、`ON`、`AT`、`NON_RETAIN`、`VAR_ACCESS`、`VAR_CONFIG`）

**变量识别**：正则 `(?i)\b([a-zA-Z_]\w*)\s*:\s*(BOOL|INT|...)\b`，兜底解析文档中的变量声明。

**上下文感知**：检测光标是否在 `VAR...END_VAR` 块内，在块内优先显示类型补全，在块外优先显示关键字补全。

**公共静态字段**：

```csharp
internal static readonly HashSet<string> AllKeywords; // 所有关键字的快速查找集合
```

### 5.9 `SquigglyUnderlineRenderer` — 波浪线渲染器

**实现**：`IBackgroundRenderer` 接口，在 `KnownLayer.Selection` 层绘制贝塞尔曲线波浪线。

**性能优化**：画刷按颜色缓存（`_penCache`），避免重复创建。

---

## 六、主题系统

### 6.1 内置主题

| 主题 | 枚举值 | 色系基础 |
|------|--------|----------|
| Catppuccin Mocha | `EditorTheme.Dark` | 深色 `#1e1e2e` 底 |
| Catppuccin Latte | `EditorTheme.Light` | 浅色 `#eff1f5` 底 |

### 6.2 主题覆盖范围

- 编辑器背景、前景色、行号颜色
- 状态栏背景、前景色、分隔线颜色
- 选区高亮色、当前行高亮/边框色
- 语法高亮 7 种 Token 颜色
- 补全窗口背景、前景、选中项、悬浮项颜色
- 状态栏统计文字颜色（普通 / 选中时）

### 6.3 切换方式

```csharp
MyEditor.EditorTheme = EditorTheme.Light;
// 或
MyEditor.EditorTheme = EditorTheme.Dark;
```

通过 WPF 绑定：

```xml
<editor:StEditorControl EditorTheme="{Binding CurrentTheme}" />
```

---

## 七、状态栏

编辑器底部状态栏显示三项信息：

| 区域 | 内容 | 示例 |
|------|------|------|
| 光标位置 | 行号、列号 | `行 42, 列 10` |
| 统计 | 无选区时：字符数 + 行数 | `1234 个字符  56 行` |
|  | 有选区时：选中字符数 + 行数 | `(20 字符  3 行)` |
| 缩放 | 当前缩放百分比，点击重置为 100% | `100%` |

**缩放操作**：
- `Ctrl + 滚轮`：以 10% 步进缩放，范围 10%-500%
- 点击缩放百分比文字：重置为 100%

---

## 八、集成指南

### 8.1 最小集成示例

**XAML**：

```xml
<Window x:Class="MyApp.MainWindow"
        xmlns:editor="clr-namespace:IronST.StEditor">
    <editor:StEditorControl
        x:Name="MyEditor"
        Text="{Binding Code, Mode=TwoWay}"
        EditorTheme="Dark" />
</Window>
```

**代码后置**：

```csharp
// 启动后设置初始文本
MyEditor.Text = "PROGRAM Main\nVAR\n  x : INT;\nEND_VAR\nEND_PROGRAM";
```

### 8.2 完整集成示例（含 Frontend Pipeline 对接）

```csharp
public partial class MainWindow : Window
{
    private DispatcherTimer _analysisTimer;
    private string _pendingText = "";

    public MainWindow()
    {
        InitializeComponent();

        // 监听文本变化，防抖 500ms 后触发分析
        MyEditor.TextContentChanged += (_, _) =>
        {
            _pendingText = MyEditor.Text;
            _analysisTimer.Stop();
            _analysisTimer.Start();
        };

        _analysisTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _analysisTimer.Tick += (_, _) =>
        {
            _analysisTimer.Stop();
            RunAnalysis(_pendingText);
        };
    }

    private void RunAnalysis(string text)
    {
        // 1. 调用你的 Frontend Pipeline
        var result = FrontendPipeline.Analyze(text);

        // 2. 转换为编辑器数据契约
        MyEditor.AnalysisData = new EditorAnalysisData
        {
            Variables = result.Variables
                .Select(v => new SymbolInfo
                {
                    Name = v.Name,
                    TypeName = v.TypeName,
                    Line = v.Line,
                    Column = v.Column,
                }).ToList(),

            Functions = result.Functions
                .Select(f => new SymbolInfo
                {
                    Name = f.Name,
                    Description = $"返回类型: {f.ReturnType}",
                }).ToList(),

            Diagnostics = result.Diagnostics
                .Select(d => new DiagnosticInfo
                {
                    Offset = d.StartOffset,
                    Length = d.Length,
                    Message = d.Message,
                    Severity = d.IsError
                        ? DiagnosticSeverity.Error
                        : DiagnosticSeverity.Warning,
                }).ToList(),
        };
    }

    // 跳转到某一行
    private void GoToError_Click(object sender, RoutedEventArgs e)
    {
        MyEditor.NavigateToRequest = new NavigationRequest(line: 42, column: 10);
    }

    // 窗口关闭时释放资源
    protected override void OnClosed(EventArgs e)
    {
        MyEditor.Shutdown();
        base.OnClosed(e);
    }
}
```

### 8.3 直接设置波浪线（不经过 AnalysisData）

```csharp
// 手动指定波浪线区域
MyEditor.UnderlineRanges = new List<TextUnderlineRange>
{
    new TextUnderlineRange(offset: 100, length: 5),
    new TextUnderlineRange(offset: 200, length: 3, Colors.Goldenrod),
};

// 清除波浪线
MyEditor.UnderlineRanges = null;
```

### 8.4 切换主题

```csharp
MyEditor.EditorTheme = EditorTheme.Light;
```

### 8.5 设置只读模式

```csharp
MyEditor.IsReadOnly = true;
```

### 8.6 隐藏行号

```xml
<editor:StEditorControl ShowLineNumbers="False" />
```

---

## 九、扩展指南

### 9.1 添加新的功能模块

实现 `IEditorFeature` 接口，在 `StEditorControl.InitializeFeatures` 中添加到 `_enteredChain` 数组：

```csharp
// 1. 定义功能
public class MyFeature : EditorFeatureBase
{
    public override bool OnTextEntered(TextEditor editor, string inputText)
    {
        if (inputText != "#") return false;
        // ... 处理逻辑 ...
        return true;
    }
}

// 2. 在 StEditorControl.OnLoaded → InitializeFeatures 中注册
_enteredChain = new IEditorFeature[]
{
    _completion, _autoAssign, _autoUppercase, _autoEndBlock,
    new MyFeature(),   // ← 新增
};
```

> 执行顺序很重要：`Completion` 必须排在 `AutoEndBlock` 之前，因为补全窗口插入关键字后通过 `AfterInsert` 回调触发 snippet。

### 9.2 扩展补全数据

只需在外部构造 `EditorAnalysisData` 时添加更多数据：

```csharp
MyEditor.AnalysisData = new EditorAnalysisData
{
    Variables = existingVars,
    Functions = new List<SymbolInfo>
    {
        new() { Name = "TON", Description = "接通延时定时器" },
        new() { Name = "CTU", Description = "递增计数器" },
    },
    TypeDefinitions = new List<SymbolInfo>
    {
        new() { Name = "MotorState", Description = "电机状态枚举" },
    },
};
```

补全窗口会自动显示这些项，无需修改编辑器代码。

### 9.3 添加新的块折叠对

在 `StFoldingStrategy.BlockPairs` 数组中追加：

```csharp
private static readonly (string Start, string End)[] BlockPairs =
{
    // ... 已有的 ...
    ("VAR", "END_VAR"),               // 新增
    ("VAR_INPUT", "END_VAR"),         // 新增
};
```

### 9.4 添加新的块关键字 Snippet

在 `AutoEndBlock.Templates` 字典中追加：

```csharp
["WHILE"] = new(
    "WHILE {0} DO\n{2}\n{1}END_WHILE;",
    "condition"),
// 新增：
["REPEAT"] = new(
    "REPEAT\n{2}\n{1}UNTIL {0};",
    "condition"),
```

---

## 十、内部实现备注

### 10.1 块注释状态机

`StSyntaxHighlightingTransformer` 维护一个 `_lineEndStates` 数组，记录每行结束时是否处于块注释内。`ColorizeLine` 着色时读取上一行的结束状态作为本行的起始状态，保证跨行注释正确着色。

`StateRebuilder` 在文本变化后延迟 200ms 调用 `RebuildLineStates` 重建整个状态数组。

### 10.2 上下文分析（AnalyzeContext）

`StCompletionProvider.AnalyzeContext` 通过线性扫描光标前的文本，跟踪 `VAR...END_VAR` 嵌套深度来判断光标是否在变量声明块内。扫描过程中正确跳过：
- 块注释 `(* ... *)`
- 行注释 `// ...`
- 字符串字面量 `'...'`

### 10.3 线程安全

所有 UI 操作均在 WPF UI 线程上执行。`DispatcherTimer` 保证回调在 UI 线程触发。`EditorAnalysisData` 的所有集合使用 `IReadOnlyList`，建议外部构造后不再修改。

### 10.4 资源释放

宿主应用应在窗口关闭时调用 `Shutdown()` 方法：
- 停止 `StateRebuilder` 的防抖定时器
- 关闭补全窗口
- 移除波浪线渲染器

---

## 十一、依赖项

| 库 | 用途 | 版本要求 |
|---|---|---|
| **ICSharpCode.AvalonEdit** | 代码编辑器核心 | 6.0+ |
| **.NET** | 运行时 | .NET 6.0+（WPF） |

> 编辑器项目**不依赖** `IronST.Frontend` 或 `Antlr4.Runtime`。所有语义数据通过 `EditorAnalysisData` 从外部注入。