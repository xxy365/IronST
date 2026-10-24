using System.Text;
using IronST.IRBuilder.IR;

namespace IronST.IRBuilder.Emitter;

/// <summary>
/// 负责将 MetadataTable 输出为 .ll 格式的 metadata 段。
/// </summary>
class MetadataEmitter
{
    public void Emit(StringBuilder sb, MetadataTable table)
    {
        if (table.All.Count == 0 && table.AllNamed.Count == 0)
            return;

        sb.AppendLine();

        // 按编号顺序输出 !0 = ..., !1 = ...
        foreach (var (id, node) in table.All.OrderBy(kv => kv.Key)) {
            sb.Append($"!{id} = ");
            EmitNode(sb, node);
            sb.AppendLine();
        }

        // 输出有名 metadata
        foreach (var (name, node) in table.AllNamed) {
            sb.Append($"!{name} = ");
            EmitNode(sb, node);
            sb.AppendLine();
        }
    }

    /// <summary>
    /// 输出单个 metadata 节点。
    /// 第一阶段：原样输出存储的结构。
    /// 后续阶段：根据节点类型输出 DI* 语法。
    /// </summary>
    private void EmitNode(StringBuilder sb, MetadataNode node)
    {
        switch (node) {
            case MDString s:
                sb.Append($"!\"{Escape(s.Value)}\"");
                break;

            case MDTuple t:
                sb.Append("!{");
                for (int i = 0; i < t.Operands.Count; i++) {
                    if (i > 0) sb.Append(", ");
                    EmitOperand(sb, t.Operands[i]);
                }
                sb.Append('}');
                break;

            // 后续加入 DIFile, DILocation 等的专门输出
            default:
                sb.Append($"!\"<unsupported: {node.GetType().Name}>\"");
                break;
        }
    }

    private void EmitOperand(StringBuilder sb, object? operand)
    {
        switch (operand) {
            case MetadataNode md:
                if (md.Id >= 0) sb.Append($"!{md.Id}");
                else EmitNode(sb, md);
                break;
            case int i:
                sb.Append($"i32 {i}");
                break;
            case long l:
                sb.Append($"i64 {l}");
                break;
            case string s:
                sb.Append($"!\"{Escape(s)}\"");
                break;
            case null:
                sb.Append("null");
                break;
            default:
                sb.Append($"!\"<unsupported: {operand.GetType().Name}>\"");
                break;
        }
    }

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}