// SpillSourceMapTests.cs — 调用后恢复 spill 槽的合成指令也要带源行。
//
// spill 的 SETL/GETL 由 FunctionEmitter 额外插入，本身没有独立源码位置；
// EmitInstr 会把 null 源回退到当前 IR 指令的 _currentSrc。该测试锁定这一
// 行为，避免以后调试器在跨调用表达式上丢失行号。
using IronST.LLVMCodeGen.CodeGen;
using IronST.LLVMCodeGen.Debug;
using IronST.Runtime.Shared;

namespace IronST.BytecodeGen.Tests;

public class SpillSourceMapTests
{
    private const string Source = """
        FUNCTION Inc : DINT
        VAR_INPUT
            x : DINT;
        END_VAR
            Inc := x + 1;
        END_FUNCTION

        PROGRAM Main
        VAR
            a : DINT;
            result : DINT;
        END_VAR
            a := 5;
            result := a + Inc(1);
        END_PROGRAM
        """;

    [Fact]
    public void SpillReloadAfterCall_InheritsUsingInstructionSource()
    {
        var analysis = FrontendTestHelper.ExpectSuccess(Source);
        var debugEmitter = new IRDebugEmitter();
        var module = new IRGenerator(debugEmitter).Generate(analysis);
        var irSourceMap = debugEmitter.BuildSourceMap(module, "test.st");
        var result = new ByteCodeGenerator("test.st")
            .Generate(module, irSourceMap: irSourceMap);

        var main = result.DebugModule.Functions.Single(f => f.Name == "Main");
        int callIndex = main.Instructions.FindIndex(i => i.Opcode == OpCode.CALL);
        Assert.True(callIndex >= 0, "expected Inc to be emitted as CALL");

        int addIndex = main.Instructions.FindIndex(
            callIndex + 1,
            i => i.Opcode == OpCode.IADD);
        Assert.True(addIndex > callIndex, "expected IADD after the call");

        var syntheticLoads = main.Instructions
            .Skip(callIndex + 1)
            .Take(addIndex - callIndex - 1)
            .Where(i => i.Opcode == OpCode.GETL)
            .ToList();

        Assert.NotEmpty(syntheticLoads);
        Assert.All(syntheticLoads, i =>
        {
            Assert.Equal("test.st", i.SrcFile);
            Assert.Equal(14, i.SrcLine);
        });
    }
}
