using IronST.IRBuilder.Parser;
using IronST.IRBuilder.IR;
using Xunit;

namespace IronST.IRBuilder.Tests;

/// <summary>Parser 测试：验证 .ll 文本能正确解析为 IR 对象模型</summary>
public class ParserTests
{
    private readonly LLParser _parser = new();

    [Fact]
    public void Parse_模块元数据()
    {
        var ll = @"
source_filename = ""test.c""
target datalayout = ""e-m:e-p270:32:32-p271:32:32-p272:64:64-i64:64-i128:128-f80:128-n8:16:32:64-S128""
target triple = ""x86_64-unknown-linux-gnu""
";
        var mod = _parser.Parse(ll);
        Assert.Equal("test.c", mod.SourceFilename);
        Assert.Contains("e-m:e", mod.TargetDataLayout!);
        Assert.Equal("x86_64-unknown-linux-gnu", mod.TargetTriple);
    }

    [Fact]
    public void Parse_函数声明()
    {
        var ll = "declare i32 @printf(ptr, ...)\n";
        var mod = _parser.Parse(ll);

        Assert.Single(mod.Functions);
        var func = mod.Functions[0];
        Assert.Equal("printf", func.Name);
        Assert.True(func.IsDeclaration);
        Assert.Equal(IrType.Int32, func.FunctionType.ReturnType);
        Assert.True(func.FunctionType.IsVarArg);
    }

    [Fact]
    public void Parse_简单函数定义()
    {
        var ll = @"
define i32 @add(i32 %a, i32 %b) {
entry:
  %sum = add i32 %a, %b
  ret i32 %sum
}
";
        var mod = _parser.Parse(ll);
        var func = mod.Functions[0];

        Assert.Equal("add", func.Name);
        Assert.Equal(2, func.Parameters.Count);
        Assert.Equal("a", func.Parameters[0].Name);
        Assert.Equal("b", func.Parameters[1].Name);
        Assert.Single(func.BasicBlocks);

        var bb = func.BasicBlocks[0];
        Assert.Equal("entry", bb.Name);
        Assert.Equal(2, bb.Instructions.Count);
        Assert.IsType<BinaryOperator>(bb.Instructions[0]);
        Assert.IsType<ReturnInst>(bb.Instructions[1]);
    }

    [Fact]
    public void Parse_全局变量()
    {
        var ll = @"@.str = private unnamed_addr constant [6 x i8] c""hello\00"", align 1
";
        var mod = _parser.Parse(ll);

        Assert.Single(mod.GlobalVariables);
        var gv = mod.GlobalVariables[0];
        Assert.Equal(".str", gv.Name);
        Assert.True(gv.IsConstant);
        Assert.Equal(LinkageType.Private, gv.Linkage);
    }

    [Fact]
    public void Parse_多基本块()
    {
        var ll = @"
define i32 @abs(i32 %x) {
entry:
  %cmp = icmp slt i32 %x, 0
  br i1 %cmp, label %neg, label %end

neg:
  %neg_val = sub i32 0, %x
  br label %end

end:
  %result = phi i32 [ %x, %entry ], [ %neg_val, %neg ]
  ret i32 %result
}
";
        var mod = _parser.Parse(ll);
        var func = mod.Functions[0];

        Assert.Equal(3, func.BasicBlocks.Count);
        Assert.Equal("entry", func.BasicBlocks[0].Name);
        Assert.Equal("neg", func.BasicBlocks[1].Name);
        Assert.Equal("end", func.BasicBlocks[2].Name);

        // 验证 phi 节点
        var endBB = func.BasicBlocks[2];
        var phi = endBB.Instructions[0];
        Assert.IsType<PHINode>(phi);
    }

    [Fact]
    public void Parse_Store和Load()
    {
        var ll = @"
define void @test() {
entry:
  %x = alloca i32, align 4
  store i32 42, ptr %x, align 4
  %v = load i32, ptr %x, align 4
  ret void
}
";
        var mod = _parser.Parse(ll);
        var bb = mod.Functions[0].BasicBlocks[0];

        Assert.IsType<AllocaInst>(bb.Instructions[0]);
        Assert.IsType<StoreInst>(bb.Instructions[1]);
        Assert.IsType<LoadInst>(bb.Instructions[2]);
        Assert.IsType<ReturnInst>(bb.Instructions[3]);
    }

    [Fact]
    public void Parse_GetElementPtr()
    {
        var ll = @"
define ptr @test() {
entry:
  %arr = alloca [10 x i32], align 4
  %ptr = getelementptr inbounds i32, ptr %arr, i32 0, i32 5
  ret ptr %ptr
}
";
        var mod = _parser.Parse(ll);
        var bb = mod.Functions[0].BasicBlocks[0];

        Assert.IsType<GetElementPtrInst>(bb.Instructions[1]);
        var gep = (GetElementPtrInst)bb.Instructions[1];
        Assert.True(gep.InBounds);
    }

    [Fact]
    public void Parse_类型转换()
    {
        var ll = @"
define i64 @test(i32 %v) {
entry:
  %ext = sext i32 %v to i64
  ret i64 %ext
}
";
        var mod = _parser.Parse(ll);
        var inst = mod.Functions[0].BasicBlocks[0].Instructions[0];
        Assert.IsType<CastInst>(inst);

        var cast = (CastInst)inst;
        Assert.Equal(CastOp.SExt, cast.Op);
        Assert.Equal(IrType.Int64, cast.Type);
    }

    [Fact]
    public void Parse_Call指令()
    {
        var ll = @"
declare i32 @callee(i32)

define i32 @caller(i32 %arg) {
entry:
  %r = call i32 @callee(i32 %arg)
  ret i32 %r
}
";
        var mod = _parser.Parse(ll);
        Assert.Equal(2, mod.Functions.Count);

        var callerBB = mod.Functions[1].BasicBlocks[0];
        Assert.IsType<CallInst>(callerBB.Instructions[0]);
    }

    [Fact]
    public void Parse_命名结构体()
    {
        var ll = @"
%Point = type { i32, float }
";
        var mod = _parser.Parse(ll);
        Assert.Single(mod.NamedTypes);
        Assert.Equal("Point", mod.NamedTypes[0].Name);
    }

    [Fact]
    public void Parse_Select指令()
    {
        var ll = @"
define i32 @test(i1 %c) {
entry:
  %r = select i1 %c, i32 1, i32 0
  ret i32 %r
}
";
        var mod = _parser.Parse(ll);
        Assert.IsType<SelectInst>(mod.Functions[0].BasicBlocks[0].Instructions[0]);
    }
}