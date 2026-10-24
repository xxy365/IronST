namespace IronST.IRBuilder.IR;

/// <summary>顶层模块：包含全局变量、函数定义/声明、命名类型等</summary>
public class Module
{
    public string? SourceFilename { get; set; }
    public string? TargetDataLayout { get; set; }
    // 默认是未知
    public string? TargetTriple { get; set; } = "unknown-unknown-unknown";

    public List<NamedStructType> NamedTypes { get; } = new();
    public List<GlobalVariable> GlobalVariables { get; } = new();
    public List<Function> Functions { get; } = new();

    // metadata 表
    public MetadataTable Metadata { get; } = new();

    public Module(string? sourceFilename = null)
    {
        SourceFilename = sourceFilename;
    }

    public void AddFunction(Function func)
    {
        func.Module = this;
        Functions.Add(func);
    }

    public void AddGlobalVariable(GlobalVariable gv)
    {
        gv.Module = this;
        GlobalVariables.Add(gv);
    }

    public NamedStructType AddNamedType(string name, StructType body)
    {
        var named = new NamedStructType(name) { Body = body };
        NamedTypes.Add(named);
        return named;
    }

    /// <summary>添加全局字符串常量（自动追加 null 终止符）</summary>
    public GlobalVariable AddGlobalString(
        string name,
        string value,
        LinkageType linkage = LinkageType.Private
    )
    {
        var data = ConstantDataArray.FromString(value);
        var gv = new GlobalVariable(name, (ArrayType)data.Type!, true, linkage, data)
        {
            Alignment = 1,
            IsUnnamedAddr = true,
        };
        AddGlobalVariable(gv);
        return gv;
    }
}
