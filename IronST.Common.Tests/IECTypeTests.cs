// tests/IronST.Common.Tests/IECTypeTests.cs
using IronST.Common.Types;

namespace IronST.Common.Tests;

/// <summary>
/// IECType 内置单例、工厂方法、分类属性的完整测试。
/// </summary>
public class IECTypeTests
{
    // ═══════════════════════════════════════════════════════════════
    //  1. 内置单例 — 名称 / Kind / 位宽 / 符号性
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    // -------- 有符号整数 --------
    [InlineData("SINT", IECType.KindTag.SINT, 8, true)]
    [InlineData("INT", IECType.KindTag.INT, 16, true)]
    [InlineData("DINT", IECType.KindTag.DINT, 32, true)]
    [InlineData("LINT", IECType.KindTag.LINT, 64, true)]
    // -------- 无符号整数 --------
    [InlineData("USINT", IECType.KindTag.USINT, 8, false)]
    [InlineData("UINT", IECType.KindTag.UINT, 16, false)]
    [InlineData("UDINT", IECType.KindTag.UDINT, 32, false)]
    [InlineData("ULINT", IECType.KindTag.ULINT, 64, false)]
    // -------- 位串 --------
    [InlineData("BYTE", IECType.KindTag.BYTE, 8, false)]
    [InlineData("WORD", IECType.KindTag.WORD, 16, false)]
    [InlineData("DWORD", IECType.KindTag.DWORD, 32, false)]
    [InlineData("LWORD", IECType.KindTag.LWORD, 64, false)]
    // -------- 浮点 --------
    [InlineData("REAL", IECType.KindTag.REAL, 32, true)]
    [InlineData("LREAL", IECType.KindTag.LREAL, 64, true)]
    // -------- BOOL --------
    [InlineData("BOOL", IECType.KindTag.BOOL, 1, false)]
    public void BuiltinSingletons_HaveCorrectProperties(
        string name, IECType.KindTag kind, int bitWidth, bool isSigned)
    {
        var type = IECType.LookupBuiltin(name)!;

        Assert.NotNull(type);
        Assert.Equal(kind, type.Kind);
        Assert.Equal(name, type.Name);
        Assert.Equal(bitWidth, type.BitWidth);
        Assert.Equal(isSigned, type.IsSigned);
    }

    [Theory]
    [InlineData("TIME", IECType.KindTag.TIME, 64)]
    [InlineData("DATE", IECType.KindTag.DATE, 64)]
    [InlineData("TOD", IECType.KindTag.TOD, 64)]
    [InlineData("DT", IECType.KindTag.DT, 64)]
    public void DateTimeTypes_HaveCorrectKindAndWidth(
        string name, IECType.KindTag kind, int bitWidth)
    {
        var type = IECType.LookupBuiltin(name)!;

        Assert.NotNull(type);
        Assert.Equal(kind, type.Kind);
        Assert.Equal(bitWidth, type.BitWidth);
    }

    [Fact]
    public void Void_HasZeroBitWidth()
    {
        Assert.Equal(IECType.KindTag.VOID, IECType.Void.Kind);
        Assert.Equal(0, IECType.Void.BitWidth);
        Assert.Equal("VOID", IECType.Void.Name);
    }

    [Fact]
    public void DefaultString_IsConfiguredCorrectly()
    {
        var s = IECType.DefaultString;

        Assert.Equal(IECType.KindTag.STRING, s.Kind);
        Assert.Equal("STRING", s.Name);
        Assert.Equal(254, s.MaxStringLength);
        Assert.False(s.IsWideString);
        Assert.Equal(8, s.BitWidth);
    }

    [Fact]
    public void DefaultWString_IsConfiguredCorrectly()
    {
        var ws = IECType.DefaultWString;

        Assert.Equal(IECType.KindTag.WSTRING, ws.Kind);
        Assert.Equal("WSTRING", ws.Name);
        Assert.Equal(254, ws.MaxStringLength);
        Assert.True(ws.IsWideString);
        Assert.Equal(16, ws.BitWidth);
    }

    // ═══════════════════════════════════════════════════════════════
    //  2. LookupBuiltin — 别名 / 大小写 / 未知
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("INT")]
    [InlineData("int")]
    [InlineData("Int")]
    [InlineData("iNt")]
    public void LookupBuiltin_IsCaseInsensitive(string input)
    {
        var result = IECType.LookupBuiltin(input);
        Assert.NotNull(result);
        Assert.Equal(IECType.KindTag.INT, result.Kind);
    }

    [Theory]
    [InlineData("T", "TIME")]
    [InlineData("D", "DATE")]
    [InlineData("TOD", "TIME_OF_DAY")]
    [InlineData("DT", "DATE_AND_TIME")]
    public void LookupBuiltin_ShortAliases_ResolveSameSingleton(
        string alias, string fullName)
    {
        var byAlias = IECType.LookupBuiltin(alias)!;
        var byFull = IECType.LookupBuiltin(fullName)!;

        Assert.NotNull(byAlias);
        Assert.NotNull(byFull);
        Assert.Same(byAlias, byFull); // 应返回同一个单例实例
    }

    [Theory]
    [InlineData("NO_SUCH_TYPE")]
    [InlineData("")]
    [InlineData("int_t")]
    [InlineData("STRUCT")]      // 复合类型不在查找表里
    public void LookupBuiltin_Unknown_ReturnsNull(string name)
    {
        Assert.Null(IECType.LookupBuiltin(name));
    }

    // ═══════════════════════════════════════════════════════════════
    //  3. 分类属性 — IsInteger / IsReal / IsNumeric / IsUnsigned
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void IsInteger_IncludesBool()
    {
        // BOOL 在 IEC 61131-3 中被视为 1-bit 整数，这是刻意设计
        Assert.True(IECType.Bool.IsInteger);
    }

    [Theory]
    [InlineData("SINT")]
    [InlineData("INT")]
    [InlineData("DINT")]
    [InlineData("LINT")]
    [InlineData("USINT")]
    [InlineData("UINT")]
    [InlineData("UDINT")]
    [InlineData("ULINT")]
    [InlineData("BYTE")]
    [InlineData("WORD")]
    [InlineData("DWORD")]
    [InlineData("LWORD")]
    public void IntegerTypes_AreAllIdentified(string name)
    {
        var t = IECType.LookupBuiltin(name)!;
        Assert.True(t.IsInteger, $"{name} should be IsInteger");
        Assert.True(t.IsNumeric, $"{name} should be IsNumeric");
        Assert.False(t.IsReal, $"{name} should not be IsReal");
    }

    [Theory]
    [InlineData("REAL")]
    [InlineData("LREAL")]
    public void RealTypes_AreAllIdentified(string name)
    {
        var t = IECType.LookupBuiltin(name)!;
        Assert.True(t.IsReal, $"{name} should be IsReal");
        Assert.True(t.IsNumeric, $"{name} should be IsNumeric");
        Assert.False(t.IsInteger, $"{name} should not be IsInteger");
    }

    [Theory]
    [InlineData("USINT")]
    [InlineData("UINT")]
    [InlineData("UDINT")]
    [InlineData("ULINT")]
    [InlineData("BYTE")]
    [InlineData("WORD")]
    [InlineData("DWORD")]
    [InlineData("LWORD")]
    public void UnsignedTypes_AreAllIdentified(string name)
    {
        var t = IECType.LookupBuiltin(name)!;
        Assert.True(t.IsUnsigned, $"{name} should be IsUnsigned");
        Assert.False(t.IsSigned);
    }

    [Theory]
    [InlineData("SINT")]
    [InlineData("INT")]
    [InlineData("DINT")]
    [InlineData("LINT")]
    [InlineData("REAL")]
    [InlineData("LREAL")]
    public void SignedTypes_AreNotUnsigned(string name)
    {
        var t = IECType.LookupBuiltin(name)!;
        Assert.False(t.IsUnsigned, $"{name} should not be IsUnsigned");
    }

    [Theory]
    [InlineData("TIME")]
    [InlineData("DATE")]
    [InlineData("TOD")]
    [InlineData("DT")]
    [InlineData("STRING")]
    [InlineData("WSTRING")]
    [InlineData("VOID")]
    public void NonNumericTypes_AreNeitherIntegerNorReal(string name)
    {
        var t = name == "VOID" ? IECType.Void
              : IECType.LookupBuiltin(name)!;

        Assert.False(t.IsInteger, $"{name} should not be IsInteger");
        Assert.False(t.IsReal, $"{name} should not be IsReal");
        Assert.False(t.IsNumeric, $"{name} should not be IsNumeric");
    }

    // ═══════════════════════════════════════════════════════════════
    //  4. 工厂方法 — MakeString
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void MakeString_Narrow_ProducesCorrectType()
    {
        var s = IECType.MakeString(80);

        Assert.Equal(IECType.KindTag.STRING, s.Kind);
        Assert.Equal("STRING(80)", s.Name);
        Assert.Equal(80, s.MaxStringLength);
        Assert.False(s.IsWideString);
        Assert.Equal(8, s.BitWidth);
    }

    [Fact]
    public void MakeString_Wide_ProducesCorrectType()
    {
        var ws = IECType.MakeString(100, wide: true);

        Assert.Equal(IECType.KindTag.WSTRING, ws.Kind);
        Assert.Equal("WSTRING(100)", ws.Name);
        Assert.Equal(100, ws.MaxStringLength);
        Assert.True(ws.IsWideString);
        Assert.Equal(16, ws.BitWidth);
    }

    // ═══════════════════════════════════════════════════════════════
    //  5. 工厂方法 — MakeArray
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void MakeArray_SingleDimension()
    {
        var arr = IECType.MakeArray(IECType.Int, new() { (0, 9) });

        Assert.Equal(IECType.KindTag.ARRAY, arr.Kind);
        Assert.Same(IECType.Int, arr.ElementType);
        Assert.NotNull(arr.Dimensions);
        Assert.Single(arr.Dimensions!);
        Assert.Equal((0, 9), arr.Dimensions![0]);
        Assert.Equal("ARRAY[0..9] OF INT", arr.Name);
    }

    [Fact]
    public void MakeArray_MultiDimension()
    {
        var arr = IECType.MakeArray(IECType.Real, new() { (1, 3), (0, 4) });

        Assert.Equal(IECType.KindTag.ARRAY, arr.Kind);
        Assert.Same(IECType.Real, arr.ElementType);
        Assert.Equal(2, arr.Dimensions!.Count);
        Assert.Equal((1, 3), arr.Dimensions![0]);
        Assert.Equal((0, 4), arr.Dimensions![1]);
        Assert.Equal("ARRAY[1..3,0..4] OF REAL", arr.Name);
    }

    // ═══════════════════════════════════════════════════════════════
    //  6. 工厂方法 — MakeStruct / MakeEnum / MakeReference
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void MakeStruct_ProducesCorrectType()
    {
        var fields = new Dictionary<string, IECType>(StringComparer.OrdinalIgnoreCase)
        {
            ["X"] = IECType.Int,
            ["Y"] = IECType.Real,
        };
        var s = IECType.MakeStruct("Point", fields);

        Assert.Equal(IECType.KindTag.STRUCT, s.Kind);
        Assert.Equal("Point", s.Name);
        Assert.NotNull(s.Fields);
        Assert.Equal(2, s.Fields!.Count);
        Assert.Same(IECType.Int, s.Fields["X"]);
        Assert.Same(IECType.Real, s.Fields["Y"]);
    }

    [Fact]
    public void MakeEnum_ProducesCorrectType()
    {
        var e = IECType.MakeEnum("Color", new() { "Red", "Green", "Blue" });

        Assert.Equal(IECType.KindTag.ENUM, e.Kind);
        Assert.Equal("Color", e.Name);
        Assert.Equal(32, e.BitWidth);
        Assert.False(e.IsSigned);
        Assert.Equal(new[] { "Red", "Green", "Blue" }, e.EnumValues);
    }

    [Fact]
    public void MakeReference_ProducesCorrectType()
    {
        var r = IECType.MakeReference(IECType.DInt);

        Assert.Equal(IECType.KindTag.REFERENCE, r.Kind);
        Assert.Equal("REFERENCE TO DINT", r.Name);
        Assert.Same(IECType.DInt, r.ReferencedType);
    }

    // ═══════════════════════════════════════════════════════════════
    //  7. 内置单例确实是单例（引用相等）
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BuiltinSingletons_AreReferentiallyEqual()
    {
        // 每次 LookupBuiltin 都返回同一个对象
        Assert.Same(IECType.Int, IECType.LookupBuiltin("INT"));
        Assert.Same(IECType.Bool, IECType.LookupBuiltin("BOOL"));
        Assert.Same(IECType.DefaultString, IECType.LookupBuiltin("STRING"));
    }

    [Fact]
    public void FactoryMethods_ProduceDistinctInstances()
    {
        var s1 = IECType.MakeString(80);
        var s2 = IECType.MakeString(80);
        Assert.NotSame(s1, s2); // 每次调用都是新实例
    }
}
