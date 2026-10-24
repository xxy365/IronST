using System.Text;

namespace IronST.IRBuilder.IR;

// ================================================================
// 指令基类
// ================================================================

/// <summary>所有 LLVM IR 指令的抽象基类。指令也是 Value（SSA 形式）。</summary>
public abstract class Instruction : Value
{
    public BasicBlock? Parent { get; internal set; }
    protected readonly List<Value> _operands = new();
    public IReadOnlyList<Value> Operands => _operands;

    public virtual bool IsTerminator => false;
    public bool ProducesResult => Type is not null && Type is not VoidType;

    //  metadata 附加项  key 是 "dbg"、"tbaa"、"range" 等，value 是对应的 metadata 节点
    public Dictionary<string, MetadataNode>? MetadataAttachments { get; set; }

    // 便捷方法
    public void SetMetadata(string kind, MetadataNode node)
    {
        MetadataAttachments ??= new();
        MetadataAttachments[kind] = node;
    }

    protected void AddOperand(Value v) => _operands.Add(v);

    /// <summary>替换指定索引的操作数（供 Parser 解析后修正前向引用）</summary>
    public virtual void SetOperand(int index, Value v) => _operands[index] = v;
}

// ================================================================
// 终结指令
// ================================================================

/// <summary>ret 指令</summary>
public class ReturnInst : Instruction
{
    public Value? ReturnValue { get; }

    public ReturnInst(Value? returnValue = null)
    {
        ReturnValue = returnValue;
        Type = IrType.Void;
        if (returnValue != null)
            AddOperand(returnValue);
    }

    public override bool IsTerminator => true;
}

/// <summary>br 指令（条件或无条件跳转）</summary>
public class BranchInst : Instruction
{
    public Value? Condition { get; }
    public BasicBlock TrueDest { get; }
    public BasicBlock? FalseDest { get; }

    /// <summary>无条件跳转：br label %dest</summary>
    public BranchInst(BasicBlock dest)
    {
        TrueDest = dest;
        Type = IrType.Void;
        AddOperand(dest);
    }

    /// <summary>条件跳转：br i1 %cond, label %true, label %false</summary>
    public BranchInst(Value condition, BasicBlock trueDest, BasicBlock falseDest)
    {
        Condition = condition;
        TrueDest = trueDest;
        FalseDest = falseDest;
        Type = IrType.Void;
        AddOperand(condition);
        AddOperand(trueDest);
        AddOperand(falseDest);
    }

    public override bool IsTerminator => true;
    public bool IsConditional => Condition != null;
}

/// <summary>switch 指令</summary>
public class SwitchInst : Instruction
{
    public Value Condition { get; }
    public BasicBlock DefaultDest { get; }
    public List<(ConstantInt Value, BasicBlock Dest)> Cases { get; } = new();

    public SwitchInst(Value condition, BasicBlock defaultDest)
    {
        Condition = condition;
        DefaultDest = defaultDest;
        Type = IrType.Void;
        AddOperand(condition);
        AddOperand(defaultDest);
    }

    public void AddCase(ConstantInt value, BasicBlock dest)
    {
        Cases.Add((value, dest));
        AddOperand(value);
        AddOperand(dest);
    }

    public override bool IsTerminator => true;
}

/// <summary>unreachable 指令</summary>
public class UnreachableInst : Instruction
{
    public UnreachableInst() => Type = IrType.Void;

    public override bool IsTerminator => true;
}

// ================================================================
// 二元运算指令
// ================================================================

/// <summary>二元运算：add, sub, mul, fadd 等</summary>
public class BinaryOperator : Instruction
{
    public BinaryOp Op { get; }
    public Value LHS => _operands[0];
    public Value RHS => _operands[1];
    public bool NoSignedWrap { get; set; }
    public bool NoUnsignedWrap { get; set; }

    public BinaryOperator(BinaryOp op, Value lhs, Value rhs, string? name = null)
    {
        Op = op;
        Name = name;
        Type = lhs.Type;
        AddOperand(lhs);
        AddOperand(rhs);
    }

    public static BinaryOperator CreateAdd(Value l, Value r, string? n = null) =>
        new(BinaryOp.Add, l, r, n);

    public static BinaryOperator CreateSub(Value l, Value r, string? n = null) =>
        new(BinaryOp.Sub, l, r, n);

    public static BinaryOperator CreateMul(Value l, Value r, string? n = null) =>
        new(BinaryOp.Mul, l, r, n);

    public static BinaryOperator CreateFAdd(Value l, Value r, string? n = null) =>
        new(BinaryOp.FAdd, l, r, n);

    public static BinaryOperator CreateFSub(Value l, Value r, string? n = null) =>
        new(BinaryOp.FSub, l, r, n);

    public static BinaryOperator CreateFMul(Value l, Value r, string? n = null) =>
        new(BinaryOp.FMul, l, r, n);

    public static BinaryOperator CreateSDiv(Value l, Value r, string? n = null) =>
        new(BinaryOp.SDiv, l, r, n);

    public static BinaryOperator CreateUDiv(Value l, Value r, string? n = null) =>
        new(BinaryOp.UDiv, l, r, n);

    public static BinaryOperator CreateSRem(Value l, Value r, string? n = null) =>
        new(BinaryOp.SRem, l, r, n);

    public static BinaryOperator CreateFDiv(Value l, Value r, string? n = null) =>
        new(BinaryOp.FDiv, l, r, n);

    public static BinaryOperator CreateFRem(Value l, Value r, string? n = null) =>
        new(BinaryOp.FRem, l, r, n);

    public static BinaryOperator CreateShl(Value l, Value r, string? n = null) =>
        new(BinaryOp.Shl, l, r, n);

    public static BinaryOperator CreateAShr(Value l, Value r, string? n = null) =>
        new(BinaryOp.AShr, l, r, n);

    public static BinaryOperator CreateLShr(Value l, Value r, string? n = null) =>
        new(BinaryOp.LShr, l, r, n);

    public static BinaryOperator CreateAnd(Value l, Value r, string? n = null) =>
        new(BinaryOp.And, l, r, n);

    public static BinaryOperator CreateOr(Value l, Value r, string? n = null) =>
        new(BinaryOp.Or, l, r, n);

    public static BinaryOperator CreateXor(Value l, Value r, string? n = null) =>
        new(BinaryOp.Xor, l, r, n);
}

// ================================================================
// 比较指令
// ================================================================

/// <summary>icmp 指令</summary>
public class ICmpInst : Instruction
{
    public ICmpPredicate Predicate { get; }
    public Value LHS => _operands[0];
    public Value RHS => _operands[1];

    public ICmpInst(ICmpPredicate predicate, Value lhs, Value rhs, string? name = null)
    {
        Predicate = predicate;
        Name = name;
        Type = IrType.Int1;
        AddOperand(lhs);
        AddOperand(rhs);
    }
}

/// <summary>fcmp 指令</summary>
public class FCmpInst : Instruction
{
    public FCmpPredicate Predicate { get; }
    public Value LHS => _operands[0];
    public Value RHS => _operands[1];

    public FCmpInst(FCmpPredicate predicate, Value lhs, Value rhs, string? name = null)
    {
        Predicate = predicate;
        Name = name;
        Type = IrType.Int1;
        AddOperand(lhs);
        AddOperand(rhs);
    }
}

// ================================================================
// 内存操作指令
// ================================================================

/// <summary>alloca 指令</summary>
public class AllocaInst : Instruction
{
    public IrType AllocatedType { get; }
    public Value? ArraySize { get; }
    public int Alignment { get; set; }

    public AllocaInst(
        IrType allocatedType,
        Value? arraySize = null,
        string? name = null,
        int alignment = 0
    )
    {
        AllocatedType = allocatedType;
        ArraySize = arraySize;
        Alignment = alignment;
        Name = name;
        Type = IrType.Pointer;
        if (arraySize != null)
            AddOperand(arraySize);
    }
}

/// <summary>load 指令</summary>
public class LoadInst : Instruction
{
    public IrType LoadedType { get; }
    public Value Pointer => _operands[0];
    public bool IsVolatile { get; set; }
    public int Alignment { get; set; }

    public LoadInst(IrType loadedType, Value pointer, string? name = null, int alignment = 0)
    {
        LoadedType = loadedType;
        Alignment = alignment;
        Name = name;
        Type = loadedType;
        AddOperand(pointer);
    }
}

/// <summary>store 指令</summary>
public class StoreInst : Instruction
{
    public Value StoredValue => _operands[0];
    public Value Pointer => _operands[1];
    public bool IsVolatile { get; set; }
    public int Alignment { get; set; }

    public StoreInst(Value value, Value pointer, int alignment = 0)
    {
        Alignment = alignment;
        Type = IrType.Void;
        AddOperand(value);
        AddOperand(pointer);
    }
}

/// <summary>getelementptr 指令</summary>
public class GetElementPtrInst : Instruction
{
    public IrType SourceElementType { get; }
    public Value Pointer => _operands[0];
    public bool InBounds { get; set; }

    private readonly int _indexCount;
    public IEnumerable<Value> Indices
    {
        get
        {
            for (int i = 1; i <= _indexCount; i++)
                yield return _operands[i];
        }
    }

    public GetElementPtrInst(
        IrType sourceElementType,
        Value pointer,
        IReadOnlyList<Value> indices,
        string? name = null,
        bool inBounds = false
    )
    {
        SourceElementType = sourceElementType;
        InBounds = inBounds;
        Name = name;
        Type = IrType.Pointer;
        AddOperand(pointer);
        foreach (var idx in indices)
            AddOperand(idx);
        _indexCount = indices.Count;
    }
}

// ================================================================
// 类型转换指令
// ================================================================

/// <summary>类型转换指令</summary>
public class CastInst : Instruction
{
    public CastOp Op { get; }
    public Value Source => _operands[0];

    public CastInst(CastOp op, Value source, IrType destType, string? name = null)
    {
        Op = op;
        Name = name;
        Type = destType;
        AddOperand(source);
    }

    public static CastInst CreateTrunc(Value v, IrType d, string? n = null) =>
        new(CastOp.Trunc, v, d, n);

    public static CastInst CreateZExt(Value v, IrType d, string? n = null) =>
        new(CastOp.ZExt, v, d, n);

    public static CastInst CreateSExt(Value v, IrType d, string? n = null) =>
        new(CastOp.SExt, v, d, n);

    public static CastInst CreateFPTrunc(Value v, IrType d, string? n = null) =>
        new(CastOp.FPTrunc, v, d, n);

    public static CastInst CreateFPExt(Value v, IrType d, string? n = null) =>
        new(CastOp.FPExt, v, d, n);

    public static CastInst CreateFPToUI(Value v, IrType d, string? n = null) =>
        new(CastOp.FPToUI, v, d, n);

    public static CastInst CreateFPToSI(Value v, IrType d, string? n = null) =>
        new(CastOp.FPToSI, v, d, n);

    public static CastInst CreateUIToFP(Value v, IrType d, string? n = null) =>
        new(CastOp.UIToFP, v, d, n);

    public static CastInst CreateSIToFP(Value v, IrType d, string? n = null) =>
        new(CastOp.SIToFP, v, d, n);

    public static CastInst CreatePtrToInt(Value v, IrType d, string? n = null) =>
        new(CastOp.PtrToInt, v, d, n);

    public static CastInst CreateIntToPtr(Value v, IrType d, string? n = null) =>
        new(CastOp.IntToPtr, v, d, n);

    public static CastInst CreateBitCast(Value v, IrType d, string? n = null) =>
        new(CastOp.BitCast, v, d, n);

    public static CastInst CreateAddrSpaceCast(Value v, IrType d, string? n = null) =>
        new(CastOp.AddrSpaceCast, v, d, n);
}

// ================================================================
// 其他指令
// ================================================================

/// <summary>phi 节点</summary>
public class PHINode : Instruction
{
    // 存储 incoming 对：(Value, Block)
    private readonly List<(Value Value, BasicBlock Block)> _incoming = new();
    public IReadOnlyList<(Value Value, BasicBlock Block)> Incoming => _incoming;

    public PHINode(IrType type, string? name = null)
    {
        Type = type;
        Name = name;
    }

    public void AddIncoming(Value value, BasicBlock block)
    {
        _incoming.Add((value, block));
        AddOperand(value);
        AddOperand(block);
    }

    /// <summary>替换指定索引的 incoming 值（用于前向引用解析）</summary>
    public void ReplaceIncomingValue(int index, Value newValue)
    {
        var old = _incoming[index];
        _incoming[index] = (newValue, old.Block);
        // 同步更新 _operands（布局: val0, bb0, val1, bb1, ...）
        _operands[index * 2] = newValue;
    }

    public override void SetOperand(int index, Value v)
    {
        _operands[index] = v;

        int pairIndex = index / 2;
        if (pairIndex < _incoming.Count)
        {
            var old = _incoming[pairIndex];
            if (index % 2 == 0)
                // 偶数位置是 Value
                _incoming[pairIndex] = (v, old.Block);
            else
                // 奇数位置是 Block
                _incoming[pairIndex] = (old.Value, (BasicBlock)v);
        }
    }
}

/// <summary>select 指令</summary>
public class SelectInst : Instruction
{
    public Value Condition => _operands[0];
    public Value TrueValue => _operands[1];
    public Value FalseValue => _operands[2];

    public SelectInst(Value condition, Value trueValue, Value falseValue, string? name = null)
    {
        Name = name;
        Type = trueValue.Type;
        AddOperand(condition);
        AddOperand(trueValue);
        AddOperand(falseValue);
    }
}

/// <summary>call 指令</summary>
public class CallInst : Instruction
{
    public FunctionType CalleeType { get; }
    public Value Callee => _operands[0];
    public int ArgumentCount { get; }
    public bool IsTailCall { get; set; }

    public CallInst(
        FunctionType calleeType,
        Value callee,
        IReadOnlyList<Value> arguments,
        string? name = null
    )
    {
        CalleeType = calleeType;
        Name = name;
        Type = calleeType.ReturnType;
        AddOperand(callee);
        foreach (var arg in arguments)
            AddOperand(arg);
        ArgumentCount = arguments.Count;
    }

    public Value GetArgument(int index) => _operands[index + 1];
}

/// <summary>extractvalue 指令</summary>
public class ExtractValueInst : Instruction
{
    public Value Aggregate => _operands[0];
    public IReadOnlyList<int> Indices { get; }

    public ExtractValueInst(Value aggregate, IReadOnlyList<int> indices, string? name = null)
    {
        Indices = indices;
        Name = name;
        Type = ResolveResultType(aggregate.Type, indices);
        AddOperand(aggregate);
    }

    private static IrType ResolveResultType(IrType aggType, IReadOnlyList<int> indices)
    {
        var cur = aggType;
        foreach (var idx in indices)
            cur = cur switch
            {
                StructType st => st.ElementTypes[idx],
                ArrayType at => at.ElementType,
                _ => throw new InvalidOperationException($"无法对 {cur} 使用索引 {idx}"),
            };
        return cur;
    }
}

/// <summary>insertvalue 指令</summary>
public class InsertValueInst : Instruction
{
    public Value Aggregate => _operands[0];
    public Value InsertedValue => _operands[1];
    public IReadOnlyList<int> Indices { get; }

    public InsertValueInst(
        Value aggregate,
        Value insertedValue,
        IReadOnlyList<int> indices,
        string? name = null
    )
    {
        Indices = indices;
        Name = name;
        Type = aggregate.Type;
        AddOperand(aggregate);
        AddOperand(insertedValue);
    }
}
