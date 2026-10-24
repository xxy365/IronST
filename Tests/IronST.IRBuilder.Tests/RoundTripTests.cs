using IronST.IRBuilder.Emitter;
using IronST.IRBuilder.IR;
using IronST.IRBuilder.Parser;
using Xunit;

namespace IronST.IRBuilder.Tests;

/// <summary>往返测试：API 构建 → 发射 → 解析 → 再发射，验证一致性</summary>
public class RoundTripTests
{
    [Fact]
    public void 构建_发射_解析_发射_结果一致()
    {
        // 1. 用 API 构建
        var mod = BuildTestModule();
        var emitter = new LLEmitter();
        var ll1 = emitter.Emit(mod);

        // 2. 解析
        var parser = new LLParser();
        var parsed = parser.Parse(ll1);

        // 3. 再次发射
        var ll2 = emitter.Emit(parsed);

        // 4. 两次输出应包含相同的关键结构
        Assert.Contains("define i32 @add(i32 %a, i32 %b)", ll2);
        Assert.Contains("ret i32 %sum", ll2);
    }

    [Fact]
    public void 构建_发射_解析_验证对象模型()
    {
        var mod = BuildTestModule();
        var emitter = new LLEmitter();
        var ll = emitter.Emit(mod);

        var parser = new LLParser();
        var parsed = parser.Parse(ll);

        Assert.Single(parsed.Functions);
        var func = parsed.Functions[0];
        Assert.Equal("add", func.Name);
        Assert.Equal(IrType.Int32, func.FunctionType.ReturnType);
        Assert.Equal(2, func.Parameters.Count);

        var bb = func.BasicBlocks[0];
        Assert.Equal("entry", bb.Name);
        Assert.True(bb.Instructions.Count >= 2);
        Assert.IsType<ReturnInst>(bb.Instructions[^1]);
    }

    private static Module BuildTestModule()
    {
        var mod = new Module("roundtrip.ll");
        mod.TargetTriple = "x86_64-unknown-linux-gnu";

        var ft = new FunctionType(IrType.Int32, new[] { IrType.Int32, IrType.Int32 });
        var func = new Function("add", ft);
        func.Parameters[0].Name = "a";
        func.Parameters[1].Name = "b";

        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);

        var sum = BinaryOperator.CreateAdd(func.Parameters[0], func.Parameters[1], "sum");
        entry.AddInstruction(sum);
        entry.AddInstruction(new ReturnInst(sum));

        mod.AddFunction(func);
        return mod;
    }
}