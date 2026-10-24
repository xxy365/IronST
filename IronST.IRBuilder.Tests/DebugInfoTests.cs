using IronST.IRBuilder.IR;
using Xunit;

namespace IronST.IRBuilder.Tests;

/// <summary>
/// DebugInfo 类型测试：DIFile, DILocation, DISubprogram, DICompileUnit 等
/// </summary>
public class DebugInfoTests
{
    // ==================== DIFile ====================

    [Fact]
    public void DIFile_基本属性()
    {
        var file = new DIFile
        {
            Filename = "test.c",
            Directory = "/home/user"
        };

        Assert.Equal("test.c", file.Filename);
        Assert.Equal("/home/user", file.Directory);
    }

    [Fact]
    public void DIFile_继承DINode()
    {
        var file = new DIFile();
        Assert.IsAssignableFrom<MetadataNode>(file);
    }

    [Fact]
    public void DIFile_Id可设置()
    {
        var file = new DIFile();
        file.Id = 3;
        Assert.Equal(3, file.Id);
    }

    // ==================== DILocation ====================

    [Fact]
    public void DILocation_基本属性()
    {
        var loc = new DILocation
        {
            Line = 10,
            Column = 5
        };

        Assert.Equal(10, loc.Line);
        Assert.Equal(5, loc.Column);
    }

    [Fact]
    public void DILocation_Scope引用()
    {
        var file = new DIFile { Filename = "test.c", Directory = "/" };
        var sub = new DISubprogram { Name = "main", File = file, Line = 1 };
        var loc = new DILocation { Line = 5, Column = 3, Scope = sub };

        Assert.Same(sub, loc.Scope);
    }

    [Fact]
    public void DILocation_InlinedAt默认Null()
    {
        var loc = new DILocation { Line = 1, Column = 1 };
        Assert.Null(loc.InlinedAt);
    }

    // ==================== DISubprogram ====================

    [Fact]
    public void DISubprogram_基本属性()
    {
        var file = new DIFile { Filename = "calc.c", Directory = "/src" };
        var sp = new DISubprogram
        {
            Name = "add",
            File = file,
            Line = 3,
            ScopeLine = 3,
            IsDefinition = true,
            IsLocal = false
        };

        Assert.Equal("add", sp.Name);
        Assert.Equal("calc.c", ((DIFile)sp.File!).Filename);
        Assert.Equal(3, sp.Line);
        Assert.True(sp.IsDefinition);
    }

    // ==================== DICompileUnit ====================

    [Fact]
    public void DICompileUnit_基本属性()
    {
        var file = new DIFile { Filename = "main.c", Directory = "/home" };
        var cu = new DICompileUnit
        {
            Language = 12, // DW_LANG_C99
            File = file,
            Producer = "clang 18.0",
            IsOptimized = false,
            EmissionKind = "FullDebug"
        };

        Assert.Equal(12, cu.Language);
        Assert.False(cu.IsOptimized);
        Assert.Equal("FullDebug", cu.EmissionKind);
        Assert.Same(file, cu.File);
    }

    [Fact]
    public void DICompileUnit_Subprograms列表()
    {
        var cu = new DICompileUnit();
        cu.Subprograms.Add(new DISubprogram { Name = "foo" });
        cu.Subprograms.Add(new DISubprogram { Name = "bar" });

        Assert.Equal(2, cu.Subprograms.Count);
    }

    // ==================== DISubroutineType ====================

    [Fact]
    public void DISubroutineType_基本属性()
    {
        var types = new MDTuple();
        var subType = new DISubroutineType { Types = types };
        Assert.Same(types, subType.Types);
    }

    // ==================== DIBasicType ====================

    [Fact]
    public void DIBasicType_基本属性()
    {
        var bt = new DIBasicType
        {
            Name = "int",
            Size = 32,
            Encoding = 5 // DW_ATE_signed
        };

        Assert.Equal("int", bt.Name);
        Assert.Equal(32, bt.Size);
        Assert.Equal(5, bt.Encoding);
    }

    // ==================== DILexicalBlock ====================

    [Fact]
    public void DILexicalBlock_基本属性()
    {
        var scope = new DISubprogram { Name = "main" };
        var block = new DILexicalBlock
        {
            Scope = scope,
            Line = 10,
            Column = 5
        };

        Assert.Same(scope, block.Scope);
        Assert.Equal(10, block.Line);
        Assert.Equal(5, block.Column);
    }
}