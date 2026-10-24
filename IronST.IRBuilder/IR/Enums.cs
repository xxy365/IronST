namespace IronST.IRBuilder.IR;

/// <summary>链接类型</summary>
public enum LinkageType
{
    External, Internal, Private, Weak, WeakAny,
    Appending, Linkonce, LinkonceODR, WeakODR,
    Common, AvailableExternally, ExternalWeak
}

/// <summary>可见性样式</summary>
public enum VisibilityStyle { Default, Hidden, Protected }

/// <summary>线程本地模式</summary>
public enum ThreadLocalMode { None, LocalDynamic, InitialExec, LocalExec }

/// <summary>二元运算操作码</summary>
public enum BinaryOp
{
    Add, FAdd, Sub, FSub, Mul, FMul,
    UDiv, SDiv, FDiv, URem, SRem, FRem,
    Shl, LShr, AShr, And, Or, Xor,
}

/// <summary>整数比较谓词</summary>
public enum ICmpPredicate { Eq, Ne, Ugt, Uge, Ult, Ule, Sgt, Sge, Slt, Sle }

/// <summary>浮点比较谓词</summary>
public enum FCmpPredicate
{
    False, Oeq, Ogt, Oge, Olt, Ole, One, Ord,
    Ueq, Ugt, Uge, Ult, Ule, Une, Uno, True
}

/// <summary>类型转换操作码</summary>
public enum CastOp
{
    Trunc, ZExt, SExt, FPTrunc, FPExt,
    FPToUI, FPToSI, UIToFP, SIToFP,
    PtrToInt, IntToPtr, BitCast, AddrSpaceCast
}

/// <summary>浮点类型种类</summary>
public enum FloatKind { Half, BFloat, Float, Double, X86_FP80, FP128, PPC_FP128 }

/// <summary>调用约定</summary>
public enum CallingConv { C = 0, Fast = 8, Cold = 9, GHC = 10, Tail = 18, Swift = 19 }