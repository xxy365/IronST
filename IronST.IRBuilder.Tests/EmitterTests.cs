using IronST.IRBuilder.Emitter;
using IronST.IRBuilder.IR;
using Xunit;

namespace IronST.IRBuilder.Tests;

/// <summary>Emitter 测试：验证 .ll 文本输出符合 LLVM 22 规范</summary>
public class EmitterTests
{
    private readonly LLEmitter _emitter = new();

    [Fact]
    public void Emit_空模块()
    {
        var mod = new Module("test.ll");
        mod.TargetTriple = "x86_64-unknown-linux-gnu";
        var ir = _emitter.Emit(mod);

        Assert.Contains("source_filename = \"test.ll\"", ir);
        Assert.Contains("target triple = \"x86_64-unknown-linux-gnu\"", ir);
    }

    [Fact]
    public void Emit_函数声明()
    {
        var mod = new Module();
        var ft = new FunctionType(IrType.Int32, new[] { IrType.Pointer }, isVarArg: true);
        mod.AddFunction(new Function("printf", ft));

        var ir = _emitter.Emit(mod);
        Assert.Contains("declare i32 @printf(ptr, ...)", ir);
    }

    [Fact]
    public void Emit_简单函数定义()
    {
        var mod = new Module();
        var ft = new FunctionType(IrType.Int32, new[] { IrType.Int32, IrType.Int32 });
        var func = new Function("add", ft);
        func.Parameters[0].Name = "a";
        func.Parameters[1].Name = "b";

        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);

        var addInst = BinaryOperator.CreateAdd(func.Parameters[0], func.Parameters[1], "sum");
        entry.AddInstruction(addInst);
        entry.AddInstruction(new ReturnInst(addInst));

        mod.AddFunction(func);
        var ir = _emitter.Emit(mod);

        Assert.Contains("define i32 @add(i32 %a, i32 %b)", ir);
        Assert.Contains("%sum = add i32 %a, %b", ir);
        Assert.Contains("ret i32 %sum", ir);
    }

    [Fact]
    public void Emit_OpaquePointer_不出i32星号()
    {
        var mod = new Module();
        var ft = new FunctionType(IrType.Int32, new[] { IrType.Pointer });
        var func = new Function("test", ft);
        func.Parameters[0].Name = "ptr";
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);

        var load = new LoadInst(IrType.Int32, func.Parameters[0], "val");
        entry.AddInstruction(load);
        entry.AddInstruction(new ReturnInst(load));

        mod.AddFunction(func);
        var ir = _emitter.Emit(mod);

        // LLVM 22 opaque pointer: 不能出现 i32* 或 i8* 等
        Assert.DoesNotContain("i32*", ir);
        Assert.DoesNotContain("i8*", ir);
        Assert.Contains("load i32, ptr %ptr", ir);
    }

    [Fact]
    public void Emit_全局变量()
    {
        var mod = new Module();
        var init = new ConstantInt(IrType.Int32, 0);
        var gv = new GlobalVariable("g", IrType.Int32, false, LinkageType.Internal, init);
        gv.Alignment = 4;
        mod.AddGlobalVariable(gv);

        var ir = _emitter.Emit(mod);
        Assert.Contains("@g = internal global i32 0, align 4", ir);
    }

    [Fact]
    public void Emit_全局常量字符串()
    {
        var mod = new Module();
        mod.AddGlobalString(".str", "Hello!");

        var ir = _emitter.Emit(mod);
        // 常量字符串应包含长度和内容
        Assert.Contains("@.str", ir);
        Assert.Contains("constant", ir);
        Assert.Contains("[7 x i8]", ir); // "Hello!" + null = 7 bytes
    }

    [Fact]
    public void Emit_条件分支和Phi()
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

        // entry:
        var cmp = new ICmpInst(ICmpPredicate.Slt, func.Parameters[0],
            new ConstantInt(IrType.Int32, 0), "cmp");
        entry.AddInstruction(cmp);
        entry.AddInstruction(new BranchInst(cmp, negBB, endBB));

        // neg:
        var neg = BinaryOperator.CreateSub(new ConstantInt(IrType.Int32, 0),
            func.Parameters[0], "neg_val");
        negBB.AddInstruction(neg);
        negBB.AddInstruction(new BranchInst(endBB));

        // end:
        var phi = new PHINode(IrType.Int32, "result");
        phi.AddIncoming(func.Parameters[0], entry);
        phi.AddIncoming(neg, negBB);
        endBB.AddInstruction(phi);
        endBB.AddInstruction(new ReturnInst(phi));

        mod.AddFunction(func);
        var ir = _emitter.Emit(mod);

        Assert.Contains("br i1 %cmp, label %neg, label %end", ir);
        Assert.Contains("phi i32 [ %x, %entry ], [ %neg_val, %neg ]", ir);
    }

    [Fact]
    public void Emit_类型转换()
    {
        var mod = new Module();
        var func = new Function("cast_test", new FunctionType(IrType.Int64, new[] { IrType.Int32 }));
        func.Parameters[0].Name = "v";
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);

        var sext = CastInst.CreateSExt(func.Parameters[0], IrType.Int64, "extended");
        entry.AddInstruction(sext);
        entry.AddInstruction(new ReturnInst(sext));

        mod.AddFunction(func);
        var ir = _emitter.Emit(mod);

        Assert.Contains("sext i32 %v to i64", ir);
    }

    [Fact]
    public void Emit_GetElementPtr()
    {
        var mod = new Module();
        var func = new Function("gep_test", new FunctionType(IrType.Pointer, Array.Empty<IrType>()));
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);

        var alloca = new AllocaInst(IrType.Int32, null, "addr");
        entry.AddInstruction(alloca);
        var gep = new GetElementPtrInst(IrType.Int32, alloca,
            new Value[] { new ConstantInt(IrType.Int32, 0), new ConstantInt(IrType.Int32, 3) },
            "ptr", inBounds: true);
        entry.AddInstruction(gep);
        entry.AddInstruction(new ReturnInst(gep));

        mod.AddFunction(func);
        var ir = _emitter.Emit(mod);

        Assert.Contains("getelementptr inbounds i32, ptr %addr, i32 0, i32 3", ir);
    }

    [Fact]
    public void Emit_Store和Load()
    {
        var mod = new Module();
        var func = new Function("mem_test", new FunctionType(IrType.Void, Array.Empty<IrType>()));
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);

        var alloca = new AllocaInst(IrType.Int32, null, "x", alignment: 4);
        entry.AddInstruction(alloca);
        var store = new StoreInst(new ConstantInt(IrType.Int32, 42), alloca, alignment: 4);
        entry.AddInstruction(store);
        var load = new LoadInst(IrType.Int32, alloca, "val", alignment: 4);
        entry.AddInstruction(load);
        entry.AddInstruction(new ReturnInst());

        mod.AddFunction(func);
        var ir = _emitter.Emit(mod);

        Assert.Contains("alloca i32, align 4", ir);
        Assert.Contains("store i32 42, ptr %x, align 4", ir);
        Assert.Contains("load i32, ptr %x, align 4", ir);
    }

    [Fact]
    public void Emit_Call指令()
    {
        var mod = new Module();
        // 声明 callee
        var calleeFt = new FunctionType(IrType.Int32, new[] { IrType.Int32 });
        mod.AddFunction(new Function("callee", calleeFt));

        // 调用者
        var func = new Function("caller", new FunctionType(IrType.Int32, new[] { IrType.Int32 }));
        func.Parameters[0].Name = "arg";
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);

        var calleeFunc = mod.Functions[0]; // @callee
        var call = new CallInst(calleeFt, calleeFunc, new[] { func.Parameters[0] }, "r");
        entry.AddInstruction(call);
        entry.AddInstruction(new ReturnInst(call));

        mod.AddFunction(func);
        var ir = _emitter.Emit(mod);

        Assert.Contains("declare i32 @callee(i32)", ir);
        Assert.Contains("%r = call i32 @callee(i32 %arg)", ir);
    }

    [Fact]
    public void Emit_Switch指令()
    {
        var mod = new Module();
        var func = new Function("sw", new FunctionType(IrType.Void, new[] { IrType.Int32 }));
        func.Parameters[0].Name = "v";

        var def = new BasicBlock("default");
        var c1 = new BasicBlock("case1");
        var c2 = new BasicBlock("case2");

        func.AppendBasicBlock(def);
        func.AppendBasicBlock(c1);
        func.AppendBasicBlock(c2);

        var sw = new SwitchInst(func.Parameters[0], def);
        sw.AddCase(new ConstantInt(IrType.Int32, 1), c1);
        sw.AddCase(new ConstantInt(IrType.Int32, 2), c2);

        def.AddInstruction(new ReturnInst());
        c1.AddInstruction(new ReturnInst());
        c2.AddInstruction(new ReturnInst());

        // 先在入口块中放 switch
        var entry = new BasicBlock("entry");
        func.BasicBlocks.Insert(0, entry);
        entry.AddInstruction(sw);

        mod.AddFunction(func);
        var ir = _emitter.Emit(mod);

        Assert.Contains("switch i32 %v, label %default [", ir);
        Assert.Contains("i32 1, label %case1", ir);
        Assert.Contains("i32 2, label %case2", ir);
    }

    [Fact]
    public void Emit_命名结构体类型()
    {
        var mod = new Module();
        var body = new StructType(new IrType[] { IrType.Int32, IrType.Float });
        mod.AddNamedType("Point", body);

        var ir = _emitter.Emit(mod);
        Assert.Contains("%Point = type { i32, float }", ir);
    }

    [Fact]
    public void Emit_ExtractValue_InsertValue()
    {
        var mod = new Module();
        var retTy = new StructType(new IrType[] { IrType.Int32, IrType.Float });
        var func = new Function("ev_test", new FunctionType(retTy, Array.Empty<IrType>()));
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);

        var agg = new UndefValue(retTy);
        var insertVal = new InsertValueInst(agg, new ConstantInt(IrType.Int32, 7),
            new[] { 0 }, "with_val");
        entry.AddInstruction(insertVal);

        var extract = new ExtractValueInst(insertVal, new[] { 0 }, "extracted");
        entry.AddInstruction(extract);

        var cast = CastInst.CreateBitCast(extract, IrType.Int32);
        entry.AddInstruction(cast);
        entry.AddInstruction(new ReturnInst(insertVal));

        mod.AddFunction(func);
        var ir = _emitter.Emit(mod);

        Assert.Contains("insertvalue { i32, float } undef, i32 7, 0", ir);
        Assert.Contains("extractvalue { i32, float } %with_val, 0", ir);
    }

    [Fact]
    public void Emit_无名字值自动编号()
    {
        var mod = new Module();
        var func = new Function("auto_num", new FunctionType(IrType.Int32, new[] { IrType.Int32 }));
        func.Parameters[0].Name = "a";

        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);

        // 不设名字的指令应被自动编号
        var add = new BinaryOperator(BinaryOp.Add, func.Parameters[0],
            new ConstantInt(IrType.Int32, 1)); // 无名字
        entry.AddInstruction(add);

        var mul = new BinaryOperator(BinaryOp.Mul, add,
            new ConstantInt(IrType.Int32, 2)); // 无名字
        entry.AddInstruction(mul);

        entry.AddInstruction(new ReturnInst(mul));

        mod.AddFunction(func);
        var ir = _emitter.Emit(mod);

        // 应包含 %0 和 %1 的自动编号
        Assert.Contains("%0 = add", ir);
        Assert.Contains("%1 = mul", ir);
    }
}