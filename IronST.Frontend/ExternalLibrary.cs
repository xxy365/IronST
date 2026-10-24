namespace IronST.Frontend.Analysis;

/// <summary>
/// 外部库声明 — 插件/标准库/用户自定义库的签名描述。
/// </summary>
public sealed class ExternalLibrary
{
    public required string Name { get; init; }
    public List<ExternalFunction> Functions { get; init; } = [];
    public List<ExternalFunctionBlock> FunctionBlocks { get; init; } = [];
    public List<ExternalType> Types { get; init; } = [];
}

public sealed class ExternalFunction
{
    public required string Name { get; init; }
    public string? ReturnTypeName { get; init; }       // null = VOID
    public List<ExternalParameter> Parameters { get; init; } = [];
}

public sealed class ExternalFunctionBlock
{
    public required string Name { get; init; }
    public List<ExternalParameter> InputVars { get; init; } = [];
    public List<ExternalParameter> OutputVars { get; init; } = [];
    public List<ExternalParameter> InOutVars { get; init; } = [];
    public List<(string Name, string TypeName)> InternalVars { get; init; } = [];
}

public sealed class ExternalParameter
{
    public required string Name { get; init; }
    public required string TypeName { get; init; }
}

public sealed class ExternalType
{
    public required string Name { get; init; }
    public required TypeKind Kind { get; init; }
    public List<(string Name, string TypeName)>? Fields { get; init; }        // Struct
    public List<string>? EnumMembers { get; init; }                           // Enum
    public Dictionary<string, long>? EnumMemberValues { get; init; }          // Enum 显式值
    public string? UnderlyingType { get; init; }                              // SimpleType / Subrange
}