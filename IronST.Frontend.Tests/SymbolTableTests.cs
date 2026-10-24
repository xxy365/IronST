// tests/IronST.Frontend.Tests/SymbolTableTests.cs
using IronST.Common.Symbol;
using IronST.Common.Types;
using IronST.Common.Diagnostics;
using IronST.Frontend.Analysis;

namespace IronST.Frontend.Tests;

public class SymbolTableTests
{
    private static VariableSymbol MakeVar(string name, IECType? type = null)
        => new() { Name = name, Type = type ?? IECType.Int };

    private static FunctionSymbol MakeFunc(string name, IECType? ret = null)
        => new()
        {
            Name = name,
            ReturnType = ret ?? IECType.Void,
            Parameters = new(),
        };

    // ═══════════════════════════════════════════════════════════════
    //  1. 基本声明与查找
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TryDeclare_NewSymbol_ReturnsTrue()
    {
        var table = new SymbolTable();
        var sym = MakeVar("x");

        Assert.True(table.TryDeclare(sym));
        Assert.Same(sym, table.Resolve("x"));
    }

    [Fact]
    public void TryDeclare_Duplicate_ReturnsFalse()
    {
        var table = new SymbolTable();
        var sym1 = MakeVar("x", IECType.Int);
        var sym2 = MakeVar("x", IECType.DInt);

        Assert.True(table.TryDeclare(sym1));
        Assert.False(table.TryDeclare(sym2));

        // 第一个声明保持不变
        Assert.Same(sym1, table.Resolve("x"));
    }

    [Fact]
    public void Resolve_Unknown_ReturnsNull()
    {
        var table = new SymbolTable();
        Assert.Null(table.Resolve("no_such_thing"));
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        var table = new SymbolTable();
        table.TryDeclare(MakeVar("MyVar"));

        Assert.NotNull(table.Resolve("MYVAR"));
        Assert.NotNull(table.Resolve("myvar"));
        Assert.NotNull(table.Resolve("MyVar"));
    }

    // ═══════════════════════════════════════════════════════════════
    //  2. 作用域嵌套
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void EnterScope_CreatesChildThatCanSeeParent()
    {
        var parent = new SymbolTable();
        var global = MakeVar("g");
        parent.TryDeclare(global);

        var child = parent.EnterScope();

        Assert.Same(global, child.Resolve("g"));
    }

    [Fact]
    public void ChildScope_ShadowsParentSymbol()
    {
        var parent = new SymbolTable();
        var parentX = MakeVar("x", IECType.Int);
        parent.TryDeclare(parentX);

        var child = parent.EnterScope();
        var childX = MakeVar("x", IECType.DInt);
        child.TryDeclare(childX);

        // 子作用域里的 x 遮蔽了父作用域的 x
        Assert.Same(childX, child.Resolve("x"));
        // 父作用域不受影响
        Assert.Same(parentX, parent.Resolve("x"));
    }

    [Fact]
    public void ParentScope_CannotSeeChildSymbols()
    {
        var parent = new SymbolTable();
        var child = parent.EnterScope();
        child.TryDeclare(MakeVar("local"));

        Assert.Null(parent.Resolve("local"));
    }

    [Fact]
    public void DeepNesting_ResolvesUpMultipleLevels()
    {
        var root = new SymbolTable();
        var rootSym = MakeVar("root_var");
        root.TryDeclare(rootSym);

        var level1 = root.EnterScope();
        var level1Sym = MakeVar("level1_var");
        level1.TryDeclare(level1Sym);

        var level2 = level1.EnterScope();

        // level2 能看到 root 和 level1 的符号
        Assert.Same(rootSym, level2.Resolve("root_var"));
        Assert.Same(level1Sym, level2.Resolve("level1_var"));
        Assert.Null(level2.Resolve("nope"));
    }

    [Fact]
    public void ChildShadow_ParentResolvesOriginal()
    {
        var root = new SymbolTable();
        root.TryDeclare(MakeVar("v", IECType.Int));

        var child = root.EnterScope();
        child.TryDeclare(MakeVar("v", IECType.Real));

        Assert.Equal(IECType.Real, (child.Resolve("v") as VariableSymbol)!.Type);
        Assert.Equal(IECType.Int, (root.Resolve("v") as VariableSymbol)!.Type);
    }

    // ═══════════════════════════════════════════════════════════════
    //  3. Locals 只返回当前层
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Locals_DoesNotIncludeParentSymbols()
    {
        var parent = new SymbolTable();
        parent.TryDeclare(MakeVar("global_x"));

        var child = parent.EnterScope();
        child.TryDeclare(MakeVar("local_y"));

        Assert.Single(child.Locals);
        Assert.True(child.Locals.ContainsKey("local_y"));
        Assert.False(child.Locals.ContainsKey("global_x"));
    }

    [Fact]
    public void Locals_EmptyOnFreshScope()
    {
        var table = new SymbolTable();
        Assert.Empty(table.Locals);
    }

    [Fact]
    public void Locals_ReflectsAllDeclarationsInScope()
    {
        var table = new SymbolTable();
        table.TryDeclare(MakeVar("a"));
        table.TryDeclare(MakeVar("b"));
        table.TryDeclare(MakeFunc("fn"));

        Assert.Equal(3, table.Locals.Count);
    }

    // ═══════════════════════════════════════════════════════════════
    //  4. 不同符号种类共存
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void DifferentSymbolKinds_CoexistInSameScope()
    {
        var table = new SymbolTable();
        table.TryDeclare(MakeVar("x"));
        table.TryDeclare(MakeFunc("Add"));
        table.TryDeclare(new TypeSymbol { Name = "MyType", Type = IECType.MakeEnum("MyType", new() { "A" }) });

        Assert.Equal(SymbolKind.Variable, table.Resolve("x")!.Kind);
        Assert.Equal(SymbolKind.Function, table.Resolve("Add")!.Kind);
        Assert.Equal(SymbolKind.Type, table.Resolve("MyType")!.Kind);
    }

    [Fact]
    public void FunctionAndVariable_SameName_VariableShadowsInChild()
    {
        // 函数在全局，变量在子作用域（如 POU 局部声明了与函数同名的变量）
        var global = new SymbolTable();
        global.TryDeclare(MakeFunc("Calc"));

        var local = global.EnterScope();
        var localVar = MakeVar("Calc", IECType.DInt);
        local.TryDeclare(localVar);

        // 子作用域里的变量遮蔽了全局的函数
        Assert.Same(localVar, local.Resolve("Calc"));
        Assert.Equal(SymbolKind.Variable, local.Resolve("Calc")!.Kind);

        // 全局仍然看到函数
        Assert.Equal(SymbolKind.Function, global.Resolve("Calc")!.Kind);
    }

    // ═══════════════════════════════════════════════════════════════
    //  5. 边界情况
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void EmptyName_IsValidSymbol()
    {
        // 技术上允许，虽然语义上不应该出现
        var table = new SymbolTable();
        Assert.True(table.TryDeclare(MakeVar("")));
        Assert.NotNull(table.Resolve(""));
    }

    [Fact]
    public void CaseInsensitive_BothWays()
    {
        var table = new SymbolTable();
        table.TryDeclare(MakeVar("ABC"));

        // 以不同大小写查找都能找到
        Assert.NotNull(table.Resolve("abc"));
        Assert.NotNull(table.Resolve("Abc"));
        Assert.NotNull(table.Resolve("ABC"));

        // 以相同名称不同大小写声明应该被拒绝（因为 key 相同）
        Assert.False(table.TryDeclare(MakeVar("abc")));
    }
}
