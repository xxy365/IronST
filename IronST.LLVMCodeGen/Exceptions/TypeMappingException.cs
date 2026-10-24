namespace IronST.LLVMCodeGen.Exceptions;

/// <summary>
/// IEC 类型 → LLVM IR 类型映射失败时抛出。
/// </summary>
public class TypeMappingException : CodeGenException
{
    public string? IecTypeName { get; }

    public TypeMappingException(string message)
        : base(message) { }

    public TypeMappingException(string iecTypeName, string reason)
        : base($"无法映射 IEC 类型 '{iecTypeName}': {reason}")
    {
        IecTypeName = iecTypeName;
    }
}