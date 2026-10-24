using IronST.Common.Types;

namespace IronST.Common.Symbol;

/// <summary>
/// FUNCTION_BLOCK 符号。包含内部成员和 Action。
/// </summary>
public class FunctionBlockSymbol : Symbol
{
    public Dictionary<string, VariableSymbol> Members { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, FunctionSymbol> Actions { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public FunctionBlockSymbol() => Kind = SymbolKind.FunctionBlock;
}
