// src/IronST.Common/Symbol/Symbol.cs
using IronST.Common.Diagnostics;

namespace IronST.Common.Symbol;

/// <summary>
/// 所有符号的基类。
/// </summary>
public abstract class Symbol
{
    public string Name { get; init; } = "";
    public SymbolKind Kind { get; init; }
    public SourceSpan Declaration { get; init; } = SourceSpan.Unknown;
}
