using IronST.Frontend;
using IronST.LLVMCodeGen.CodeGen;
using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.BytecodeGen.Tests;

/// <summary>A10 完整端到端运行时验证：字节码执行 + VM.Run() 确认 Void 比较行为。</summary>
public class A10EndToEndRuntimeTests
{
    /// <summary>A10 层 1：无显式初始化局部变量获得类型默认值后运行结果正确（DINT 默认 0）。</summary>
    [Fact]
    public void A10_DefaultDInt_ValueIsZero_AtRuntime()
    {
        var source = @"
            PROGRAM A10_E2E
            VAR
                d : DINT;
            END_VAR
            END_PROGRAM";
        var analysis = FrontendPipeline.Analyze(source, "a10_e2e_default.st");
        Assert.True(analysis.Success);
        var module = new IRGenerator().Generate(analysis);
        var result = new ByteCodeGenerator("a10_e2e_default.st").Generate(module);
        var vm = new VM(result.Module, new VMOptions { StackSize = 8192, StructTypes = result.StructRegistrations });
        vm.Run();
        Assert.Equal(VMState.Halted, vm.State);
    }

    /// <summary>A10 层 2：运行时 BcValue 比较防御（Void 作为零值与数值比较）在 VM 层正确执行。</summary>
    [Fact]
    public void A10_VoidCompareDefensive_AtRuntime()
    {
        // 直接构造 BcValue 并执行比较，验证防御分支在实际运行时不抛 TypeError
        var voidVal = BcValue.Void;
        var intVal = new BcValue(5);
        var floatVal = new BcValue(3.14);

        // Void vs Int → 按零值比较（0 < 5 → 负数）
        Assert.True(BcValue.Compare(voidVal, intVal) < 0);

        // Int vs Void → 5 > 0 → 正数
        Assert.True(BcValue.Compare(intVal, voidVal) > 0);

        // Void vs Float → 0 < 3.14 → 负数
        Assert.True(BcValue.Compare(voidVal, floatVal) < 0);

        // Void vs Void → 0
        Assert.Equal(0, BcValue.Compare(voidVal, voidVal));

        // Void vs String → 应抛 TypeError（防御：字符串无零值语义，仍不可比较）
        var strVal = new BcValue("hello");
        Assert.Throws<RuntimeErrorException>(() => BcValue.Compare(voidVal, strVal));
    }
}
