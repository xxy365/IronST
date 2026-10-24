namespace IronST.LLVMCodeGen.Exceptions;

/// <summary>
/// 代码生成阶段发生的通用异常。
/// </summary>
public class CodeGenException : Exception
{
    public string? SourceFile { get; }
    public int SourceLine { get; }

    public CodeGenException(string message)
        : base(message) { }

    public CodeGenException(string message, string sourceFile, int sourceLine)
        : base($"[{sourceFile}:{sourceLine}] {message}")
    {
        SourceFile = sourceFile;
        SourceLine = sourceLine;
    }

    public CodeGenException(string message, Exception innerException)
        : base(message, innerException) { }
}