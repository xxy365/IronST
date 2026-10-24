using IronST.Common.Diagnostics;
using IronST.Common.Symbol;
using IronST.Common.Types;
using IronST.Frontend.AST;

namespace IronST.Frontend.Analysis;

public class AnalysisResult
{
    public CompilationUnitNode AST { get; set; } = null!;
    public SymbolTable Symbols { get; set; } = null!;
    public List<Diagnostic> Diagnostics { get; set; } = [];

    // 变量清单
    public IReadOnlyList<VariableInfo> Variables { get; set; } = [];

    // POU 信息（函数签名、返回值、参数）
    public IReadOnlyList<POUInfo> POUs { get; set; } = [];

    // 自定义类型（结构体字段、枚举成员）
    public IReadOnlyList<TypeInfo> CustomTypes { get; set; } = [];

    // 符号引用（用于"转到定义"、"查找所有引用"）
    public IReadOnlyList<SymbolReference> References { get; set; } = [];

    // 调用关系（谁调用了谁）
    public IReadOnlyList<CallSite> CallSites { get; set; } = [];

    public bool Success => Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
}

// ═══════════════════════════════════════════
//  POU 信息 — 补全函数名、悬停显示签名
// ═══════════════════════════════════════════

public record POUInfo(
    string Name,
    POUKind Kind, // Program / Function / FunctionBlock
    string? ReturnTypeName, // Function 的返回类型，其他为 null
    List<ParameterInfo> Parameters,
    string? FilePath,
    int Line, // POU 声明起始行,
    bool IsExternal = false        // 库注入的为 true
);

public enum POUKind
{
    Program,
    Function,
    FunctionBlock,
}

public record ParameterInfo(
    string Name,
    string TypeName,
    string Direction, // "VAR_INPUT", "VAR_OUTPUT", "VAR_IN_OUT"
    string? FilePath,
    int Line
);

// ═══════════════════════════════════════════
//  类型信息 — dot-completion、悬停显示结构体定义
// ═══════════════════════════════════════════

public record TypeInfo(
    string Name,
    TypeKind Kind, // Struct / Enum / Subrange / SimpleType
    List<FieldInfo>? Fields, // Struct 有字段列表
    List<string>? EnumMembers, // Enum 有枚举成员
    Dictionary<string, long>? EnumMemberValues, // 枚举值
    string? UnderlyingType, // 简单类型别名指向谁
    string? FilePath,
    int Line
);

public enum TypeKind
{
    Struct,
    Enum,
    Subrange,
    SimpleType,
}

public record FieldInfo(string Name, string TypeName, string? FilePath, int Line);

// ═══════════════════════════════════════════
//  符号引用 — "转到定义" + "查找所有引用"
// ═══════════════════════════════════════════

public record SymbolReference(
    string SymbolName, // 变量名 / 函数名 / 类型名
    SymbolKind Kind, // Variable / Function / Type / ...
    ReferenceUsage Usage, // Declaration / Read / Write / Call / TypeUsage
    string? POUName, // 所在 POU
    string? FilePath,
    int Line,
    int Column
);


public enum ReferenceUsage
{
    Declaration,
    Read,
    Write,
    Call,
    TypeUsage,
}

// ═══════════════════════════════════════════
//  调用关系 — "调用层次"、"查找所有调用"
// ═══════════════════════════════════════════

public record CallSite(
    string CallerName,
    string CalleeName,
    string? CallerPOU,
    string? FilePath,
    int Line,
    List<CallArgumentInfo> Arguments
)
{
    // 自动从列表长度推算，不需要单独传参
    public int ArgumentCount => Arguments.Count;
}


/// <summary>单个调用参数的完整信息</summary>
public record CallArgumentInfo(
    int Index,                      // 第几个参数（从 0 开始）

    // ── 形参信息（从被调函数签名推断） ──
    string? ParameterName,          // 形参名
    string? ParameterTypeName,      // 形参类型名（如 "INT", "REAL"）
    ParameterDirection Direction,   // VAR_INPUT / VAR_OUTPUT / VAR_IN_OUT

    // ── 实参信息（从调用处提取） ──
    string ExpressionSummary,       // 实参摘要（变量名、字面量、表达式）
    string? ResolvedTypeName,       // 实参推导后的类型名
    ArgumentBindingStyle Binding    // 绑定方式
);

/// <summary>参数方向</summary>
public enum ParameterDirection
{
    Input,          // VAR_INPUT  ->  传值，调用时 :=
    Output,         // VAR_OUTPUT ->  传出，调用时 =>
    InOut,          // VAR_IN_OUT ->  引用，调用时 :=
    Unknown         // 无法推断（可能是内置函数）
}

/// <summary>参数绑定方式</summary>
public enum ArgumentBindingStyle
{
    Positional,     // Add(1, 2)           -> 按位置
    NamedInput,     // Add(a := 1, b := 2) -> 按名 + :=
    NamedOutput     // Calc(x => result)   -> 按名 + =>
}

/// <summary>从源码中提取的变量信息</summary>
public record VariableInfo(
    string Name,
    string TypeName, // 如 "INT", "REAL", "ARRAY OF BOOL"
    string POUName, // 所属 POU 名（Main, MotorControl 等）
    string VarBlockKind, // "VAR", "VAR_INPUT", "VAR_OUTPUT" 等
    string? FilePath,
    int Line, // 声明所在行
    ConstantInitializer? InitialValue = null, // F3：显式初始值（编译期常量求值结果）
    // A8：声明类型（已解析）。`TypeName` 是给人看的显示字符串（如
    // "ARRAY[0..1] OF P"），无法反解成类型；全局聚合的 IR 类型必须用它，
    // 否则数组全局会退化成 DInt（实测 `@xs = global i32 0`）。
    IECType? DeclaredType = null
);

/// <summary>
/// 变量的显式初始值（编译期常量求值结果）。
///
/// F3（缺陷登记簿 §3.1）：此前语义层**没有任何字段**承载初始值，
/// 于是 <c>VAR_GLOBAL x : INT := 5;</c> 在 IR 层退化成零初始化。
/// </summary>
/// <param name="Type">声明类型（决定 IR 侧落成整型常量还是浮点常量）。</param>
/// <param name="IsReal">
/// <c>true</c> ⇒ 值在 <paramref name="RealValue"/>；<c>false</c> ⇒ 值在 <paramref name="IntValue"/>。
/// </param>
/// <param name="IntValue">整数值（含 BOOL：TRUE=1 / FALSE=0）。</param>
/// <param name="RealValue">实数值。</param>
public sealed record ConstantInitializer(
    IECType Type,
    bool IsReal,
    long IntValue,
    double RealValue)
{
    public static ConstantInitializer Int(IECType type, long value) =>
        new(type, false, value, 0.0);

    public static ConstantInitializer Real(IECType type, double value) =>
        new(type, true, 0, value);
}