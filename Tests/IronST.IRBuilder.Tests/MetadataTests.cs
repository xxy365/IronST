using IronST.IRBuilder.IR;
using Xunit;

namespace IronST.IRBuilder.Tests;

/// <summary>
/// MetadataNode / MDString / MDTuple / MetadataTable 基础测试
/// </summary>
public class MetadataTests
{
    // ==================== MDString ====================

    [Fact]
    public void MDString_存储和读取值()
    {
        var s = new MDString("hello");
        Assert.Equal("hello", s.Value);
    }

    [Fact]
    public void MDString_空字符串()
    {
        var s = new MDString("");
        Assert.Equal("", s.Value);
    }

    [Fact]
    public void MDString_特殊字符()
    {
        var s = new MDString("a\"b\\c\nd");
        Assert.Equal("a\"b\\c\nd", s.Value);
    }

    // ==================== MDTuple ====================

    [Fact]
    public void MDTuple_空元组()
    {
        var t = new MDTuple();
        Assert.Empty(t.Operands);
    }

    [Fact]
    public void MDTuple_带操作数()
    {
        var t = new MDTuple(new object?[] { "key", 42, new MDString("val") });
        Assert.Equal(3, t.Operands.Count);
        Assert.Equal("key", t.Operands[0]);
        Assert.Equal(42, t.Operands[1]);
        Assert.IsType<MDString>(t.Operands[2]);
    }

    [Fact]
    public void MDTuple_嵌套元组()
    {
        var inner = new MDTuple(new object?[] { 1, 2 });
        var outer = new MDTuple(new object?[] { inner });
        Assert.Single(outer.Operands);
        Assert.IsType<MDTuple>(outer.Operands[0]);
    }

    // ==================== MetadataNode Id ====================

    [Fact]
    public void MetadataNode_默认Id为负一()
    {
        var n = new MDTuple();
        Assert.Equal(-1, n.Id);
    }

    [Fact]
    public void MetadataNode_可设置Id()
    {
        var n = new MDTuple();
        n.Id = 5;
        Assert.Equal(5, n.Id);
    }

    // ==================== MetadataTable ====================

    [Fact]
    public void MetadataTable_存取编号节点()
    {
        var table = new MetadataTable();
        var node = new MDString("test");

        table.SetNode(0, node);

        Assert.Same(node, table.GetNode(0));
        Assert.Null(table.GetNode(1));
    }

    [Fact]
    public void MetadataTable_SetNode自动设置Id()
    {
        var table = new MetadataTable();
        var node = new MDTuple();

        Assert.Equal(-1, node.Id);
        table.SetNode(7, node);
        Assert.Equal(7, node.Id);
    }

    [Fact]
    public void MetadataTable_覆盖同编号节点()
    {
        var table = new MetadataTable();
        var first = new MDString("first");
        var second = new MDString("second");

        table.SetNode(0, first);
        table.SetNode(0, second);

        Assert.Same(second, table.GetNode(0));
    }

    [Fact]
    public void MetadataTable_存取有名节点()
    {
        var table = new MetadataTable();
        var node = new MDTuple();

        table.SetNamed("llvm.dbg.cu", node);

        Assert.Same(node, table.GetNamed("llvm.dbg.cu"));
        Assert.Null(table.GetNamed("llvm.dbg.foo"));
    }

    [Fact]
    public void MetadataTable_All遍历()
    {
        var table = new MetadataTable();
        table.SetNode(0, new MDString("a"));
        table.SetNode(1, new MDString("b"));

        Assert.Equal(2, table.All.Count);
        Assert.Contains(0, table.All.Keys);
        Assert.Contains(1, table.All.Keys);
    }

    [Fact]
    public void MetadataTable_AllNamed遍历()
    {
        var table = new MetadataTable();
        table.SetNamed("foo", new MDTuple());
        table.SetNamed("bar", new MDTuple());

        Assert.Equal(2, table.AllNamed.Count);
    }

    [Fact]
    public void MetadataTable_空表查询返回null()
    {
        var table = new MetadataTable();
        Assert.Null(table.GetNode(0));
        Assert.Null(table.GetNamed("anything"));
        Assert.Empty(table.All);
        Assert.Empty(table.AllNamed);
    }
}