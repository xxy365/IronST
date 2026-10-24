using IronST.LLVMCodeGen.Tests.Helpers;

namespace IronST.LLVMCodeGen.Tests;

public class GlobalVariableCodeGenTests
{
    [Fact]
    public void VarGlobal_ShouldDeclareAsGlobalI32()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM MainProg
            VAR_GLOBAL
                gCounter : DINT;
            END_VAR
                gCounter := 100;
            END_PROGRAM
            """);

        // 验证全局变量声明：@gCounter = global i32 0, align 4
        Assert.Contains("@gCounter = global i32 0, align 4", ir);
        // 验证 store 目标是 @gCounter
        Assert.Contains("store i32 100, ptr @gCounter", ir);
    }

    [Fact]
    public void VarGlobal_Assignment_ShouldStoreToGlobal()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM MainProg
            VAR_GLOBAL
                gValue : DINT;
            END_VAR
                gValue := 42;
            END_PROGRAM
            """);

        Assert.Contains("@gValue = global i32 0, align 4", ir);
        Assert.Contains("store i32 42, ptr @gValue", ir);
    }

    [Fact]
    public void VarGlobal_MultipleTypes_ShouldDeclareAllWithCorrectTypes()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM MainProg
            VAR_GLOBAL
                gInt : DINT;
                gReal : REAL;
                gFlag : BOOL;
            END_VAR
                gInt := 1;
                gReal := 2.5;
                gFlag := TRUE;
            END_PROGRAM
            """);

        // 每个全局变量声明，类型和对齐正确
        Assert.Contains("@gInt = global i32 0, align 4", ir);
        Assert.Contains("@gReal = global float 0, align 4", ir);
        Assert.Contains("@gFlag = global i1 0, align 1", ir);
    }

    [Fact]
    public void VarGlobal_ReadAccess_ShouldLoadFromGlobal()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM MainProg
            VAR_GLOBAL
                gVal : DINT;
            END_VAR
            VAR
                local : DINT;
            END_VAR
                local := gVal;
            END_PROGRAM
            """);

        // load 源是全局变量 @gVal
        Assert.Contains("load i32, ptr @gVal", ir);
    }

    [Fact]
    public void VarExternal_ShouldReferenceSameGlobal()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM Producer
            VAR_GLOBAL
                shared : DINT;
            END_VAR
                shared := 99;
            END_PROGRAM

            PROGRAM Consumer
            VAR_EXTERNAL
                shared : DINT;
            END_VAR
                shared := 200;
            END_PROGRAM
            """);

        // 全局变量只声明一次
        Assert.Contains("@shared = global i32 0, align 4", ir);
        // 两个 PROGRAM 都 store 到同一个 @shared
        Assert.Contains("store i32 99, ptr @shared", ir);
        Assert.Contains("store i32 200, ptr @shared", ir);
    }
}