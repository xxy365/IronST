using IronST.Common.Types;

namespace IronST.Common.Symbol;

/// <summary>
/// 变量符号。对应 VAR / VAR_INPUT / VAR_OUTPUT 等声明。
/// </summary>
public class VariableSymbol : Symbol
{
    public IECType Type { get; init; } = IECType.Void;
    public VarScope Scope { get; init; }
    public VarModifier Modifier { get; init; }
    public string? DirectAddress { get; init; }

    public VariableSymbol() => Kind = SymbolKind.Variable;
}

public enum VarScope
{
    Local,
    Input,
    Output,
    InOut,
    Global,
    External,
    Temp,
    Access,

    //对应 VAR_CONFIG
    Config,
}

public enum VarModifier
{
    None,
    Constant,
    Retain,
    NonRetain,
}
