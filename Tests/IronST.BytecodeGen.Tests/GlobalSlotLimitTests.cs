// GlobalSlotLimitTests.cs — F6：全局槽位 short 容量守卫
using IronST.BytecodeGen;
using IronST.IRBuilder.IR;
using IronST.Runtime.Shared;

namespace IronST.BytecodeGen.Tests;

/// <summary>
/// F6：全局槽位按 16 位有符号编码，而 <c>ByteCodeGenerator</c> 的循环计数器
/// <c>gIdx</c> 自身也是 <c>short</c>。一旦超出上限，<c>gIdx++</c> 从 32767 回绕成
/// -32768，<c>gIdx &lt; Count</c> 依然成立 → 死循环，且 <c>globalIndex</c> 被写入
/// 负索引。因此守卫必须放在<b>进入循环之前</b>。
/// </summary>
public class GlobalSlotLimitTests
{
    private static Module ModuleWithGlobals(int count)
    {
        var m = new Module("limit.st");
        for (int i = 0; i < count; i++)
            m.AddGlobalVariable(
                new GlobalVariable($"g{i}", IrType.Int32, false, LinkageType.Internal)
            );
        return m;
    }

    /// <summary>恰好 32767 个全局变量：最后一个有效索引是 32766，应当正常编译。</summary>
    [Fact]
    public void Generate_AtLimit_Succeeds()
    {
        var result = new ByteCodeGenerator("limit.st")
            .Generate(ModuleWithGlobals(BytecodeAssembler.MaxGlobals));

        Assert.Equal(BytecodeAssembler.MaxGlobals, result.Module.GlobalCount);
        Assert.Equal(BytecodeAssembler.MaxGlobals, result.DebugModule.GlobalNames.Count);
        Assert.Equal("g0", result.DebugModule.GlobalNames[0]);
        Assert.Equal(
            $"g{BytecodeAssembler.MaxGlobals - 1}",
            result.DebugModule.GlobalNames[^1]
        );
    }

    /// <summary>
    /// 32768 个：必须<b>抛异常</b>而不是挂死。本用例若无守卫会无限循环，
    /// 因此它本身就是「死循环已修复」的证据。
    /// </summary>
    [Fact]
    public void Generate_OnePastLimit_ThrowsInsteadOfHanging()
    {
        var module = ModuleWithGlobals(BytecodeAssembler.MaxGlobals + 1);

        var ex = Assert.Throws<InvalidOperationException>(
            () => new ByteCodeGenerator("limit.st").Generate(module)
        );

        // 消息须带上限，便于定位是哪条容量约束被触发
        Assert.Contains(BytecodeAssembler.MaxGlobals.ToString(), ex.Message);
    }
}
