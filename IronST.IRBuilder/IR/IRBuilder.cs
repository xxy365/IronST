namespace IronST.IRBuilder.IR;

/// <summary>指令构建器：在基本块中便捷地构建指令</summary>
public class IRBuilder
{
    private BasicBlock _block;

    public IRBuilder(BasicBlock block) => _block = block;

    public BasicBlock InsertBlock
    {
        get => _block;
        set => _block = value;
    }

    /// <summary>
    /// 每条指令插入当前块之后回调。
    ///
    /// 代码生成器用它给指令挂调试位置（当前语句的源码行）。不挂的话，源码
    /// 映射只覆盖少数几条指令、中间留大片空洞，取行号时只能"就近靠前"，
    /// 于是 FOR 的初始化和循环回跳会被算到上一条语句的行上 —— 单步高亮会
    /// 莫名跳回错误的行。
    /// </summary>
    public Action<Instruction>? OnInsert { get; set; }

    /// <summary>插入一条 IRBuilder 未封装的指令（同样触发 <see cref="OnInsert"/>）。</summary>
    public T AddInstruction<T>(T inst) where T : Instruction => Insert(inst);

    private T Insert<T>(T inst) where T : Instruction
    {
        _block.AddInstruction(inst);
        OnInsert?.Invoke(inst);
        return inst;
    }

    // ---- 终结指令 ----

    public ReturnInst CreateRet(Value value) => Insert(new ReturnInst(value));
    public ReturnInst CreateRetVoid() => Insert(new ReturnInst());
    public BranchInst CreateBr(BasicBlock dest) => Insert(new BranchInst(dest));
    public BranchInst CreateCondBr(Value cond, BasicBlock t, BasicBlock f) => Insert(new BranchInst(cond, t, f));
    public UnreachableInst CreateUnreachable() => Insert(new UnreachableInst());

    // ---- 二元运算 ----

    public Value CreateAdd(Value l, Value r, string? n = null) => Insert(BinaryOperator.CreateAdd(l, r, n));
    public Value CreateSub(Value l, Value r, string? n = null) => Insert(BinaryOperator.CreateSub(l, r, n));
    public Value CreateMul(Value l, Value r, string? n = null) => Insert(BinaryOperator.CreateMul(l, r, n));
    public Value CreateFAdd(Value l, Value r, string? n = null) => Insert(BinaryOperator.CreateFAdd(l, r, n));
    public Value CreateFSub(Value l, Value r, string? n = null) => Insert(BinaryOperator.CreateFSub(l, r, n));
    public Value CreateFMul(Value l, Value r, string? n = null) => Insert(BinaryOperator.CreateFMul(l, r, n));
    public Value CreateSDiv(Value l, Value r, string? n = null) => Insert(BinaryOperator.CreateSDiv(l, r, n));
    public Value CreateAnd(Value l, Value r, string? n = null) => Insert(BinaryOperator.CreateAnd(l, r, n));
    public Value CreateOr(Value l, Value r, string? n = null) => Insert(BinaryOperator.CreateOr(l, r, n));
    public Value CreateXor(Value l, Value r, string? n = null) => Insert(BinaryOperator.CreateXor(l, r, n));

    // ---- 比较 ----

    public Value CreateICmp(ICmpPredicate p, Value l, Value r, string? n = null) => Insert(new ICmpInst(p, l, r, n));
    public Value CreateFCmp(FCmpPredicate p, Value l, Value r, string? n = null) => Insert(new FCmpInst(p, l, r, n));

    // ---- 内存 ----

    public Value CreateAlloca(IrType ty, Value? arrSize = null, string? n = null, int align = 0) =>
        Insert(new AllocaInst(ty, arrSize, n, align));

    public Value CreateLoad(IrType ty, Value ptr, string? n = null, int align = 0) =>
        Insert(new LoadInst(ty, ptr, n, align));

    public StoreInst CreateStore(Value val, Value ptr, int align = 0) =>
        Insert(new StoreInst(val, ptr, align));

    public Value CreateGEP(IrType elemTy, Value ptr, IReadOnlyList<Value> indices,
        string? n = null, bool inBounds = false) =>
        Insert(new GetElementPtrInst(elemTy, ptr, indices, n, inBounds));

    // ---- 类型转换 ----

    public Value CreateTrunc(Value v, IrType d, string? n = null) => Insert(CastInst.CreateTrunc(v, d, n));
    public Value CreateZExt(Value v, IrType d, string? n = null) => Insert(CastInst.CreateZExt(v, d, n));
    public Value CreateSExt(Value v, IrType d, string? n = null) => Insert(CastInst.CreateSExt(v, d, n));
    public Value CreateBitCast(Value v, IrType d, string? n = null) => Insert(CastInst.CreateBitCast(v, d, n));
    public Value CreateIntToPtr(Value v, IrType d, string? n = null) => Insert(CastInst.CreateIntToPtr(v, d, n));
    public Value CreatePtrToInt(Value v, IrType d, string? n = null) => Insert(CastInst.CreatePtrToInt(v, d, n));

    // ---- 其他 ----

    public Value CreateCall(FunctionType ft, Value callee, IReadOnlyList<Value> args, string? n = null) =>
        Insert(new CallInst(ft, callee, args, n));

    public Value CreateSelect(Value cond, Value t, Value f, string? n = null) =>
        Insert(new SelectInst(cond, t, f, n));

    public PHINode CreatePhi(IrType ty, string? n = null) =>
        Insert(new PHINode(ty, n));
}