using IronST.IRBuilder.Emitter;
using IronST.IRBuilder.IR;
using IronST.IRBuilder.Parser;

namespace IronST.IRBuilder.Tests;

/// <summary>
/// 针对已知 Bug 的验证测试。
/// 每个测试均验证正确的预期行为，因代码存在对应 Bug 而失败。
/// </summary>
public class BugVerificationTests
{
    // ====================================================================
    // Bug #1: SkipLine() 吃到 EOF，attributes 后面的内容全部丢失
    // ====================================================================

    [Fact]
    public void Parse_FileWithAttributesBeforeFunction_ShouldStillParseFunction()
    {
        var source = @"
source_filename = ""test.c""
attributes #0 = { nounwind }
define i32 @foo() {
entry:
  ret i32 0
}
";
        var module = new LLParser().Parse(source);

        Assert.Single(module.Functions);
        Assert.Equal("foo", module.Functions[0].Name);
    }

    [Fact]
    public void ParseAndEmit_FunctionAfterAttributes_ShouldAppearInOutput()
    {
        var source = @"
source_filename = ""test.c""
target datalayout = ""e-m:w-p270:32:32-p271:32:32-p272:64:64-i64:64-i128:128-f80:128-n8:16:32:64-S128""
target triple = ""x86_64-pc-windows-msvc""
attributes #0 = { nounwind }
define i32 @add(i32 %a, i32 %b) {
entry:
  %result = add i32 %a, %b
  ret i32 %result
}
";
        var module = new LLParser().Parse(source);
        var output = new LLEmitter().Emit(module);

        Assert.Contains("define i32 @add", output);
        Assert.Contains("%result = add i32", output);
    }

    // ====================================================================
    // Bug #2: 函数解析时 Visibility 和 CallingConvention 被丢弃
    // ====================================================================

    [Fact]
    public void Parse_FunctionWithHiddenVisibility_ShouldPreserveVisibility()
    {
        var source = @"
define hidden i32 @foo() {
entry:
  ret i32 0
}
";
        var module = new LLParser().Parse(source);
        var func = module.Functions.First(f => f.Name == "foo");

        Assert.Equal(VisibilityStyle.Hidden, func.Visibility);
    }

    [Fact]
    public void Parse_FunctionWithProtectedVisibility_ShouldPreserveVisibility()
    {
        var source = @"
define protected i32 @foo() {
entry:
  ret i32 0
}
";
        var module = new LLParser().Parse(source);
        var func = module.Functions.First(f => f.Name == "foo");

        Assert.Equal(VisibilityStyle.Protected, func.Visibility);
    }

    [Fact]
    public void Parse_FunctionWithFastcc_ShouldPreserveCallingConvention()
    {
        var source = @"
define fastcc i32 @foo() {
entry:
  ret i32 0
}
";
        var module = new LLParser().Parse(source);
        var func = module.Functions.First(f => f.Name == "foo");

        Assert.Equal(CallingConv.Fast, func.CallingConvention);
    }

    [Fact]
    public void Parse_FunctionWithColdcc_ShouldPreserveCallingConvention()
    {
        var source = @"
define coldcc i32 @foo() {
entry:
  ret i32 0
}
";
        var module = new LLParser().Parse(source);
        var func = module.Functions.First(f => f.Name == "foo");

        Assert.Equal(CallingConv.Cold, func.CallingConvention);
    }

    [Fact]
    public void Parse_FunctionWithNumericCallingConv_ShouldPreserveCallingConvention()
    {
        var source = @"
define cc 8 i32 @foo() {
entry:
  ret i32 0
}
";
        var module = new LLParser().Parse(source);
        var func = module.Functions.First(f => f.Name == "foo");

        Assert.Equal(CallingConv.Fast, func.CallingConvention);
    }

    [Fact]
    public void ParseAndEmit_HiddenVisibility_ShouldAppearInOutput()
    {
        var source = @"
define hidden i32 @foo() {
entry:
  ret i32 0
}
";
        var module = new LLParser().Parse(source);
        var output = new LLEmitter().Emit(module);

        Assert.Contains("hidden", output);
    }

    [Fact]
    public void ParseAndEmit_Fastcc_ShouldAppearInOutput()
    {
        var source = @"
define fastcc i32 @foo() {
entry:
  ret i32 0
}
";
        var module = new LLParser().Parse(source);
        var output = new LLEmitter().Emit(module);

        Assert.Contains("fastcc", output);
    }

    // ====================================================================
    // Bug #3: 无名基本块名称冲突
    // ====================================================================

    [Fact]
    public void Parse_UnnamedEntryFollowedByLabeledBlock_ShouldHaveTwoBlocks()
    {
        var source = @"
define void @foo() {
  %x = add i32 1, 2
  br label %next
next:
  ret void
}
";
        var module = new LLParser().Parse(source);
        var func = module.Functions.First(f => f.Name == "foo");

        Assert.Equal(2, func.BasicBlocks.Count);
    }

    // ====================================================================
    // Bug #4: RemoveInstruction 未清 Parent
    // ====================================================================

    [Fact]
    public void RemoveInstruction_ShouldClearParent()
    {
        var bb = new BasicBlock("entry");
        var inst = new ReturnInst(new ConstantInt(IrType.Int32, 0));
        bb.AddInstruction(inst);

        Assert.Same(bb, inst.Parent);

        bb.RemoveInstruction(inst);

        Assert.Null(inst.Parent);
    }

    // ====================================================================
    // Bug #5: CallingConvStr 缺少 GHC/Tail/Swift 的关键字
    // ====================================================================

    [Fact]
    public void Emit_GHCCallingConvention_ShouldUseCanonicalKeyword()
    {
        var func = new Function("ghc_fn",
            new FunctionType(IrType.Void, System.Array.Empty<IrType>()));
        func.CallingConvention = CallingConv.GHC;

        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);
        entry.AddInstruction(new ReturnInst());

        var module = new Module("test.ll");
        module.AddFunction(func);

        var output = new LLEmitter().Emit(module);

        Assert.Contains("ghccc", output);
    }

    [Fact]
    public void Emit_TailCallingConvention_ShouldUseCanonicalKeyword()
    {
        var func = new Function("tail_fn",
            new FunctionType(IrType.Void, System.Array.Empty<IrType>()));
        func.CallingConvention = CallingConv.Tail;

        var entry = new BasicBlock("entry");
        func.AppendBasicBlock(entry);
        entry.AddInstruction(new ReturnInst());

        var module = new Module("test.ll");
        module.AddFunction(func);

        var output = new LLEmitter().Emit(module);

        Assert.Contains("tailcc", output);
    }

    // ====================================================================
    // Bug #6: EmitGlobalVar 丢失 ThreadLocal 和 Section
    // ====================================================================

    [Fact]
    public void Emit_GlobalVariableWithThreadLocal_ShouldEmitThreadLocal()
    {
        var gv = new GlobalVariable("tls_var", IrType.Int32, false, LinkageType.Internal);
        gv.ThreadLocal = ThreadLocalMode.LocalExec;
        gv.Initializer = new ConstantInt(IrType.Int32, 0);

        var module = new Module("test.ll");
        module.AddGlobalVariable(gv);

        var output = new LLEmitter().Emit(module);

        Assert.Contains("thread_local(localexec)", output);
    }

    [Fact]
    public void Emit_GlobalVariableWithSection_ShouldEmitSection()
    {
        var gv = new GlobalVariable("sec_var", IrType.Int32, false, LinkageType.Internal);
        gv.Section = ".mydata";
        gv.Initializer = new ConstantInt(IrType.Int32, 0);

        var module = new Module("test.ll");
        module.AddGlobalVariable(gv);

        var output = new LLEmitter().Emit(module);

        Assert.Contains("section \".mydata\"", output);
    }

    // ====================================================================
    // Bug #7: PHINode 的 SetOperand 不同步 _incoming
    // ====================================================================

    [Fact]
    public void PHINode_SetOperand_ShouldSyncIncomingList()
    {
        var bb1 = new BasicBlock("b1");
        var bb2 = new BasicBlock("b2");
        var val1 = new ConstantInt(IrType.Int32, 1);
        var val2 = new ConstantInt(IrType.Int32, 2);
        var val3 = new ConstantInt(IrType.Int32, 3);

        var phi = new PHINode(IrType.Int32, "x");
        phi.AddIncoming(val1, bb1);
        phi.AddIncoming(val2, bb2);

        // 通过基类 SetOperand 替换第一个 incoming 值
        ((Instruction)phi).SetOperand(0, val3);

        // _incoming 也应该同步更新
        Assert.Same(val3, phi.Incoming[0].Value);
    }

    // ====================================================================
    // Bug #9: 对未声明函数的调用不会添加声明到 Module
    // ====================================================================

    [Fact]
    public void Parse_CallToUndeclaredFunction_ShouldAddDeclarationToModule()
    {
        var source = @"
define i32 @foo() {
entry:
  %r = call i32 @bar()
  ret i32 %r
}
";
        var module = new LLParser().Parse(source);

        var barFunc = module.Functions.FirstOrDefault(f => f.Name == "bar");
        Assert.NotNull(barFunc);
        Assert.True(barFunc.IsDeclaration);
    }
}