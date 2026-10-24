using IronST.IRBuilder.IR;
using Xunit;

namespace IronST.IRBuilder.Tests;

/// <summary>类型系统测试</summary>
public class TypeSystemTests
{
    [Fact]
    public void VoidType_输出正确()
    {
        Assert.Equal("void", IrType.Void.ToIRString());
    }

    [Theory]
    [InlineData(1, "i1")]
    [InlineData(8, "i8")]
    [InlineData(16, "i16")]
    [InlineData(32, "i32")]
    [InlineData(64, "i64")]
    [InlineData(128, "i128")]
    public void IntegerType_正确位宽和字符串(int bits, string expected)
    {
        var t = new IntegerType(bits);
        Assert.Equal(bits, t.BitWidth);
        Assert.Equal(expected, t.ToIRString());
    }

    [Fact]
    public void IntegerType_相等性()
    {
        var a = new IntegerType(32);
        var b = new IntegerType(32);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void FloatType_各种浮点类型()
    {
        Assert.Equal("half", new FloatType(FloatKind.Half).ToIRString());
        Assert.Equal("bfloat", new FloatType(FloatKind.BFloat).ToIRString());
        Assert.Equal("float", new FloatType(FloatKind.Float).ToIRString());
        Assert.Equal("double", new FloatType(FloatKind.Double).ToIRString());
        Assert.Equal("x86_fp80", new FloatType(FloatKind.X86_FP80).ToIRString());
        Assert.Equal("fp128", new FloatType(FloatKind.FP128).ToIRString());
        Assert.Equal("ppc_fp128", new FloatType(FloatKind.PPC_FP128).ToIRString());
    }

    [Fact]
    public void PointerType_OpaquePointer()
    {
        Assert.Equal("ptr", IrType.Pointer.ToIRString());
        var ptrAddr = new PointerType(1);
        Assert.Equal("ptr addrspace(1)", ptrAddr.ToIRString());
    }

    [Fact]
    public void FunctionType_正确格式()
    {
        var ft = new FunctionType(IrType.Int32, new IrType[] { IrType.Int32, IrType.Float });
        Assert.Equal("i32 (i32, float)", ft.ToIRString());
        Assert.False(ft.IsVarArg);
    }

    [Fact]
    public void FunctionType_可变参数()
    {
        var ft = new FunctionType(IrType.Int32, new IrType[] { IrType.Pointer }, isVarArg: true);
        Assert.Equal("i32 (ptr, ...)", ft.ToIRString());
        Assert.True(ft.IsVarArg);
    }

    [Fact]
    public void FunctionType_无参数()
    {
        var ft = new FunctionType(IrType.Void, Array.Empty<IrType>());
        Assert.Equal("void ()", ft.ToIRString());
    }

    [Fact]
    public void ArrayType_正确格式()
    {
        var at = new ArrayType(IrType.Int32, 10);
        Assert.Equal("[10 x i32]", at.ToIRString());
    }

    [Fact]
    public void StructType_字面结构体()
    {
        var st = new StructType(new IrType[] { IrType.Int32, IrType.Float, IrType.Pointer });
        Assert.Equal("{ i32, float, ptr }", st.ToIRString());
    }

    [Fact]
    public void StructType_打包结构体()
    {
        var st = new StructType(new IrType[] { IrType.Int8, IrType.Int32 }, isPacked: true);
        Assert.Equal("<{ i8, i32 }>", st.ToIRString());
    }

    [Fact]
    public void NamedStructType_命名结构体()
    {
        var named = new NamedStructType("MyStruct");
        Assert.Equal("%MyStruct", named.ToIRString());
    }

    [Fact]
    public void VectorType_固定向量()
    {
        var vt = new VectorType(4, IrType.Float);
        Assert.Equal("<4 x float>", vt.ToIRString());
    }

    [Fact]
    public void VectorType_可伸缩向量()
    {
        var vt = new VectorType(4, IrType.Int32, isScalable: true);
        Assert.Equal("<vscale x 4 x i32>", vt.ToIRString());
    }

    [Fact]
    public void LabelAndMetadata_特殊类型()
    {
        Assert.Equal("label", IrType.Label.ToIRString());
        Assert.Equal("metadata", IrType.Metadata.ToIRString());
        Assert.Equal("token", IrType.Token.ToIRString());
    }
}