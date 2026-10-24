# IronST.Frontend API 摘要

ST 语言（IEC 61131-3）前端分析引擎，纯 C#，基于 ANTLR4。
功能：词法/语法解析、AST 构建、语义分析、符号收集、诊断、时间字面量解析、标准库函数注册。
一次调用 `FrontendPipeline.Analyze()` 获取全部结果，无状态。

---

## 命名空间

- 入口：`IronST.Frontend`
- 分析结果：`IronST.Frontend.Analysis`
- AST 节点：`IronST.Frontend.AST`
- 通用类型：`IronST.Common.Types`
- 通用符号：`IronST.Common.Symbol`
- 通用诊断：`IronST.Common.Diagnostics`

---

## 入口

```csharp
using IronST.Frontend;
using IronST.Frontend.Analysis;

AnalysisResult result = FrontendPipeline.Analyze(sourceCode, "main.st");
```

`fileName` 参数可选，会传播到所有 `SourceSpan.FileName` 和 `Diagnostic.Span.FileName`。

---

## AnalysisResult

| 属性 | 类型 | 说明 |
|------|------|------|
| `Success` | `bool` | 无 Error 级诊断时为 true |
| `AST` | `CompilationUnitNode` | 完整抽象语法树 |
| `Symbols` | `SymbolTable` | 全局符号表 |
| `Diagnostics` | `List<Diagnostic>` | 语法 + 语义诊断 |
| `Variables` | `List<VariableInfo>` | 所有声明的变量 |
| `POUs` | `List<POUInfo>` | POU 签名（函数/函数块/程序） |
| `CustomTypes` | `List<TypeInfo>` | 用户自定义类型 |
| `References` | `List<SymbolReference>` | 所有符号出现位置 |
| `CallSites` | `List<CallSite>` | 调用点及参数详情 |

---

## VariableInfo

```csharp
public record VariableInfo(
    string Name,          // 变量名
    string TypeName,      // 显示名："INT", "REAL", "STRING(?)", "ARRAY OF REAL"
    string POUName,       // 所属 POU 名
    string VarBlockKind,  // "VAR" / "VAR_INPUT" / "VAR_OUTPUT" / "VAR_IN_OUT" / "VAR_GLOBAL" / "VAR_EXTERNAL" / "VAR_TEMP"
    string? FilePath,
    int Line              // 声明行号
);
```

---

## POUInfo / ParameterInfo

```csharp
public record POUInfo(
    string Name,
    POUKind Kind,                       // Program / Function / FunctionBlock
    string? ReturnTypeName,             // Function 有返回类型，其余为 null
    List<ParameterInfo> Parameters,
    string? FilePath,
    int Line                            // POU 声明起始行
);

public record ParameterInfo(
    string Name,
    string TypeName,
    string Direction,                   // "VAR_INPUT" / "VAR_OUTPUT" / "VAR_IN_OUT" / "VAR"
    string? FilePath,
    int Line
);
```

---

## TypeInfo / FieldInfo

```csharp
public record TypeInfo(
    string Name,
    TypeKind Kind,                      // Struct / Enum / Subrange / SimpleType
    List<FieldInfo>? Fields,            // Struct 有，其余 null
    List<string>? EnumMembers,          // Enum 有，其余 null
    Dictionary<string, long>? EnumMemberValues,  // Enum 有，成员名→整数值映射
    string? UnderlyingType,             // SimpleType/Subrange 的底层类型名
    string? FilePath,
    int Line
);

public record FieldInfo(string Name, string TypeName, string? FilePath, int Line);
```

枚举成员值自动计算：显式赋值（`Blue := 10`）保留，隐式递增（前一个值 +1，初始为 0）。

---

## SymbolReference

```csharp
public record SymbolReference(
    string SymbolName,
    SymbolKind Kind,                    // Variable / Function / FunctionBlock / Type / Program
    ReferenceUsage Usage,               // Declaration / Read / Write / Call / TypeUsage
    string? POUName,
    string? FilePath,
    int Line,
    int Column
);
```

---

## CallSite / CallArgumentInfo

```csharp
public record CallSite(
    string CallerName,                  // 调用方 POU 名
    string CalleeName,                  // 被调函数/函数块/实例名
    string? CallerPOU,
    string? FilePath,
    int Line,
    List<CallArgumentInfo> Arguments
)
{
    public int ArgumentCount => Arguments.Count;  // 自动计算
}

public record CallArgumentInfo(
    int Index,                          // 从 0 开始
    string? ParameterName,              // 形参名（位置参数也能匹配）
    string? ParameterTypeName,          // 形参类型名（对自定义类型为内部显示名）
    ParameterDirection Direction,       // Input / Output / InOut / Unknown
    string ExpressionSummary,           // 实参摘要
    string? ResolvedTypeName,           // 实参推导类型名
    ArgumentBindingStyle Binding        // Positional / NamedInput / NamedOutput
);
```

---

## Diagnostic

```csharp
public record Diagnostic(
    DiagnosticSeverity Severity,        // Error / Warning / Info
    ErrorCode Code,                     // E1xxx 语法 / E2xxx 语义
    string Message,
    SourceSpan? Span                    // 行号、列号、文件名
);
```

| 错误码 | 含义 |
|--------|------|
| `E1001` | 期望 token 不匹配 |
| `E1002` | 无法解析 |
| `E1003` | 多余输入 |
| `E1004` | 无法识别的字符 |
| `E2001` | 未声明的标识符 |
| `E2006` | 未知的函数或程序块 |
| `E2008` | 结构体没有该字段 / 枚举没有该成员 |
| `E2015` | 未知的类型 |

---

## SymbolTable

```csharp
// 按名查找（大小写不敏感）
Symbol? sym = result.Symbols.Resolve("MyFunction");

switch (sym)
{
    case FunctionSymbol fs:       // fs.Name, fs.ReturnType, fs.Parameters
    case FunctionBlockSymbol fbs: // fbs.Name, fbs.Members (Dictionary<string, VariableSymbol>)
    case TypeSymbol ts:           // ts.Name, ts.Type (IECType)
    case VariableSymbol vs:       // vs.Name, vs.Type, vs.Scope, vs.DirectAddress
}
```

- `EnterScope()` → 子作用域
- `TryDeclare(symbol)` → 当前作用域已有同名返回 false
- `Resolve(name)` → 从当前向外逐层查找
- `Locals` → 仅当前作用域的符号

---

## SourceSpan

```csharp
public readonly record struct SourceSpan(
    int Line, int Column, int EndLine, int EndColumn, string? FileName
);
```

AST 中**每个节点**都继承自 `ASTNode`，携带 `Span`。
`SourceSpan.Unknown` 为全零值。

---

## AST 节点层级（精简）

```
CompilationUnitNode
├── TypeDeclarations : List<TypeDeclNode>
├── POUs : List<POUNode>
│   ├── ProgramNode          // PROGRAM ... END_PROGRAM
│   ├── FunctionNode         // FUNCTION name : returnType
│   │   └── ReturnTypeName
│   └── FunctionBlockNode    // FUNCTION_BLOCK name
│       └── BodyItems : List<FBBodyItemNode>
│           ├── FBStatementItemNode (Statement)
│           └── FBActionItemNode (ActionNode)
└── Configurations : List<ConfigurationNode>
    └── Resources → Tasks → ProgramConfigs
```

POUNode 共有：
- `Name` : `string`
- `VarBlocks` : `List<VarDeclBlockNode>`
- `Body` : `List<StatementNode>`（FunctionBlock 用 `BodyItems`）

### 语句节点

| 节点类型 | 对应 ST |
|----------|---------|
| `AssignmentNode` | `target := value;` |
| `RefAssignmentNode` | `target REF= value;` |
| `CallStatementNode` | `name(args);` |
| `ReturnNode` | `RETURN;` |
| `ExitNode` | `EXIT;` |
| `IfNode` | `IF/ELSIF/ELSE/END_IF` |
| `ForNode` | `FOR/TO/BY/DO/END_FOR` |
| `WhileNode` | `WHILE/DO/END_WHILE` |
| `RepeatNode` | `REPEAT/UNTIL/END_REPEAT` |
| `CaseNode` | `CASE/OF/ELSE/END_CASE` |

### 表达式节点

| 节点类型 | 说明 |
|----------|------|
| `VariableExprNode` | 变量访问，含 `Segments`（支持 `.`、`^`、`[]`）。枚举引用 `Color.Red` 也生成此节点 |
| `FunctionCallExprNode` | 函数调用表达式，`Name` + `Arguments`。内置和用户函数统一 |
| `BinaryExprNode` | 二元运算，`Op` + `Left` + `Right` |
| `UnaryExprNode` | 一元运算，`Op` + `Operand` |
| `LiteralExprNode` | 字面量，`Kind` + `RawText` + `ParsedValue` + `ResolvedType` |
| `TypedLiteralExprNode` | 类型字面量，`TypeName` + `RawValue`（如 `INT#42`） |

所有表达式节点都有 `ResolvedType : IECType?`（语义分析后填充）。

### 变量声明

```csharp
VarDeclBlockNode   → Scope(VarScope) + Modifier(VarModifier) + Items
VarDeclItemNode    → Names: List<VarNameNode> + TypeName: DataTypeNode + InitialValue?
VarNameNode        → Name + DirectAddress? (如 "%IX0.0")
DataTypeNode       → SimpleDataTypeNode / ArrayDataTypeNode / ReferenceDataTypeNode
```

---

## 核心枚举

### POUKind
`Program` / `Function` / `FunctionBlock`

### TypeKind
`Struct` / `Enum` / `Subrange` / `SimpleType`

### SymbolKind
`Variable` / `Function` / `FunctionBlock` / `Type` / `Program`

### ReferenceUsage
`Declaration` / `Read` / `Write` / `Call` / `TypeUsage`

### ParameterDirection
`Input`（VAR_INPUT）/ `Output`（VAR_OUTPUT）/ `InOut`（VAR_IN_OUT）/ `Unknown`

### ArgumentBindingStyle
`Positional`（按位置）/ `NamedInput`（`:=`）/ `NamedOutput`（`=>`）

### DiagnosticSeverity
`Error` / `Warning` / `Info`

### VarScope
`Local` / `Input` / `Output` / `InOut` / `Global` / `External` / `Temp` / `Access`

### VarModifier
`None` / `Constant` / `Retain` / `NonRetain`

### BinaryOp
`Add` / `Sub` / `Mul` / `Div` / `Mod` / `Power` / `Eq` / `Neq` / `Lt` / `Gt` / `Lte` / `Gte` / `And` / `Or` / `Xor`

### UnaryOp
`Not` / `Neg`

### LiteralKind
`Integer` / `Real` / `BasedInteger` / `Boolean` / `String` / `Time` / `Date` / `TimeOfDay` / `DateTime`

---

## 预注册标准库函数

语义分析阶段自动注册到符号表，调用时不报 E2006，`ResolvedType` 和参数信息完整。

### 数学函数

| 函数 | 返回类型 | 参数 |
|------|----------|------|
| `ABS` | DINT | `(input: DINT)` |
| `SQRT` | LREAL | `(input: LREAL)` |
| `LN` / `LOG` / `EXP` | LREAL | `(input: LREAL)` |
| `SIN` / `COS` / `TAN` | LREAL | `(input: LREAL)` |
| `ASIN` / `ACOS` / `ATAN` | LREAL | `(input: LREAL)` |
| `ATAN2` | LREAL | `(x: LREAL, y: LREAL)` |
| `EXPT` | LREAL | `(base: LREAL, exponent: LREAL)` |

### 字符串函数

| 函数 | 返回类型 | 参数 |
|------|----------|------|
| `LEN` | INT | `(input: STRING)` |
| `LEFT` / `RIGHT` | STRING | `(input: STRING, length: INT)` |
| `MID` | STRING | `(input: STRING, length: INT, start: INT)` |
| `CONCAT` | STRING | `(in1: STRING, in2: STRING)` |
| `INSERT` | STRING | `(in1: STRING, in2: STRING, position: INT)` |
| `DELETE` | STRING | `(input: STRING, length: INT, start: INT)` |
| `REPLACE` | STRING | `(in1: STRING, in2: STRING, length: INT, start: INT)` |
| `FIND` | INT | `(in1: STRING, in2: STRING)` |

### CodeGen 使用

```csharp
// 标准函数已注册到符号表，调用时不报 E2006
// FunctionCallExprNode.Name = "SQRT", ResolvedType = IECType.LReal
// CodeGen 按 Name 分流生成 IR（call libc / 内联指令）

var call = (FunctionCallExprNode)expr;
switch (call.Name.ToUpperInvariant())
{
    case "SQRT": return EmitMathCall("llvm.sqrt.f64", call);
    case "SIN":  return EmitMathCall("llvm.sin.f64", call);
    case "LEN":  return EmitStringLength(call);
    // ...
}
```

---

## 枚举支持

### 成员值计算

语义分析自动计算枚举成员的整数值，存入 `TypeInfo.EnumMemberValues` 和 `IECType.EnumMemberValues`。

```st
TYPE Color : (
    Red,           (* = 0，隐式 *)
    Green,         (* = 1，隐式 *)
    Blue := 10,    (* = 10，显式 *)
    Yellow         (* = 11，Blue+1 自动递增 *)
); END_TYPE;
```

```csharp
// TypeInfo / IECType 中：
EnumMembers: ["Red", "Green", "Blue", "Yellow"]
EnumMemberValues: { "Red": 0, "Green": 1, "Blue": 10, "Yellow": 11 }
```

### 枚举引用

语义分析支持两种引用方式，`ResolvedType` 设为对应枚举类型的 `IECType`：

| 语法 | AST 节点 | ResolvedType |
|------|----------|-------------|
| `Color.Red` | `VariableExprNode`（2 个 Segment） | `IECType { Kind=ENUM, Name="Color" }` |
| `Red`（无前缀） | `VariableExprNode`（1 个 Segment） | 同上（全局枚举类型搜索） |

对 `Color.Red`，`References` 中会记录 `Color` 的 `TypeUsage` 引用。

### CodeGen 使用

```csharp
// 方式 1：从 VariableExprNode 取成员名，查 EnumMemberValues
var varExpr = (VariableExprNode)expr;
var memberName = varExpr.Segments.Last().Name;
long value = expr.ResolvedType.EnumMemberValues[memberName];
return new ConstantInt(IrType.Int32, value);

// 方式 2：从 TypeInfo 查
var enumType = result.CustomTypes.FirstOrDefault(t => t.Name == "Color");
long value = enumType.EnumMemberValues["Red"];  // → 0
```

---

## TimeLiteralParser — 时间字面量解析

位于 `IronST.Frontend.Analysis`，内部静态类。语义分析阶段自动调用，将 `LiteralExprNode` 的 `RawText` 解析为数值存入 `ParsedValue`。

CodeGen 直接取值即可，无需自行解析文本。

### 存储方案

| 类型 | 示例 | 存储含义 | ParsedValue |
|------|------|----------|-------------|
| TIME | `T#5s3ms` | 总毫秒数 | `5003L` |
| TIME | `T#60s` | 总毫秒数 | `60000L` |
| TIME | `T#1d2h30m` | 总毫秒数 | `95400000L` |
| DATE | `D#2024-01-01` | 自 1970-01-01 起的天数 | `19723L` |
| TIME_OF_DAY | `TOD#12:30:00` | 自 00:00:00 起的毫秒数 | `45000000L` |
| DATE_AND_TIME | `DT#2024-01-01-12:30:00` | 自 1970-01-01 起的总毫秒数 | `1704112200000L` |

### TIME 单位换算

| 后缀 | 换算为 ms |
|------|-----------|
| `d` | × 86400000 |
| `h` | × 3600000 |
| `m` / `min` | × 60000 |
| `s` | × 1000 |
| `ms` | × 1 |
| `us` / `µs` | 截断为 0 |
| `ns` | 截断为 0 |
| 无后缀 | 默认秒 × 1000 |

示例：`T#1d2h30m5s3ms` = 86400000 + 7200000 + 1800000 + 5000 + 3 = `95405003L`

### DATE / TIME_OF_DAY / DATE_AND_TIME

- **DATE**：`D#2024-01-01` → 从 1970-01-01 到该日期的天数
- **TIME_OF_DAY**：`TOD#12:30:00.5` → 当天 0 时起的毫秒数，支持秒的小数部分
- **DATE_AND_TIME**：`DT#2024-01-01-12:30:00` → 天数 × 86400000 + 当天毫秒数，支持 `-` 或空格分隔日期和时间

### CodeGen 使用

```csharp
case LiteralKind.Time:
case LiteralKind.Date:
case LiteralKind.TimeOfDay:
case LiteralKind.DateTime:
    return new ConstantInt(IrType.Int64, (long)lit.ParsedValue);
```

### 注意事项

- `ParsedValue` 为 `null` 时表示解析失败，CodeGen 应做 null 检查
- `RawText` 始终保留原始文本，可用于错误消息或调试显示
- `TryParse` 内部捕获所有异常，解析失败不中断语义分析

---

## 断点与调试支持

`AnalysisResult` 为调试器提供源码级元数据，不执行 ST 程序。

### 断点类型

| 断点类型 | 数据来源 |
|----------|----------|
| 行断点 | 遍历 AST 的 `StatementNode.Span.Line` |
| 函数断点 | `POUs` → `Name` + `Line` |
| 调用处断点 | `CallSites` → `CalleeName` + `Line` + `Arguments` |
| 写入监控 | `References` → `Usage == Write` → `Line`、`Column` |

### 断点命中后

| 功能 | 数据来源 |
|------|----------|
| 变量窗口 | `Variables` 按 `POUName` 过滤 |
| 调用栈 | `CallSites` 按 `CalleeName` 链式回溯 `CallerName` |
| 结构体展开 | `CustomTypes` → `Kind == Struct` → `Fields` |
| 参数详情 | `CallSite.Arguments` → 方向、类型、实参摘要 |
| 符号查询 | `Symbols.Resolve(name)` → 按名查找 |

### 语句映射表构建

遍历 AST 收集所有 `StatementNode` 及 `Span.Line`，建立「行号 → 语句节点」映射。
需要递归进入 `IfNode`、`ForNode`、`WhileNode`、`RepeatNode`、`CaseNode` 的嵌套体。
`FunctionBlockNode` 使用 `BodyItems` 而非 `Body`。

可断点节点：`AssignmentNode`、`CallStatementNode`、`ReturnNode`、`ExitNode`、`IfNode`、`ForNode`、`WhileNode`、`RepeatNode`、`CaseNode`、`RefAssignmentNode`。
不可断点：变量声明行、`END_xxx` 关键字行、`PROGRAM/FUNCTION/FUNCTION_BLOCK` 声明行。

---

## 重要提醒（防幻觉）

1. **一次调用**：`FrontendPipeline.Analyze(source, fileName?)` 无状态，每次调用独立。
2. **TypeName 显示规则**：带长度的字符串为 `STRING(?)` 而非 `STRING(50)`；数组为 `ARRAY OF ELEMENT` 而非 `ARRAY[0..9] OF ELEMENT`；自定义结构体在 `CallArgumentInfo.ParameterTypeName` 中显示为 `STRUCT(N fields)` 而非原始类型名。
3. **大小写不敏感**：符号表的 `Resolve` 和 `TryDeclare` 均为 `OrdinalIgnoreCase`。
4. **FunctionBlock 的 Body vs BodyItems**：`FunctionBlockNode` 用 `BodyItems` 存储语句和 Action 的交错序列；`Body` 属性在 FunctionBlock 上为空。`ProgramNode` 和 `FunctionNode` 用 `Body`。
5. **Function 返回值注入**：语义分析会将函数名本身作为隐式变量注册到作用域，类型为返回类型（如 `FUNCTION Add : INT` 中 `Add` 可被赋值）。
6. **Program 注册为 Void Function**：`ProgramNode` 在符号表中注册为 `FunctionSymbol`，`ReturnType = IECType.Void`，`Parameters` 为空。
7. **FunctionBlock 名可当类型用**：如 `VAR ctr : Counter;`，`ResolveTypeName` 会将 `FunctionBlockSymbol` 转为 `IECType.MakeStruct`。
8. **未知类型不中断分析**：遇到 `E2015`（未知类型）时回退为 `IECType.DInt`，其余变量正常解析。
9. **References 中 Column**：部分引用（如 POU 声明、类型声明）的 Column 为 0，不精确。
10. **CallArgumentInfo.ParameterTypeName**：对内置类型显示原始名（如 `"INT"`），对自定义结构体显示 `STRUCT(N fields)`，对数组显示 `ARRAY OF X`。
11. **时间字面量 ParsedValue**：`Time`/`Date`/`TimeOfDay`/`DateTime` 的 `ParsedValue` 由 `TimeLiteralParser` 自动填充为 `long`；其他字面量类型（`Integer`/`Real`/`Boolean`/`String`）的 `ParsedValue` 由 Parser 直接设置。解析失败时 `ParsedValue` 为 `null`。
12. **标准库函数已注册**：`SQRT`、`SIN`、`LEN`、`CONCAT` 等标准函数已注册到符号表，调用时不报 E2006，`FunctionCallExprNode.ResolvedType` 为对应返回类型。`INT(x)`/`REAL(x)` 等类型转换函数由 `LookupBuiltin` 处理，同样不报错。
13. **枚举引用**：`Color.Red` 生成 `VariableExprNode`（2 个 Segment），`ResolvedType` 为枚举类型 `IECType`；单独的 `Red` 通过全局枚举类型搜索解析。非法成员（`Color.XYZ`）报 E2008。
14. **枚举成员值**：`TypeInfo.EnumMemberValues` 和 `IECType.EnumMemberValues` 存储完整映射（显式赋值 + 隐式递增）。
15. **不支持的功能**：运行时调试执行、断点命中计数、内存布局、单步执行。这些都是下游运行时的职责。