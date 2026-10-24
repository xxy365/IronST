using IronST.IRBuilder.IR;
using Xunit;

namespace IronST.IRBuilder.Tests;

/// <summary>
/// Instruction 的 MetadataAttachments 功能测试
/// </summary>
public class MetadataInstructionTests
{
    [Fact]
    public void SetMetadata_首次调用创建字典()
    {
        var inst = new UnreachableInst();
        Assert.Null(inst.MetadataAttachments);

        var node = new MDTuple();
        inst.SetMetadata("dbg", node);

        Assert.NotNull(inst.MetadataAttachments);
        Assert.Single(inst.MetadataAttachments!);
        Assert.Same(node, inst.MetadataAttachments["dbg"]);
    }

    [Fact]
    public void SetMetadata_多个Kind()
    {
        var inst = new UnreachableInst();
        var dbg = new MDTuple();
        var tbaa = new MDTuple();

        inst.SetMetadata("dbg", dbg);
        inst.SetMetadata("tbaa", tbaa);

        Assert.Equal(2, inst.MetadataAttachments!.Count);
        Assert.Same(dbg, inst.MetadataAttachments["dbg"]);
        Assert.Same(tbaa, inst.MetadataAttachments["tbaa"]);
    }

    [Fact]
    public void SetMetadata_覆盖同Kind()
    {
        var inst = new UnreachableInst();
        var first = new MDTuple();
        var second = new MDTuple();

        inst.SetMetadata("dbg", first);
        inst.SetMetadata("dbg", second);

        Assert.Single(inst.MetadataAttachments!);
        Assert.Same(second, inst.MetadataAttachments!["dbg"]);
    }

    [Fact]
    public void SetMetadata_可用于ReturnInst()
    {
        var inst = new ReturnInst();
        var node = new DILocation { Line = 10, Column = 5 };
        inst.SetMetadata("dbg", node);

        Assert.NotNull(inst.MetadataAttachments);
        Assert.IsType<DILocation>(inst.MetadataAttachments!["dbg"]);
        Assert.Equal(10, ((DILocation)inst.MetadataAttachments["dbg"]).Line);
    }

    [Fact]
    public void SetMetadata_可用于BinaryOperator()
    {
        var func = new Function("f", new FunctionType(IrType.Int32, new[] { IrType.Int32, IrType.Int32 }));
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);

        var add = BinaryOperator.CreateAdd(func.Parameters[0], func.Parameters[1], "s");
        var node = new DILocation { Line = 3, Column = 12 };
        add.SetMetadata("dbg", node);

        Assert.NotNull(add.MetadataAttachments);
        Assert.Same(node, add.MetadataAttachments!["dbg"]);
    }

    [Fact]
    public void SetMetadata_不影响指令类型和操作数()
    {
        var func = new Function("f", new FunctionType(IrType.Int32, new[] { IrType.Int32 }));
        func.Parameters[0].Name = "a";
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);

        var ret = new ReturnInst(func.Parameters[0]);
        ret.SetMetadata("dbg", new MDTuple());

        Assert.True(ret.IsTerminator);
        Assert.NotNull(ret.ReturnValue);
        Assert.Equal("a", ret.ReturnValue!.Name);
    }

    [Fact]
    public void SetMetadata_MetadataNode的Id正确()
    {
        var table = new MetadataTable();
        var node = new MDTuple();
        table.SetNode(42, node);

        Assert.Equal(42, node.Id);

        var inst = new ReturnInst();
        inst.SetMetadata("dbg", node);

        Assert.Equal(42, inst.MetadataAttachments!["dbg"].Id);
    }
}