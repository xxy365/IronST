namespace IronST.IRBuilder.IR;

/// <summary>
/// 存储模块中所有 metadata 定义。对应 .ll 文件末尾的 !0 = ... !1 = ... 段。
/// </summary>
public class MetadataTable
{
    // !0 = !DIFile(...) 这类按编号索引的节点
    private readonly Dictionary<int, MetadataNode> _nodes = new();

    // !llvm.dbg.cu = !{!0} 这类有名 metadata
    private readonly Dictionary<string, MetadataNode> _named = new();

    /// <summary>按编号获取或添加</summary>
    public MetadataNode? GetNode(int id) =>
        _nodes.TryGetValue(id, out var n) ? n : null;

    public void SetNode(int id, MetadataNode node)
    {
        node.Id = id;
        _nodes[id] = node;
    }

    /// <summary>有名 metadata，如 !llvm.dbg.cu</summary>
    public MetadataNode? GetNamed(string name) =>
        _named.TryGetValue(name, out var n) ? n : null;

    public void SetNamed(string name, MetadataNode node) =>
        _named[name] = node;

    /// <summary>遍历所有按编号存储的节点（输出时用）</summary>
    public IReadOnlyDictionary<int, MetadataNode> All => _nodes;

    /// <summary>遍历所有有名 metadata（输出时用）</summary>
    public IReadOnlyDictionary<string, MetadataNode> AllNamed => _named;
}