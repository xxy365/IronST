namespace IronST.IRBuilder.IR;

/// <summary>
/// metadata 节点基类。对应 .ll 中的 !0 = !DIFile(...) 或 !1 = !{...}
/// 所有 DI* 类型、MDTuple 都继承自此类。
/// </summary>
public abstract class MetadataNode
{
    public int Id { get; set; } = -1; // !N 中的 N，-1 表示未分配
}

/// <summary>
/// metadata 字符串。对应 .ll 中的 !"hello"
/// </summary>
public class MDString : MetadataNode
{
    public string Value { get; }

    public MDString(string value) => Value = value;

    public override string ToString() => $"!\"{Value}\"";
}

/// <summary>
/// metadata 元组。对应 .ll 中的 !{!0, i32 1, !1}
/// 通用容器，operands 可以是 MetadataNode、常量整数、类型等。
/// </summary>
public class MDTuple : MetadataNode
{
    public List<object?> Operands { get; } = new();

    public MDTuple() { }

    public MDTuple(IEnumerable<object?> operands) => Operands.AddRange(operands);
}
