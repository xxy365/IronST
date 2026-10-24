using IronST.IRBuilder.IR;
using IronST.IRBuilder.Parser;
using Xunit;

namespace IronST.IRBuilder.Tests;

/// <summary>
/// Parser 对 metadata 的解析测试
/// </summary>
public class MetadataParserTests
{
    private readonly LLParser _parser = new();

    // ==================== metadata 定义解析 ====================

    [Fact]
    public void Parse_编号MetadataString()
    {
        var ll =
            @"!0 = !""hello""
define void @f() { entry: ret void }
";
        var mod = _parser.Parse(ll);
        Assert.NotNull(mod.Metadata.GetNode(0));
        Assert.IsType<MDString>(mod.Metadata.GetNode(0));
        Assert.Equal("hello", ((MDString)mod.Metadata.GetNode(0)!).Value);
    }

    [Fact]
    public void Parse_编号MetadataTuple()
    {
        var ll =
            @"!0 = !{i32 1, i32 2, i32 3}
define void @f() { entry: ret void }
";
        var mod = _parser.Parse(ll);
        var node = mod.Metadata.GetNode(0);
        Assert.NotNull(node);
        Assert.IsType<MDTuple>(node);
        var tuple = (MDTuple)node!;
        Assert.Equal(3, tuple.Operands.Count);
    }

    [Fact]
    public void Parse_有名Metadata()
    {
        var ll =
            @"!llvm.dbg.cu = !{!0}
!0 = !DICompileUnit(language: DW_LANG_C99, file: !1)
!1 = !DIFile(filename: ""test.c"", directory: ""/home"")
define void @f() { entry: ret void }
";
        var mod = _parser.Parse(ll);

        Assert.NotNull(mod.Metadata.GetNamed("llvm.dbg.cu"));
        Assert.NotNull(mod.Metadata.GetNode(0));
        Assert.NotNull(mod.Metadata.GetNode(1));
    }

    [Fact]
    public void Parse_DIFile节点()
    {
        var ll =
            @"!0 = !DIFile(filename: ""main.c"", directory: ""/src"")
define void @f() { entry: ret void }
";
        var mod = _parser.Parse(ll);
        var node = mod.Metadata.GetNode(0);
        Assert.NotNull(node);
        Assert.IsType<MDTuple>(node);
    }

    [Fact]
    public void Parse_DILocation节点()
    {
        var ll =
            @"
!0 = !DIFile(filename: ""test.c"", directory: ""/"")
!1 = !DICompileUnit(language: DW_LANG_C99, file: !0)
!2 = !DISubprogram(name: ""main"", scope: !1, file: !0, line: 1)
!3 = !DILocation(line: 5, column: 3, scope: !2)

define void @main() { entry: ret void }
";
        var mod = _parser.Parse(ll);

        Assert.NotNull(mod.Metadata.GetNode(3));
        var loc = mod.Metadata.GetNode(3);
        Assert.IsType<MDTuple>(loc);
    }

    [Fact]
    public void Parse_多个Metadata定义()
    {
        var ll =
            @"
!0 = !DIFile(filename: ""a.c"", directory: ""/"")
!1 = !DIFile(filename: ""b.c"", directory: ""/"")
!2 = !""some string""
!3 = !{!0, !1}

define void @f() { entry: ret void }
";
        var mod = _parser.Parse(ll);

        Assert.Equal(4, mod.Metadata.All.Count);
        Assert.NotNull(mod.Metadata.GetNode(0));
        Assert.NotNull(mod.Metadata.GetNode(1));
        Assert.NotNull(mod.Metadata.GetNode(2));
        Assert.NotNull(mod.Metadata.GetNode(3));
    }

    [Fact]
    public void Parse_Metadata定义在函数之前()
    {
        var ll =
            @"
!0 = !DIFile(filename: ""x.c"", directory: ""/"")

define i32 @main() {
entry:
  ret i32 0
}
";
        var mod = _parser.Parse(ll);

        Assert.NotNull(mod.Metadata.GetNode(0));
        Assert.Single(mod.Functions);
    }

    [Fact]
    public void Parse_Metadata定义在函数之后()
    {
        var ll =
            @"
define i32 @main() {
entry:
  ret i32 0
}

!0 = !DIFile(filename: ""x.c"", directory: ""/"")
";
        var mod = _parser.Parse(ll);

        Assert.Single(mod.Functions);
        Assert.NotNull(mod.Metadata.GetNode(0));
    }

    // ==================== 指令级 metadata 附加 ====================

    [Fact]
    public void Parse_指令附加DbgMetadata()
    {
        var ll =
            @"
!0 = !DIFile(filename: ""test.c"", directory: ""/"")
!1 = !DICompileUnit(language: DW_LANG_C99, file: !0)
!2 = !DISubprogram(name: ""main"", scope: !1, file: !0, line: 1)
!3 = !DILocation(line: 2, column: 5, scope: !2)

define i32 @main() {
entry:
  ret i32 0, !dbg !3
}
";
        var mod = _parser.Parse(ll);
        var retInst = mod.Functions[0].BasicBlocks[0].Instructions[0];

        Assert.NotNull(retInst.MetadataAttachments);
        Assert.True(retInst.MetadataAttachments!.ContainsKey("dbg"));
    }

    [Fact]
    public void Parse_指令无Metadata()
    {
        var ll =
            @"
define i32 @main() {
entry:
  ret i32 0
}
";
        var mod = _parser.Parse(ll);
        var retInst = mod.Functions[0].BasicBlocks[0].Instructions[0];

        Assert.Null(retInst.MetadataAttachments);
    }

    [Fact]
    public void Parse_Add指令带Dbg()
    {
        var ll =
            @"
!0 = !DILocation(line: 3, column: 10, scope: !1)
!1 = !DISubprogram(name: ""test"")

define i32 @test(i32 %a, i32 %b) {
entry:
  %sum = add i32 %a, %b, !dbg !0
  ret i32 %sum
}
";
        var mod = _parser.Parse(ll);
        var addInst = mod.Functions[0].BasicBlocks[0].Instructions[0];

        Assert.NotNull(addInst.MetadataAttachments);
        Assert.True(addInst.MetadataAttachments!.ContainsKey("dbg"));
    }

    [Fact]
    public void Parse_Store指令带Dbg()
    {
        var ll =
            @"
!0 = !DILocation(line: 4, column: 3, scope: !1)
!1 = !DISubprogram(name: ""test"")

define void @test(ptr %p) {
entry:
  store i32 42, ptr %p, align 4, !dbg !0
  ret void
}
";
        var mod = _parser.Parse(ll);
        var storeInst = mod.Functions[0].BasicBlocks[0].Instructions[0];

        Assert.NotNull(storeInst.MetadataAttachments);
        Assert.True(storeInst.MetadataAttachments!.ContainsKey("dbg"));
    }

    [Fact]
    public void Parse_Load指令带Dbg()
    {
        var ll =
            @"
!0 = !DILocation(line: 5, column: 7, scope: !1)
!1 = !DISubprogram(name: ""test"")

define i32 @test(ptr %p) {
entry:
  %v = load i32, ptr %p, align 4, !dbg !0
  ret i32 %v
}
";
        var mod = _parser.Parse(ll);
        var loadInst = mod.Functions[0].BasicBlocks[0].Instructions[0];

        Assert.NotNull(loadInst.MetadataAttachments);
        Assert.True(loadInst.MetadataAttachments!.ContainsKey("dbg"));
    }

    [Fact]
    public void Parse_Alloca指令带Dbg()
    {
        var ll =
            @"
!0 = !DILocation(line: 2, column: 3, scope: !1)
!1 = !DISubprogram(name: ""test"")

define void @test() {
entry:
  %x = alloca i32, align 4, !dbg !0
  ret void
}
";
        var mod = _parser.Parse(ll);
        var allocaInst = mod.Functions[0].BasicBlocks[0].Instructions[0];

        Assert.NotNull(allocaInst.MetadataAttachments);
        Assert.True(allocaInst.MetadataAttachments!.ContainsKey("dbg"));
    }

    [Fact]
    public void Parse_返回指令无Dbg_正常解析()
    {
        var ll =
            @"
define void @f() {
entry:
  ret void
}
";
        var mod = _parser.Parse(ll);
        var ret = mod.Functions[0].BasicBlocks[0].Instructions[0];
        Assert.Null(ret.MetadataAttachments);
    }

    // ==================== 复杂 debug 结构 ====================

    [Fact]
    public void Parse_完整的Debug结构()
    {
        var ll = """
!llvm.dbg.cu = !{!0}
!llvm.module.flags = !{!1, !2}

!0 = distinct !DICompileUnit(language: DW_LANG_C99, file: !3, producer: "clang version 18", isOptimized: false, emissionKind: FullDebug, subprograms: !{!4})
!1 = !{i32 2, !"Debug Info Version", i32 3}
!2 = !{i32 2, !"Dwarf Version", i32 4}
!3 = !DIFile(filename: "main.c", directory: "/home/user")
!4 = distinct !DISubprogram(name: "main", scope: !3, file: !3, line: 1, type: !5, scopeLine: 1, flags: DIFlagPrototyped, spFlags: DISPFlagDefinition, unit: !0)
!5 = !DISubroutineType(types: !{!6, !7})
!6 = !DIBasicType(name: "int", size: 32, encoding: DW_ATE_signed)
!7 = !DIDerivedType(tag: DW_TAG_pointer_type, baseType: !8, size: 64)
!8 = !DIBasicType(name: "char", size: 8, encoding: DW_ATE_signed_char)
!9 = !DILocation(line: 2, column: 3, scope: !4)
!10 = !DILocation(line: 3, column: 3, scope: !4)

define i32 @main() !dbg !4 {
entry:
  ret i32 0, !dbg !10
}
""";
        var mod = _parser.Parse(ll);

        // 验证有名 metadata
        Assert.NotNull(mod.Metadata.GetNamed("llvm.dbg.cu"));
        Assert.NotNull(mod.Metadata.GetNamed("llvm.module.flags"));

        // 验证编号 metadata 存在
        for (int i = 0; i <= 10; i++)
            Assert.NotNull(mod.Metadata.GetNode(i));

        // 验证函数级 debug
        var func = mod.Functions[0];
        Assert.Single(func.BasicBlocks);

        // 验证指令级 debug
        var retInst = func.BasicBlocks[0].Instructions[0];
        Assert.NotNull(retInst.MetadataAttachments);
        Assert.True(retInst.MetadataAttachments!.ContainsKey("dbg"));
    }

    [Fact]
    public void Parse_多个指令各自有不同的Dbg()
    {
        var ll =
            @"
!0 = !DILocation(line: 2, column: 5, scope: !99)
!1 = !DILocation(line: 3, column: 5, scope: !99)
!2 = !DILocation(line: 4, column: 5, scope: !99)
!99 = !DISubprogram(name: ""foo"")

define i32 @foo(i32 %a, i32 %b) {
entry:
  %sum = add i32 %a, %b, !dbg !0
  %prod = mul i32 %sum, %a, !dbg !1
  ret i32 %prod, !dbg !2
}
";
        var mod = _parser.Parse(ll);
        var bb = mod.Functions[0].BasicBlocks[0];

        // add 指令有 !dbg !0
        Assert.NotNull(bb.Instructions[0].MetadataAttachments);
        Assert.True(bb.Instructions[0].MetadataAttachments!.ContainsKey("dbg"));

        // mul 指令有 !dbg !1
        Assert.NotNull(bb.Instructions[1].MetadataAttachments);
        Assert.True(bb.Instructions[1].MetadataAttachments!.ContainsKey("dbg"));

        // ret 指令有 !dbg !2
        Assert.NotNull(bb.Instructions[2].MetadataAttachments);
        Assert.True(bb.Instructions[2].MetadataAttachments!.ContainsKey("dbg"));
    }
}
