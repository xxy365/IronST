// ═══════════════════════════════════════════════════════════════
//  SourceLocationTests.cs — 源码位置追踪 + SourceMap
//
//  测试 DebugInstr / DebugFunction 数据结构
//  测试 DebugModule.BuildSourceMap / SourceMap.FindByPc / ToJson / Dump
//
//  CompileResult.SourceMap PC 对齐测试已移除，
//  归入 CodeGenIntegrationTests（需真实 LLVM IR 路径）。
// ═══════════════════════════════════════════════════════════════
using OpCode = IronST.Runtime.Shared.OpCode;

namespace IronST.BytecodeGen.Tests;

public class SourceLocationTests
{
    // ═══════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════

    /// <summary>无参函数：FRAME + body + LEAVE + RET</summary>
    private static DebugFunction Func(string name, int locals, params DebugInstr[] body)
    {
        var list = new List<DebugInstr> { DebugInstr.Frame((short)locals) };
        list.AddRange(body);
        list.Add(DebugInstr.Op(OpCode.LEAVE));
        list.Add(DebugInstr.Op(OpCode.RET));
        var f = new DebugFunction
        {
            Name = name,
            ParamCount = 0,
            TotalLocals = locals,
        };
        f.Instructions.AddRange(list);
        return f;
    }

    /// <summary>手动构建 DebugFunction（完全控制每条指令）。</summary>
    private static DebugFunction RawFunc(
        string name,
        int paramCount,
        int locals,
        params DebugInstr[] instrs)
    {
        var f = new DebugFunction
        {
            Name = name,
            ParamCount = paramCount,
            TotalLocals = locals,
        };
        f.Instructions.AddRange(instrs);
        return f;
    }

    /// <summary>快速标注源码位置。</summary>
    private static DebugInstr WithSrc(DebugInstr instr, string file, int line)
    {
        instr.SrcFile = file;
        instr.SrcLine = line;
        return instr;
    }

    // ═══════════════════════════════════════════════════════════
    //  Category A — DebugInstr 数据结构
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void DebugInstr_DefaultSourceFields_AreNullAndZero()
    {
        var instr = DebugInstr.I8(OpCode.LDC, 8, 42);
        Assert.Null(instr.SrcFile);
        Assert.Equal(0, instr.SrcLine);
    }

    [Fact]
    public void DebugInstr_SetSourceFields_RoundTrips()
    {
        var instr = DebugInstr.Slot(OpCode.SETL, 8, 0);
        instr.SrcFile = "calc.st";
        instr.SrcLine = 15;
        Assert.Equal("calc.st", instr.SrcFile);
        Assert.Equal(15, instr.SrcLine);
    }

    // ═══════════════════════════════════════════════════════════
    //  Category A — DebugFunction 数据结构
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void DebugFunction_DefaultSourceFields_AreNullAndZero()
    {
        var func = new DebugFunction { Name = "Main" };
        Assert.Null(func.SourceFile);
        Assert.Equal(0, func.SourceStartLine);
        Assert.Equal(0, func.SourceEndLine);
    }

    [Fact]
    public void DebugFunction_SetSourceFields_RoundTrips()
    {
        var func = new DebugFunction
        {
            Name = "Main",
            SourceFile = "test.st",
            SourceStartLine = 1,
            SourceEndLine = 30,
        };
        Assert.Equal("test.st", func.SourceFile);
        Assert.Equal(1, func.SourceStartLine);
        Assert.Equal(30, func.SourceEndLine);
    }

    // ═══════════════════════════════════════════════════════════
    //  Category A — SourceMap 值相等
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void SourceLoc_ValueEquality()
    {
        var a = new SourceMap.SourceLoc("test.st", 10);
        var b = new SourceMap.SourceLoc("test.st", 10);
        var c = new SourceMap.SourceLoc("test.st", 11);
        var d = new SourceMap.SourceLoc("other.st", 10);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.NotEqual(a, d);
    }

    [Fact]
    public void FunctionRange_ValueEquality()
    {
        var a = new SourceMap.FunctionRange("Main", "test.st", 1, 10, 0);
        var b = new SourceMap.FunctionRange("Main", "test.st", 1, 10, 0);
        var c = new SourceMap.FunctionRange("Main", "test.st", 1, 20, 0);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    // ═══════════════════════════════════════════════════════════
    //  Category B — DebugModule.BuildSourceMap 基础
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void BuildSourceMap_EmptyModule_ReturnsEmptyMap()
    {
        var dm = new DebugModule();
        var map = dm.BuildSourceMap();
        Assert.Empty(map.InstructionMap);
        Assert.Empty(map.FunctionRanges);
    }

    [Fact]
    public void BuildSourceMap_NoSourceLocations_InstructionMapEmpty()
    {
        var dm = new DebugModule();
        dm.Functions.Add(
            Func("Main", 1,
                DebugInstr.I8(OpCode.LDC, 8, 42),
                DebugInstr.Slot(OpCode.SETL, 8, 0)));

        var map = dm.BuildSourceMap();
        Assert.Empty(map.InstructionMap);
    }

    [Fact]
    public void BuildSourceMap_WithSourceLocations_MapsByInstructionIndex()
    {
        var func = RawFunc(
            "Main",
            0,
            1,
            DebugInstr.Frame(1),
            WithSrc(DebugInstr.I8(OpCode.LDC, 8, 42), "test.st", 5),
            WithSrc(DebugInstr.Slot(OpCode.SETL, 8, 0), "test.st", 5),
            WithSrc(DebugInstr.Op(OpCode.LEAVE), "test.st", 6),
            DebugInstr.Op(OpCode.RET));

        var dm = new DebugModule();
        dm.Functions.Add(func);

        var map = dm.BuildSourceMap();

        Assert.Equal(3, map.InstructionMap.Count);
        Assert.Equal(new SourceMap.SourceLoc("test.st", 5), map.InstructionMap[3]);
        Assert.Equal(new SourceMap.SourceLoc("test.st", 5), map.InstructionMap[9]);
        Assert.Equal(new SourceMap.SourceLoc("test.st", 6), map.InstructionMap[13]);
        Assert.False(map.InstructionMap.ContainsKey(0));
        Assert.False(map.InstructionMap.ContainsKey(14));
    }

    // ═══════════════════════════════════════════════════════════
    //  Category B — BuildSourceMap 函数范围
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void BuildSourceMap_FunctionRange_PopulatedWhenSourceFileSet()
    {
        var func = new DebugFunction
        {
            Name = "Main",
            ParamCount = 0,
            TotalLocals = 0,
            SourceFile = "test.st",
            SourceStartLine = 1,
            SourceEndLine = 30,
        };
        func.Instructions.Add(DebugInstr.Frame(0));
        func.Instructions.Add(DebugInstr.Op(OpCode.LEAVE));
        func.Instructions.Add(DebugInstr.Op(OpCode.RET));

        var dm = new DebugModule();
        dm.Functions.Add(func);

        var map = dm.BuildSourceMap();

        Assert.Single(map.FunctionRanges);
        Assert.Equal("Main", map.FunctionRanges[0].Name);
        Assert.Equal("test.st", map.FunctionRanges[0].File);
        Assert.Equal(1, map.FunctionRanges[0].StartLine);
        Assert.Equal(30, map.FunctionRanges[0].EndLine);
        Assert.Equal(0, map.FunctionRanges[0].EntryPc);
    }

    [Fact]
    public void BuildSourceMap_FunctionRange_AlwaysRecorded_EvenWithoutSourceFile()
    {
        var func = new DebugFunction
        {
            Name = "Main",
            ParamCount = 0,
            TotalLocals = 0,
        };
        func.Instructions.Add(DebugInstr.Frame(0));
        func.Instructions.Add(DebugInstr.Op(OpCode.LEAVE));
        func.Instructions.Add(DebugInstr.Op(OpCode.RET));

        var dm = new DebugModule();
        dm.Functions.Add(func);

        var map = dm.BuildSourceMap();

        Assert.Single(map.FunctionRanges);
        Assert.Equal("Main", map.FunctionRanges[0].Name);
        Assert.Equal("", map.FunctionRanges[0].File);
        Assert.Equal(0, map.FunctionRanges[0].StartLine);
        Assert.Equal(0, map.FunctionRanges[0].EndLine);
        Assert.Equal(0, map.FunctionRanges[0].EntryPc);
    }

    [Fact]
    public void BuildSourceMap_FunctionRange_RecordedEvenWhenSourceFileEmpty()
    {
        var func = new DebugFunction
        {
            Name = "Main",
            ParamCount = 0,
            TotalLocals = 0,
            SourceFile = "",
        };
        func.Instructions.Add(DebugInstr.Frame(0));
        func.Instructions.Add(DebugInstr.Op(OpCode.LEAVE));
        func.Instructions.Add(DebugInstr.Op(OpCode.RET));

        var dm = new DebugModule();
        dm.Functions.Add(func);

        var map = dm.BuildSourceMap();

        Assert.Single(map.FunctionRanges);
        Assert.Equal("Main", map.FunctionRanges[0].Name);
        Assert.Equal("", map.FunctionRanges[0].File);
    }

    [Fact]
    public void BuildSourceMap_MultipleFunctions_OffsetsAccumulate()
    {
        var func1 = new DebugFunction
        {
            Name = "Main",
            ParamCount = 0,
            TotalLocals = 0,
            SourceFile = "a.st",
            SourceStartLine = 1,
            SourceEndLine = 10,
        };
        func1.Instructions.AddRange(
            new[]
            {
                WithSrc(DebugInstr.Frame(0), "a.st", 1),
                DebugInstr.Op(OpCode.LEAVE),
                DebugInstr.Op(OpCode.RET),
            });

        var func2 = new DebugFunction
        {
            Name = "Helper",
            ParamCount = 0,
            TotalLocals = 0,
            SourceFile = "b.st",
            SourceStartLine = 20,
            SourceEndLine = 30,
        };
        func2.Instructions.AddRange(
            new[]
            {
                WithSrc(DebugInstr.Frame(0), "b.st", 20),
                DebugInstr.Op(OpCode.LEAVE),
                DebugInstr.Op(OpCode.RET),
            });

        var dm = new DebugModule();
        dm.Functions.Add(func1);
        dm.Functions.Add(func2);

        var map = dm.BuildSourceMap();

        Assert.Equal(2, map.InstructionMap.Count);
        Assert.Equal(new SourceMap.SourceLoc("a.st", 1), map.InstructionMap[0]);
        Assert.Equal(new SourceMap.SourceLoc("b.st", 20), map.InstructionMap[5]);

        Assert.Equal(2, map.FunctionRanges.Count);
        Assert.Equal(0, map.FunctionRanges[0].EntryPc);
        Assert.Equal(5, map.FunctionRanges[1].EntryPc);
    }

    // ═══════════════════════════════════════════════════════════
    //  Category B — SourceMap.FindByPc 精确匹配
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void FindByPc_ExactMatch_ReturnsLocation()
    {
        var dm = new DebugModule();
        var func = RawFunc(
            "Main",
            0,
            0,
            WithSrc(DebugInstr.Frame(0), "test.st", 1),
            WithSrc(DebugInstr.Op(OpCode.LEAVE), "test.st", 2),
            DebugInstr.Op(OpCode.RET));
        dm.Functions.Add(func);

        var map = dm.BuildSourceMap();

        Assert.NotNull(map.FindByPc(0));
        Assert.Equal(1, map.FindByPc(0)!.Value.Line);

        Assert.NotNull(map.FindByPc(3));
        Assert.Equal(2, map.FindByPc(3)!.Value.Line);
    }

    // ═══════════════════════════════════════════════════════════
    //  Category B — SourceMap.FindByPc 最近匹配
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void FindByPc_BetweenMapped_ReturnsPrevious()
    {
        var dm = new DebugModule();
        var func = RawFunc(
            "Main",
            0,
            0,
            WithSrc(DebugInstr.Frame(0), "test.st", 1),
            DebugInstr.Op(OpCode.NOP),
            WithSrc(DebugInstr.Op(OpCode.LEAVE), "test.st", 3),
            DebugInstr.Op(OpCode.RET));
        dm.Functions.Add(func);

        var map = dm.BuildSourceMap();

        var found = map.FindByPc(1);
        Assert.NotNull(found);
        Assert.Equal(1, found!.Value.Line);
    }

    [Fact]
    public void FindByPc_ExactlyOnMapped_ReturnsThatLocation()
    {
        var dm = new DebugModule();
        var func = RawFunc(
            "Main",
            0,
            0,
            DebugInstr.Frame(0),
            DebugInstr.Op(OpCode.NOP),
            WithSrc(DebugInstr.Op(OpCode.LEAVE), "test.st", 5),
            DebugInstr.Op(OpCode.RET));
        dm.Functions.Add(func);

        var map = dm.BuildSourceMap();

        Assert.Equal(5, map.FindByPc(4)!.Value.Line);
        Assert.Null(map.FindByPc(1));
    }

    // ═══════════════════════════════════════════════════════════
    //  Category B — SourceMap.FindByPc 边界
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void FindByPc_AfterLast_ReturnsLast()
    {
        var dm = new DebugModule();
        var func = RawFunc(
            "Main",
            0,
            0,
            WithSrc(DebugInstr.Frame(0), "test.st", 1),
            DebugInstr.Op(OpCode.LEAVE),
            DebugInstr.Op(OpCode.RET));
        dm.Functions.Add(func);

        var map = dm.BuildSourceMap();

        var found = map.FindByPc(100);
        Assert.NotNull(found);
        Assert.Equal(1, found!.Value.Line);
    }

    [Fact]
    public void FindByPc_NegativePc_ReturnsNull()
    {
        var dm = new DebugModule();
        var func = RawFunc(
            "Main",
            0,
            0,
            WithSrc(DebugInstr.Frame(0), "test.st", 1),
            DebugInstr.Op(OpCode.LEAVE),
            DebugInstr.Op(OpCode.RET));
        dm.Functions.Add(func);

        var map = dm.BuildSourceMap();
        Assert.Null(map.FindByPc(-1));
    }

    [Fact]
    public void FindByPc_EmptyMap_ReturnsNull()
    {
        var map = new SourceMap();
        Assert.Null(map.FindByPc(0));
        Assert.Null(map.FindByPc(100));
    }

    // ═══════════════════════════════════════════════════════════
    //  Category B — SourceMap.FindByPc 多函数连续索引
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void FindByPc_MultipleFunctions_CrossFunctionLookup()
    {
        var func1 = RawFunc(
            "Main",
            0,
            0,
            WithSrc(DebugInstr.Frame(0), "a.st", 1),
            DebugInstr.Op(OpCode.LEAVE),
            DebugInstr.Op(OpCode.RET));
        var func2 = RawFunc(
            "Helper",
            0,
            0,
            WithSrc(DebugInstr.Frame(0), "b.st", 10),
            DebugInstr.Op(OpCode.RET));

        var dm = new DebugModule();
        dm.Functions.Add(func1);
        dm.Functions.Add(func2);

        var map = dm.BuildSourceMap();

        Assert.Equal("a.st", map.FindByPc(0)!.Value.File);
        Assert.Equal("b.st", map.FindByPc(5)!.Value.File);
        Assert.Equal("b.st", map.FindByPc(8)!.Value.File);
        Assert.Equal(1, map.FindByPc(3)!.Value.Line);
    }

    // ═══════════════════════════════════════════════════════════
    //  Category B — SourceMap.ToJson
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ToJson_EmptyMap_ContainsEmptyArrays()
    {
        var map = new SourceMap();
        var json = map.ToJson();

        Assert.Contains("\"functions\":[]", json);
        Assert.Contains("\"map\":[]", json);
    }

    [Fact]
    public void ToJson_WithFunction_ContainsFunctionInfo()
    {
        var dm = new DebugModule();
        var func = new DebugFunction
        {
            Name = "Main",
            ParamCount = 0,
            TotalLocals = 0,
            SourceFile = "test.st",
            SourceStartLine = 1,
            SourceEndLine = 10,
        };
        func.Instructions.Add(DebugInstr.Frame(0));
        func.Instructions.Add(DebugInstr.Op(OpCode.LEAVE));
        func.Instructions.Add(DebugInstr.Op(OpCode.RET));
        dm.Functions.Add(func);

        var json = dm.BuildSourceMap().ToJson();

        Assert.Contains("\"name\":\"Main\"", json);
        Assert.Contains("\"file\":\"test.st\"", json);
        Assert.Contains("\"start\":1", json);
        Assert.Contains("\"end\":10", json);
    }

    [Fact]
    public void ToJson_WithInstructionMap_ContainsMapEntries()
    {
        var dm = new DebugModule();
        var func = RawFunc(
            "Main",
            0,
            0,
            WithSrc(DebugInstr.Frame(0), "test.st", 1),
            WithSrc(DebugInstr.Op(OpCode.LEAVE), "test.st", 2),
            DebugInstr.Op(OpCode.RET));
        dm.Functions.Add(func);

        var json = dm.BuildSourceMap().ToJson();

        Assert.Contains("\"pc\":0", json);
        Assert.Contains("\"pc\":3", json);
        Assert.Contains("\"line\":1", json);
        Assert.Contains("\"line\":2", json);
        Assert.DoesNotContain("\"pc\":1", json);
    }

    // ═══════════════════════════════════════════════════════════
    //  Category B — Dump 输出
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Dump_IncludesFunctionSourceRange()
    {
        var func = new DebugFunction
        {
            Name = "Main",
            ParamCount = 0,
            TotalLocals = 1,
            SourceFile = "test.st",
            SourceStartLine = 1,
            SourceEndLine = 10,
        };
        func.Instructions.AddRange(
            new[] { DebugInstr.Frame(1), DebugInstr.Op(OpCode.LEAVE), DebugInstr.Op(OpCode.RET) });

        var dm = new DebugModule();
        dm.Functions.Add(func);

        var dump = dm.Dump();

        Assert.Contains("test.st", dump);
        Assert.Contains(":1-10", dump);
    }

    [Fact]
    public void Dump_IncludesInstructionSourceLocation()
    {
        var func = RawFunc(
            "Main",
            0,
            1,
            DebugInstr.Frame(1),
            WithSrc(DebugInstr.I8(OpCode.LDC, 8, 42), "calc.st", 15),
            DebugInstr.Slot(OpCode.SETL, 8, 0),
            DebugInstr.Op(OpCode.LEAVE),
            DebugInstr.Op(OpCode.RET));

        var dm = new DebugModule();
        dm.Functions.Add(func);

        var dump = dm.Dump();

        Assert.Contains("calc.st:15", dump);
    }

    // ═══════════════════════════════════════════════════════════
    //  Category B — 边界：SrcFile / SrcLine 边界值
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void BuildSourceMap_EmptyStringSrcFile_StillIncluded()
    {
        var func = RawFunc(
            "Main",
            0,
            0,
            WithSrc(DebugInstr.Frame(0), "", 1),
            DebugInstr.Op(OpCode.LEAVE),
            DebugInstr.Op(OpCode.RET));

        var dm = new DebugModule();
        dm.Functions.Add(func);

        var map = dm.BuildSourceMap();
        Assert.Single(map.InstructionMap);
    }

    [Fact]
    public void BuildSourceMap_ZeroSrcLine_Excluded()
    {
        var func = RawFunc(
            "Main",
            0,
            0,
            WithSrc(DebugInstr.Frame(0), "test.st", 0),
            DebugInstr.Op(OpCode.LEAVE),
            DebugInstr.Op(OpCode.RET));

        var dm = new DebugModule();
        dm.Functions.Add(func);

        var map = dm.BuildSourceMap();
        Assert.Empty(map.InstructionMap);
    }

    [Fact]
    public void BuildSourceMap_NegativeSrcLine_Excluded()
    {
        var func = RawFunc(
            "Main",
            0,
            0,
            WithSrc(DebugInstr.Frame(0), "test.st", -1),
            DebugInstr.Op(OpCode.LEAVE),
            DebugInstr.Op(OpCode.RET));

        var dm = new DebugModule();
        dm.Functions.Add(func);

        var map = dm.BuildSourceMap();
        Assert.Empty(map.InstructionMap);
    }

    // ═══════════════════════════════════════════════════════════
    //  Category B — 多文件源码映射
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void BuildSourceMap_MultipleFiles_TrackedCorrectly()
    {
        var func = RawFunc(
            "Main",
            0,
            0,
            DebugInstr.Frame(0),
            WithSrc(DebugInstr.Op(OpCode.NOP), "a.st", 1),
            WithSrc(DebugInstr.Op(OpCode.NOP), "b.st", 5),
            WithSrc(DebugInstr.Op(OpCode.NOP), "a.st", 10),
            DebugInstr.Op(OpCode.LEAVE),
            DebugInstr.Op(OpCode.RET));

        var dm = new DebugModule();
        dm.Functions.Add(func);

        var map = dm.BuildSourceMap();

        Assert.Equal(3, map.InstructionMap.Count);
        Assert.Equal("a.st", map.InstructionMap[3].File);
        Assert.Equal(1, map.InstructionMap[3].Line);
        Assert.Equal("b.st", map.InstructionMap[4].File);
        Assert.Equal(5, map.InstructionMap[4].Line);
        Assert.Equal("a.st", map.InstructionMap[5].File);
        Assert.Equal(10, map.InstructionMap[5].Line);
    }

    // ═══════════════════════════════════════════════════════════
    //  Category B — 跨函数连续指令索引
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void BuildSourceMap_FunctionRanges_EntryPcMatchesInstructionIndex()
    {
        var func1 = RawFunc(
            "Main",
            0,
            0,
            DebugInstr.Frame(0),
            DebugInstr.Op(OpCode.LEAVE),
            DebugInstr.Op(OpCode.RET));
        func1.SourceFile = "a.st";

        var func2 = RawFunc("Helper", 0, 0, DebugInstr.Frame(0), DebugInstr.Op(OpCode.RET));
        func2.SourceFile = "b.st";

        var dm = new DebugModule();
        dm.Functions.Add(func1);
        dm.Functions.Add(func2);

        var map = dm.BuildSourceMap();

        Assert.Equal(2, map.FunctionRanges.Count);
        Assert.Equal(0, map.FunctionRanges[0].EntryPc);
        Assert.Equal(5, map.FunctionRanges[1].EntryPc);
    }
}
