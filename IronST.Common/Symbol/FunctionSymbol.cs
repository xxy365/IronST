// src/IronST.Common/Symbol/FunctionSymbol.cs
using IronST.Common.Types;

namespace IronST.Common.Symbol;

/// <summary>
/// 函数符号。包含返回类型和参数列表。
/// </summary>
public class FunctionSymbol : Symbol
{
    public IECType ReturnType { get; init; } = IECType.Void;
    public List<VariableSymbol> Parameters { get; init; } = new();

    public FunctionSymbol() => Kind = SymbolKind.Function;
}
