# IronST.Common 公共类型 API 摘要

跨项目共享的基础类型层，纯 C#，零依赖。
包含：诊断系统、符号体系、IEC 类型系统、源码位置。
Frontend / CodeGen / Runtime 均引用此项目。

---

## 命名空间

| 命名空间 | 内容 |
|----------|------|
| `IronST.Common.Diagnostics` | `Diagnostic`、`DiagnosticBag`、`DiagnosticSeverity`、`ErrorCode`、`SourceSpan` |
| `IronST.Common.Symbol` | `Symbol`（基类）、`FunctionSymbol`、`FunctionBlockSymbol`、`TypeSymbol`、`VariableSymbol`、`SymbolKind`、`VarScope`、`VarModifier` |
| `IronST.Common.Types` | `IECType` |

---

## 诊断系统

### Diagnostic

```csharp
public class Diagnostic
{
    public DiagnosticSeverity Severity { get; init; }
    public string Message { get; init; }
    public SourceSpan? Span { get; init; }
    public ErrorCode Code { get; init; }
}
```

`ToString()` 格式：`{FileName}:{Line}:{Column}: {Severity} [{Code}] {Description}: {Message}`

### DiagnosticBag

诊断收集器，各阶段（解析、语义分析、代码生成）统一使用。

```csharp
public class DiagnosticBag
{
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    public bool HasErrors { get; }       // 存在 Error 级诊断时为 true
    public int ErrorCount { get; }

    public void Report(DiagnosticSeverity severity, string message, SourceSpan span, ErrorCode code = ErrorCode.E1000);
    public void Error(string message, SourceSpan span, ErrorCode code = ErrorCode.E1000);
    public void Warning(string message, SourceSpan span, ErrorCode code = ErrorCode.E1000);
    public void Info(string message, SourceSpan span, ErrorCode code = ErrorCode.E1000);
    public void AddRange(IEnumerable<Diagnostic> diagnostics);
    public void Clear();
}
```

### DiagnosticSeverity

```csharp
public enum DiagnosticSeverity
{
    Error,
    Warning,
    Info,
    Hint,      // 比 Frontend API 摘要多一个级别
}
```

---

## ErrorCode

| 错误码 | 分类 | 描述 |
|--------|------|------|
| `E1000` | 语法 | 未知语法错误 |
| `E1001` | 语法 | 不匹配的输入 |
| `E1002` | 语法 | 无法匹配的语法结构 |
| `E1003` | 语法 | 多余的输入 |
| `E1004` | 语法 | 无法识别的字符 |
| `E1005` | 语法 | 错误恢复时跳过了标记 |
| `E1006` | 语法 | 缺少关键字 |
| `E1007` | 语法 | 缺少分号 |
| `E1008` | 语法 | 缺少标识符 |
| `E1009` | 语法 | 缺少表达式 |
| `E1010` | 语法 | 未关闭的块，缺少结束关键字 |
| `E2000` | 语义 | 未知语义错误 |
| `E2001` | 语义 | 未声明的标识符 |
| `E2002` | 语义 | 重复声明 |
| `E2003` | 语义 | 类型不兼容 |
| `E2004` | 语义 | 参数数量不匹配 |
| `E2005` | 语义 | 参数类型不匹配 |
| `E2006` | 语义 | 未知的函数或程序块 |
| `E2007` | 语义 | 未知的结构体类型 |
| `E2008` | 语义 | 结构体字段不存在 |
| `E2009` | 语义 | 赋值目标不可写 |
| `E2010` | 语义 | 编译期除零 |
| `E2011` | 语义 | 函数返回值未赋值 |
| `E2012` | 语义 | 保留关键字不能用作标识符 |
| `E2013` | 语义 | 数组下标越界（编译期可检测） |
| `E2014` | 语义 | 类型转换溢出 |
| `E2015` | 语义 | 未知的类型 |
| `E3000` | 代码生成 | 未知代码生成错误 |
| `E3001` | 代码生成 | LLVM IR 生成失败 |
| `E3002` | 代码生成 | 字节码生成失败 |
| `E3003` | 代码生成 | 不支持的语言特性 |
| `E3004` | 代码生成 | 类型转换失败 |
| `E3005` | 代码生成 | 内部编译器错误 |
| `E4000` | 运行时 | 未知运行时错误 |
| `E4001` | 运行时 | 数组越界 |
| `E4002` | 运行时 | 空引用 |
| `E4003` | 运行时 | 宿主函数未注册 |
| `E4004` | 运行时 | 栈溢出 |

扩展方法 `ErrorCode.GetDescription()` 通过反射读取 `[Description]` 特性。

---

## SourceSpan

```csharp
public readonly record struct SourceSpan(
    int Line,          // 从 1 开始
    int Column,        // 从 1 开始
    int EndLine,
    int EndColumn,
    string? FileName = null)
{
    public static readonly SourceSpan Unknown = new(0, 0, 0, 0);
}
```

---

## 符号体系

### Symbol（基类）

```csharp
public abstract class Symbol
{
    public string Name { get; init; }
    public SymbolKind Kind { get; init; }
    public SourceSpan Declaration { get; init; }   // 默认 SourceSpan.Unknown
}
```

### SymbolKind

```csharp
public enum SymbolKind
{
    Variable,
    Function,
    FunctionBlock,
    Type,
    Program,
}
```

### FunctionSymbol

```csharp
public class FunctionSymbol : Symbol
{
    public IECType ReturnType { get; init; }              // 默认 IECType.Void
    public List<VariableSymbol> Parameters { get; init; } // 默认空
}
```

### FunctionBlockSymbol

```csharp
public class FunctionBlockSymbol : Symbol
{
    public Dictionary<string, VariableSymbol> Members { get; init; }  // OrdinalIgnoreCase
    public Dictionary<string, FunctionSymbol> Actions { get; init; }  // OrdinalIgnoreCase
}
```

### TypeSymbol

```csharp
public class TypeSymbol : Symbol
{
    public IECType Type { get; init; }   // 默认 IECType.Void
}
```

### VariableSymbol

```csharp
public class VariableSymbol : Symbol
{
    public IECType Type { get; init; }           // 默认 IECType.Void
    public VarScope Scope { get; init; }
    public VarModifier Modifier { get; init; }
    public string? DirectAddress { get; init; }  // 如 "%IX0.0"
}
```

### VarScope

```csharp
public enum VarScope
{
    Local,
    Input,      // VAR_INPUT
    Output,     // VAR_OUTPUT
    InOut,      // VAR_IN_OUT
    Global,     // VAR_GLOBAL
    External,   // VAR_EXTERNAL
    Temp,       // VAR_TEMP
    Access,     // VAR_ACCESS
    Config,     // VAR_CONFIG（Frontend API 摘要中未列出）
}
```

### VarModifier

```csharp
public enum VarModifier
{
    None,
    Constant,
    Retain,
    NonRetain,
}
```

---

## IECType — IEC 61131-3 类型系统

### 核心结构

```csharp
public sealed class IECType
{
    public KindTag Kind { get; init; }
    public string Name { get; init; }
    public int BitWidth { get; init; }
    public bool IsSigned { get; init; }          // 默认 true

    // STRING / WSTRING
    public int? MaxStringLength { get; init; }
    public bool IsWideString { get; init; }

    // ARRAY
    public IECType? ElementType { get; init; }
    public List<(int Lower, int Upper)>? Dimensions { get; init; }

    // STRUCT
    public Dictionary<string, IECType>? Fields { get; init; }

    // ENUM
    public List<string>? EnumValues { get; init; }
    public Dictionary<string, long>? EnumMemberValues { get; init; }

    // REFERENCE
    public IECType? ReferencedType { get; init; }
    public string? ReferenceTargetName { get; init; }  // 源码书写的目标类型名
}
```

### KindTag

```
BOOL  SINT  INT  DINT  LINT  USINT  UINT  UDINT  ULINT
BYTE  WORD  DWORD  LWORD
REAL  LREAL
TIME  DATE  TOD  DT
STRING  WSTRING
ARRAY  STRUCT  ENUM  REFERENCE  VOID
```

### 分类辅助属性

| 属性 | 包含的 Kind |
|------|------------|
| `IsInteger` | BOOL, SINT, INT, DINT, LINT, USINT, UINT, UDINT, ULINT, BYTE, WORD, DWORD, LWORD |
| `IsReal` | REAL, LREAL |
| `IsNumeric` | IsInteger ∪ IsReal |
| `IsUnsigned` | USINT, UINT, UDINT, ULINT, BYTE, WORD, DWORD, LWORD |

### 内置类型单例

| 单例 | Kind | BitWidth | IsSigned |
|------|------|----------|----------|
| `IECType.Bool` | BOOL | 1 | false |
| `IECType.SInt` | SINT | 8 | true |
| `IECType.Int` | INT | 16 | true |
| `IECType.DInt` | DINT | 32 | true |
| `IECType.LInt` | LINT | 64 | true |
| `IECType.USInt` | USINT | 8 | false |
| `IECType.UInt` | UINT | 16 | false |
| `IECType.UDInt` | UDINT | 32 | false |
| `IECType.ULInt` | ULINT | 64 | false |
| `IECType.Byte` | BYTE | 8 | false |
| `IECType.Word` | WORD | 16 | false |
| `IECType.DWord` | DWORD | 32 | false |
| `IECType.LWord` | LWORD | 64 | false |
| `IECType.Real` | REAL | 32 | true |
| `IECType.LReal` | LREAL | 64 | true |
| `IECType.Time` | TIME | 64 | true |
| `IECType.Date` | DATE | 64 | true |
| `IECType.Tod` | TOD | 64 | true |
| `IECType.Dt` | DT | 64 | true |
| `IECType.Void` | VOID | 0 | true |
| `IECType.Enum` | ENUM | 32 | false |
| `IECType.DefaultString` | STRING | 8 | true |
| `IECType.DefaultWString` | WSTRING | 16 | true |

**注意**：`DefaultString` / `DefaultWString` 的 `MaxStringLength` 为 **254**（IEC 默认值）。

### 内置类型查找表（大小写不敏感）

```csharp
IECType? t = IECType.LookupBuiltin("INT");  // → IECType.Int
```

支持的查找键名（含别名）：

| 键名 | 单例 |
|------|------|
| `BOOL` | Bool |
| `BYTE` / `WORD` / `DWORD` / `LWORD` | Byte / Word / DWord / LWord |
| `SINT` / `INT` / `DINT` / `LINT` | SInt / Int / DInt / LInt |
| `USINT` / `UINT` / `UDINT` / `ULINT` | USInt / UInt / UDInt / ULInt |
| `REAL` / `LREAL` | Real / LReal |
| `TIME` / `T` | Time |
| `DATE` / `D` | Date |
| `TIME_OF_DAY` / `TOD` | Tod |
| `DATE_AND_TIME` / `DT` | Dt |
| `STRING` | DefaultString |
| `WSTRING` | DefaultWString |

### 工厂方法

```csharp
// 带长度的字符串
IECType MakeString(int maxLen, bool wide = false)
// → STRING(maxLen) 或 WSTRING(maxLen)

// 数组
IECType MakeArray(IECType element, List<(int Lower, int Upper)> dims)
// → ARRAY[0..9,0..3] OF REAL

// 结构体
IECType MakeStruct(string name, Dictionary<string, IECType> fields)

// 枚举（两个重载）
IECType MakeEnum(string name, List<string> values)
IECType MakeEnum(string name, List<string> values, Dictionary<string, long> memberValues)
// 枚举默认 BitWidth=32, IsSigned=false

// 引用
IECType MakeReference(IECType target, string? targetName = null)
// → "REFERENCE TO {target.Name}"
// targetName：源码中书写的目标类型名。前向引用时 target 可能是占位符
// IECType.Void，靠 targetName 在类型不动点收敛后回填真实目标。
```

---

## 重要提醒（防幻觉）

1. **DiagnosticSeverity 有四个值**：`Error` / `Warning` / `Info` / `Hint`（Frontend 摘要中只列了前三个，`Hint` 也存在但前端未使用）。
2. **VarScope 有 Config**：`Config` 对应 `VAR_CONFIG`，Frontend 摘要中遗漏了此值。
3. **IECType 是 sealed class**，不是 record。内置类型和工厂方法创建的实例不可变（只用 `init`），但没有值相等语义。
4. **LookupBuiltin 大小写不敏感**：`"int"`、`"INT"`、`"Int"` 返回同一实例。
5. **默认 String 长度**：`DefaultString.MaxStringLength = 254`，`DefaultWString.MaxStringLength = 254`。
6. **MakeEnum 有两个重载**：无 `EnumMemberValues` 的版本只存成员名列表，`EnumMemberValues` 为 null。带映射的版本由语义分析填充。
7. **FunctionBlockSymbol.Actions**：存储函数块内定义的 Action，键名为 Action 名，值为 `FunctionSymbol`。
8. **所有字典比较器**：`FunctionBlockSymbol.Members`、`FunctionBlockSymbol.Actions`、`IECType.Builtins` 查找表均使用 `OrdinalIgnoreCase`。
9. **SourceSpan 计数从 1 开始**，`Unknown` 全零值为 `(0,0,0,0)`。
