// C8 回归测试：验证寄存器回收机制（临时寄存器安全回收 + 死值回收）在顺序块中正确工作
using IronST.Frontend;
using IronST.LLVMCodeGen.CodeGen;
using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.BytecodeGen.Tests;

public class C8RegressionTests
{
    /// <summary>
    /// C8 修复验证：100 条普通语句可编译且不溢出寄存器（临时寄存器安全回收生效）。
    /// </summary>
    [Fact]
    public void OneHundredOrdinaryStatements_Compile()
    {
        var source = $$"""
            PROGRAM Main
            VAR
                a : ARRAY[0..99] OF DINT;
            END_VAR
            {{string.Join("\n", System.Linq.Enumerable.Range(0, 100).Select(i => $"    a[{i}] := {i};"))}}
            END_PROGRAM
            """;
        var analysis = FrontendPipeline.Analyze(source, "c8_reg.st");
        Assert.True(analysis.Success, "frontend failed: " + string.Join("\n", analysis.Diagnostics.Select(d => d.ToString())));
        var module = new IRGenerator().Generate(analysis);
        var result = new ByteCodeGenerator("c8_reg.st").Generate(module);
        Assert.NotNull(result.Module);
    }
}
