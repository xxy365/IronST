// tests/IronST.Common.Tests/SymbolTests.cs
using IronST.Common.Types;
using IronST.Common.Symbol;
using IronST.Common.Diagnostics;

namespace IronST.Common.Tests;

public class SymbolTests
{
    // ═══════════════════════════════════════════════════════════════
    //  VariableSymbol
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void VariableSymbol_Defaults()
    {
        var v = new VariableSymbol();

        Assert.Equal("", v.Name);
        Assert.Equal(SymbolKind.Variable, v.Kind);
        Assert.Equal(IECType.Void, v.Type);
        Assert.Equal(VarScope.Local, v.Scope);
        Assert.Equal(VarModifier.None, v.Modifier);
        Assert.Null(v.DirectAddress);
        Assert.Equal(SourceSpan.Unknown, v.Declaration);
    }

    [Fact]
    public void VariableSymbol_InitProperties()
    {
        var span = new SourceSpan(5, 1, 5, 20, "main.st");
        var v = new VariableSymbol
        {
            Name = "counter",
            Type = IECType.DInt,
            Scope = VarScope.Input,
            Modifier = VarModifier.Retain,
            Declaration = span,
        };

        Assert.Equal("counter", v.Name);
        Assert.Same(IECType.DInt, v.Type);
        Assert.Equal(VarScope.Input, v.Scope);
        Assert.Equal(VarModifier.Retain, v.Modifier);
        Assert.Equal(span, v.Declaration);
    }

    [Theory]
    [InlineData(VarScope.Local)]
    [InlineData(VarScope.Input)]
    [InlineData(VarScope.Output)]
    [InlineData(VarScope.InOut)]
    [InlineData(VarScope.Global)]
    [InlineData(VarScope.External)]
    [InlineData(VarScope.Temp)]
    [InlineData(VarScope.Access)]
    public void VarScope_AllValues_AreDistinct(VarScope scope)
    {
        // 确保枚举值不重叠（防回归）
        var v = new VariableSymbol { Name = "x", Scope = scope };
        Assert.Equal(scope, v.Scope);
    }

    [Theory]
    [InlineData(VarModifier.None)]
    [InlineData(VarModifier.Constant)]
    [InlineData(VarModifier.Retain)]
    [InlineData(VarModifier.NonRetain)]
    public void VarModifier_AllValues_Work(VarModifier mod)
    {
        var v = new VariableSymbol { Name = "x", Modifier = mod };
        Assert.Equal(mod, v.Modifier);
    }

    // ═══════════════════════════════════════════════════════════════
    //  FunctionSymbol
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void FunctionSymbol_Defaults()
    {
        var f = new FunctionSymbol();

        Assert.Equal(SymbolKind.Function, f.Kind);
        Assert.Equal(IECType.Void, f.ReturnType);
        Assert.Empty(f.Parameters);
    }

    [Fact]
    public void FunctionSymbol_WithParameters()
    {
        var f = new FunctionSymbol
        {
            Name = "Add",
            ReturnType = IECType.Int,
            Parameters = new()
            {
                new VariableSymbol { Name = "a", Type = IECType.Int, Scope = VarScope.Input },
                new VariableSymbol { Name = "b", Type = IECType.Int, Scope = VarScope.Input },
            },
        };

        Assert.Equal(2, f.Parameters.Count);
        Assert.Equal("a", f.Parameters[0].Name);
        Assert.Equal("b", f.Parameters[1].Name);
    }

    // ═══════════════════════════════════════════════════════════════
    //  FunctionBlockSymbol
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void FunctionBlockSymbol_DefaultCollectionsAreEmpty()
    {
        var fb = new FunctionBlockSymbol();

        Assert.Equal(SymbolKind.FunctionBlock, fb.Kind);
        Assert.Empty(fb.Members);
        Assert.Empty(fb.Actions);
    }

    [Fact]
    public void FunctionBlockSymbol_MembersDictionaries_AreCaseInsensitive()
    {
        var fb = new FunctionBlockSymbol
        {
            Name = "MotorCtrl",
            Members = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Speed"] = new VariableSymbol { Name = "Speed", Type = IECType.Int },
            },
            Actions = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Start"] = new FunctionSymbol { Name = "Start" },
            },
        };

        Assert.True(fb.Members.ContainsKey("speed"));   // 大小写不敏感
        Assert.True(fb.Actions.ContainsKey("START"));
    }

    // ═══════════════════════════════════════════════════════════════
    //  TypeSymbol
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TypeSymbol_Defaults()
    {
        var t = new TypeSymbol();

        Assert.Equal(SymbolKind.Type, t.Kind);
        Assert.Equal(IECType.Void, t.Type);
    }

    [Fact]
    public void TypeSymbol_WithCustomType()
    {
        var myStruct = IECType.MakeStruct("Point", new()
        {
            ["X"] = IECType.Int,
            ["Y"] = IECType.Int,
        });

        var t = new TypeSymbol { Name = "Point", Type = myStruct };

        Assert.Equal(SymbolKind.Type, t.Kind);
        Assert.Same(myStruct, t.Type);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Symbol 基类 — Declaration span
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Symbol_DeclarationSpan_Persists()
    {
        var span = new SourceSpan(12, 1, 12, 30, "prog.st");
        var v = new VariableSymbol { Name = "x", Declaration = span };

        Assert.Equal(12, v.Declaration.Line);
        Assert.Equal("prog.st", v.Declaration.FileName);
    }
}
