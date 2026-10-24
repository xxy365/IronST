using IronST.IRBuilder.IR;
using Xunit;

using IRBuilderClass = IronST.IRBuilder.IR.IRBuilder;

namespace IronST.IRBuilder.Tests;

public class IRBuilderTests2
{
    [Fact]
    public void CreateRetVoid()
    {
        var entry = new BasicBlock("entry");
        var builder = new IRBuilderClass(entry);
        var ret = builder.CreateRetVoid();

        Assert.IsType<ReturnInst>(ret);
        Assert.Null(ret.ReturnValue);
        Assert.True(ret.IsTerminator);
    }

    [Fact]
    public void CreateBr_无条件跳转()
    {
        var func = new Function("f", new FunctionType(IrType.Void, Array.Empty<IrType>()));
        var entry = new BasicBlock("entry");
        var dest = new BasicBlock("dest");
        func.AppendBasicBlock(entry);
        func.AppendBasicBlock(dest);

        var builder = new IRBuilderClass(entry);
        var br = builder.CreateBr(dest);

        Assert.IsType<BranchInst>(br);
        Assert.False(br.IsConditional);
        Assert.Equal(dest, br.TrueDest);
    }

    [Fact]
    public void CreateUnreachable()
    {
        var entry = new BasicBlock("entry");
        var builder = new IRBuilderClass(entry);
        var u = builder.CreateUnreachable();

        Assert.IsType<UnreachableInst>(u);
        Assert.True(u.IsTerminator);
    }

    [Fact]
    public void CreateMul()
    {
        var func = new Function("f", new FunctionType(IrType.Int32, new[] { IrType.Int32, IrType.Int32 }));
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);
        var builder = new IRBuilderClass(entry);

        var mul = builder.CreateMul(func.Parameters[0], func.Parameters[1], "r");
        Assert.IsType<BinaryOperator>(mul);
        Assert.Equal(BinaryOp.Mul, ((BinaryOperator)mul).Op);
    }

    [Fact]
    public void CreateFAdd()
    {
        var func = new Function("f", new FunctionType(IrType.Float, new[] { IrType.Float, IrType.Float }));
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);
        var builder = new IRBuilderClass(entry);

        var r = builder.CreateFAdd(func.Parameters[0], func.Parameters[1], "r");
        Assert.IsType<BinaryOperator>(r);
        Assert.Equal(BinaryOp.FAdd, ((BinaryOperator)r).Op);
    }

    [Fact]
    public void CreateFSub_FMul()
    {
        var func = new Function("f", new FunctionType(IrType.Double, new[] { IrType.Double, IrType.Double }));
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);
        var builder = new IRBuilderClass(entry);

        var sub = builder.CreateFSub(func.Parameters[0], func.Parameters[1]);
        var mul = builder.CreateFMul(func.Parameters[0], func.Parameters[1]);

        Assert.Equal(BinaryOp.FSub, ((BinaryOperator)sub).Op);
        Assert.Equal(BinaryOp.FMul, ((BinaryOperator)mul).Op);
    }

    [Fact]
    public void CreateSDiv()
    {
        var func = new Function("f", new FunctionType(IrType.Int32, new[] { IrType.Int32, IrType.Int32 }));
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);
        var builder = new IRBuilderClass(entry);

        var r = builder.CreateSDiv(func.Parameters[0], func.Parameters[1]);
        Assert.Equal(BinaryOp.SDiv, ((BinaryOperator)r).Op);
    }

    [Fact]
    public void CreateAnd_Or_Xor()
    {
        var func = new Function("f", new FunctionType(IrType.Int32, new[] { IrType.Int32, IrType.Int32 }));
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);
        var builder = new IRBuilderClass(entry);

        var and = builder.CreateAnd(func.Parameters[0], func.Parameters[1]);
        var or = builder.CreateOr(func.Parameters[0], func.Parameters[1]);
        var xor = builder.CreateXor(func.Parameters[0], func.Parameters[1]);

        Assert.Equal(BinaryOp.And, ((BinaryOperator)and).Op);
        Assert.Equal(BinaryOp.Or, ((BinaryOperator)or).Op);
        Assert.Equal(BinaryOp.Xor, ((BinaryOperator)xor).Op);
    }

    [Fact]
    public void CreateFCmp()
    {
        var func = new Function("f", new FunctionType(IrType.Int1, new[] { IrType.Float, IrType.Float }));
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);
        var builder = new IRBuilderClass(entry);

        var cmp = builder.CreateFCmp(FCmpPredicate.Ogt, func.Parameters[0], func.Parameters[1], "r");
        Assert.IsType<FCmpInst>(cmp);
        Assert.Equal(IrType.Int1, cmp.Type);
        Assert.Equal(FCmpPredicate.Ogt, ((FCmpInst)cmp).Predicate);
    }

    [Fact]
    public void CreateCall()
    {
        var func = new Function("caller", new FunctionType(IrType.Int32, new[] { IrType.Int32 }));
        func.Parameters[0].Name = "a";
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);

        var calleeFt = new FunctionType(IrType.Int32, new[] { IrType.Int32 });
        var callee = new Function("callee", calleeFt);

        var builder = new IRBuilderClass(entry);
        var call = builder.CreateCall(calleeFt, callee, new Value[] { func.Parameters[0] }, "r");

        Assert.IsType<CallInst>(call);
        Assert.Equal(IrType.Int32, call.Type);
        Assert.Equal(1, ((CallInst)call).ArgumentCount);
    }

    [Fact]
    public void CreateSelect()
    {
        var func = new Function("f", new FunctionType(IrType.Int32, new[] { IrType.Int1 }));
        func.Parameters[0].Name = "c";
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);
        var builder = new IRBuilderClass(entry);

        var sel = builder.CreateSelect(func.Parameters[0],
            new ConstantInt(IrType.Int32, 1),
            new ConstantInt(IrType.Int32, 0), "r");

        Assert.IsType<SelectInst>(sel);
        Assert.Equal(IrType.Int32, sel.Type);
    }

    [Fact]
    public void CreateZExt_SExt_Trunc()
    {
        var func = new Function("f", new FunctionType(IrType.Int64, new[] { IrType.Int32 }));
        func.Parameters[0].Name = "v";
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);
        var builder = new IRBuilderClass(entry);

        var zext = builder.CreateZExt(func.Parameters[0], IrType.Int64);
        Assert.Equal(IrType.Int64, zext.Type);
        Assert.Equal(CastOp.ZExt, ((CastInst)zext).Op);

        var sext = builder.CreateSExt(func.Parameters[0], IrType.Int64);
        Assert.Equal(CastOp.SExt, ((CastInst)sext).Op);
    }

    [Fact]
    public void CreateBitCast()
    {
        var func = new Function("f", new FunctionType(IrType.Int64, new[] { IrType.Double }));
        func.Parameters[0].Name = "v";
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);
        var builder = new IRBuilderClass(entry);

        var cast = builder.CreateBitCast(func.Parameters[0], IrType.Int64);
        Assert.Equal(CastOp.BitCast, ((CastInst)cast).Op);
        Assert.Equal(IrType.Int64, cast.Type);
    }

    [Fact]
    public void CreatePtrToInt_IntToPtr()
    {
        var func = new Function("f", new FunctionType(IrType.Int64, new[] { IrType.Pointer }));
        func.Parameters[0].Name = "p";
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);
        var builder = new IRBuilderClass(entry);

        var p2i = builder.CreatePtrToInt(func.Parameters[0], IrType.Int64);
        Assert.Equal(CastOp.PtrToInt, ((CastInst)p2i).Op);

        var i2p = builder.CreateIntToPtr(func.Parameters[0], IrType.Pointer);
        Assert.Equal(CastOp.IntToPtr, ((CastInst)i2p).Op);
    }
}