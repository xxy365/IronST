namespace IronST.Common.Symbol;

/// <summary>
/// 统一符号键 —— 全链<b>唯一</b>的符号键生成入口。
/// </summary>
/// <remarks>
/// 规则（已冻结：docs/阶段0B设计.md §2.2 <c>SYM-1.b</c> / <c>SYM-1.e</c>）：
/// <list type="number">
/// <item><b>大小写不敏感</b>；</item>
/// <item><b>不依赖当前区域设置</b> —— 只用 <c>char.ToUpperInvariant(char)</c>，
/// 禁止调用文化敏感的 <c>ToUpper()</c> / <c>ToLower()</c>；</item>
/// <item><b>逐字符大写、不扩字符</b>，故与 <c>StringComparer.OrdinalIgnoreCase</c>
/// 语义一致（<c>"ß"</c> 不会被折成 <c>"SS"</c>，不产生虚假合并）；</item>
/// <item><b>只产出比较键，不替代原始名称</b> —— 原始名保留用于诊断、调试信息与
/// 接口序列化（<c>SYM-1.a</c>）。字典里存的仍是原始名，只是<b>比较</b>走本键。</item>
/// </list>
/// 声明冲突检测、引用解析、接口摘要匹配、跨模块符号合并四处<b>必须统一走本类</b>，
/// 不得在各层各自实现大小写规则（否则跨模块名称会不一致）。
/// <para>
/// 宿主函数名不适用本规则自动跟随：宿主 ABI 的名称 ↔ ID 映射须稳定，
/// 规则变更属 ABI 变更（<c>SYM-1.f</c>）。
/// </para>
/// </remarks>
public static class SymbolKey
{
    /// <summary>
    /// 生成规范化符号键（用于存储、序列化与跨模块符号合并）。
    /// </summary>
    public static string Of(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        var chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            chars[i] = Fold(chars[i]);
        return new string(chars);
    }

    /// <summary>
    /// 字典比较器 —— 与 <see cref="Of"/> 同一条规则，但比较路径<b>零分配</b>。
    /// </summary>
    /// <remarks>
    /// <c>Equals</c> 与 <c>GetHashCode</c> 都基于 <see cref="Fold(char)"/>，
    /// 二者恒等价，因此键与比较器不会给出互相矛盾的结果。
    /// </remarks>
    public static IEqualityComparer<string> Comparer { get; } = SymbolKeyComparer.Instance;

    /// <summary>逐字符不变文化大写。长度不变，故键长 = 原名长。</summary>
    private static char Fold(char c) => char.ToUpperInvariant(c);

    private sealed class SymbolKeyComparer : IEqualityComparer<string>
    {
        internal static readonly SymbolKeyComparer Instance = new();

        public bool Equals(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            if (x.Length != y.Length) return false;
            if (string.Equals(x, y, StringComparison.Ordinal)) return true;

            for (int i = 0; i < x.Length; i++)
                if (Fold(x[i]) != Fold(y[i]))
                    return false;
            return true;
        }

        public int GetHashCode(string obj)
        {
            if (obj is null) return 0;

            unchecked
            {
                int h = 17;
                for (int i = 0; i < obj.Length; i++)
                    h = (h * 31) + Fold(obj[i]);
                return h;
            }
        }
    }
}
