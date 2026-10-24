// ArrayBoundsEndToEndTests.cs — 数组越界端到端（C1）
// ST 源码 → FrontendPipeline → IRGenerator → ByteCodeGenerator → 发布 VM 执行。
using IronST.LLVMCodeGen.CodeGen;
using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.BytecodeGen.Tests;

public class ArrayBoundsEndToEndTests
{
    private static VM CompileAndRun(string source)
    {
        var analysis = FrontendTestHelper.Analyze(source);
        Assert.True(analysis.Success);

        var module = new IRGenerator().Generate(analysis);
        var result = new ByteCodeGenerator("test.st").Generate(module);

        var vm = new VM(result.Module, new VMOptions
        {
            StackSize = 8192,
            StructTypes = result.StructRegistrations,
        });
        vm.Run();
        return vm;
    }

    [Fact]
    public void ArrayInBounds_ExecutesToHalt()
    {
        var vm = CompileAndRun("""
            PROGRAM Main
            VAR a : ARRAY[0..9] OF INT; i : DINT; END_VAR
                i := 5;
                a[i] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
    }

    [Fact]
    public void ArrayOutOfBounds_ThrowsArrayBounds()
    {
        var ex = Assert.Throws<RuntimeErrorException>(() => CompileAndRun("""
            PROGRAM Main
            VAR a : ARRAY[0..9] OF INT; i : DINT; END_VAR
                i := 10;
                a[i] := 1;
            END_PROGRAM
            """));

        Assert.Equal(RuntimeErrorType.ArrayBounds, ex.ErrorType);
    }

    [Fact]
    public void ArrayNegativeIndex_ThrowsArrayBounds()
    {
        var ex = Assert.Throws<RuntimeErrorException>(() => CompileAndRun("""
            PROGRAM Main
            VAR a : ARRAY[0..9] OF INT; i : DINT; END_VAR
                i := -1;
                a[i] := 1;
            END_PROGRAM
            """));

        Assert.Equal(RuntimeErrorType.ArrayBounds, ex.ErrorType);
    }

    [Fact]
    public void ArrayConstantIndex_OutOfBounds_ThrowsArrayBounds()
    {
        // 常量下标 99 也应有 BOUNDS 检查（编译期不折叠，运行时拦截）
        var ex = Assert.Throws<RuntimeErrorException>(() => CompileAndRun("""
            PROGRAM Main
            VAR a : ARRAY[0..9] OF INT; END_VAR
                a[99] := 1;
            END_PROGRAM
            """));

        Assert.Equal(RuntimeErrorType.ArrayBounds, ex.ErrorType);
    }
}
