namespace IronST.IRBuilder.IR;

/// <summary>所有 SSA 值的抽象基类</summary>
public abstract class Value
{
    /// <summary>值的名称（不含 % 或 @ 前缀），null 表示无名值</summary>
    public string? Name { get; set; }

    /// <summary>值的 LLVM IR 类型</summary>
    public IrType Type { get; protected set; } = null!;

    public override string ToString() => $"{Type.ToIRString()} %{Name ?? "?"}";
}