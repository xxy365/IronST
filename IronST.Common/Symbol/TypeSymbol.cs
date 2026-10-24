// src/IronST.Common/Symbol/TypeSymbol.cs
using IronST.Common.Types;

namespace IronST.Common.Symbol;

/// <summary>
/// 用户自定义类型符号（TYPE ... END_TYPE 声明）。
/// </summary>
public class TypeSymbol : Symbol
{
    /// <summary>
    /// 类型实体。两趟类型注册（F7）下，符号先以占位符入表，
    /// 待其字段类型全部可解析后再回填实际类型。
    /// </summary>
    public IECType Type { get; set; } = IECType.Void;

    public TypeSymbol() => Kind = SymbolKind.Type;
}
