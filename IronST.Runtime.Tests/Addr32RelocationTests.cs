// Addr32RelocationTests.cs — 0B 步骤 3 · 切片 2：Addr32 走"记录—解析—回填"链路
using IronST.Runtime.Shared;

namespace IronST.Runtime.Tests;

/// <summary>
/// 切片 2 把 Addr32 从 <c>_pendingPatches</c> 的即时补丁迁移到 <c>RelocationTable</c>：
/// 发射期<b>记录</b>，<c>ApplyPatches</c> 按记录<b>解析</b>符号并<b>回填</b>。
/// Off8 保持原路径不动（逐类迁移）。
///
/// <para>
/// 指令布局：<c>JMP/CALL = [op][LE32]</c> 共 5 字节，故操作数偏移 = 操作码偏移 + 1。
/// </para>
/// </summary>
public class Addr32RelocationTests
{
    // ═══════════════════════════════════════════════════════════════
    //  记录：三个发射点
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Jmp_ForwardTarget_RecordsAddr32()
    {
        var asm = new BytecodeAssembler();
        asm.Jmp("end");
        asm.Raw.BRK();
        asm.Label("end");

        var r = Assert.Single(asm.Relocations.Records);

        Assert.Equal(RelocationKind.Addr32, r.Kind);
        Assert.Equal(RelocationSection.Code, r.Section);
        Assert.Equal("end", r.SymbolName);
        Assert.Equal(1, r.Offset);              // JMP 在 PC 0，操作数从 1 开始
        Assert.Equal(32, r.EncodingWidthBits);
        Assert.Equal(0, r.Addend);
    }

    [Fact]
    public void Call_ForwardTarget_RecordsAddr32()
    {
        var asm = new BytecodeAssembler();
        asm.Call("later");
        asm.Raw.BRK();
        asm.Label("later");

        var r = Assert.Single(asm.Relocations.Records);

        Assert.Equal(RelocationKind.Addr32, r.Kind);
        Assert.Equal("later", r.SymbolName);
        Assert.Equal(1, r.Offset);
    }

    [Fact]
    public void CallFunc_UndefinedAtEmitTime_RecordsAddr32()
    {
        var asm = new BytecodeAssembler();
        asm.CallFunc("Helper");                            // 前向引用 → 记录
        asm.DefFunc("Helper", 0, () => { });

        var r = Assert.Single(asm.Relocations.Records);

        Assert.Equal(RelocationKind.Addr32, r.Kind);
        Assert.Equal("Helper", r.SymbolName);
        Assert.Equal(1, r.Offset);
    }

    [Fact]
    public void CallFunc_DefinedAtEmitTime_DoesNotRecord()
    {
        // 已定义的函数在发射期直接解析 —— 不产生重定位（与迁移前一致）
        var asm = new BytecodeAssembler();
        asm.DefFunc("Helper", 0, () => { });
        asm.CallFunc("Helper");

        Assert.Empty(asm.Relocations.Records);
        var m = asm.Build();
        Assert.NotEmpty(m.Code);
    }

    [Fact]
    public void Records_SourceIsUnknown_AtAssemblerLayer()
    {
        // 汇编器拿不到源码行号；记录显式标记为未知，等上层接入后补（已知缺口）
        var asm = new BytecodeAssembler();
        asm.Jmp("end");
        asm.Label("end");

        Assert.True(asm.Relocations.Records.Single().Source.IsUnknown);
    }

    // ═══════════════════════════════════════════════════════════════
    //  解析 + 回填：与迁移前逐字节一致
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Build_PatchesFromTable_BytesMatchLabelPC()
    {
        var asm = new BytecodeAssembler();
        asm.Jmp("target");                 // PC 0..4
        asm.Raw.BRK();                     // PC 5
        asm.Label("target");               // PC 6
        asm.Raw.RET();

        var m = asm.Build();

        Assert.Equal(6u, BitConverter.ToUInt32(m.Code, 1));
    }

    [Fact]
    public void CallFunc_Forward_ResolvesToFunctionPC()
    {
        var asm = new BytecodeAssembler();
        asm.CallFunc("Helper");            // PC 0..4
        asm.DefFunc("Helper", 0, () => { });   // funcPC = 5

        var m = asm.Build();

        Assert.Equal(5u, BitConverter.ToUInt32(m.Code, 1));
    }

    [Fact]
    public void MixedOff8AndAddr32_BothApply()
    {
        // 两条链路并存：条件分支走 Off8 原路径，无条件跳转走 Addr32 记录表
        var asm = new BytecodeAssembler();
        asm.Label("top");                  // PC 0
        asm.Bz(0, "skip");                 // PC 0..2，patch offset 2
        asm.Jmp("end");                    // PC 3..7，patch offset 4
        asm.Label("skip");                 // PC 8
        asm.Raw.BRK();                     // PC 8
        asm.Label("end");                  // PC 9
        asm.Raw.RET();

        var m = asm.Build();

        Assert.Equal(5, (sbyte)m.Code[2]);                 // Off8：rel = 8 - 3 = 5
        Assert.Equal(9u, BitConverter.ToUInt32(m.Code, 4)); // Addr32：目标 PC = 9
        Assert.Single(asm.Relocations.Records);            // 表里只有 Addr32 那条
    }

    [Fact]
    public void Build_Twice_ProducesSameBytes()
    {
        // ApplyPatches 幂等：再次 Build 不会重复改写
        var asm = new BytecodeAssembler();
        asm.Jmp("end");
        asm.Raw.BRK();
        asm.Label("end");
        asm.Raw.RET();

        var first = asm.Build().Code;
        var second = asm.Build().Code;

        Assert.Equal(first, second);
    }

    [Fact]
    public void Records_SurviveBuild()
    {
        // 回填后记录仍保留（供链接器读取），不因解析而被清空
        var asm = new BytecodeAssembler();
        asm.Jmp("end");
        asm.Raw.BRK();
        asm.Label("end");
        asm.Raw.RET();

        var m = asm.Build();

        Assert.Single(asm.Relocations.Records);
        Assert.Equal("end", asm.Relocations.Records[0].SymbolName);
        Assert.Equal(6u, BitConverter.ToUInt32(m.Code, 1));
    }

    // ═══════════════════════════════════════════════════════════════
    //  诊断
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void UndefinedLabel_DiagnosticIncludesReferenceSite()
    {
        // DG-1：未定义符号错误必须含引用来源（此处给到代码段内偏移）
        var asm = new BytecodeAssembler();
        asm.Jmp("missing");

        var ex = Assert.Throws<InvalidOperationException>(() => asm.Build());

        Assert.Contains("Undefined label or function: 'missing'", ex.Message);
        Assert.Contains("code offset 1", ex.Message);
        Assert.Contains("Addr32", ex.Message);
    }

    [Fact]
    public void UndefinedFunction_DiagnosticIncludesReferenceSite()
    {
        var asm = new BytecodeAssembler();
        asm.CallFunc("Nope");

        var ex = Assert.Throws<InvalidOperationException>(() => asm.Build());

        Assert.Contains("Undefined label or function: 'Nope'", ex.Message);
        Assert.Contains("code offset 1", ex.Message);
    }

    [Fact]
    public void Off8_DiagnosticKeepsOriginalMessage()
    {
        // Off8 尚未迁移，诊断原文不变（逐类迁移，不动未迁移路径）
        var asm = new BytecodeAssembler();
        asm.Label("start");
        asm.Bz(0, "missing");

        var ex = Assert.Throws<InvalidOperationException>(() => asm.Build());

        Assert.Equal("Undefined label or function: 'missing'", ex.Message);
    }

    [Fact]
    public void ApplyPatches_RejectsNonAddr32Records()
    {
        // 逐类迁移：表里出现尚未迁移的种类时显式拒绝，不静默跳过
        var asm = new BytecodeAssembler();
        asm.Relocations.Add(RelocationRecord.Create(
            RelocationKind.Off8, RelocationSection.Code, 1, "x"));

        var ex = Assert.Throws<InvalidOperationException>(() => asm.Build());

        Assert.Contains("Off8", ex.Message);
        Assert.Contains("not handled", ex.Message);
    }
}
