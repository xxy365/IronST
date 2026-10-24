namespace IronST.StEditor;

/// <summary>
/// 从外部注入到编辑器的分析结果。
/// 编辑器只消费这些数据，不依赖任何 Frontend 内部类型。
/// </summary>
public sealed class EditorAnalysisData
{
    /// <summary>从代码中提取的变量列表</summary>
    public IReadOnlyList<SymbolInfo> Variables { get; init; }
        = Array.Empty<SymbolInfo>();

    /// <summary>函数 / 功能块 / 程序</summary>
    public IReadOnlyList<SymbolInfo> Functions { get; init; }
        = Array.Empty<SymbolInfo>();

    /// <summary>用户自定义类型（TYPE / STRUCT）</summary>
    public IReadOnlyList<SymbolInfo> TypeDefinitions { get; init; }
        = Array.Empty<SymbolInfo>();

    /// <summary>诊断信息（错误 / 警告），用于波浪线下划线</summary>
    public IReadOnlyList<DiagnosticInfo> Diagnostics { get; init; }
        = Array.Empty<DiagnosticInfo>();
}

/// <summary>
/// 通用符号信息，适用于变量、函数、类型。
/// </summary>
public sealed class SymbolInfo
{
    public required string Name { get; init; }
    public string TypeName { get; init; } = "";
    public string Description { get; init; } = "";
    public int Line { get; init; }
    public int Column { get; init; }
}

/// <summary>
/// 诊断信息，映射到波浪线下划线。
/// </summary>
public sealed class DiagnosticInfo
{
    public int Offset { get; init; }
    public int Length { get; init; }
    public string Message { get; init; } = "";
    public DiagnosticSeverity Severity { get; init; }
}

public enum DiagnosticSeverity { Warning, Error }
