using IronST.IRBuilder.IR;

namespace IronST.IRBuilder.Parser;

/// <summary>
/// 负责解析 .ll 中的 metadata 段。
/// 集成方式：LLParser 在解析完指令后调用此类处理 !N = ... 定义。
/// </summary>
class MetadataParser
{
    private readonly MetadataTable _table;
    private readonly Func<IrType>? _parseTypeRef;
    private readonly Func<Value>? _parseValueRef;

    public MetadataParser(MetadataTable table,
        Func<IrType>? parseTypeRef = null,
        Func<Value>? parseValueRef = null)
    {
        _table = table;
        _parseTypeRef = parseTypeRef;
        _parseValueRef = parseValueRef;
    }

    /// <summary>
    /// 尝试解析一个 metadata 定义。
    /// 输入: "!0 = !DIFile(filename: "x.c", directory: "/home")"
    /// tokens 从 "!0" 开始。
    /// </summary>
    public bool TryParse(List<Token> tokens, ref int pos)
    {
        if (pos + 2 >= tokens.Count) return false;
        if (tokens[pos].Kind != TokenKind.IntLit &&
            tokens[pos].Kind != TokenKind.Ident) return false;

        // 检查是否是 "!" 开头的 metadata 引用
        // 实际 token 中 "!" 被归入标识符或单独处理
        // 需要根据实际 Lexer 的行为调整

        return false; // 骨架，逐步实现
    }

    /// <summary>
    /// 解析 metadata 节点内容。
    /// 比如 "DIFile(filename: \"x.c\", directory: \"/home\")"
    /// </summary>
    public MetadataNode? ParseNode(List<Token> tokens, ref int pos)
    {
        // 第一阶段实现：只存原始 token，不展开语义
        return ParseRawTuple(tokens, ref pos);
    }

    /// <summary>
    /// 第一阶段：将 !{...} 或 !DI...(...) 整体存为 MDTuple
    /// 不解析具体语义，只保证结构完整。
    /// </summary>
    private MDTuple ParseRawTuple(List<Token> tokens, ref int pos)
    {
        var tuple = new MDTuple();
        // 跳过括号内的所有内容，逐个 operand 解析
        // 实际实现时逐 token 处理
        return tuple;
    }

    /// <summary>
    /// 解析 !dbg !N 附加项，附加到指令上。
    /// </summary>
    public void AttachToInstruction(Instruction inst, string kind, int metadataId)
    {
        var node = _table.GetNode(metadataId);
        if (node != null) {
            inst.SetMetadata(kind, node);
        } else {
            // metadata 节点尚未定义（前向引用），先创建占位
            var placeholder = new MDTuple();
            placeholder.Id = metadataId;
            _table.SetNode(metadataId, placeholder);
            inst.SetMetadata(kind, placeholder);
        }
    }
}