using IronST.IRBuilder.Emitter;
using IronST.IRBuilder.IR;
using Xunit;

namespace IronST.IRBuilder.Tests;

public class EmitterTests2
{
    private readonly LLEmitter _emitter = new();

    [Fact]
    public void Emit_Select指令()
    {
        var mod = new Module();
        var func = new Function("sel", new FunctionType(IrType.Int32, new[] { IrType.Int1 }));
        func.Parameters[0].Name = "c";
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);

        var sel = new SelectInst(func.Parameters[0],
            new ConstantInt(IrType.Int32, 1),
            new ConstantInt(IrType.Int32, 0), "r");
        entry.AddInstruction(sel);
        entry.AddInstruction(new ReturnInst(sel));

        mod.AddFunction(func);
        var ir = _emitter.Emit(mod);

        Assert.Contains("select i1 %c, i32 1, i32 0", ir);
        Assert.Contains("%r =", ir);
    }

    [Fact]
    public void Emit_Unreachable()
    {
        var mod = new Module();
        var func = new Function("test", new FunctionType(IrType.Void, Array.Empty<IrType>()));
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);
        entry.AddInstruction(new UnreachableInst());
        mod.AddFunction(func);

        var ir = _emitter.Emit(mod);
        Assert.Contains("unreachable", ir);
    }

    [Fact]
    public void Emit_TailCall()
    {
        var mod = new Module();
        var calleeFt = new FunctionType(IrType.Int32, new[] { IrType.Int32 });
        mod.AddFunction(new Function("callee", calleeFt));

        var func = new Function("caller", new FunctionType(IrType.Int32, new[] { IrType.Int32 }));
        func.Parameters[0].Name = "a";
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);

        var call = new CallInst(calleeFt, mod.Functions[0],
            new Value[] { func.Parameters[0] }, "r") { IsTailCall = true };
        entry.AddInstruction(call);
        entry.AddInstruction(new ReturnInst(call));

        mod.AddFunction(func);
        var ir = _emitter.Emit(mod);

        Assert.Contains("tail call i32 @callee(i32 %a)", ir);
    }

    [Fact]
    public void Emit_Nsw标记()
    {
        var mod = new Module();
        var func = new Function("nsw", new FunctionType(IrType.Int32, new[] { IrType.Int32, IrType.Int32 }));
        func.Parameters[0].Name = "a";
        func.Parameters[1].Name = "b";
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);

        var add = new BinaryOperator(BinaryOp.Add, func.Parameters[0], func.Parameters[1], "r")
            { NoSignedWrap = true };
        entry.AddInstruction(add);
        entry.AddInstruction(new ReturnInst(add));

        mod.AddFunction(func);
        var ir = _emitter.Emit(mod);

        Assert.Contains("add nsw i32 %a, %b", ir);
    }

    [Fact]
    public void Emit_Nuw标记()
    {
        var mod = new Module();
        var func = new Function("nuw", new FunctionType(IrType.Int32, new[] { IrType.Int32, IrType.Int32 }));
        func.Parameters[0].Name = "a";
        func.Parameters[1].Name = "b";
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);

        var add = new BinaryOperator(BinaryOp.Add, func.Parameters[0], func.Parameters[1], "r")
            { NoUnsignedWrap = true };
        entry.AddInstruction(add);
        entry.AddInstruction(new ReturnInst(add));

        mod.AddFunction(func);
        var ir = _emitter.Emit(mod);

        Assert.Contains("add nuw i32 %a, %b", ir);
    }

    [Fact]
    public void Emit_PrivateLinkage()
    {
        var mod = new Module();
        var init = new ConstantInt(IrType.Int32, 0);
        var gv = new GlobalVariable("g", IrType.Int32, true, LinkageType.Private, init) { Alignment = 4 };
        mod.AddGlobalVariable(gv);

        var ir = _emitter.Emit(mod);
        Assert.Contains("@g = private constant i32 0, align 4", ir);
    }

    [Fact]
    public void Emit_InternalLinkage()
    {
        var mod = new Module();
        var gv = new GlobalVariable("g", IrType.Int32, false, LinkageType.Internal,
            new ConstantInt(IrType.Int32, 42));
        mod.AddGlobalVariable(gv);

        var ir = _emitter.Emit(mod);
        Assert.Contains("@g = internal global i32 42", ir);
    }

    [Fact]
    public void Emit_HiddenVisibility()
    {
        var mod = new Module();
        var func = new Function("test", new FunctionType(IrType.Void, Array.Empty<IrType>()))
            { Visibility = VisibilityStyle.Hidden };
        func.AppendBasicBlock(new BasicBlock("entry"));
        func.BasicBlocks[0].AddInstruction(new ReturnInst());
        mod.AddFunction(func);

        var ir = _emitter.Emit(mod);
        Assert.Contains("hidden", ir);
    }

    [Fact]
    public void Emit_Poison常量()
    {
        var mod = new Module();
        var func = new Function("test", new FunctionType(IrType.Int32, Array.Empty<IrType>()));
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);
        entry.AddInstruction(new ReturnInst(new PoisonValue(IrType.Int32)));
        mod.AddFunction(func);

        var ir = _emitter.Emit(mod);
        Assert.Contains("ret i32 poison", ir);
    }

    [Fact]
    public void Emit_Undef常量()
    {
        var mod = new Module();
        var func = new Function("test", new FunctionType(IrType.Int32, Array.Empty<IrType>()));
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);
        entry.AddInstruction(new ReturnInst(new UndefValue(IrType.Int32)));
        mod.AddFunction(func);

        var ir = _emitter.Emit(mod);
        Assert.Contains("ret i32 undef", ir);
    }

    [Fact]
    public void Emit_Null指针常量()
    {
        var mod = new Module();
        var func = new Function("test", new FunctionType(IrType.Pointer, Array.Empty<IrType>()));
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);
        entry.AddInstruction(new ReturnInst(new ConstantPointerNull()));
        mod.AddFunction(func);

        var ir = _emitter.Emit(mod);
        Assert.Contains("ret ptr null", ir);
    }

    [Fact]
    public void Emit_Fcmp指令()
    {
        var mod = new Module();
        var func = new Function("cmp", new FunctionType(IrType.Int1, new[] { IrType.Float, IrType.Float }));
        func.Parameters[0].Name = "a";
        func.Parameters[1].Name = "b";
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);

        var fcmp = new FCmpInst(FCmpPredicate.Olt, func.Parameters[0], func.Parameters[1], "r");
        entry.AddInstruction(fcmp);
        entry.AddInstruction(new ReturnInst(fcmp));

        mod.AddFunction(func);
        var ir = _emitter.Emit(mod);

        Assert.Contains("fcmp olt float %a, %b", ir);
    }

    [Fact]
    public void Emit_VoidCall()
    {
        var mod = new Module();
        var calleeFt = new FunctionType(IrType.Void, Array.Empty<IrType>());
        mod.AddFunction(new Function("vfunc", calleeFt));

        var func = new Function("caller", new FunctionType(IrType.Void, Array.Empty<IrType>()));
        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);

        var call = new CallInst(calleeFt, mod.Functions[0], Array.Empty<Value>());
        entry.AddInstruction(call);
        entry.AddInstruction(new ReturnInst());

        mod.AddFunction(func);
        var ir = _emitter.Emit(mod);

        Assert.Contains("call void @vfunc()", ir);
    }

    [Fact]
    public void Emit_FastCallingConvention()
    {
        var mod = new Module();
        var func = new Function("fast", new FunctionType(IrType.Int32, new[] { IrType.Int32 }))
            { CallingConvention = CallingConv.Fast };
        func.Parameters[0].Name = "a";
        func.AppendBasicBlock(new BasicBlock("entry"));
        func.BasicBlocks[0].AddInstruction(new ReturnInst(func.Parameters[0]));
        mod.AddFunction(func);

        var ir = _emitter.Emit(mod);
        Assert.Contains("fastcc", ir);
    }

    [Fact]
    public void Emit_打包结构体()
    {
        var mod = new Module();
        var packed = new StructType(new IrType[] { IrType.Int8, IrType.Int32 }, isPacked: true);
        var func = new Function("test", new FunctionType(IrType.Void, new[] { packed }));
        func.Parameters[0].Name = "s";
        func.AppendBasicBlock(new BasicBlock("entry"));
        func.BasicBlocks[0].AddInstruction(new ReturnInst());
        mod.AddFunction(func);

        var ir = _emitter.Emit(mod);
        Assert.Contains("<{ i8, i32 }>", ir);
    }

    [Fact]
    public void Emit_无参数函数()
    {
        var mod = new Module();
        var func = new Function("noparam", new FunctionType(IrType.Int32, Array.Empty<IrType>()));
        func.AppendBasicBlock(new BasicBlock("entry"));
        func.BasicBlocks[0].AddInstruction(new ReturnInst(new ConstantInt(IrType.Int32, 0)));
        mod.AddFunction(func);

        var ir = _emitter.Emit(mod);
        Assert.Contains("define i32 @noparam()", ir);
    }
}