// ═══════════════════════════════════════════════════════════════
//  SourceResolver.cs — PC → 源位置 的解析器工厂
// ═══════════════════════════════════════════════════════════════
namespace IronST.Runtime.Shared;

/// <summary>
/// 把「PC → (源文件, 行号)」表包成 VM 用的 <see cref="VMOptions.SourceLocator"/>。
///
/// <para>
/// 为什么不放在前端/字节码层：表本身只是数据（<c>int → (string, int)</c>），
/// <c>Runtime.Shared</c> 拿它不需要认识 AST 或源码映射格式；反过来，同一段线性查找
/// 逻辑若让每个宿主各写一遍（CLI、IDE、示例宿主），就会重复 —— 这正是"位置解析
/// 散落在消费端"的由来。收敛到这里，宿主只做一次"把表交进来"。
/// </para>
/// </summary>
public static class SourceResolver
{
    /// <summary>
    /// 构造解析器；表为 <c>null</c> 或空 ⇒ 返回 <c>null</c>
    /// （VM 不做回填，运行期错误只报 PC —— 与历史行为一致）。
    /// </summary>
    public static Func<uint, (string file, int line)?>? FromMap(
        IReadOnlyDictionary<int, (string file, int line)>? map)
        => map is null || map.Count == 0 ? null : new Func<uint, (string file, int line)?>(pc => Lookup(map, pc));

    /// <summary>
    /// 取 <c>key ≤ pc</c> 的最近一条映射；没有则 <c>null</c>。
    /// 源映射的 key 是「指令起始 PC」，所以这等价于"这条指令属于哪一行"。
    /// </summary>
    public static (string file, int line)? Lookup(
        IReadOnlyDictionary<int, (string file, int line)> map, uint pc)
    {
        int best = -1;
        foreach (var key in map.Keys)
            if (key <= pc && key > best)
                best = key;

        return best >= 0 ? map[best] : null;
    }

    /// <summary>
    /// 把整张映射平移到新基址：<c>新PC = codeBase + 旧PC</c>。
    /// </summary>
    /// <remarks>
    /// 用于跨「链接」保留行号：链接器把每个输入模块的机器码原样搬到
    /// <c>codeBase[i]</c>（见 <c>STModLinker.LinkWithLayout</c>），指令本身只发生平移，
    /// 所以源映射跟着平移即可继续对上。
    /// </remarks>
    public static Dictionary<int, (string file, int line)> Shift(
        IReadOnlyDictionary<int, (string file, int line)> map, int codeBase)
    {
        var shifted = new Dictionary<int, (string file, int line)>(map.Count);
        foreach (var kv in map)
            shifted[codeBase + kv.Key] = kv.Value;
        return shifted;
    }

    /// <summary>
    /// 合并若干「(基址, 映射)」段：每段先按 <see cref="Shift"/> 平移，再拼成一张表。
    /// PC 冲突时后段覆盖前段（同一 PC 只可能来自一个模块，冲突即输入重复）。
    /// </summary>
    public static Dictionary<int, (string file, int line)> Merge(
        IEnumerable<(int CodeBase, IReadOnlyDictionary<int, (string file, int line)> Map)> parts)
    {
        var merged = new Dictionary<int, (string file, int line)>();
        foreach (var (codeBase, map) in parts)
            foreach (var kv in Shift(map, codeBase))
                merged[kv.Key] = kv.Value;
        return merged;
    }
}
