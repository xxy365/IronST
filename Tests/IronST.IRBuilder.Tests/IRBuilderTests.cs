using IronST.IRBuilder.IR;
using Xunit;

// 用别名解决命名空间和类同名
using IRBuilderClass = IronST.IRBuilder.IR.IRBuilder;

namespace IronST.IRBuilder.Tests;

/// <summary>IR 构建测试：验证通过 API 构建 IR 对象模型的正确性</summary>
public class IRBuilderTests
{
    [Fact]
    public void 创建空模块()
    {
        var mod = new Module("test.ll");
        Assert.Equal("test.ll", mod.SourceFilename);
        Assert.Empty(mod.Functions);
        Assert.Empty(mod.GlobalVariables);
    }

    [Fact]
    public void 添加函数声明()
    {
        var mod = new Module();
        var ft = new FunctionType(IrType.Int32, new[] { IrType.Int32, IrType.Int32 });
        var func = new Function("add", ft);
        mod.AddFunction(func);

        Assert.Single(mod.Functions);
        Assert.True(func.IsDeclaration);
        Assert.Equal("add", func.Name);
        Assert.Equal(2, func.Parameters.Count);
    }

    [Fact]
    public void 添加函数定义_含基本块()
    {
        var mod = new Module();
        var ft = new FunctionType(IrType.Int32, new[] { IrType.Int32, IrType.Int32 });
        var func = new Function("add", ft, LinkageType.External);
        func.Parameters[0].Name = "a";
        func.Parameters[1].Name = "b";

        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);

        var addInst = BinaryOperator.CreateAdd(func.Parameters[0], func.Parameters[1], "sum");
        entry.AddInstruction(addInst);

        var ret = new ReturnInst(addInst);
        entry.AddInstruction(ret);

        mod.AddFunction(func);

        Assert.False(func.IsDeclaration);
        Assert.Single(func.BasicBlocks);
        Assert.Equal(2, func.BasicBlocks[0].Instructions.Count);
        Assert.True(func.BasicBlocks[0].Terminator is ReturnInst);
    }

    [Fact]
    public void 使用IRBuilder_构建指令()
    {
        var func = new Function("test", new FunctionType(IrType.Void, Array.Empty<IrType>()));
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);

        var builder = new IRBuilderClass(entry);
        var alloca = builder.CreateAlloca(IrType.Int32, n: "x");
        var val = builder.CreateLoad(IrType.Int32, alloca, n: "loaded");
        builder.CreateStore(val, alloca);
        builder.CreateRetVoid();

        Assert.Equal(4, entry.Instructions.Count);
    }

    [Fact]
    public void 全局变量_常量字符串()
    {
        var mod = new Module();
        var gv = mod.AddGlobalString(".str", "Hello");

        Assert.Single(mod.GlobalVariables);
        Assert.True(gv.IsConstant);
        Assert.Equal(LinkageType.Private, gv.Linkage);
        Assert.Equal(IrType.Pointer, gv.Type);
    }

    [Fact]
    public void 全局变量_带初始值()
    {
        var mod = new Module();
        var init = new ConstantInt(IrType.Int32, 42);
        var gv = new GlobalVariable("counter", IrType.Int32, false, LinkageType.Internal, init);
        gv.Alignment = 4;
        mod.AddGlobalVariable(gv);

        Assert.Single(mod.GlobalVariables);
        Assert.False(gv.IsConstant);
    }

    [Fact]
    public void 常量_各种类型()
    {
        var ci = new ConstantInt(IrType.Int32, 42);
        Assert.Equal(42L, ci.Value);

        var cf = new ConstantFP(IrType.Float, 3.14);
        Assert.InRange(cf.Value, 3.13, 3.15);

        var nullPtr = new ConstantPointerNull();
        Assert.Equal(IrType.Pointer, nullPtr.Type);

        var undef = UndefValue.Get(IrType.Int32);
        Assert.Equal(IrType.Int32, undef.Type);

        var poison = PoisonValue.Get(IrType.Int64);
        Assert.Equal(IrType.Int64, poison.Type);
    }

    [Fact]
    public void 多个基本块_条件分支()
    {
        var func = new Function("abs", new FunctionType(IrType.Int32, new[] { IrType.Int32 }));
        func.Parameters[0].Name = "x";

        var entry = new BasicBlock("entry");
        var thenBB = new BasicBlock("then");
        var endBB = new BasicBlock("end");

        func.AppendBasicBlock(entry);
        func.AppendBasicBlock(thenBB);
        func.AppendBasicBlock(endBB);

        var builder = new IRBuilderClass(entry);

        var cmp = builder.CreateICmp(ICmpPredicate.Slt, func.Parameters[0],
            new ConstantInt(IrType.Int32, 0), "cmp");
        builder.CreateCondBr(cmp, thenBB, endBB);

        builder.InsertBlock = thenBB;
        var neg = builder.CreateSub(new ConstantInt(IrType.Int32, 0), func.Parameters[0], "neg");
        builder.CreateBr(endBB);

        builder.InsertBlock = endBB;
        var phi = builder.CreatePhi(IrType.Int32, "result");
        phi.AddIncoming(func.Parameters[0], entry);
        phi.AddIncoming(neg, thenBB);
        builder.CreateRet(phi);

        Assert.Equal(3, func.BasicBlocks.Count);
        Assert.Equal(2, entry.Instructions.Count);
        Assert.Equal(2, thenBB.Instructions.Count);
        Assert.Equal(2, endBB.Instructions.Count);
    }

    [Fact]
    public void 命名结构体类型()
    {
        var mod = new Module();
        var body = new StructType(new IrType[] { IrType.Int32, IrType.Float });
        var named = mod.AddNamedType("Point", body);

        Assert.Single(mod.NamedTypes);
        Assert.Equal("Point", named.Name);
        Assert.Equal(body, named.Body);
    }

    [Fact]
    public void GetElementPtr_指令构建()
    {
        var func = new Function("test", new FunctionType(IrType.Pointer, Array.Empty<IrType>()));
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);

        var builder = new IRBuilderClass(entry);
        var alloca = builder.CreateAlloca(new ArrayType(IrType.Int32, 10), n: "arr");
        var idx0 = new ConstantInt(IrType.Int32, 0);
        var idx1 = new ConstantInt(IrType.Int32, 5);
        var gep = builder.CreateGEP(IrType.Int32, alloca, new Value[] { idx0, idx1 }, n: "ptr", inBounds: true);

        Assert.IsType<GetElementPtrInst>(gep);
        var gepInst = (GetElementPtrInst)gep;
        Assert.True(gepInst.InBounds);
        Assert.Equal(IrType.Int32, gepInst.SourceElementType);
    }
}