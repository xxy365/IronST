namespace IronST.IRBuilder.IR;

/// <summary>常量基类</summary>
public abstract class Constant : Value { }

/// <summary>整数常量</summary>
public class ConstantInt : Constant
{
    public long Value { get; }

    public ConstantInt(IrType type, long value)
    {
        Type = type;
        Value = value;
    }

    public static ConstantInt Get(int bitWidth, long value) => new(new IntegerType(bitWidth), value);
}

/// <summary>浮点常量</summary>
public class ConstantFP : Constant
{
    public double Value { get; }

    public ConstantFP(IrType type, double value)
    {
        Type = type;
        Value = value;
    }

    public static ConstantFP Get(FloatKind kind, double value) => new(new FloatType(kind), value);
}

/// <summary>指针 null 常量</summary>
public class ConstantPointerNull : Constant
{
    public ConstantPointerNull() => Type = IrType.Pointer;
}

/// <summary>通用 null 常量（用于结构体、数组等聚合类型的 null）</summary>
public class ConstantNull : Constant
{
    public ConstantNull(IrType type) => Type = type;
}

/// <summary>undef 常量</summary>
public class UndefValue : Constant
{
    public UndefValue(IrType type) => Type = type;
    public static UndefValue Get(IrType type) => new(type);
}

/// <summary>poison 常量</summary>
public class PoisonValue : Constant
{
    public PoisonValue(IrType type) => Type = type;
    public static PoisonValue Get(IrType type) => new(type);
}

/// <summary>数组常量</summary>
public class ConstantArray : Constant
{
    public IrType ElementType { get; }
    public IReadOnlyList<Constant> Values { get; }

    public ConstantArray(ArrayType arrayType, IReadOnlyList<Constant> values)
    {
        Type = arrayType;
        ElementType = arrayType.ElementType;
        Values = values;
    }
}

/// <summary>结构体常量</summary>
public class ConstantStruct : Constant
{
    public IReadOnlyList<Constant> Values { get; }

    public ConstantStruct(StructType structType, IReadOnlyList<Constant> values)
    {
        Type = structType;
        Values = values;
    }
}

/// <summary>数据数组常量（常用于字符串字面量）</summary>
public class ConstantDataArray : Constant
{
    public byte[] Data { get; }

    public ConstantDataArray(byte[] data)
    {
        Data = data;
        Type = new ArrayType(IrType.Int8, data.Length);
    }

    /// <summary>从字符串创建，自动追加 null 终止符</summary>
    public static ConstantDataArray FromString(string s)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(s + '\0');
        return new ConstantDataArray(bytes);
    }
}

/// <summary>聚合类型的零初始化常量</summary>
public class ConstantAggregateZero : Constant
{
    public ConstantAggregateZero(IrType type) => Type = type;
}