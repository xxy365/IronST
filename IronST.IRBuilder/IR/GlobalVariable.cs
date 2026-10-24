namespace IronST.IRBuilder.IR;

/// <summary>全局变量/常量</summary>
public class GlobalVariable : Value
{
    public Module? Module { get; internal set; }
    public IrType ValueType { get; }
    public bool IsConstant { get; set; }
    public LinkageType Linkage { get; set; }
    public VisibilityStyle Visibility { get; set; } = VisibilityStyle.Default;
    public ThreadLocalMode ThreadLocal { get; set; } = ThreadLocalMode.None;
    public Constant? Initializer { get; set; }

    /// <summary>
    /// <see cref="Initializer"/> 是否来自**源码里的显式初始值**。
    ///
    /// F3：全局变量默认是零初始化（<c>Initializer</c> = 类型零值），
    /// 但「零初始化」与「显式写了 <c>:= 0</c>」在字节码层是两回事 ——
    /// 后者需要在模块顶层真的发射一条 <c>SETG</c>。
    /// 本标志把两者区分开，**没写初值的程序因此零字节变动**。
    /// </summary>
    public bool HasExplicitInitializer { get; set; }
    public int Alignment { get; set; }
    public bool IsUnnamedAddr { get; set; }
    public string? Section { get; set; }

    public GlobalVariable(string name, IrType valueType, bool isConstant,
        LinkageType linkage, Constant? initializer = null)
    {
        Name = name;
        ValueType = valueType;
        IsConstant = isConstant;
        Linkage = linkage;
        Initializer = initializer;
        Type = IrType.Pointer; // LLVM 22: 全局变量的类型是 ptr
    }
}