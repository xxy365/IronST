using IronST.IRBuilder.Emitter;
using IronST.IRBuilder.IR;
using IronST.IRBuilder.Parser;
using Xunit;

namespace IronST.IRBuilder.Tests;

public class RoundTripTests2
{
    private readonly LLEmitter _emitter = new();
    private readonly LLParser _parser = new();

    [Fact]
    public void 多基本块_Phi往返()
    {
        var mod = new Module();
        var func = new Function("abs", new FunctionType(IrType.Int32, new[] { IrType.Int32 }));
        func.Parameters[0].Name = "x";

        var entry = new BasicBlock("entry");
        var negBB = new BasicBlock("neg");
        var endBB = new BasicBlock("end");

        func.AppendBasicBlock(entry);
        func.AppendBasicBlock(negBB);
        func.AppendBasicBlock(endBB);

        var cmp = new ICmpInst(ICmpPredicate.Slt, func.Parameters[0],
            new ConstantInt(IrType.Int32, 0), "cmp");
        entry.AddInstruction(cmp);
        entry.AddInstruction(new BranchInst(cmp, negBB, endBB));

        var neg = BinaryOperator.CreateSub(new ConstantInt(IrType.Int32, 0), func.Parameters[0], "neg_val");
        negBB.AddInstruction(neg);
        negBB.AddInstruction(new BranchInst(endBB));

        var phi = new PHINode(IrType.Int32, "result");
        phi.AddIncoming(func.Parameters[0], entry);
        phi.AddIncoming(neg, negBB);
        endBB.AddInstruction(phi);
        endBB.AddInstruction(new ReturnInst(phi));

        mod.AddFunction(func);

        var ll1 = _emitter.Emit(mod);
        var parsed = _parser.Parse(ll1);
        var ll2 = _emitter.Emit(parsed);

        Assert.Contains("phi i32", ll2);
        Assert.Contains("%entry", ll2);
        Assert.Contains("%neg", ll2);
        Assert.Contains("br i1", ll2);
    }

    [Fact]
    public void 函数声明往返()
    {
        var mod = new Module();
        var ft = new FunctionType(IrType.Int32, new[] { IrType.Pointer }, isVarArg: true);
        mod.AddFunction(new Function("printf", ft));

        var ll1 = _emitter.Emit(mod);
        var parsed = _parser.Parse(ll1);
        var ll2 = _emitter.Emit(parsed);

        Assert.Contains("declare i32 @printf(ptr, ...)", ll2);
    }

    [Fact]
    public void 命名结构体往返()
    {
        var mod = new Module();
        var body = new StructType(new IrType[] { IrType.Int32, IrType.Float });
        mod.AddNamedType("Point", body);

        var ll1 = _emitter.Emit(mod);
        var parsed = _parser.Parse(ll1);
        var ll2 = _emitter.Emit(parsed);

        Assert.Contains("%Point = type { i32, float }", ll2);
    }

    [Fact]
    public void 全局变量往返()
    {
        var mod = new Module();
        var init = new ConstantInt(IrType.Int32, 42);
        var gv = new GlobalVariable("counter", IrType.Int32, false, LinkageType.Internal, init)
            { Alignment = 4 };
        mod.AddGlobalVariable(gv);

        var ll1 = _emitter.Emit(mod);
        var parsed = _parser.Parse(ll1);
        var ll2 = _emitter.Emit(parsed);

        Assert.Contains("@counter = internal global i32 42, align 4", ll2);
    }

    [Fact]
    public void 全局字符串往返()
    {
        var mod = new Module();
        mod.AddGlobalString(".str", "Hello!");

        var ll1 = _emitter.Emit(mod);
        var parsed = _parser.Parse(ll1);
        var ll2 = _emitter.Emit(parsed);

        Assert.Contains("@.str", ll2);
        Assert.Contains("constant", ll2);
    }

    [Fact]
    public void 类型转换往返()
    {
        var mod = new Module();
        var func = new Function("ext", new FunctionType(IrType.Int64, new[] { IrType.Int32 }));
        func.Parameters[0].Name = "v";
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);

        var sext = CastInst.CreateSExt(func.Parameters[0], IrType.Int64, "extended");
        entry.AddInstruction(sext);
        entry.AddInstruction(new ReturnInst(sext));
        mod.AddFunction(func);

        var ll1 = _emitter.Emit(mod);
        var parsed = _parser.Parse(ll1);
        var ll2 = _emitter.Emit(parsed);

        Assert.Contains("sext i32 %v to i64", ll2);
    }

    [Fact]
    public void 多函数往返()
    {
        var mod = new Module();
        var declFt = new FunctionType(IrType.Int32, new[] { IrType.Int32 });
        mod.AddFunction(new Function("callee", declFt));

        var func = new Function("caller", new FunctionType(IrType.Int32, new[] { IrType.Int32 }));
        func.Parameters[0].Name = "a";
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);

        var call = new CallInst(declFt, mod.Functions[0],
            new Value[] { func.Parameters[0] }, "r");
        entry.AddInstruction(call);
        entry.AddInstruction(new ReturnInst(call));
        mod.AddFunction(func);

        var ll1 = _emitter.Emit(mod);
        var parsed = _parser.Parse(ll1);
        var ll2 = _emitter.Emit(parsed);

        Assert.Contains("declare i32 @callee(i32)", ll2);
        Assert.Contains("define i32 @caller", ll2);
        Assert.Contains("call i32 @callee(i32 %a)", ll2);
    }
}