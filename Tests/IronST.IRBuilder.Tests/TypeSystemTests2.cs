using IronST.IRBuilder.IR;
using Xunit;

namespace IronST.IRBuilder.Tests;

public class TypeSystemTests2
{
    // ==================== 相等性 ====================

    [Fact]
    public void PointerType_相等性()
    {
        var a = new PointerType(0);
        var b = new PointerType(0);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void PointerType_不同地址空间不相等()
    {
        var a = new PointerType(0);
        var b = new PointerType(1);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void StructType_相等性()
    {
        var a = new StructType(new IrType[] { IrType.Int32, IrType.Float });
        var b = new StructType(new IrType[] { IrType.Int32, IrType.Float });
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void StructType_打包与非打包不相等()
    {
        var normal = new StructType(new IrType[] { IrType.Int8, IrType.Int32 }, isPacked: false);
        var packed = new StructType(new IrType[] { IrType.Int8, IrType.Int32 }, isPacked: true);
        Assert.NotEqual(normal, packed);
    }

    [Fact]
    public void StructType_不同元素不相等()
    {
        var a = new StructType(new IrType[] { IrType.Int32, IrType.Float });
        var b = new StructType(new IrType[] { IrType.Int32, IrType.Int32 });
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void FunctionType_相等性()
    {
        var a = new FunctionType(IrType.Int32, new IrType[] { IrType.Int32, IrType.Int32 });
        var b = new FunctionType(IrType.Int32, new IrType[] { IrType.Int32, IrType.Int32 });
        Assert.Equal(a, b);
    }

    [Fact]
    public void FunctionType_可变参数相等性()
    {
        var a = new FunctionType(IrType.Int32, new IrType[] { IrType.Pointer }, isVarArg: true);
        var b = new FunctionType(IrType.Int32, new IrType[] { IrType.Pointer }, isVarArg: true);
        Assert.Equal(a, b);
    }

    [Fact]
    public void FunctionType_可变参数与非可变不相等()
    {
        var a = new FunctionType(IrType.Int32, new IrType[] { IrType.Pointer }, isVarArg: true);
        var b = new FunctionType(IrType.Int32, new IrType[] { IrType.Pointer }, isVarArg: false);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ArrayType_相等性()
    {
        var a = new ArrayType(IrType.Int32, 10);
        var b = new ArrayType(IrType.Int32, 10);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void ArrayType_不同长度不相等()
    {
        var a = new ArrayType(IrType.Int32, 10);
        var b = new ArrayType(IrType.Int32, 20);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void NamedStructType_相等性()
    {
        var a = new NamedStructType("Foo");
        var b = new NamedStructType("Foo");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void NamedStructType_不同名字不相等()
    {
        var a = new NamedStructType("Foo");
        var b = new NamedStructType("Bar");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void VectorType_相等性()
    {
        var a = new VectorType(4, IrType.Float);
        var b = new VectorType(4, IrType.Float);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void VectorType_可伸缩与固定不相等()
    {
        var fixed_ = new VectorType(4, IrType.Int32, isScalable: false);
        var scalable = new VectorType(4, IrType.Int32, isScalable: true);
        Assert.NotEqual(fixed_, scalable);
    }

    [Fact]
    public void VectorType_不同元素数不相等()
    {
        var a = new VectorType(4, IrType.Float);
        var b = new VectorType(8, IrType.Float);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void IntegerType_不同位宽不相等()
    {
        Assert.NotEqual(IrType.Int32, IrType.Int64);
        Assert.NotEqual(IrType.Int1, IrType.Int8);
    }

    [Fact]
    public void 不同种类类型不相等()
    {
        Assert.NotEqual<IrType>(IrType.Int32, IrType.Float);
        Assert.NotEqual<IrType>(IrType.Void, IrType.Int1);
        Assert.NotEqual<IrType>(IrType.Pointer, IrType.Label);
    }

    // ==================== 边界情况 ====================
    [Fact]
    public void 空结构体_格式正确()
    {
        var st = new StructType(Array.Empty<IrType>());
        Assert.Equal("{  }", st.ToIRString());
    }

    [Fact]
    public void 空数组_格式正确()
    {
        var at = new ArrayType(IrType.Int32, 0);
        Assert.Equal("[0 x i32]", at.ToIRString());
    }

    [Fact]
    public void FunctionType_无参数()
    {
        var ft = new FunctionType(IrType.Void, Array.Empty<IrType>());
        Assert.Equal("void ()", ft.ToIRString());
        Assert.False(ft.IsVarArg);
    }

    [Fact]
    public void PointerType_非零地址空间()
    {
        var p = new PointerType(3);
        Assert.Equal("ptr addrspace(3)", p.ToIRString());
    }

    [Fact]
    public void FloatType_AllKinds不重复()
    {
        var kinds = Enum.GetValues<FloatKind>();
        var strings = kinds.Select(k => new FloatType(k).ToIRString()).Distinct().ToList();
        Assert.Equal(kinds.Length, strings.Count);
    }
}
