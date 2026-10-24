// ExternalReferenceAssemblerTests.cs — P2：外部引用在装配层的真实语义。
//
// 覆盖：
//   · 已解析的调用也必须可重定位（链接期函数会移动）；
//   · 未解析的 Addr32 写成哨兵 0xFFFFFFFF（绝不伪装成合法的 0 地址调用）；
//   · 默认（不允许未解析）仍是硬错误；
//   · 解析成功的 Addr32 把模块局部目标 PC 存进 Addend（链接期重定基标签用）。
using IronST.Runtime.Shared;

namespace IronST.Runtime.Tests;

public class ExternalReferenceAssemblerTests
{
    [Fact]
    public void RecordResolvedCalls_RecordsBackwardCall_ForLinking()
    {
        var asm = new BytecodeAssembler { RecordResolvedCalls = true };
        asm.DefFunc("Helper", 0, () => { });
        asm.CallFunc("Helper");

        var record = Assert.Single(asm.Relocations.Records);
        Assert.Equal(RelocationKind.Addr32, record.Kind);
        Assert.Equal("Helper", record.SymbolName);
    }

    [Fact]
    public void AllowUnresolvedAddr32_WritesSentinel_NotZero()
    {
        var asm = new BytecodeAssembler { AllowUnresolvedAddr32 = true };
        asm.CallFunc("Add");
        asm.Raw.BRK();

        var module = asm.Build();

        Assert.Equal(
            BytecodeAssembler.UnresolvedAddr32Sentinel,
            BitConverter.ToUInt32(module.Code, 1));
        Assert.Equal(["Add"], asm.UnresolvedSymbols);
    }

    [Fact]
    public void UnresolvedSymbols_AreDeduplicated_AndOrdered()
    {
        var asm = new BytecodeAssembler { AllowUnresolvedAddr32 = true };
        asm.CallFunc("Add");
        asm.CallFunc("Mul");
        asm.CallFunc("Add");
        asm.Build();

        Assert.Equal(["Add", "Mul"], asm.UnresolvedSymbols);
    }

    [Fact]
    public void AllowUnresolvedAddr32_False_StillThrows()
    {
        var asm = new BytecodeAssembler();
        asm.CallFunc("Add");

        var ex = Assert.Throws<InvalidOperationException>(() => asm.Build());
        Assert.Contains("Undefined label or function", ex.Message);
    }

    [Fact]
    public void ResolvedAddr32_StoresLocalPcInAddend()
    {
        var asm = new BytecodeAssembler();
        asm.Jmp("end");      // PC 0..4
        asm.Raw.BRK();       // PC 5
        asm.Label("end");    // PC 6
        asm.Raw.RET();

        asm.Build();

        var record = Assert.Single(asm.Relocations.Records);
        Assert.Equal("end", record.SymbolName);
        Assert.Equal(6, record.Addend);
    }

    [Fact]
    public void Build_Twice_WithResolvedCalls_IsByteIdentical()
    {
        var asm = new BytecodeAssembler { RecordResolvedCalls = true };
        asm.DefFunc("Helper", 0, () => { });
        asm.CallFunc("Helper");

        var first = asm.Build().Code;
        var second = asm.Build().Code;

        Assert.Equal(first, second);
    }
}
