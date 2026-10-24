using IronST.IRBuilder.Emitter;
using IronST.IRBuilder.IR;
using Xunit;

namespace IronST.IRBuilder.Tests;

/// <summary>
/// Emitter 对 metadata 的输出测试
/// </summary>
public class MetadataEmitterTests
{
    private readonly LLEmitter _emitter = new();

    [Fact]
    public void Emit_空模块无Metadata段()
    {
        var mod = new Module("test.ll");
        var ir = _emitter.Emit(mod);

        Assert.DoesNotContain("!0", ir);
    }

    [Fact]
    public void Emit_MDString节点()
    {
        var mod = new Module();
        mod.Metadata.SetNode(0, new MDString("hello"));

        var ir = _emitter.Emit(mod);

        Assert.Contains(@"!0 = !""hello""", ir);
    }

    [Fact]
    public void Emit_MDTuple节点()
    {
        var mod = new Module();
        mod.Metadata.SetNode(0, new MDTuple(new object?[] { "i32", 42 }));

        var ir = _emitter.Emit(mod);

        Assert.Contains("!0 = !{", ir);
        Assert.Contains("42", ir);
    }

    [Fact]
    public void Emit_有名Metadata()
    {
        var mod = new Module();
        var tuple = new MDTuple(new object?[] { 0 });
        mod.Metadata.SetNamed("llvm.dbg.cu", tuple);

        var ir = _emitter.Emit(mod);

        Assert.Contains("!llvm.dbg.cu = !{", ir);
    }

    [Fact]
    public void Emit_编号Metadata按顺序输出()
    {
        var mod = new Module();
        mod.Metadata.SetNode(0, new MDString("first"));
        mod.Metadata.SetNode(1, new MDString("second"));
        mod.Metadata.SetNode(2, new MDString("third"));

        var ir = _emitter.Emit(mod);

        int pos0 = ir.IndexOf(@"!0 = !""first""");
        int pos1 = ir.IndexOf(@"!1 = !""second""");
        int pos2 = ir.IndexOf(@"!2 = !""third""");

        Assert.True(pos0 >= 0);
        Assert.True(pos1 > pos0);
        Assert.True(pos2 > pos1);
    }

    [Fact]
    public void Emit_指令附加Dbg()
    {
        var mod = new Module();
        var func = new Function("test", new FunctionType(IrType.Int32, new[] { IrType.Int32 }));
        func.Parameters[0].Name = "a";
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);

        var ret = new ReturnInst(func.Parameters[0]);
        var dbgLoc = new MDTuple();
        dbgLoc.Id = 3;
        mod.Metadata.SetNode(3, dbgLoc);
        ret.SetMetadata("dbg", dbgLoc);

        entry.AddInstruction(ret);
        mod.AddFunction(func);

        var ir = _emitter.Emit(mod);

        Assert.Contains("ret i32 %a, !dbg !3", ir);
    }

    [Fact]
    public void Emit_Add指令带Dbg()
    {
        var mod = new Module();
        var func = new Function("add", new FunctionType(IrType.Int32, new[] { IrType.Int32, IrType.Int32 }));
        func.Parameters[0].Name = "x";
        func.Parameters[1].Name = "y";
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);

        var add = BinaryOperator.CreateAdd(func.Parameters[0], func.Parameters[1], "sum");
        var dbgLoc = new MDTuple();
        dbgLoc.Id = 5;
        mod.Metadata.SetNode(5, dbgLoc);
        add.SetMetadata("dbg", dbgLoc);

        entry.AddInstruction(add);
        entry.AddInstruction(new ReturnInst(add));
        mod.AddFunction(func);

        var ir = _emitter.Emit(mod);

        Assert.Contains("add i32 %x, %y, !dbg !5", ir);
    }

    [Fact]
    public void Emit_指令无Dbg_不输出Metadata附加()
    {
        var mod = new Module();
        var func = new Function("test", new FunctionType(IrType.Int32, Array.Empty<IrType>()));
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);
        entry.AddInstruction(new ReturnInst(new ConstantInt(IrType.Int32, 0)));
        mod.AddFunction(func);

        var ir = _emitter.Emit(mod);

        Assert.DoesNotContain("!dbg", ir);
    }

    [Fact]
    public void Emit_MDTuple嵌套MetadataNode引用()
    {
        var mod = new Module();
        var inner = new MDString("inner");
        mod.Metadata.SetNode(0, inner);

        var outer = new MDTuple(new object?[] { inner });
        mod.Metadata.SetNode(1, outer);

        var ir = _emitter.Emit(mod);

        // outer 应引用 !0
        Assert.Contains("!1 = !{!0}", ir);
    }

    [Fact]
    public void Emit_函数定义后输出Metadata()
    {
        var mod = new Module();
        var func = new Function("f", new FunctionType(IrType.Void, Array.Empty<IrType>()));
        func.AppendBasicBlock(new BasicBlock("entry"));
        func.BasicBlocks[0].AddInstruction(new ReturnInst());
        mod.AddFunction(func);

        mod.Metadata.SetNode(0, new MDString("data"));

        var ir = _emitter.Emit(mod);

        int funcEnd = ir.LastIndexOf('}');
        int metaStart = ir.IndexOf("!0");
        Assert.True(metaStart > funcEnd);
    }
}