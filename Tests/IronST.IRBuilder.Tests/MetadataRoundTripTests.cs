using IronST.IRBuilder.Emitter;
using IronST.IRBuilder.IR;
using IronST.IRBuilder.Parser;

namespace IronST.IRBuilder.Tests;

/// <summary>
/// Metadata 往返测试：构建/解析 → 发射 → 解析 → 再发射
/// </summary>
public class MetadataRoundTripTests
{
    private readonly LLEmitter _emitter = new();
    private readonly LLParser _parser = new();

    [Fact]
    public void 往返_无Metadata的函数()
    {
        var mod = new Module();
        var func = new Function("add", new FunctionType(IrType.Int32, new[] { IrType.Int32, IrType.Int32 }));
        func.Parameters[0].Name = "a";
        func.Parameters[1].Name = "b";
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);
        var add = BinaryOperator.CreateAdd(func.Parameters[0], func.Parameters[1], "sum");
        entry.AddInstruction(add);
        entry.AddInstruction(new ReturnInst(add));
        mod.AddFunction(func);

        var ll1 = _emitter.Emit(mod);
        var parsed = _parser.Parse(ll1);
        var ll2 = _emitter.Emit(parsed);

        Assert.DoesNotContain("!dbg", ll2);
        Assert.Contains("define i32 @add(i32 %a, i32 %b)", ll2);
    }

    [Fact]
    public void 往返_MDString定义()
    {
        var mod = new Module();
        mod.Metadata.SetNode(0, new MDString("hello"));

        var ll1 = _emitter.Emit(mod);
        var parsed = _parser.Parse(ll1);
        var ll2 = _emitter.Emit(parsed);

        Assert.Contains(@"!0 = !""hello""", ll2);
    }

    [Fact]
    public void 往返_有名Metadata()
    {
        var mod = new Module();
        mod.Metadata.SetNode(0, new MDTuple());
        mod.Metadata.SetNamed("llvm.dbg.cu", new MDTuple(new object?[] { 0 }));

        var ll1 = _emitter.Emit(mod);
        var parsed = _parser.Parse(ll1);
        var ll2 = _emitter.Emit(parsed);

        Assert.Contains("!llvm.dbg.cu", ll2);
    }

    [Fact]
    public void 往返_指令附加Dbg()
    {
        var mod = new Module();
        var func = new Function("f", new FunctionType(IrType.Int32, Array.Empty<IrType>()));
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);

        var dbgNode = new MDTuple();
        mod.Metadata.SetNode(0, dbgNode);

        var ret = new ReturnInst(new ConstantInt(IrType.Int32, 0));
        ret.SetMetadata("dbg", dbgNode);
        entry.AddInstruction(ret);
        mod.AddFunction(func);

        var ll1 = _emitter.Emit(mod);
        var parsed = _parser.Parse(ll1);
        var ll2 = _emitter.Emit(parsed);

        Assert.Contains("ret i32 0, !dbg !0", ll2);
    }

    [Fact]
    public void 往返_MDTuple包含整数引用()
    {
        var mod = new Module();
        var inner = new MDString("data");
        mod.Metadata.SetNode(0, inner);
        var outer = new MDTuple(new object?[] { inner });
        mod.Metadata.SetNode(1, outer);

        var ll1 = _emitter.Emit(mod);
        var parsed = _parser.Parse(ll1);
        var ll2 = _emitter.Emit(parsed);

        Assert.Contains("!0 = ", ll2);
        Assert.Contains("!1 = ", ll2);
    }

    [Fact]
    public void 往返_解析再发射结果包含相同关键结构()
    {
        var ll = @"
!llvm.dbg.cu = !{!0}
!0 = distinct !DICompileUnit(language: DW_LANG_C99, file: !1, producer: ""clang"", isOptimized: false)
!1 = !DIFile(filename: ""test.c"", directory: ""/home"")
!2 = !DISubprogram(name: ""main"", scope: !0, file: !1, line: 1)
!3 = !DILocation(line: 2, column: 3, scope: !2)

define i32 @main() !dbg !2 {
entry:
  ret i32 0, !dbg !3
}
";
        var parsed = _parser.Parse(ll);
        var emitted = _emitter.Emit(parsed);

        // 往返后核心结构保留
        Assert.Contains("define i32 @main()", emitted);
        Assert.Contains("ret i32 0", emitted);
        Assert.Contains("!0 = ", emitted);
        Assert.Contains("!1 = ", emitted);
        Assert.Contains("!2 = ", emitted);
        Assert.Contains("!3 = ", emitted);
    }
}