using IronST.Common.Symbol;

namespace IronST.Frontend.Analysis;

/// <summary>
/// 带作用域嵌套的符号表。
/// </summary>
public class SymbolTable
{
    // 比较走统一符号键（SYM-1.e 唯一入口），存的仍是原始名（SYM-1.a）
    private readonly Dictionary<string, Symbol> _symbols = new(SymbolKey.Comparer);
    private readonly SymbolTable? _parent;

    public SymbolTable(SymbolTable? parent = null) => _parent = parent;

    /// <summary>声明符号。当前作用域已有同名符号则返回 false。</summary>
    public bool TryDeclare(Symbol symbol) => TryDeclare(symbol, out _);

    /// <summary>
    /// 声明符号。当前作用域已有同名符号则返回 false，并通过 <paramref name="existing"/>
    /// 返回先到的同名符号，供诊断引用其声明位置。
    /// </summary>
    public bool TryDeclare(Symbol symbol, out Symbol? existing)
    {
        if (_symbols.TryGetValue(symbol.Name, out existing)) return false;
        _symbols[symbol.Name] = symbol;
        return true;
    }

    /// <summary>用新符号替换同名符号（用于用户声明覆盖外部注入的库符号）。</summary>
    public Symbol? Replace(Symbol symbol)
    {
        _symbols.TryGetValue(symbol.Name, out var old);
        _symbols[symbol.Name] = symbol;
        return old;
    }

    /// <summary>从当前作用域向外逐层查找。</summary>
    public Symbol? Resolve(string name)
    {
        if (_symbols.TryGetValue(name, out var sym)) return sym;
        return _parent?.Resolve(name);
    }

    /// <summary>进入子作用域。</summary>
    public SymbolTable EnterScope() => new(this);

    /// <summary>当前作用域直接定义的符号（不含父级）。</summary>
    public IReadOnlyDictionary<string, Symbol> Locals => _symbols;
}
