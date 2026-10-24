namespace IronST.IRBuilder.IR;

/// <summary>所有 LLVM IR 类型的抽象基类</summary>
public abstract class IrType
{
    // 预定义常用类型
    public static readonly VoidType Void = new();
    public static readonly IntegerType Int1 = new(1);
    public static readonly IntegerType Int8 = new(8);
    public static readonly IntegerType Int16 = new(16);
    public static readonly IntegerType Int32 = new(32);
    public static readonly IntegerType Int64 = new(64);
    public static readonly IntegerType Int128 = new(128);
    public static readonly FloatType Float = new(FloatKind.Float);
    public static readonly FloatType Double = new(FloatKind.Double);
    public static readonly FloatType Half = new(FloatKind.Half);
    public static readonly PointerType Pointer = new();
    public static readonly LabelType Label = new();
    public static readonly MetadataType Metadata = new();
    public static readonly TokenType Token = new();

    public abstract string ToIRString();

    public override string ToString() => ToIRString();
}

/// <summary>void 类型</summary>
public class VoidType : IrType
{
    public override string ToIRString() => "void";

    public override bool Equals(object? obj) => obj is VoidType;

    public override int GetHashCode() => "void".GetHashCode();
}

/// <summary>整数类型：i1, i8, i16, i32, i64, i128 等</summary>
public class IntegerType : IrType
{
    public int BitWidth { get; }

    public IntegerType(int bitWidth) => BitWidth = bitWidth;

    public override string ToIRString() => $"i{BitWidth}";

    public override bool Equals(object? obj) =>
        obj is IntegerType other && BitWidth == other.BitWidth;

    public override int GetHashCode() => BitWidth;
}

/// <summary>浮点类型</summary>
public class FloatType : IrType
{
    public FloatKind Kind { get; }

    public FloatType(FloatKind kind) => Kind = kind;

    public override string ToIRString() =>
        Kind switch
        {
            FloatKind.Half => "half",
            FloatKind.BFloat => "bfloat",
            FloatKind.Float => "float",
            FloatKind.Double => "double",
            FloatKind.X86_FP80 => "x86_fp80",
            FloatKind.FP128 => "fp128",
            FloatKind.PPC_FP128 => "ppc_fp128",
            _ => throw new ArgumentOutOfRangeException(nameof(Kind)),
        };

    public override bool Equals(object? obj) => obj is FloatType other && Kind == other.Kind;

    public override int GetHashCode() => (int)Kind;
}

/// <summary>不透明指针类型 (LLVM 22 强制 opaque pointer)</summary>
public class PointerType : IrType
{
    public int AddressSpace { get; }

    public PointerType(int addressSpace = 0) => AddressSpace = addressSpace;

    public override string ToIRString() =>
        AddressSpace == 0 ? "ptr" : $"ptr addrspace({AddressSpace})";

    public override bool Equals(object? obj) =>
        obj is PointerType other && AddressSpace == other.AddressSpace;

    public override int GetHashCode() => AddressSpace;
}

/// <summary>函数类型，如 i32 (i32, float)</summary>
public class FunctionType : IrType
{
    public IrType ReturnType { get; }
    public IReadOnlyList<IrType> ParameterTypes { get; }
    public bool IsVarArg { get; }

    public FunctionType(
        IrType returnType,
        IReadOnlyList<IrType> parameterTypes,
        bool isVarArg = false
    )
    {
        ReturnType = returnType;
        ParameterTypes = parameterTypes;
        IsVarArg = isVarArg;
    }

    public override string ToIRString()
    {
        var ps = string.Join(", ", ParameterTypes.Select(t => t.ToIRString()));
        if (IsVarArg)
            ps += (ps.Length > 0 ? ", " : "") + "...";
        return $"{ReturnType.ToIRString()} ({ps})";
    }

    public override bool Equals(object? obj) =>
        obj is FunctionType other
        && ReturnType.Equals(other.ReturnType)
        && IsVarArg == other.IsVarArg
        && ParameterTypes.SequenceEqual(other.ParameterTypes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ReturnType);
        hash.Add(IsVarArg);
        foreach (var p in ParameterTypes)
            hash.Add(p);
        return hash.ToHashCode();
    }
}

/// <summary>数组类型，如 [10 x i32]</summary>
public class ArrayType : IrType
{
    public IrType ElementType { get; }
    public int Length { get; }

    public ArrayType(IrType elementType, int length)
    {
        ElementType = elementType;
        Length = length;
    }

    public override string ToIRString() => $"[{Length} x {ElementType.ToIRString()}]";

    public override bool Equals(object? obj) =>
        obj is ArrayType other && Length == other.Length && ElementType.Equals(other.ElementType);

    public override int GetHashCode() => Length ^ ElementType.GetHashCode();
}

/// <summary>向量类型，如 &lt;4 x float&gt; 或 &lt;vscale x 4 x i32&gt;</summary>
public class VectorType : IrType
{
    public int NumElements { get; }
    public IrType ElementType { get; }
    public bool IsScalable { get; }

    public VectorType(int numElements, IrType elementType, bool isScalable = false)
    {
        NumElements = numElements;
        ElementType = elementType;
        IsScalable = isScalable;
    }

    public override string ToIRString() =>
        IsScalable
            ? $"<vscale x {NumElements} x {ElementType.ToIRString()}>"
            : $"<{NumElements} x {ElementType.ToIRString()}>";

    public override bool Equals(object? obj) =>
        obj is VectorType other
        && NumElements == other.NumElements
        && IsScalable == other.IsScalable
        && ElementType.Equals(other.ElementType);

    public override int GetHashCode() => NumElements;
}

/// <summary>字面结构体类型，如 { i32, float }</summary>
public class StructType : IrType
{
    public IReadOnlyList<IrType> ElementTypes { get; }
    public bool IsPacked { get; }

    public StructType(IReadOnlyList<IrType> elementTypes, bool isPacked = false)
    {
        ElementTypes = elementTypes;
        IsPacked = isPacked;
    }

    public override string ToIRString()
    {
        var inner = string.Join(", ", ElementTypes.Select(t => t.ToIRString()));
        return IsPacked ? $"<{{ {inner} }}>" : $"{{ {inner} }}";
    }

    public override bool Equals(object? obj) =>
        obj is StructType other
        && IsPacked == other.IsPacked
        && ElementTypes.SequenceEqual(other.ElementTypes);

    public override int GetHashCode() => ElementTypes.Count;
}

/// <summary>命名结构体类型，如 %MyStruct</summary>
public class NamedStructType : IrType
{
    public string Name { get; }
    public StructType? Body { get; set; } // null 表示不透明结构体

    public NamedStructType(string name) => Name = name;

    public override string ToIRString() => $"%{Name}";

    public override bool Equals(object? obj) => obj is NamedStructType other && Name == other.Name;

    public override int GetHashCode() => Name.GetHashCode();
}

/// <summary>label 类型</summary>
public class LabelType : IrType
{
    public override string ToIRString() => "label";

    public override bool Equals(object? obj) => obj is LabelType;

    public override int GetHashCode() => 7;
}

/// <summary>metadata 类型</summary>
public class MetadataType : IrType
{
    public override string ToIRString() => "metadata";

    public override bool Equals(object? obj) => obj is MetadataType;

    public override int GetHashCode() => 11;
}

/// <summary>token 类型</summary>
public class TokenType : IrType
{
    public override string ToIRString() => "token";

    public override bool Equals(object? obj) => obj is TokenType;

    public override int GetHashCode() => 13;
}
