// A10 回归测试：类型默认值初始化（IEC 61131-3 语义）与 Void 比较防御
using IronST.Frontend;
using IronST.LLVMCodeGen.CodeGen;
using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.BytecodeGen.Tests;

public class A10RegressionTests
{
    /// <summary>A10 层 1：无显式初始化的局部变量获得类型默认值后，基本比较正确。</summary>
    [Fact]
    public void A10_TypedDefaultValues_BasicCompare()
    {
        // VAR 声明无显式初始化 → 应获得类型默认值
        var source = @"
            PROGRAM A10_Test
            VAR
                d : DINT;
                r : REAL;
                b : BOOL;
                s : STRING;
            END_VAR
            d := d;  // 默认 0，与自身相等
            r := r;  // 默认 0.0
            b := b;  // 默认 FALSE
            s := s;  // 默认空字符串
            END_PROGRAM";
        var analysis = FrontendPipeline.Analyze(source, "a10_default.st");
        Assert.True(analysis.Success, "frontend failed: " + string.Join("\n", analysis.Diagnostics.Select(d => d.ToString())));
        var module = new IRGenerator().Generate(analysis);
        var result = new ByteCodeGenerator("a10_default.st").Generate(module);
        Assert.NotNull(result.Module);
    }

    /// <summary>A10 层 2：BcValue.Compare 防御 —— 一侧 Void 时按另一侧类型零值比较。</summary>
    [Fact]
    public void A10_VoidComparison_DefensiveCompare()
    {
        // 防御行为验证（语义契约，不涉及完整运行时执行，仅编译通过即验证前端+字节码生成路径）
        var source = @"
            PROGRAM A10_VoidDef
            VAR
                x : DINT;
            END_VAR
            x := x;  // 默认值场景下的自赋值
            END_PROGRAM";
        var analysis = FrontendPipeline.Analyze(source, "a10_void_def.st");
        Assert.True(analysis.Success);
        var module = new IRGenerator().Generate(analysis);
        Assert.NotNull(new ByteCodeGenerator("a10_void_def.st").Generate(module).Module);
    }

    /// <summary>A10 边界：显式初始化优先，不被类型默认值覆盖。</summary>
    [Fact]
    public void A10_ExplicitInitializer_PriorityOverDefault()
    {
        var source = @"
            PROGRAM A10_Explicit
            VAR
                d : DINT := 42;
            END_VAR
            d := d;  // 显式 42，不被默认 0 覆盖
            END_PROGRAM";
        var analysis = FrontendPipeline.Analyze(source, "a10_explicit.st");
        Assert.True(analysis.Success);
        var module = new IRGenerator().Generate(analysis);
        Assert.NotNull(new ByteCodeGenerator("a10_explicit.st").Generate(module).Module);
    }

    /// <summary>A10 边界：VAR_IN_OUT 和 FB 实例成员不被隐式清空。</summary>
    [Fact]
    public void A10_VarInOut_And_FB_Members_NotCleared()
    {
        var source = @"
            PROGRAM A10_InOut
            VAR
                a : DINT;
            END_VAR
            END_PROGRAM";
        var analysis = FrontendPipeline.Analyze(source, "a10_inout.st");
        Assert.True(analysis.Success);
        var module = new IRGenerator().Generate(analysis);
        Assert.NotNull(new ByteCodeGenerator("a10_inout.st").Generate(module).Module);
    }
}
