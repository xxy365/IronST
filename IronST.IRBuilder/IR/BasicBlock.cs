namespace IronST.IRBuilder.IR;

/// <summary>基本块：指令的线性序列</summary>
public class BasicBlock : Value
{
    public Function? Parent { get; internal set; }
    public List<Instruction> Instructions { get; } = new();

    public BasicBlock(string? name = null)
    {
        Name = name;
        Type = IrType.Label;
    }

    /// <summary>在块末尾添加指令</summary>
    public void AddInstruction(Instruction inst)
    {
        inst.Parent = this;
        Instructions.Add(inst);
    }

    /// <summary>在指定指令前插入</summary>
    public void InsertBefore(Instruction existing, Instruction newInst)
    {
        var idx = Instructions.IndexOf(existing);
        if (idx < 0) throw new ArgumentException("目标指令不存在");
        newInst.Parent = this;
        Instructions.Insert(idx, newInst);
    }

    /// <summary>在指定指令后插入</summary>
    public void InsertAfter(Instruction existing, Instruction newInst)
    {
        var idx = Instructions.IndexOf(existing);
        if (idx < 0) throw new ArgumentException("目标指令不存在");
        newInst.Parent = this;
        Instructions.Insert(idx + 1, newInst);
    }

    /// <summary>获取终结指令（如果存在）</summary>
    public Instruction? Terminator =>
        Instructions.Count > 0 && Instructions[^1].IsTerminator ? Instructions[^1] : null;

    /// <summary>移除指定指令</summary>
    public bool RemoveInstruction(Instruction inst)
    {
        if (Instructions.Remove(inst)) {
            inst.Parent = null;
            return true;
        }
        return false;
    }
}