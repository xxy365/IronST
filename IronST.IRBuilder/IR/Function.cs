namespace IronST.IRBuilder.IR;

/// <summary>函数参数</summary>
public class Argument : Value
{
    public int Index { get; }
    public Function ParentFunction { get; }

    public Argument(IrType type, int index, Function parent)
    {
        Type = type;
        Index = index;
        ParentFunction = parent;
    }
}

/// <summary>函数定义或声明</summary>
public class Function : Value
{
    public Module? Module { get; internal set; }
    public FunctionType FunctionType { get; }
    public LinkageType Linkage { get; set; } = LinkageType.External;
    public VisibilityStyle Visibility { get; set; } = VisibilityStyle.Default;
    public IReadOnlyList<Argument> Parameters { get; }
    public List<BasicBlock> BasicBlocks { get; } = new();
    public bool IsDeclaration => BasicBlocks.Count == 0;
    public List<string> Attributes { get; } = new();
    public int? Alignment { get; set; }
    public CallingConv CallingConvention { get; set; } = CallingConv.C;

    // 函数级 metadata 附加项（如 !dbg !4）
    public Dictionary<string, MetadataNode>? MetadataAttachments { get; set; }

    // 设置函数级 metadata
    public void SetMetadata(string kind, MetadataNode node)
    {
        MetadataAttachments ??= new();
        MetadataAttachments[kind] = node;
    }

    public Function(
        string name,
        FunctionType functionType,
        LinkageType linkage = LinkageType.External
    )
    {
        Name = name;
        FunctionType = functionType;
        Type = IrType.Pointer;
        Linkage = linkage;

        var args = new List<Argument>();
        for (int i = 0; i < functionType.ParameterTypes.Count; i++)
            args.Add(new Argument(functionType.ParameterTypes[i], i, this));
        Parameters = args;
    }

    public void AppendBasicBlock(BasicBlock block)
    {
        block.Parent = this;
        BasicBlocks.Add(block);
    }
}
