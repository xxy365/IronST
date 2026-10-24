# IronST.Frontend API 参考文档

> `FrontendPipeline.Analyze()` 的返回值 —— ST 语言前端分析结果

---

## 目录

1. [快速开始](#1-快速开始)
2. [AnalysisResult](#2-analysisresult)
3. [Variables — 变量收集](#3-variables--变量收集)
4. [POUs — 程序组织单元](#4-pous--程序组织单元)
5. [CustomTypes — 自定义类型](#5-customtypes--自定义类型)
6. [References — 符号引用](#6-references--符号引用)
7. [CallSites — 调用关系与参数](#7-callsites--调用关系与参数)
8. [Diagnostics — 诊断信息](#8-diagnostics--诊断信息)
9. [Symbols — 符号表](#9-symbols--符号表)
10. [TimeLiteralParser — 时间字面量解析](#10-timeliteralparser--时间字面量解析)
11. [枚举参考](#11-枚举参考)
12. [使用示例](#12-使用示例)
13. [断点与调试支持](#13-断点与调试支持)
14. [调试集成示例](#14-调试集成示例)

---

## 1. 快速开始

```csharp
using IronST.Frontend;
using IronST.Frontend.Analysis;

// 一次调用，获取全部分析结果
var result = FrontendPipeline.Analyze(sourceCode, "main.st");

// 检查是否有错误
if (!result.Success)
{
    foreach (var diag in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
        Console.WriteLine(diag);
}

// 读取变量列表（供补全用）
foreach (var v in result.Variables)
    Console.WriteLine($"{v.Name} : {v.TypeName} ({v.VarBlockKind})");

// 读取调用关系（供参数提示用）
foreach (var call in result.CallSites)
{
    foreach (var arg in call.Arguments)
        Console.WriteLine($"  {arg.ParameterName}: {arg.Direction} = {arg.ExpressionSummary}");
}
```

---

## 2. AnalysisResult

`FrontendPipeline.Analyze()` 的返回类型。

```csharp
namespace IronST.Frontend.Analysis;

public class AnalysisResult
{
    /// <summary>是否分析成功（无 Error 级别诊断）</summary>
    public bool Success { get; }

    /// <summary>抽象语法树</summary>
    public CompilationUnitNode AST { get; set; }

    /// <summary>全局符号表</summary>
    public SymbolTable Symbols { get; set; }

    /// <summary>所有诊断信息（语法错误 + 语义错误）</summary>
    public List<Diagnostic> Diagnostics { get; set; }

    /// <summary>变量清单（补全、悬停提示）</summary>
    public List<VariableInfo> Variables { get; set; }

    /// <summary>POU 信息（函数签名、参数）</summary>
    public List<POUInfo> POUs { get; set; }

    /// <summary>自定义类型（结构体字段、枚举成员）</summary>
    public List<TypeInfo> CustomTypes { get; set; }

    /// <summary>符号引用（转到定义、查找引用）</summary>
    public List<SymbolReference> References { get; set; }

    /// <summary>调用关系（调用层次、参数信息）</summary>
    public List<CallSite> CallSites { get; set; }
}
```

| 属性 | 用途 | 典型 IDE 功能 |
|---|---|---|
| `AST` | 完整抽象语法树（含 SourceSpan） | 行断点定位、语句级映射 |
| `Variables` | 所有声明的变量 | 自动补全、悬停提示、调试变量窗口 |
| `POUs` | 函数/函数块/程序签名 | 函数补全、签名帮助、函数断点 |
| `CustomTypes` | 用户定义类型 | 类型补全、dot-completion、调试结构体展开 |
| `References` | 所有符号出现位置 | 转到定义、查找引用、重命名、写入监控断点 |
| `CallSites` | 调用点及参数详情 | 参数提示、调用层次、调用处断点、调试调用栈 |
| `Diagnostics` | 编译诊断 | 错误列表、红色波浪线 |
| `Symbols` | 全局符号表 | 高级查询、调试时按名查找符号 |

---

## 3. Variables — 变量收集

```csharp
public record VariableInfo(
    string Name,          // 变量名
    string TypeName,      // 类型显示名，如 "INT", "REAL", "STRING(?)", "ARRAY OF BOOL"
    string POUName,       // 所属 POU 名称
    string VarBlockKind,  // "VAR", "VAR_INPUT", "VAR_OUTPUT", "VAR_IN_OUT", "VAR_GLOBAL", "VAR_EXTERNAL", "VAR_TEMP"
    string? FilePath,     // 源文件路径
    int Line              // 声明所在行号
);
```

### ST 源码 → 数据映射

```st
FUNCTION MotorControl : INT
VAR_INPUT
    targetSpeed : INT;        ← VariableInfo { Name="targetSpeed", TypeName="INT", VarBlockKind="VAR_INPUT", POUName="MotorControl" }
END_VAR
VAR_OUTPUT
    actualSpeed : INT;        ← VariableInfo { Name="actualSpeed", TypeName="INT", VarBlockKind="VAR_OUTPUT", POUName="MotorControl" }
END_VAR
VAR
    error : BOOL;             ← VariableInfo { Name="error",     TypeName="BOOL", VarBlockKind="VAR",         POUName="MotorControl" }
    buffer : ARRAY[0..9] OF REAL;  ← VariableInfo { Name="buffer", TypeName="ARRAY OF REAL", VarBlockKind="VAR", POUName="MotorControl" }
END_VAR
END_FUNCTION
```

### 使用示例

```csharp
// 自动补全：过滤变量
var items = result.Variables
    .Where(v => v.Name.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
    .Select(v => new CompletionItem(v.Name, $"变量: {v.TypeName} | {v.VarBlockKind}"));

// 悬停提示
var hovered = result.Variables.FirstOrDefault(v => v.Name == wordUnderCursor);
if (hovered != null)
    return $"变量: {hovered.TypeName}\n{hovered.VarBlockKind} | {hovered.POUName}";
```

---

## 4. POUs — 程序组织单元

```csharp
public record POUInfo(
    string Name,                        // POU 名称
    POUKind Kind,                       // Program / Function / FunctionBlock
    string? ReturnTypeName,             // Function 返回类型名，其他为 null
    List<ParameterInfo> Parameters,     // 参数列表
    string? FilePath,
    int Line
);

public record ParameterInfo(
    string Name,          // 参数名
    string TypeName,      // 类型显示名
    string Direction,     // "VAR_INPUT", "VAR_OUTPUT", "VAR_IN_OUT", "VAR"
    string? FilePath,
    int Line
);
```

### ST 源码 → 数据映射

```st
FUNCTION_BLOCK PID
VAR_INPUT
    setpoint : REAL;      ← ParameterInfo { Name="setpoint", TypeName="REAL", Direction="VAR_INPUT" }
    measured : REAL;      ← ParameterInfo { Name="measured", TypeName="REAL", Direction="VAR_INPUT" }
END_VAR
VAR_OUTPUT
    output : REAL;        ← ParameterInfo { Name="output",   TypeName="REAL", Direction="VAR_OUTPUT" }
END_VAR
VAR_IN_OUT
    state : BOOL;         ← ParameterInfo { Name="state",    TypeName="BOOL", Direction="VAR_IN_OUT" }
END_VAR
VAR
    kp : REAL;            ← ParameterInfo { Name="kp",       TypeName="REAL", Direction="VAR" }
END_VAR
END_FUNCTION_BLOCK

       ↓

POUInfo {
    Name: "PID",
    Kind: FunctionBlock,
    ReturnTypeName: null,
    Parameters: [ ... 5 个 ParameterInfo ... ]
}
```

```st
FUNCTION Add : INT               ← POUInfo { Kind=Function, ReturnTypeName="INT" }
PROGRAM Main                     ← POUInfo { Kind=Program, ReturnTypeName=null, Parameters=[] }
```

### 使用示例

```csharp
// 函数补全
var funcs = result.POUs
    .Where(p => p.Kind == POUKind.Function)
    .Where(p => p.Name.StartsWith(partial))
    .Select(p => new CompletionItem(p.Name, $"函数: {p.Name} : {p.ReturnTypeName}"));

// 签名帮助（参数提示弹窗）
var pou = result.POUs.FirstOrDefault(p => p.Name == calledFunctionName);
if (pou != null)
{
    var signature = string.Join(", ", pou.Parameters.Select(
        p => $"{p.Direction} {p.Name}: {p.TypeName}"));
    // 显示: "VAR_INPUT setpoint: REAL, VAR_INPUT measured: REAL, VAR_OUTPUT output: REAL"
}
```

---

## 5. CustomTypes — 自定义类型

```csharp
public record TypeInfo(
    string Name,              // 类型名
    TypeKind Kind,            // Struct / Enum / Subrange / SimpleType
    List<FieldInfo>? Fields,  // Struct 字段列表，其他为 null
    List<string>? EnumMembers,// Enum 成员列表，其他为 null
    string? UnderlyingType,   // SimpleType/Subrange 的底层类型
    string? FilePath,
    int Line
);

public record FieldInfo(
    string Name,          // 字段名
    string TypeName,      // 字段类型显示名
    string? FilePath,
    int Line
);
```

### ST 源码 → 数据映射

```st
TYPE Point :                   ← TypeInfo { Name="Point", Kind=Struct }
STRUCT
    X : REAL;                  ← FieldInfo { Name="X", TypeName="REAL" }
    Y : REAL;                  ← FieldInfo { Name="Y", TypeName="REAL" }
END_STRUCT;
END_TYPE;

TYPE Color : (Red, Green, Blue);  ← TypeInfo { Name="Color", Kind=Enum, EnumMembers=["Red","Green","Blue"] }

TYPE MyInt : DINT;             ← TypeInfo { Name="MyInt", Kind=SimpleType, UnderlyingType="DINT" }
```

### 使用示例

```csharp
// 类型名补全
var types = result.CustomTypes
    .Where(t => t.Name.StartsWith(partial));

// Dot-completion（结构体字段）
// 用户输入 "p." 时触发
var varType = result.Variables.FirstOrDefault(v => v.Name == "p")?.TypeName;
var structDef = result.CustomTypes.FirstOrDefault(
    t => t.Name == varType && t.Kind == TypeKind.Struct);

if (structDef?.Fields != null)
{
    // 显示: X, Y
    foreach (var field in structDef.Fields)
        yield return new CompletionItem(field.Name, field.TypeName);
}

// 悬停提示：结构体定义
if (type.Kind == TypeKind.Struct)
{
    var fields = string.Join("\n", type.Fields.Select(f => $"  {f.Name} : {f.TypeName}"));
    return $"TYPE {type.Name} :\nSTRUCT\n{fields}\nEND_STRUCT;\nEND_TYPE;";
}
```

---

## 6. References — 符号引用

```csharp
public record SymbolReference(
    string SymbolName,        // 符号名（变量名/函数名/类型名）
    SymbolKind Kind,          // Variable / Function / FunctionBlock / Type / Program
    ReferenceUsage Usage,     // Declaration / Read / Write / Call / TypeUsage
    string? POUName,          // 所在 POU
    string? FilePath,         // 源文件路径
    int Line,                 // 行号
    int Column                // 列号
);
```

### ST 源码 → 数据映射

```st
PROGRAM Main                   ← SymbolReference { SymbolName="Main", Kind=Program, Usage=Declaration }
VAR
    x : INT;                   ← SymbolReference { SymbolName="x",    Kind=Variable, Usage=Declaration }
END_VAR
x := x + 1;                   ← SymbolReference { SymbolName="x",    Kind=Variable, Usage=Write }     // 左边
                               ← SymbolReference { SymbolName="x",    Kind=Variable, Usage=Read }      // 右边第1个
                               ← SymbolReference { SymbolName="x",    Kind=Variable, Usage=Read }      // 右边第2个
END_PROGRAM

FUNCTION Add : INT             ← SymbolReference { SymbolName="Add",  Kind=Function, Usage=Declaration }
...
END_FUNCTION

PROGRAM Main
r := Add(1, 2);               ← SymbolReference { SymbolName="Add",  Kind=Function, Usage=Call }
END_PROGRAM
```

### 使用示例

```csharp
// 转到定义
var def = result.References.FirstOrDefault(
    r => r.SymbolName == cursorWord && r.Usage == ReferenceUsage.Declaration);
if (def != null)
    NavigateTo(def.FilePath, def.Line, def.Column);

// 查找所有引用
var refs = result.References
    .Where(r => r.SymbolName == selectedWord)
    .ToList();
HighlightReferences(refs);

// 重命名预览
var allRefs = result.References.Where(r => r.SymbolName == oldName);
foreach (var r in allRefs)
    ShowRenamePreview(r.FilePath, r.Line, r.Column, oldName, newName);
```

---

## 7. CallSites — 调用关系与参数

### CallSite

```csharp
public record CallSite(
    string CallerName,                    // 调用方 POU 名
    string CalleeName,                    // 被调函数/函数块名
    string? CallerPOU,                    // 调用方所在 POU
    string? FilePath,
    int Line,
    List<CallArgumentInfo> Arguments      // 参数详情列表
)
{
    public int ArgumentCount => Arguments.Count;  // 参数个数（自动计算）
}
```

### CallArgumentInfo

```csharp
public record CallArgumentInfo(
    int Index,                            // 第几个参数（从 0 开始）
    string? ParameterName,                // 形参名
    string? ParameterTypeName,            // 形参类型名
    ParameterDirection Direction,         // Input / Output / InOut / Unknown
    string ExpressionSummary,             // 实参摘要（变量名、字面量、表达式）
    string? ResolvedTypeName,             // 实参推导后的类型名
    ArgumentBindingStyle Binding          // Positional / NamedInput / NamedOutput
);
```

### ST 源码 → 数据映射

#### 位置参数调用

```st
FUNCTION Add : INT
VAR_INPUT
    a : INT;
    b : INT;
END_VAR
Add := a + b;
END_FUNCTION

PROGRAM Main
VAR r : INT; END_VAR
r := Add(1, 2);              ← CallSite {
END_PROGRAM                      CalleeName: "Add",
                                 CallerName: "Main",
                                 Arguments: [
                                   {
                                     Index: 0,
                                     ParameterName: "a",          ← 按索引自动匹配
                                     ParameterTypeName: "INT",    ← 形参类型
                                     Direction: Input,            ← VAR_INPUT
                                     ExpressionSummary: "1",      ← 实参摘要
                                     ResolvedTypeName: "INT",     ← 实参类型
                                     Binding: Positional          ← 按位置
                                   },
                                   {
                                     Index: 1,
                                     ParameterName: "b",
                                     ParameterTypeName: "INT",
                                     Direction: Input,
                                     ExpressionSummary: "2",
                                     ResolvedTypeName: "INT",
                                     Binding: Positional
                                   }
                                 ]
                               }
```

#### 命名参数调用

```st
r := Add(a := 10, b := 20);   ← Arguments: [
                                      { ParameterName: "a", Binding: NamedInput, ExpressionSummary: "10" },
                                      { ParameterName: "b", Binding: NamedInput, ExpressionSummary: "20" }
                                    ]
```

#### FB 调用（混合 Input/Output/InOut）

```st
FUNCTION_BLOCK PID
VAR_INPUT
    setpoint : REAL;
    measured : REAL;
END_VAR
VAR_OUTPUT
    controlOutput : REAL;
END_VAR
VAR_IN_OUT
    integralState : REAL;
END_VAR
END_FUNCTION_BLOCK

PROGRAM Main
VAR
    pid : PID;
    sp, pv, output, integral : REAL;
END_VAR
pid(setpoint := sp, measured := pv, controlOutput => output, integralState := integral);
END_PROGRAM
```

```
CallSite {
    CalleeName: "pid",
    CallerName: "Main",
    Arguments: [
        {
            ParameterName: "setpoint",
            Direction: Input,                    ← VAR_INPUT
            Binding: NamedInput,                 ← := 语法
            ExpressionSummary: "sp"
        },
        {
            ParameterName: "measured",
            Direction: Input,
            Binding: NamedInput,
            ExpressionSummary: "pv"
        },
        {
            ParameterName: "controlOutput",
            Direction: Output,                   ← VAR_OUTPUT
            Binding: NamedOutput,                ← => 语法
            ExpressionSummary: "output"
        },
        {
            ParameterName: "integralState",
            Direction: InOut,                    ← VAR_IN_OUT
            Binding: NamedInput,                 ← := 语法
            ExpressionSummary: "integral"
        }
    ]
}
```

### 使用示例

```csharp
// 参数提示弹窗
var call = result.CallSites
    .Where(c => c.CalleeName == functionName)
    .Where(c => cursorLine >= c.Line)  // 当前行在调用之后
    .OrderByDescending(c => c.Line)
    .FirstOrDefault();

if (call != null)
{
    // 构建签名字符串
    var parts = call.Arguments.Select((arg, i) =>
    {
        var dir = arg.Direction switch
        {
            ParameterDirection.Output => "OUT ",
            ParameterDirection.InOut  => "IN/OUT ",
            _ => ""
        };
        var active = (i == currentArgIndex) ? "▸" : " ";
        return $"{active} {dir}{arg.ParameterName}: {arg.ParameterTypeName}";
    });
    ShowSignatureHelp(parts);
}

// 参数数量校验
var expected = result.POUs.FirstOrDefault(p => p.Name == calleeName)?.Parameters.Count;
if (expected.HasValue && call.ArgumentCount != expected.Value)
    ShowError($"函数 {calleeName} 期望 {expected} 个参数，但传入了 {call.ArgumentCount} 个");

// 调用层次
var callers = result.CallSites
    .Where(c => c.CalleeName == selectedFunction)
    .Select(c => c.CallerName);
// 显示谁调用了 selectedFunction

var callees = result.CallSites
    .Where(c => c.CallerName == selectedFunction)
    .Select(c => c.CalleeName);
// 显示 selectedFunction 调用了谁
```

---

## 8. Diagnostics — 诊断信息

```csharp
public record Diagnostic(
    DiagnosticSeverity Severity,   // Error / Warning / Info
    ErrorCode Code,                // E1xxx 语法错误 / E2xxx 语义错误
    string Message,                // 描述信息
    SourceSpan? Span               // 位置（行号、列号、文件名）
);
```

### 常见错误码

| 错误码 | 描述 | 含义 |
|---|---|---|
| `E2001` | 未声明的标识符 | 使用了未声明的变量 |
| `E2006` | 未知的函数或程序块 | 调用了未声明的函数 |
| `E2008` | 结构体没有该字段 | 访问了不存在的结构体字段 |
| `E2015` | 未知的类型 | 变量声明、函数参数、结构体成员中使用了不存在的类型名 |
| `E1xxx` | 语法错误 | 源码语法不符合 ST 规范 |

### 使用示例

```csharp
// 推送到错误列表
foreach (var d in result.Diagnostics)
{
    var item = new ErrorItem
    {
        Severity = d.Severity.ToString(),       // "Error", "Warning"
        Code = d.Code.ToString(),               // "E2001"
        Description = d.Message,                // "未声明的变量 'x'"
        FilePath = d.Span?.FileName ?? "",
        Line = d.Span?.Line ?? 0,
        Column = d.Span?.Column ?? 0
    };
    ErrorList.Add(item);
}

// 红色波浪线
var errorRanges = result.Diagnostics
    .Where(d => d.Severity == DiagnosticSeverity.Error && d.Span.HasValue)
    .Select(d => new TextRange(d.Span!.Value.Line, d.Span.Value.Column,
                               d.Span.Value.EndLine, d.Span.Value.EndColumn));
```

---

## 9. Symbols — 符号表

```csharp
// 全局符号表，可通过名称查找任意符号
var sym = result.Symbols.Resolve("MyFunction");

// 符号类型判断
switch (sym)
{
    case FunctionSymbol fs:
        // fs.Name, fs.ReturnType, fs.Parameters
        break;
    case FunctionBlockSymbol fbs:
        // fbs.Name, fbs.Members
        break;
    case TypeSymbol ts:
        // ts.Name, ts.Type (IECType)
        break;
    case VariableSymbol vs:
        // vs.Name, vs.Type, vs.Scope
        break;
}
```

---

## 10. TimeLiteralParser — 时间字面量解析

位于 `IronST.Frontend.Analysis`，内部静态类。

语义分析阶段（`SemanticAnalyzer.InferType`）自动调用，将时间字面量的 `RawText` 解析为数值并存入 `LiteralExprNode.ParsedValue`。下游 CodeGen 直接取值即可，无需自行解析文本。

### 存储方案

| 类型 | ST 字面量示例 | 存储字段 | 存储类型 | 存储值示例 |
|------|--------------|----------|----------|-----------|
| TIME | `T#5s3ms` | 总毫秒数 | `long` | `5003` |
| TIME | `T#60s` | 总毫秒数 | `long` | `60000` |
| TIME | `T#1d2h30m` | 总毫秒数 | `long` | `95400000` |
| DATE | `D#2024-01-01` | 自 1970-01-01 起的天数 | `long` | `19723` |
| TIME_OF_DAY | `TOD#12:30:00` | 自 00:00:00 起的毫秒数 | `long` | `45000000` |
| DATE_AND_TIME | `DT#2024-01-01-12:30:00` | 自 1970-01-01 起的总毫秒数 | `long` | `1703920200000` |

### TIME 换算规则

```st
T#1d2h30m5s3ms
  = 1 × 86400000  (d → ms)
  + 2 × 3600000   (h → ms)
  + 30 × 60000    (m → ms)
  + 5 × 1000      (s → ms)
  + 3              (ms)
  = 95405003 ms
```

支持的单位后缀：

| 后缀 | 含义 | 换算为 ms |
|------|------|-----------|
| `d` | 天 | × 86400000 |
| `h` | 小时 | × 3600000 |
| `m` / `min` | 分钟 | × 60000 |
| `s` | 秒 | × 1000 |
| `ms` | 毫秒 | × 1 |
| `us` / `µs` | 微秒 | 截断为 0（ms 精度） |
| `ns` | 纳秒 | 截断为 0（ms 精度） |
| 无后缀 | 默认秒 | × 1000 |

### DATE 计算规则

```st
D#2024-01-01
  → 从 1970-01-01 到 2024-01-01 经过的天数
  = 19723 天
```

### TIME_OF_DAY 计算规则

```st
TOD#12:30:00.5
  = 12 × 3600000 + 30 × 60000 + 0 × 1000 + 500
  = 45000500 ms
```

支持秒的小数部分（如 `12:30:00.5` = 500ms）。

### DATE_AND_TIME 计算规则

```st
DT#2024-01-01-12:30:00
  = 天数 × 86400000 + 当天毫秒数
  = 19723 × 86400000 + 45000000
  = 1703920200000 ms
```

支持两种分隔格式：`DT#2024-01-01-12:30:00` 和 `DT#2024-01-01 12:30:00`。

### ST 源码 → 数据映射

```st
delay := T#5s3ms;     ← LiteralExprNode { Kind=Time,        RawText="T#5s3ms",              ParsedValue=5003L,              ResolvedType=IECType.Time }
today := D#2024-01-01;← LiteralExprNode { Kind=Date,        RawText="D#2024-01-01",         ParsedValue=19723L,             ResolvedType=IECType.Date }
lunch := TOD#12:30:00;← LiteralExprNode { Kind=TimeOfDay,   RawText="TOD#12:30:00",         ParsedValue=45000000L,          ResolvedType=IECType.Tod }
stamp := DT#2024-01-01-12:30:00; ← LiteralExprNode { Kind=DateTime, RawText="DT#2024-01-01-12:30:00", ParsedValue=1703920200000L, ResolvedType=IECType.Dt }
```

### CodeGen 使用方式

```csharp
// ExpressionEmitter.EmitLiteral 中：
case LiteralKind.Time:
case LiteralKind.Date:
case LiteralKind.TimeOfDay:
case LiteralKind.DateTime:
    return new ConstantInt(IrType.Int64, (long)lit.ParsedValue);
```

### 注意事项

- `ParsedValue` 为 `null` 时表示解析失败（如格式错误），CodeGen 应做 null 检查
- `RawText` 始终保留原始文本，可用于错误消息或调试显示
- `TryParse` 内部捕获所有异常，解析失败不中断语义分析

---

## 11. 枚举参考

### POUKind

| 值 | 含义 |
|---|---|
| `Program` | PROGRAM |
| `Function` | FUNCTION |
| `FunctionBlock` | FUNCTION_BLOCK |

### TypeKind

| 值 | 含义 |
|---|---|
| `Struct` | STRUCT 定义 |
| `Enum` | 枚举定义 |
| `Subrange` | 子范围类型 |
| `SimpleType` | 简单类型别名 |

### SymbolKind

| 值 | 含义 |
|---|---|
| `Variable` | 变量 |
| `Function` | FUNCTION |
| `FunctionBlock` | FUNCTION_BLOCK |
| `Type` | 用户自定义类型 |
| `Program` | PROGRAM |

### ReferenceUsage

| 值 | 含义 |
|---|---|
| `Declaration` | 声明处（转到定义的目标） |
| `Read` | 读取引用 |
| `Write` | 写入引用 |
| `Call` | 函数调用 |
| `TypeUsage` | 类型使用（如变量声明中用到的类型名） |

### ParameterDirection

| 值 | ST 关键字 | 含义 |
|---|---|---|
| `Input` | `VAR_INPUT` | 输入参数（传值） |
| `Output` | `VAR_OUTPUT` | 输出参数（传出） |
| `InOut` | `VAR_IN_OUT` | 输入输出参数（引用） |
| `Unknown` | — | 无法推断（如内置函数） |

### ArgumentBindingStyle

| 值 | ST 语法 | 含义 |
|---|---|---|
| `Positional` | `Add(1, 2)` | 按位置传递 |
| `NamedInput` | `Add(a := 1)` | 按名 + `:=` |
| `NamedOutput` | `fb(y => result)` | 按名 + `=>` |

### DiagnosticSeverity

| 值 | 含义 |
|---|---|
| `Error` | 编译错误 |
| `Warning` | 编译警告 |
| `Info` | 信息提示 |

### LiteralKind

| 值 | 含义 | ParsedValue 类型 |
|---|---|---|
| `Integer` | 整数字面量 | `long`（由 Parser 直接解析） |
| `Real` | 浮点字面量 | `double` |
| `BasedInteger` | 基数整数（如 `2#1010`） | `long` |
| `Boolean` | TRUE / FALSE | `bool` |
| `String` | 字符串字面量 | `string` |
| `Time` | T# 持续时间 | `long`（毫秒，由 TimeLiteralParser 解析） |
| `Date` | D# 日期 | `long`（天数，由 TimeLiteralParser 解析） |
| `TimeOfDay` | TOD# 当天时间 | `long`（毫秒，由 TimeLiteralParser 解析） |
| `DateTime` | DT# 日期时间 | `long`（总毫秒，由 TimeLiteralParser 解析） |

---

## 12. 使用示例

### 完整 IDE 集成流程

```csharp
public class EditorViewModel
{
    private AnalysisResult? _analysis;

    // 文本变化时调用（停顿 300ms 后）
    public void OnTextChanged(string code, string filePath)
    {
        _analysis = FrontendPipeline.Analyze(code, filePath);

        // 1. 推送错误列表
        UpdateErrorList(_analysis.Diagnostics);

        // 2. 刷新补全缓存
        RefreshCompletionCache(_analysis);
    }

    // 自动补全
    public List<CompletionItem> GetCompletions(string partial, int caretOffset)
    {
        if (_analysis == null) return [];

        var items = new List<CompletionItem>();

        // 关键字
        items.AddRange(GetKeywordCompletions(partial));

        // 变量
        items.AddRange(_analysis.Variables
            .Where(v => v.Name.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
            .Select(v => new CompletionItem(v.Name, $"变量: {v.TypeName}", CompletionKind.Variable)));

        // 函数
        items.AddRange(_analysis.POUs
            .Where(p => p.Name.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
            .Select(p => new CompletionItem(p.Name, $"函数: {p.Name}", CompletionKind.Function)));

        // 类型
        items.AddRange(_analysis.CustomTypes
            .Where(t => t.Name.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
            .Select(t => new CompletionItem(t.Name, $"类型: {t.Kind}", CompletionKind.Type)));

        return items;
    }

    // Dot-completion（结构体字段）
    public List<CompletionItem> GetDotCompletions(string varName)
    {
        if (_analysis == null) return [];

        var variable = _analysis.Variables.FirstOrDefault(
            v => v.Name.Equals(varName, StringComparison.OrdinalIgnoreCase));
        if (variable == null) return [];

        var structType = _analysis.CustomTypes.FirstOrDefault(
            t => t.Name.Equals(variable.TypeName, StringComparison.OrdinalIgnoreCase)
              && t.Kind == TypeKind.Struct);
        if (structType?.Fields == null) return [];

        return structType.Fields.Select(f =>
            new CompletionItem(f.Name, f.TypeName, CompletionKind.Field)).ToList();
    }

    // 悬停提示
    public string? GetHoverInfo(string word)
    {
        if (_analysis == null) return null;

        // 变量
        var variable = _analysis.Variables.FirstOrDefault(
            v => v.Name.Equals(word, StringComparison.OrdinalIgnoreCase));
        if (variable != null)
            return $"**变量** `{variable.Name}` : `{variable.TypeName}`\n" +
                   $"{variable.VarBlockKind} | {variable.POUName}";

        // POU
        var pou = _analysis.POUs.FirstOrDefault(
            p => p.Name.Equals(word, StringComparison.OrdinalIgnoreCase));
        if (pou != null)
        {
            var sig = pou.Kind switch
            {
                POUKind.Function       => $"FUNCTION {pou.Name} : {pou.ReturnTypeName}",
                POUKind.FunctionBlock  => $"FUNCTION_BLOCK {pou.Name}",
                POUKind.Program        => $"PROGRAM {pou.Name}",
                _ => pou.Name
            };
            if (pou.Parameters.Count > 0)
            {
                var parms = string.Join("\n", pou.Parameters.Select(
                    p => $"  {p.Direction} {p.Name} : {p.TypeName}"));
                sig += $"\n{parms}";
            }
            return sig;
        }

        // 类型
        var type = _analysis.CustomTypes.FirstOrDefault(
            t => t.Name.Equals(word, StringComparison.OrdinalIgnoreCase));
        if (type != null)
        {
            if (type.Kind == TypeKind.Struct && type.Fields != null)
            {
                var fields = string.Join("\n", type.Fields.Select(
                    f => $"  {f.Name} : {f.TypeName}"));
                return $"TYPE {type.Name} :\nSTRUCT\n{fields}\nEND_STRUCT;\nEND_TYPE;";
            }
            if (type.Kind == TypeKind.Enum && type.EnumMembers != null)
                return $"TYPE {type.Name} : ({string.Join(", ", type.EnumMembers)}); END_TYPE;";
        }

        return null;
    }

    // 转到定义
    public (string? filePath, int line, int column)? GoToDefinition(string word)
    {
        if (_analysis == null) return null;

        var decl = _analysis.References.FirstOrDefault(
            r => r.SymbolName.Equals(word, StringComparison.OrdinalIgnoreCase)
              && r.Usage == ReferenceUsage.Declaration);

        return decl != null ? (decl.FilePath, decl.Line, decl.Column) : null;
    }

    // 查找所有引用
    public List<SymbolReference> FindAllReferences(string word)
    {
        if (_analysis == null) return [];

        return _analysis.References
            .Where(r => r.SymbolName.Equals(word, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // 参数提示
    public CallArgumentInfo[]? GetSignatureHelp(string functionName, int argumentIndex)
    {
        if (_analysis == null) return null;

        var call = _analysis.CallSites
            .Where(c => c.CalleeName.Equals(functionName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.Line)
            .FirstOrDefault();

        return call?.Arguments.ToArray();
    }
}
```

---

## 13. 断点与调试支持

`AnalysisResult` 同时为调试器提供源码级元数据。前端不执行 ST 程序，但通过 AST、符号引用和调用关系为各类断点提供「在哪里断」和「断了看什么」的全部信息。

### 13.1 支持的断点类型总览

| 断点类型 | 需要的数据 | 数据来源 |
|---|---|---|
| 行断点 | 语句所在的行号、列号 | `AST` 中每个节点的 `Span` |
| 函数断点 | POU 名称和入口行号 | `POUs` |
| 调用处断点 | 调用点的位置和参数详情 | `CallSites` |
| 写入监控断点 | 变量所有被写入的位置 | `References`（`Usage == Write`） |
| 条件断点合法性验证 | 变量是否存在、类型是否匹配 | `Variables` + `Symbols` |

断点命中后，调试器需要展示的信息：

| 调试器功能 | 数据来源 |
|---|---|
| 变量窗口（当前作用域变量列表） | `Variables` 按 `POUName` 过滤 |
| 变量类型 | `VariableInfo.TypeName` |
| 调用栈（谁调用了当前函数） | `CallSites` 按 `CalleeName` 查 `CallerName` |
| 调用参数信息 | `CallSite.Arguments`（方向、类型、绑定方式） |
| 结构体字段展开 | `CustomTypes` 中 `Kind == Struct` 的 `Fields` |
| 枚举成员提示 | `CustomTypes` 中 `Kind == Enum` 的 `EnumMembers` |

### 13.2 AST 与 SourceSpan

AST 中每个节点都继承自 `ASTNode`，携带精确的源码位置：

```csharp
public abstract class ASTNode
{
    public SourceSpan Span { get; set; } = SourceSpan.Unknown;
}

public readonly record struct SourceSpan(
    int Line,       // 起始行号
    int Column,     // 起始列号
    int EndLine,    // 结束行号
    int EndColumn,  // 结束列号
    string? FileName
);
```

**语句级断点映射**：遍历 AST，收集所有 `StatementNode` 及其 `Span.Line`，即可建立「行号 → 语句节点」的映射表。

```st
PROGRAM Main           ← 行 2：非可执行行，不可断
VAR
    x : INT;           ← 行 4：变量声明，不可断
END_VAR                ← 行 5：非可执行行，不可断
x := 42;               ← 行 6：AssignmentNode，可断
IF x > 0 THEN          ← 行 7：IfNode，可断
    x := x + 1;        ← 行 8：AssignmentNode，可断
END_IF                 ← 行 9：非可执行行，不可断
END_PROGRAM            ← 行 10：非可执行行，不可断
```

可断点的节点类型包括：

| AST 节点 | 对应 ST 语句 |
|---|---|
| `AssignmentNode` | `x := expr;` |
| `CallStatementNode` | `fb(args);` |
| `ReturnNode` | `RETURN;` |
| `ExitNode` | `EXIT;` |
| `IfNode` | `IF ... THEN` |
| `ForNode` | `FOR ... DO` |
| `WhileNode` | `WHILE ... DO` |
| `RepeatNode` | `REPEAT` |
| `CaseNode` | `CASE ... OF` |
| `RefAssignmentNode` | `REF=` |

### 13.3 行断点

用户在编辑器某行点击左侧边栏，设置行断点。

```csharp
/// <summary>
/// 从 AST 中建立「行号 → 语句节点」映射，用于行断点验证。
/// 仅包含可执行语句，不包含声明行和关键字行。
/// </summary>
public static List<(int Line, StatementNode Node)> BuildStatementLineMap(CompilationUnitNode ast)
{
    var map = new List<(int Line, StatementNode Node)>();
    foreach (var pou in ast.POUs)
        CollectStatements(pou, map);
    return map;
}

private static void CollectStatements(POUNode pou, List<(int Line, StatementNode)> map)
{
    foreach (var stmt in pou.Body)
        CollectRecursive(stmt, map);

    if (pou is FunctionBlockNode fb)
    {
        foreach (var item in fb.BodyItems)
        {
            if (item is FBStatementItemNode si)
                CollectRecursive(si.Statement, map);
            else if (item is FBActionItemNode ai)
                foreach (var stmt in ai.Action.Body)
                    CollectRecursive(stmt, map);
        }
    }
}

private static void CollectRecursive(StatementNode stmt, List<(int Line, StatementNode)> map)
{
    map.Add((stmt.Span.Line, stmt));

    switch (stmt)
    {
        case IfNode ifn:
            foreach (var s in ifn.ThenBody) CollectRecursive(s, map);
            foreach (var (_, body) in ifn.ElsIfBranches)
                foreach (var s in body) CollectRecursive(s, map);
            if (ifn.ElseBody != null)
                foreach (var s in ifn.ElseBody) CollectRecursive(s, map);
            break;
        case ForNode fn:
            foreach (var s in fn.Body) CollectRecursive(s, map);
            break;
        case WhileNode wn:
            foreach (var s in wn.Body) CollectRecursive(s, map);
            break;
        case RepeatNode rn:
            foreach (var s in rn.Body) CollectRecursive(s, map);
            break;
        case CaseNode cn:
            foreach (var b in cn.Branches)
                foreach (var s in b.Body) CollectRecursive(s, map);
            if (cn.ElseBody != null)
                foreach (var s in cn.ElseBody) CollectRecursive(s, map);
            break;
    }
}
```

```csharp
// 使用：判断某行是否可打断点
var stmtMap = BuildStatementLineMap(result.AST);
bool canBreak = stmtMap.Any(s => s.Line == userClickedLine);
```

### 13.4 函数断点

用户指定 POU 名称，在该 POU 入口处断下。

```csharp
// 通过 POU 名称定位入口行
string targetPOU = "MotorControl";
var pou = result.POUs.FirstOrDefault(p => p.Name == targetPOU);

if (pou != null)
{
    int entryLine = pou.Line;   // POU 声明所在行
    POUKind kind = pou.Kind;    // Function / FunctionBlock / Program
    // 在 entryLine 打断点
}
```

```st
FUNCTION MotorControl : INT   ← POUInfo { Name="MotorControl", Kind=Function, Line=2 }
VAR_INPUT
    targetSpeed : INT;
END_VAR
MotorControl := targetSpeed;
END_FUNCTION
```

### 13.5 写入监控断点（Data Breakpoint）

用户希望在变量被赋值时暂停。

```csharp
// 查找变量所有被写入的位置
string watchVar = "speed";
var writeLocations = result.References
    .Where(r => r.SymbolName == watchVar
             && r.SymbolKind == SymbolKind.Variable
             && r.Usage == ReferenceUsage.Write)
    .ToList();

// 每个位置都是一个可插入断点的行号
foreach (var loc in writeLocations)
    Console.WriteLine($"写入 {watchVar}: 行 {loc.Line}, 列 {loc.Column}");
```

```st
PROGRAM Main
VAR speed : INT; END_VAR
speed := 100;         ← Write，行 4
speed := 200;         ← Write，行 5
IF speed > 150 THEN   ← Read，不是写入，不会被监控
    speed := 0;       ← Write，行 7
END_IF
END_PROGRAM
```

### 13.6 调用处断点

用户希望在特定函数调用处暂停。

```csharp
// 找到所有调用 pid 的位置
var pidCalls = result.CallSites
    .Where(c => c.CalleeName == "pid")
    .ToList();

foreach (var call in pidCalls)
{
    Console.WriteLine($"行 {call.Line}：{call.CallerName} 调用了 pid");

    // 展示调用参数信息
    foreach (var arg in call.Arguments)
    {
        Console.WriteLine($"  参数 {arg.Index}: {arg.ParameterName} " +
                          $"({arg.Direction}) = {arg.ExpressionSummary}");
    }
}
```

### 13.7 断点命中：变量窗口

断点命中后，调试器展示当前 POU 的所有变量。

```csharp
// 按 POU 过滤，获取断点命中处可见的变量
string currentPOU = "MotorControl";
var scopeVars = result.Variables
    .Where(v => v.POUName == currentPOU)
    .ToList();

foreach (var v in scopeVars)
{
    Console.WriteLine($"{v.Name} : {v.TypeName}  ({v.VarBlockKind})");
}
// 输出：
// targetSpeed : INT  (VAR_INPUT)
// actualSpeed : INT  (VAR_OUTPUT)
// error : BOOL       (VAR)
// buffer : ARRAY OF REAL  (VAR)
```

### 13.8 断点命中：调用栈

断点命中后，调试器需要构建调用栈。

```csharp
// 构建调用栈（向上回溯）
string currentPOU = "Multiply";
var callStack = new List<string> { currentPOU };

while (true)
{
    var caller = result.CallSites
        .Where(c => c.CalleeName == currentPOU)
        .Select(c => c.CallerName)
        .FirstOrDefault();

    if (caller == null) break;
    callStack.Add(caller);
    currentPOU = caller;
}

// callStack: ["Multiply", "Square", "Main"]
```

### 13.9 断点命中：结构体展开

调试器展开结构体变量时，需要字段信息。

```csharp
// 展开结构体变量的字段
var variable = result.Variables.FirstOrDefault(v => v.Name == "p1");
var structType = result.CustomTypes.FirstOrDefault(
    t => t.Name == variable?.TypeName && t.Kind == TypeKind.Struct);

if (structType?.Fields != null)
{
    foreach (var field in structType.Fields)
        Console.WriteLine($"  {field.Name} : {field.TypeName}");
}
// 输出：
//   X : REAL
//   Y : REAL
```

### 13.10 断点命中：调用参数详情

断点命中在调用处时，展示完整的参数绑定信息。

```csharp
// 获取某个调用点的完整参数信息
var call = result.CallSites
    .Where(c => c.CalleeName == "pid")
    .Where(c => c.Line == breakpointLine)
    .FirstOrDefault();

if (call != null)
{
    foreach (var arg in call.Arguments)
    {
        // 参数方向标记
        var dirMarker = arg.Direction switch
        {
            ParameterDirection.Output => "OUT ",
            ParameterDirection.InOut  => "IN/OUT ",
            _ => ""
        };

        // 绑定方式标记
        var bindMarker = arg.Binding switch
        {
            ArgumentBindingStyle.NamedOutput => "=>",
            ArgumentBindingStyle.NamedInput  => ":=",
            _ => "(按位置)"
        };

        Console.WriteLine($"{dirMarker}{arg.ParameterName}: {arg.ParameterTypeName} " +
                          $"{bindMarker} {arg.ExpressionSummary} (实际类型: {arg.ResolvedTypeName})");
    }
}
// 输出：
// setpoint: REAL := sp (实际类型: REAL)
// measured: REAL := pv (实际类型: REAL)
// OUT controlOutput: REAL => output (实际类型: REAL)
// IN/OUT integralState: REAL := integral (实际类型: REAL)
```

### 13.11 断点命中：符号表高级查询

调试器需要按名查找任意符号。

```csharp
// 按名查找符号（大小写不敏感）
var sym = result.Symbols.Resolve("MotorControl");

switch (sym)
{
    case FunctionSymbol fs:
        Console.WriteLine($"函数: {fs.Name}, 返回类型: {fs.ReturnType}");
        foreach (var p in fs.Parameters)
            Console.WriteLine($"  参数: {p.Name} : {p.Type} ({p.Scope})");
        break;

    case FunctionBlockSymbol fbs:
        Console.WriteLine($"函数块: {fbs.Name}");
        foreach (var (name, member) in fbs.Members)
            Console.WriteLine($"  成员: {name} : {member.Type} ({member.Scope})");
        break;

    case VariableSymbol vs:
        Console.WriteLine($"变量: {vs.Name} : {vs.Type} ({vs.Scope})");
        break;

    case TypeSymbol ts:
        Console.WriteLine($"类型: {ts.Name}, Kind={ts.Type.Kind}");
        break;
}
```

---

## 14. 调试集成示例

### 14.1 完整调试器集成流程

```csharp
public class DebuggerViewModel
{
    private AnalysisResult? _analysis;
    private List<(int Line, StatementNode Node)> _stmtMap = [];

    // 文本变化 / 打开文件时调用
    public void OnSourceChanged(string code, string filePath)
    {
        _analysis = FrontendPipeline.Analyze(code, filePath);
        if (_analysis.Success)
            _stmtMap = BuildStatementLineMap(_analysis.AST);
    }

    // ── 断点设置 ──

    /// <summary>行断点：判断某行是否可执行</summary>
    public bool CanBreakAtLine(int line)
        => _stmtMap.Any(s => s.Line == line);

    /// <summary>函数断点：获取 POU 入口行</summary>
    public int? GetFunctionEntryLine(string pouName)
        => _analysis?.POUs.FirstOrDefault(p => p.Name == pouName)?.Line;

    /// <summary>写入监控断点：获取变量所有写入位置</summary>
    public List<SymbolReference> GetWriteLocations(string varName)
        => _analysis?.References
            .Where(r => r.SymbolName == varName
                     && r.Usage == ReferenceUsage.Write)
            .ToList() ?? [];

    /// <summary>调用处断点：获取某个函数的所有调用点</summary>
    public List<CallSite> GetCallSites(string functionName)
        => _analysis?.CallSites
            .Where(c => c.CalleeName == functionName)
            .ToList() ?? [];

    // ── 断点命中后 ──

    /// <summary>断点命中：获取当前 POU 的变量窗口</summary>
    public List<VariableInfo> GetScopedVariables(string pouName)
        => _analysis?.Variables
            .Where(v => v.POUName == pouName)
            .ToList() ?? [];

    /// <summary>断点命中：获取调用栈</summary>
    public List<string> GetCallStack(string currentPOU)
    {
        var stack = new List<string> { currentPOU };
        var pou = currentPOU;

        while (true)
        {
            var caller = _analysis?.CallSites
                .Where(c => c.CalleeName == pou)
                .Select(c => c.CallerName)
                .FirstOrDefault();

            if (caller == null) break;
            stack.Add(caller);
            pou = caller;
        }

        return stack;
    }

    /// <summary>断点命中：展开结构体变量</summary>
    public List<FieldInfo>? ExpandStruct(string varName)
    {
        var variable = _analysis?.Variables.FirstOrDefault(v => v.Name == varName);
        if (variable == null) return null;

        var structType = _analysis?.CustomTypes.FirstOrDefault(
            t => t.Name == variable.TypeName && t.Kind == TypeKind.Struct);

        return structType?.Fields;
    }

    /// <summary>断点命中：获取调用点参数详情</summary>
    public List<CallArgumentInfo>? GetCallArguments(string functionName, int line)
        => _analysis?.CallSites
            .Where(c => c.CalleeName == functionName && c.Line == line)
            .SelectMany(c => c.Arguments)
            .ToList();

    /// <summary>条件断点：验证表达式中用到的变量是否存在</summary>
    public bool ValidateConditionVariables(string[] varNames, string pouName)
    {
        var scopeVars = _analysis?.Variables
            .Where(v => v.POUName == pouName)
            .Select(v => v.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        return varNames.All(v => scopeVars.Contains(v));
    }

    // ── 内部工具 ──

    private static List<(int Line, StatementNode Node)> BuildStatementLineMap(
        CompilationUnitNode ast)
    {
        var map = new List<(int Line, StatementNode Node)>();
        foreach (var pou in ast.POUs)
            CollectStatements(pou, map);
        return map;
    }

    private static void CollectStatements(POUNode pou, List<(int Line, StatementNode)> map)
    {
        foreach (var stmt in pou.Body)
            CollectRecursive(stmt, map);

        if (pou is FunctionBlockNode fb)
        {
            foreach (var item in fb.BodyItems)
            {
                if (item is FBStatementItemNode si)
                    CollectRecursive(si.Statement, map);
                else if (item is FBActionItemNode ai)
                    foreach (var stmt in ai.Action.Body)
                        CollectRecursive(stmt, map);
            }
        }
    }

    private static void CollectRecursive(StatementNode stmt, List<(int Line, StatementNode)> map)
    {
        map.Add((stmt.Span.Line, stmt));
        switch (stmt)
        {
            case IfNode ifn:
                foreach (var s in ifn.ThenBody) CollectRecursive(s, map);
                foreach (var (_, body) in ifn.ElsIfBranches)
                    foreach (var s in body) CollectRecursive(s, map);
                if (ifn.ElseBody != null)
                    foreach (var s in ifn.ElseBody) CollectRecursive(s, map);
                break;
            case ForNode fn:
                foreach (var s in fn.Body) CollectRecursive(s, map);
                break;
            case WhileNode wn:
                foreach (var s in wn.Body) CollectRecursive(s, map);
                break;
            case RepeatNode rn:
                foreach (var s in rn.Body) CollectRecursive(s, map);
                break;
            case CaseNode cn:
                foreach (var b in cn.Branches)
                    foreach (var s in b.Body) CollectRecursive(s, map);
                if (cn.ElseBody != null)
                    foreach (var s in cn.ElseBody) CollectRecursive(s, map);
                break;
        }
    }
}
```

### 14.2 断点类型与数据映射速查

| 用户操作 | 调用的方法 | 返回的数据 |
|---|---|---|
| 点击行号栏设置行断点 | `CanBreakAtLine(line)` | `true` / `false` |
| 输入函数名设置函数断点 | `GetFunctionEntryLine("F")` | 入口行号 `int?` |
| 选择变量设置写入监控 | `GetWriteLocations("speed")` | 所有写入位置的 `Line`、`Column` |
| 选择函数设置调用处断点 | `GetCallSites("pid")` | 所有调用点，含行号和参数详情 |
| 断点命中后查看变量 | `GetScopedVariables("Main")` | 当前 POU 的所有变量（名称、类型、作用域） |
| 断点命中后查看调用栈 | `GetCallStack("Multiply")` | `["Multiply", "Square", "Main"]` |
| 断点命中后展开结构体 | `ExpandStruct("p1")` | 结构体字段列表 `List<FieldInfo>` |
| 断点命中后查看调用参数 | `GetCallArguments("pid", 25)` | 参数方向、类型、实参摘要、绑定方式 |
| 输入条件表达式时验证 | `ValidateConditionVariables(["speed"], "Main")` | 变量是否存在 `bool` |

### 14.3 典型工作流

```
┌─────────────────────────────────────────────────────┐
│  用户编辑源码 → FrontendPipeline.Analyze()           │
│  获得 AnalysisResult                                │
└────────────────────┬────────────────────────────────┘
                     │
         ┌───────────┴───────────┐
         │                       │
    IDE 辅助功能              调试器
    (补全、提示等)          (断点、监控等)
                                 │
                    ┌────────────┼────────────┐
                    │            │            │
               设置断点     命中断点后     条件断点
                    │            │            │
            ┌───────┼───────┐    │     验证变量名
            │       │       │    │
         行断点  函数断点  写入监控  │
            │       │       │    │
     CanBreak  GetFuncEntry GetWrite  ┌──────┼──────┐
     AtLine()  Line()     Locations() │      │      │
                                      │   变量窗口  调用栈
                                      │  GetScoped GetCall
                                      │  Variables Stack()
                                      │
                                 结构体展开
                                ExpandStruct()
```