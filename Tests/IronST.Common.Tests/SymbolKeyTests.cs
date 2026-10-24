// tests/IronST.Common.Tests/SymbolKeyTests.cs
using System.Globalization;
using IronST.Common.Symbol;

namespace IronST.Common.Tests;

/// <summary>
/// 统一符号键 SYM-1.b / SYM-1.e 的验收用例
/// （docs/阶段0B设计.md §2.2、§8.6 前置检查第 1 条）。
/// </summary>
public class SymbolKeyTests
{
    // ═══════════════════════════════════════════════════════════════
    //  用例组 1：Main / main  —— 大小写不敏感
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("Main", "main", true)]
    [InlineData("Main", "MAIN", true)]
    [InlineData("Main", "mAiN", true)]
    [InlineData("Main", "Main2", false)]
    [InlineData("Main", "Main ", false)]
    [InlineData("", "", true)]
    [InlineData("Main", "", false)]
    public void Of_and_Comparer_Agree_OnCaseInsensitiveMatch(
        string left, string right, bool expected)
    {
        Assert.Equal(expected, SymbolKey.Comparer.Equals(left, right));
        Assert.Equal(expected, SymbolKey.Of(left) == SymbolKey.Of(right));
    }

    [Fact]
    public void Of_DoesNotMutateOriginalName()
    {
        string name = "Main";

        var key = SymbolKey.Of(name);

        Assert.Equal("Main", name);   // 原始名必须原样保留（SYM-1.a）
        Assert.Equal("MAIN", key);    // 键是另一个字符串
        Assert.NotSame(name, key);
    }

    // ═══════════════════════════════════════════════════════════════
    //  用例组 2：区域设置独立 —— 不随 CurrentCulture 变化
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("tr-TR")]   // 土耳其语：i 的文化敏感大写是 "İ"
    [InlineData("az-AZ")]   // 阿塞拜疆语同理
    [InlineData("en-US")]
    public void Keys_AreIndependentOfCurrentCulture(string cultureName)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);

            // 文化敏感的 ToUpper() 在 tr-TR 下会把 "i" 变成 "İ" —— 那是禁止的实现
            Assert.Equal("I", SymbolKey.Of("i"));
            Assert.Equal("MAIN", SymbolKey.Of("main"));
            Assert.True(SymbolKey.Comparer.Equals("i", "I"));
            Assert.True(SymbolKey.Comparer.Equals("FILE", "file"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Keys_AreIdentical_AcrossCultures()
    {
        var original = CultureInfo.CurrentCulture;
        string invariantKey;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            invariantKey = SymbolKey.Of("FileName");

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal(invariantKey, SymbolKey.Of("FileName"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  用例组 3：与既有 OrdinalIgnoreCase 行为等价（抽取不改语义）
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("Main", "main")]
    [InlineData("fbInstance", "FBINSTANCE")]
    [InlineData("Counter", "counter")]
    [InlineData("AB", "ab")]
    [InlineData("x", "X")]
    [InlineData("My_Var123", "MY_VAR123")]
    public void PreservesExistingOrdinalIgnoreCaseSemantics(string left, string right)
    {
        // 与今天 SymbolTable 使用的 StringComparer.OrdinalIgnoreCase 必须给出同一结果
        bool legacy = string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        Assert.True(legacy);
        Assert.True(SymbolKey.Comparer.Equals(left, right));
        Assert.Equal(SymbolKey.Of(left), SymbolKey.Of(right));
    }

    [Theory]
    [InlineData("Main", "Main2")]
    [InlineData("AB", "AC")]
    [InlineData("a", "b")]
    [InlineData("ab", "ba")]
    public void PreservesExistingOrdinalIgnoreCaseNonMatch(string left, string right)
    {
        Assert.False(string.Equals(left, right, StringComparison.OrdinalIgnoreCase));
        Assert.False(SymbolKey.Comparer.Equals(left, right));
    }

    [Fact]
    public void DoesNotExpandCharacters_NoFalseCollision()
    {
        // string.ToUpperInvariant("ß") == "SS" 会把两个不同符号折成同一个键 —— 必须避免
        Assert.False(SymbolKey.Comparer.Equals("ß", "SS"));
        Assert.NotEqual(SymbolKey.Of("ß"), SymbolKey.Of("SS"));
        Assert.Equal("ß".Length, SymbolKey.Of("ß").Length);
    }

    // ═══════════════════════════════════════════════════════════════
    //  键与哈希恒等价（字典一致性）
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void EqualNames_HaveEqualHashCodes()
    {
        string[] variants = ["Main", "main", "MAIN", "mAiN"];

        foreach (var v in variants)
        {
            Assert.True(SymbolKey.Comparer.Equals("Main", v));
            Assert.Equal(
                SymbolKey.Comparer.GetHashCode("Main"),
                SymbolKey.Comparer.GetHashCode(v));
        }
    }

    [Fact]
    public void DifferentNames_CollideOnlyByHash_NotByEquality()
    {
        // 哈希可以相等（允许），但相等性必须为假
        Assert.False(SymbolKey.Comparer.Equals("Main", "Main2"));
    }

    [Fact]
    public void Dictionary_UsesSymbolKey_AndKeepsOriginalKey()
    {
        var map = new Dictionary<string, int>(SymbolKey.Comparer);

        map["Main"] = 1;

        // 用不同大小写查同一符号 —— 命中
        Assert.True(map.TryGetValue("main", out var byLower));
        Assert.Equal(1, byLower);

        // 存进去的原始名必须原样可读出（SYM-1.a）
        Assert.Contains("Main", map.Keys);
    }

    // ═══════════════════════════════════════════════════════════════
    //  空值 / 空串
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void NullAndEmpty_AreHandledWithoutThrowing()
    {
        Assert.Equal("", SymbolKey.Of(""));
        Assert.True(SymbolKey.Comparer.Equals(null, null));
        Assert.False(SymbolKey.Comparer.Equals(null, "a"));
        Assert.False(SymbolKey.Comparer.Equals("a", null));
        Assert.Equal(
            SymbolKey.Comparer.GetHashCode(""),
            SymbolKey.Comparer.GetHashCode(""));
    }
}
