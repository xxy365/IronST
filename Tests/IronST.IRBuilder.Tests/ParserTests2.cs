using IronST.IRBuilder.IR;
using IronST.IRBuilder.Parser;
using Xunit;

namespace IronST.IRBuilder.Tests;

public class ParserTests2
{
    private readonly LLParser _parser = new();

    // ==================== 指令解析 ====================

    [Fact]
    public void Parse_Switch指令()
    {
        var ll =
            @"
define void @sw(i32 %v) {
entry:
  switch i32 %v, label %default [ i32 1, label %case1 i32 2, label %case2 ]
default:
  ret void
case1:
  ret void
case2:
  ret void
}
";
        var mod = _parser.Parse(ll);
        var entry = mod.Functions[0].BasicBlocks[0];

        Assert.IsType<SwitchInst>(entry.Instructions[0]);
        var sw = (SwitchInst)entry.Instructions[0];
        Assert.Equal("default", sw.DefaultDest.Name);
        Assert.Equal(2, sw.Cases.Count);
        Assert.Equal(1L, sw.Cases[0].Value.Value);
        Assert.Equal("case1", sw.Cases[0].Dest.Name);
        Assert.Equal(2L, sw.Cases[1].Value.Value);
        Assert.Equal("case2", sw.Cases[1].Dest.Name);
    }

    [Fact]
    public void Parse_Unreachable()
    {
        var ll =
            @"
define void @test() {
entry:
  unreachable
}
";
        var mod = _parser.Parse(ll);
        Assert.IsType<UnreachableInst>(mod.Functions[0].BasicBlocks[0].Instructions[0]);
    }

    [Fact]
    public void Parse_Phi指令_验证incoming值()
    {
        var ll = @"
define i32 @test(i32 %x) {
entry:
  %cmp = icmp sgt i32 %x, 0
  br i1 %cmp, label %pos, label %neg
pos:
  br label %end
neg:
  %negv = sub i32 0, %x
  br label %end
end:
  %result = phi i32 [ %x, %pos ], [ %negv, %neg ]
  ret i32 %result
}
";
        var mod = _parser.Parse(ll);
        var func = mod.Functions[0];

        var endBB = func.BasicBlocks.Single(b => b.Name == "end");

        var phi = (PHINode)endBB.Instructions[0];
        Assert.Equal(2, phi.Incoming.Count);

        Assert.Equal("x", phi.Incoming[0].Value.Name);
        Assert.Equal("pos", phi.Incoming[0].Block.Name);

        Assert.Equal("negv", phi.Incoming[1].Value.Name);
        Assert.Equal("neg", phi.Incoming[1].Block.Name);
    }

    [Fact]
    public void Parse_TailCall()
    {
        var ll =
            @"
declare i32 @foo(i32)

define i32 @bar(i32 %a) {
entry:
  %r = tail call i32 @foo(i32 %a)
  ret i32 %r
}
";
        var mod = _parser.Parse(ll);
        var bb = mod.Functions[1].BasicBlocks[0];

        Assert.IsType<CallInst>(bb.Instructions[0]);
        Assert.True(((CallInst)bb.Instructions[0]).IsTailCall);
    }

    [Fact]
    public void Parse_MustTailCall()
    {
        var ll =
            @"
declare i32 @foo(i32)

define i32 @bar(i32 %a) {
entry:
  %r = musttail call i32 @foo(i32 %a)
  ret i32 %r
}
";
        var mod = _parser.Parse(ll);
        var call = (CallInst)mod.Functions[1].BasicBlocks[0].Instructions[0];
        Assert.True(call.IsTailCall);
    }

    [Fact]
    public void Parse_NotailCall()
    {
        var ll =
            @"
declare i32 @foo(i32)

define i32 @bar(i32 %a) {
entry:
  %r = notail call i32 @foo(i32 %a)
  ret i32 %r
}
";
        var mod = _parser.Parse(ll);
        var call = (CallInst)mod.Functions[1].BasicBlocks[0].Instructions[0];
        Assert.False(call.IsTailCall);
    }

    [Fact]
    public void Parse_FCmp指令()
    {
        var ll =
            @"
define i1 @test(float %a, float %b) {
entry:
  %r = fcmp olt float %a, %b
  ret i1 %r
}
";
        var mod = _parser.Parse(ll);
        var inst = mod.Functions[0].BasicBlocks[0].Instructions[0];

        Assert.IsType<FCmpInst>(inst);
        var fcmp = (FCmpInst)inst;
        Assert.Equal(IrType.Int1, fcmp.Type);
    }

    [Theory]
    [InlineData("oeq")]
    [InlineData("ogt")]
    [InlineData("oge")]
    [InlineData("olt")]
    [InlineData("ole")]
    [InlineData("one")]
    [InlineData("ord")]
    [InlineData("ueq")]
    [InlineData("une")]
    [InlineData("uno")]
    [InlineData("true")]
    [InlineData("false")]
    public void Parse_FCmp谓词(string keyword)
    {
        var expected = keyword switch
        {
            "oeq" => FCmpPredicate.Oeq,
            "ogt" => FCmpPredicate.Ogt,
            "oge" => FCmpPredicate.Oge,
            "olt" => FCmpPredicate.Olt,
            "ole" => FCmpPredicate.Ole,
            "one" => FCmpPredicate.One,
            "ord" => FCmpPredicate.Ord,
            "ueq" => FCmpPredicate.Ueq,
            "une" => FCmpPredicate.Une,
            "uno" => FCmpPredicate.Uno,
            "true" => FCmpPredicate.True,
            "false" => FCmpPredicate.False,
            _ => throw new ArgumentException($"未覆盖的谓词: {keyword}"),
        };

        var ll =
            $@"
define i1 @test(float %a, float %b) {{
entry:
  %r = fcmp {keyword} float %a, %b
  ret i1 %r
}}
";
        var mod = _parser.Parse(ll);
        var fcmp = (FCmpInst)mod.Functions[0].BasicBlocks[0].Instructions[0];
        Assert.Equal(expected, fcmp.Predicate);
    }

    [Fact]
    public void Parse_NswNuw标记()
    {
        var ll =
            @"
define i32 @test(i32 %a, i32 %b) {
entry:
  %r = add nsw nuw i32 %a, %b
  ret i32 %r
}
";
        var mod = _parser.Parse(ll);
        var binOp = (BinaryOperator)mod.Functions[0].BasicBlocks[0].Instructions[0];

        Assert.Equal(BinaryOp.Add, binOp.Op);
        Assert.True(binOp.NoSignedWrap);
        Assert.True(binOp.NoUnsignedWrap);
    }

    [Fact]
    public void Parse_VolatileLoadStore()
    {
        var ll =
            @"
define void @test(ptr %p) {
entry:
  store volatile i32 42, ptr %p
  %v = load volatile i32, ptr %p
  ret void
}
";
        var mod = _parser.Parse(ll);
        var bb = mod.Functions[0].BasicBlocks[0];

        Assert.True(((StoreInst)bb.Instructions[0]).IsVolatile);
        Assert.True(((LoadInst)bb.Instructions[1]).IsVolatile);
    }

    [Fact]
    public void Parse_InsertValue_ExtractValue()
    {
        var ll =
            @"
define i32 @test({i32, float} %agg) {
entry:
  %new = insertvalue {i32, float} %agg, i32 7, 0
  %val = extractvalue {i32, float} %new, 0
  ret i32 %val
}
";
        var mod = _parser.Parse(ll);
        var bb = mod.Functions[0].BasicBlocks[0];

        var insert = (InsertValueInst)bb.Instructions[0];
        Assert.Equal(new[] { 0 }, insert.Indices);

        var extract = (ExtractValueInst)bb.Instructions[1];
        Assert.Equal(new[] { 0 }, extract.Indices);
    }

    [Fact]
    public void Parse_ExtractValue_多级索引()
    {
        var ll =
            @"
define i32 @test({i32, {float, i32}} %agg) {
entry:
  %v = extractvalue {i32, {float, i32}} %agg, 1, 1
  ret i32 %v
}
";
        var mod = _parser.Parse(ll);
        var extract = (ExtractValueInst)mod.Functions[0].BasicBlocks[0].Instructions[0];

        Assert.Equal(new[] { 1, 1 }, extract.Indices);
        Assert.Equal(IrType.Int32, extract.Type);
    }

    [Fact]
    public void Parse_Select指令_类型正确()
    {
        var ll =
            @"
define i32 @test(i1 %c) {
entry:
  %r = select i1 %c, i32 1, i32 0
  ret i32 %r
}
";
        var mod = _parser.Parse(ll);
        var sel = (SelectInst)mod.Functions[0].BasicBlocks[0].Instructions[0];

        Assert.Equal(IrType.Int32, sel.Type);
        Assert.IsType<ConstantInt>(sel.TrueValue);
        Assert.Equal(1L, ((ConstantInt)sel.TrueValue).Value);
        Assert.IsType<ConstantInt>(sel.FalseValue);
        Assert.Equal(0L, ((ConstantInt)sel.FalseValue).Value);
    }

    // ==================== 常量解析 ====================

    [Fact]
    public void Parse_Zeroinitializer()
    {
        var ll =
            @"@arr = global [3 x i32] zeroinitializer
";
        var mod = _parser.Parse(ll);
        Assert.IsType<ConstantAggregateZero>(mod.GlobalVariables[0].Initializer);
    }

    [Fact]
    public void Parse_Undef常量()
    {
        var ll =
            @"
define i32 @test() {
entry:
  %u = add i32 undef, 0
  ret i32 %u
}
";
        var mod = _parser.Parse(ll);
        var addInst = (BinaryOperator)mod.Functions[0].BasicBlocks[0].Instructions[0];
        Assert.IsType<UndefValue>(addInst.LHS);
    }

    [Fact]
    public void Parse_Poison常量()
    {
        var ll =
            @"
define i32 @test() {
entry:
  %p = add i32 poison, 0
  ret i32 %p
}
";
        var mod = _parser.Parse(ll);
        var addInst = (BinaryOperator)mod.Functions[0].BasicBlocks[0].Instructions[0];
        Assert.IsType<PoisonValue>(addInst.LHS);
    }

    [Fact]
    public void Parse_Null指针常量()
    {
        var ll =
            @"@g = global ptr null
";
        var mod = _parser.Parse(ll);
        Assert.IsType<ConstantPointerNull>(mod.GlobalVariables[0].Initializer);
    }

    [Fact]
    public void Parse_TrueFalse常量()
    {
        var ll =
            @"
define i1 @t() { entry: ret i1 true }
define i1 @f() { entry: ret i1 false }
";
        var mod = _parser.Parse(ll);

        var retT = (ReturnInst)mod.Functions[0].BasicBlocks[0].Instructions[0];
        Assert.Equal(1L, ((ConstantInt)retT.ReturnValue!).Value);

        var retF = (ReturnInst)mod.Functions[1].BasicBlocks[0].Instructions[0];
        Assert.Equal(0L, ((ConstantInt)retF.ReturnValue!).Value);
    }

    [Fact]
    public void Parse_CString常量()
    {
        var ll =
            @"@g = private constant [5 x i8] c""hello""
";
        var mod = _parser.Parse(ll);
        var data = (ConstantDataArray)mod.GlobalVariables[0].Initializer!;
        Assert.Equal(5, data.Data.Length);
    }

    [Fact]
    public void Parse_CString带转义()
    {
        var ll =
            @"@g = private constant [6 x i8] c""hello\00""
";
        var mod = _parser.Parse(ll);
        var data = (ConstantDataArray)mod.GlobalVariables[0].Initializer!;
        Assert.Equal(6, data.Data.Length);
        Assert.Equal(0, data.Data[5]);
    }

    [Fact]
    public void Parse_数组常量()
    {
        var ll =
            @"@g = global [3 x i32] [i32 1, i32 2, i32 3]
";
        var mod = _parser.Parse(ll);
        var arr = (ConstantArray)mod.GlobalVariables[0].Initializer!;
        Assert.Equal(3, arr.Values.Count);
        Assert.Equal(1L, ((ConstantInt)arr.Values[0]).Value);
        Assert.Equal(2L, ((ConstantInt)arr.Values[1]).Value);
        Assert.Equal(3L, ((ConstantInt)arr.Values[2]).Value);
    }

    [Fact]
    public void Parse_结构体常量()
    {
        var ll =
            @"@g = global {i32, float} {i32 1, float 2.5}
";
        var mod = _parser.Parse(ll);
        var st = (ConstantStruct)mod.GlobalVariables[0].Initializer!;
        Assert.Equal(2, st.Values.Count);
        Assert.Equal(1L, ((ConstantInt)st.Values[0]).Value);
    }

    [Fact]
    public void Parse_负整数()
    {
        var ll =
            @"
define i32 @test(i32 %a) {
entry:
  %r = add i32 %a, -1
  ret i32 %r
}
";
        var mod = _parser.Parse(ll);
        var addInst = (BinaryOperator)mod.Functions[0].BasicBlocks[0].Instructions[0];
        Assert.Equal(-1L, ((ConstantInt)addInst.RHS).Value);
    }

    [Fact]
    public void Parse_零对齐LoadStore()
    {
        var ll =
            @"
define void @test(ptr %p) {
entry:
  store i32 42, ptr %p
  %v = load i32, ptr %p
  ret void
}
";
        var mod = _parser.Parse(ll);
        var bb = mod.Functions[0].BasicBlocks[0];
        Assert.Equal(0, ((StoreInst)bb.Instructions[0]).Alignment);
        Assert.Equal(0, ((LoadInst)bb.Instructions[1]).Alignment);
    }

    // ==================== 类型解析 ====================

    [Fact]
    public void Parse_固定向量类型()
    {
        var ll =
            @"
define <4 x float> @test(<4 x float> %v) {
entry:
  ret <4 x float> %v
}
";
        var mod = _parser.Parse(ll);
        var vt = (VectorType)mod.Functions[0].FunctionType.ReturnType;
        Assert.Equal(4, vt.NumElements);
        Assert.Equal(IrType.Float, vt.ElementType);
        Assert.False(vt.IsScalable);
    }

    [Fact]
    public void Parse_可伸缩向量类型()
    {
        var ll =
            @"
define <vscale x 4 x i32> @test(<vscale x 4 x i32> %v) {
entry:
  ret <vscale x 4 x i32> %v
}
";
        var mod = _parser.Parse(ll);
        var vt = (VectorType)mod.Functions[0].FunctionType.ReturnType;
        Assert.Equal(4, vt.NumElements);
        Assert.True(vt.IsScalable);
    }

    [Fact]
    public void Parse_打包结构体()
    {
        var ll =
            @"
define void @test() {
entry:
  %a = alloca <{ i8, i32 }>
  ret void
}
";
        var mod = _parser.Parse(ll);
        var alloca = (AllocaInst)mod.Functions[0].BasicBlocks[0].Instructions[0];
        var st = (StructType)alloca.AllocatedType;
        Assert.True(st.IsPacked);
        Assert.Equal(2, st.ElementTypes.Count);
    }

    // ==================== 结构性测试 ====================

    [Fact]
    public void Parse_多函数()
    {
        var ll =
            @"
declare i32 @foo(i32)

define i32 @bar(i32 %x) {
entry:
  %r = call i32 @foo(i32 %x)
  ret i32 %r
}
";
        var mod = _parser.Parse(ll);
        Assert.Equal(2, mod.Functions.Count);
        Assert.True(mod.Functions[0].IsDeclaration);
        Assert.False(mod.Functions[1].IsDeclaration);
        Assert.Equal("foo", mod.Functions[0].Name);
        Assert.Equal("bar", mod.Functions[1].Name);
    }

    [Fact]
    public void Parse_函数属性_跳过不报错()
    {
        var ll =
            @"
define i32 @test(i32 %a) noinline nounwind {
entry:
  ret i32 %a
}
";
        var mod = _parser.Parse(ll);
        var func = mod.Functions[0];
        Assert.Single(func.Parameters);
        Assert.Equal("test", func.Name);
    }

    [Fact]
    public void Parse_参数属性_跳过不报错()
    {
        var ll =
            @"
define i32 @test(i32 signext %a, i32 zeroext %b) {
entry:
  %r = add i32 %a, %b
  ret i32 %r
}
";
        var mod = _parser.Parse(ll);
        Assert.Equal("a", mod.Functions[0].Parameters[0].Name);
        Assert.Equal("b", mod.Functions[0].Parameters[1].Name);
    }

    [Fact]
    public void Parse_可变参数函数定义()
    {
        var ll =
            @"
define i32 @variadic(i32 %count, ...) {
entry:
  ret i32 %count
}
";
        var mod = _parser.Parse(ll);
        var func = mod.Functions[0];
        Assert.True(func.FunctionType.IsVarArg);
        Assert.Single(func.Parameters);
    }

    [Fact]
    public void Parse_无参数函数()
    {
        var ll =
            @"
define i32 @noargs() {
entry:
  ret i32 0
}
";
        var mod = _parser.Parse(ll);
        Assert.Empty(mod.Functions[0].Parameters);
        Assert.Empty(mod.Functions[0].FunctionType.ParameterTypes);
    }

    [Fact]
    public void Parse_前向引用解析()
    {
        var ll =
            @"
define i32 @fwd() {
block_c:
  %z = phi i32 [ %x, %block_a ], [ %y, %block_b ]
  ret i32 %z
block_a:
  %x = add i32 1, 2
  br label %block_c
block_b:
  %y = add i32 3, 4
  br label %block_c
}
";
        var mod = _parser.Parse(ll);
        var func = mod.Functions[0];
        Assert.Equal(3, func.BasicBlocks.Count);

        var phi = (PHINode)func.BasicBlocks[0].Instructions[0];
        Assert.Equal(2, phi.Incoming.Count);

        Assert.IsType<BinaryOperator>(phi.Incoming[0].Value);
        Assert.IsType<BinaryOperator>(phi.Incoming[1].Value);
        Assert.Equal("block_a", phi.Incoming[0].Block.Name);
        Assert.Equal("block_b", phi.Incoming[1].Block.Name);
    }

    [Fact]
    public void Parse_注释不影响解析()
    {
        var ll =
            @"
; top-level comment
define i32 @test() { ; inline
entry:
  ; inside block
  ret i32 0 ; return
}
";
        var mod = _parser.Parse(ll);
        Assert.Single(mod.Functions);
        var ret = (ReturnInst)mod.Functions[0].BasicBlocks[0].Instructions[0];
        Assert.Equal(0L, ((ConstantInt)ret.ReturnValue!).Value);
    }

    [Fact]
    public void Parse_GepInBounds()
    {
        var ll =
            @"
define ptr @test(ptr %p) {
entry:
  %r = getelementptr inbounds i32, ptr %p, i32 3
  ret ptr %r
}
";
        var mod = _parser.Parse(ll);
        var gep = (GetElementPtrInst)mod.Functions[0].BasicBlocks[0].Instructions[0];
        Assert.True(gep.InBounds);
        Assert.Equal(IrType.Int32, gep.SourceElementType);
    }
}
