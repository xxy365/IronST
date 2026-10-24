// src/IronST.Common/Diagnostics/SourceSpan.cs
namespace IronST.Common.Diagnostics;

/// <summary>
/// 源码位置信息，标记一个 AST 节点或诊断信息在源文件中的区间。
/// Line/Column 从 1 开始计数。
/// </summary>
public readonly record struct SourceSpan(
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    string? FileName = null)
{
    public static readonly SourceSpan Unknown = new(0, 0, 0, 0);

    public override string ToString()
        => FileName != null ? $"{FileName}:{Line}:{Column}" : $"{Line}:{Column}";
}
