using IronST.LLVMCodeGen.Tests.Helpers;

namespace IronST.LLVMCodeGen.Tests;

public class FunctionCodeGenTests
{
    [Fact]
    public void FunctionWithReturnValue_ShouldGenerateRetInst()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            FUNCTION Double : DINT
            VAR_INPUT
                x : DINT;
            END_VAR
                Double := x * 2;
            END_FUNCTION

            PROGRAM P
            VAR r : DINT; END_VAR
                r := Double(21);
            END_PROGRAM
            """);

        // 函数签名
        Assert.Contains("define i32 @Double(i32 %x)", ir);
        // 输入参数拷贝到栈
        Assert.Contains("store i32 %x, ptr %x.addr", ir);
        // 从栈加载参数
        Assert.Contains("load i32, ptr %x.addr", ir);
        // 函数体有乘法 x * 2
        Assert.Contains("mul i32", ir);
        // 函数体有 ret
        Assert.Contains("ret i32", ir);
        // Program 中调用
        Assert.Contains("call i32 @Double(i32 21)", ir);
    }

    [Fact]
    public void FunctionWithInputParams_ShouldCopyToAlloca()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            FUNCTION Add : DINT
            VAR_INPUT
                a : DINT;
                b : DINT;
            END_VAR
                Add := a + b;
            END_FUNCTION

            PROGRAM P
            VAR r : DINT; END_VAR
                r := Add(10, 20);
            END_PROGRAM
            """);

        // 函数签名：两个 i32 参数
        Assert.Contains("define i32 @Add(i32 %a, i32 %b)", ir);
        // 两个参数分别拷贝到栈
        Assert.Contains("store i32 %a, ptr %a.addr", ir);
        Assert.Contains("store i32 %b, ptr %b.addr", ir);
        // 从栈加载两个参数
        Assert.Contains("load i32, ptr %a.addr", ir);
        Assert.Contains("load i32, ptr %b.addr", ir);
        // 加法
        Assert.Contains("add i32", ir);
        // 调用方
        Assert.Contains("call i32 @Add(i32 10, i32 20)", ir);
    }

    [Fact]
    public void FunctionCall_ShouldGenerateCallInst()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            FUNCTION Square : DINT
            VAR_INPUT x : DINT; END_VAR
                Square := x * x;
            END_FUNCTION

            PROGRAM P
            VAR r : DINT; END_VAR
                r := Square(5);
            END_PROGRAM
            """);

        // 函数定义
        Assert.Contains("define i32 @Square(i32 %x)", ir);
        // 函数体有乘法
        Assert.Contains("mul i32", ir);
        // 调用
        Assert.Contains("call i32 @Square(i32 5)", ir);
        // ret
        Assert.Contains("ret i32", ir);
    }

    [Fact]
    public void Program_ShouldGenerateVoidFunction()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM MainProg
            VAR x : DINT; END_VAR
                x := 1;
            END_PROGRAM
            """);

        // Program 为 void 函数，无参数
        Assert.Contains("define void @MainProg()", ir);
        // 变量分配和赋值
        Assert.Contains("alloca i32", ir);
        Assert.Contains("store i32 1, ptr %x", ir);
        // ret void
        Assert.Contains("ret void", ir);
    }

    [Fact]
    public void FunctionBlock_ShouldGenerateStructAndFunction()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            FUNCTION_BLOCK Counter
            VAR_INPUT
                step : DINT;
            END_VAR
            VAR
                count : DINT;
            END_VAR
                count := count + step;
            END_FUNCTION_BLOCK
            """);

        // 结构体类型定义（VAR_INPUT(step) + VAR(count) —— 二者都是实例状态）
        // 字段名按字母序：count = 索引 0，step = 索引 1。
        Assert.Contains("%Counter = type { i32, i32 }", ir);
        // FB 函数定义：仅实例指针；VAR_INPUT 不再占形参槽（输入经实例字段传递，§6.3.10）
        Assert.Contains("define void @Counter(ptr %__instance)", ir);
        // 输入不再拷贝到临时 alloca（改为经实例 GEP 访问）
        Assert.DoesNotContain("store i32 %step, ptr %step.addr", ir);
        // step（输入）与 count 成员都通过实例 GEP 访问
        Assert.Contains("getelementptr %Counter, ptr %__instance, i32 0, i32 1", ir);
        Assert.Contains("getelementptr %Counter, ptr %__instance, i32 0, i32 0", ir);
        // count := count + step
        Assert.Contains("add i32", ir);
        // ret void
        Assert.Contains("ret void", ir);
    }

    [Fact]
    public void FBAction_ShouldGenerateSeparateFunction()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            FUNCTION_BLOCK MyFB
            VAR
                state : DINT;
            END_VAR
                state := 1;
            ACTION Reset
                state := 0;
            END_ACTION
            END_FUNCTION_BLOCK
            """);

        // FB 主函数
        Assert.Contains("define void @MyFB(ptr %__instance)", ir);
        // Action 独立函数，同签名
        Assert.Contains("define void @MyFB.Reset(ptr %__instance)", ir);
        // 主函数中 state := 1 → store i32 1
        Assert.Contains("store i32 1", ir);
        // Action 中 state := 0 → store i32 0
        Assert.Contains("store i32 0", ir);
        // state 通过 GEP 访问
        Assert.Contains("getelementptr", ir);
        // 两个函数都有 ret void
        Assert.Contains("ret void", ir);
    }

    [Fact]
    public void NestedFunctionCall_ShouldNestCalls()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            FUNCTION AddOne : DINT
            VAR_INPUT x : DINT; END_VAR
                AddOne := x + 1;
            END_FUNCTION

            FUNCTION DoubleIt : DINT
            VAR_INPUT x : DINT; END_VAR
                DoubleIt := x * 2;
            END_FUNCTION

            PROGRAM P
            VAR r : DINT; END_VAR
                r := DoubleIt(AddOne(10));
            END_PROGRAM
            """);

        // 两个函数定义
        Assert.Contains("define i32 @AddOne(i32 %x)", ir);
        Assert.Contains("define i32 @DoubleIt(i32 %x)", ir);
        // 内层调用先执行
        Assert.Contains("call i32 @AddOne(i32 10)", ir);
        // 外层调用
        Assert.Contains("call i32 @DoubleIt", ir);
        // AddOne 内部有加法
        Assert.Contains("add i32", ir);
        // DoubleIt 内部有乘法
        Assert.Contains("mul i32", ir);
    }
}