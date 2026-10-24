// ═══════════════════════════════════════════════════════════════
//  DebugModuleTests.cs — DebugModule 单元测试
// ═══════════════════════════════════════════════════════════════
using System.Text.Json;
using IronST.Runtime.Shared;
using Xunit;

using OpCode = IronST.Runtime.Shared.OpCode;

namespace IronST.BytecodeGen.Tests;

public class DebugModuleTests
{
    // ══════════════════════════════════════════
    //  1. Dump 空模块
    // ══════════════════════════════════════════

    [Fact]
    public void Dump_EmptyModule_PrintsHeader()
    {
        var dm = new DebugModule();
        string result = dm.Dump();

        Assert.Contains("=== DebugModule ===", result);
        Assert.DoesNotContain("Globals:", result);
        Assert.DoesNotContain("── ", result);
    }

    // ══════════════════════════════════════════
    //  2. Dump 全局变量
    // ══════════════════════════════════════════

    [Fact]
    public void Dump_WithGlobals_PrintsGlobalNames()
    {
        var dm = new DebugModule();
        dm.GlobalNames.Add("x");
        dm.GlobalNames.Add("y");
        dm.GlobalNames.Add("counter");

        string result = dm.Dump();

        Assert.Contains("Globals: x, y, counter", result);
    }

    // ══════════════════════════════════════════
    //  3. Dump 单函数
    // ══════════════════════════════════════════

    [Fact]
    public void Dump_SingleFunction_PrintsNameParamsLocals()
    {
        var dm = new DebugModule();
        var func = new DebugFunction
        {
            Name = "Main",
            ParamCount = 0,
            TotalLocals = 3,
        };
        func.Instructions.Add(DebugInstr.Frame(3));
        func.Instructions.Add(DebugInstr.Op(OpCode.RET));
        dm.Functions.Add(func);

        string result = dm.Dump();

        Assert.Contains("-- Main (params=0, locals=3)", result);
    }

    [Fact]
    public void Dump_FunctionWithParams_PrintsParamCount()
    {
        var dm = new DebugModule();
        var func = new DebugFunction
        {
            Name = "add",
            ParamCount = 2,
            TotalLocals = 2,
        };
        func.Instructions.Add(DebugInstr.Op(OpCode.RET));
        dm.Functions.Add(func);

        string result = dm.Dump();

        Assert.Contains("-- add (params=2, locals=2)", result);
    }

    // ══════════════════════════════════════════
    //  4. Dump 标签
    // ══════════════════════════════════════════

    [Fact]
    public void Dump_Labels_PrintsAtCorrectPositions()
    {
        var dm = new DebugModule();
        var func = new DebugFunction
        {
            Name = "loop",
            ParamCount = 0,
            TotalLocals = 2,
        };

        // 指令 0: FRAME
        func.Instructions.Add(DebugInstr.Frame(2));
        // 指令 1: GETL R8, L0
        func.Instructions.Add(DebugInstr.Slot(OpCode.GETL, 8, 0));
        // 指令 2: IINC R8
        func.Instructions.Add(DebugInstr.R1(OpCode.IINC, 8));
        // 指令 3: SETL R8, L0
        func.Instructions.Add(DebugInstr.Slot(OpCode.SETL, 8, 0));
        // 指令 4: JMP → loop_start
        func.Instructions.Add(DebugInstr.Jmp("__loop_loop_start_0"));
        // 指令 5: LEAVE
        func.Instructions.Add(DebugInstr.Op(OpCode.LEAVE));
        // 指令 6: RET
        func.Instructions.Add(DebugInstr.Op(OpCode.RET));

        // 标签定义在指令索引 1 和 6
        func.Labels["__loop_loop_start_0"] = 1;
        func.Labels["__loop_loop_end_1"] = 6;

        dm.Functions.Add(func);
        string result = dm.Dump();
        string[] lines = result.Split('\n');

        // 找到标签行
        int startLabelLine = -1;
        int endLabelLine = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("__loop_loop_start_0:"))
                startLabelLine = i;
            if (lines[i].Contains("__loop_loop_end_1:"))
                endLabelLine = i;
        }

        Assert.True(startLabelLine >= 0, "start label should appear");
        Assert.True(endLabelLine >= 0, "end label should appear");

        // 标签 __loop_loop_start_0 定义在指令 1 之前
        // 找到 FRAME 指令行（应该是标签前一行或标签后一行）
        Assert.Contains("FRAME", lines[startLabelLine - 1]);

        // __loop_loop_end_1 应该在 RET 之前（指令 6 是尾部标签）
        // 尾部标签在指令列表之后
    }

    // ══════════════════════════════════════════
    //  5. Dump 源码位置
    // ══════════════════════════════════════════

    [Fact]
    public void Dump_WithSourceLocation_PrintsFileLine()
    {
        var dm = new DebugModule();
        var func = new DebugFunction
        {
            Name = "Main",
            ParamCount = 0,
            TotalLocals = 1,
        };

        var instr = DebugInstr.R1(OpCode.LNIL, 8);
        instr.SrcFile = "main.st";
        instr.SrcLine = 42;
        func.Instructions.Add(instr);

        func.Instructions.Add(DebugInstr.Op(OpCode.RET));

        dm.Functions.Add(func);
        string result = dm.Dump();

        Assert.Contains("; main.st:42", result);
    }

    [Fact]
    public void Dump_WithoutSourceLocation_OmitsFileLine()
    {
        var dm = new DebugModule();
        var func = new DebugFunction
        {
            Name = "Main",
            ParamCount = 0,
            TotalLocals = 1,
        };

        // 没有设置 SrcFile/SrcLine
        func.Instructions.Add(DebugInstr.Frame(1));
        func.Instructions.Add(DebugInstr.Op(OpCode.RET));

        dm.Functions.Add(func);
        string result = dm.Dump();

        // FRAME 行后面不应该有分号注释（除非整行都有分号）
        // 检查 FRAME 行不包含文件名
        string[] lines = result.Split('\n');
        foreach (var line in lines)
        {
            if (line.Contains("FRAME"))
                Assert.DoesNotContain(";", line);
        }
    }

    // ══════════════════════════════════════════
    //  6. Dump 函数级源码位置
    // ══════════════════════════════════════════

    [Fact]
    public void Dump_FunctionLevelSource_PrintsStartEnd()
    {
        var dm = new DebugModule();
        var func = new DebugFunction
        {
            Name = "Main",
            ParamCount = 0,
            TotalLocals = 2,
            SourceFile = "main.st",
            SourceStartLine = 1,
            SourceEndLine = 15,
        };
        func.Instructions.Add(DebugInstr.Op(OpCode.RET));
        dm.Functions.Add(func);

        string result = dm.Dump();

        Assert.Contains("; main.st:1-15", result);
    }

    [Fact]
    public void Dump_NoFunctionLevelSource_OmitsAnnotation()
    {
        var dm = new DebugModule();
        var func = new DebugFunction
        {
            Name = "Main",
            ParamCount = 0,
            TotalLocals = 1,
        };
        func.Instructions.Add(DebugInstr.Op(OpCode.RET));
        dm.Functions.Add(func);

        string result = dm.Dump();

        // 函数头行不应该包含分号注释
        string[] lines = result.Split('\n');
        foreach (var line in lines)
        {
            if (line.Contains("── Main"))
                Assert.DoesNotContain(";", line);
        }
    }

    // ══════════════════════════════════════════
    //  7. BuildSourceMap 空模块
    // ══════════════════════════════════════════

    [Fact]
    public void BuildSourceMap_EmptyModule_ReturnsEmpty()
    {
        var dm = new DebugModule();
        var map = dm.BuildSourceMap();

        Assert.Empty(map.InstructionMap);
        Assert.Empty(map.FunctionRanges);
    }

    // ══════════════════════════════════════════
    //  8. BuildSourceMap 有源码
    // ══════════════════════════════════════════

    [Fact]
    public void BuildSourceMap_WithSource_IncludesAllLinedInstructions()
    {
        var dm = new DebugModule();
        var func = new DebugFunction
        {
            Name = "Main",
            ParamCount = 0,
            TotalLocals = 2,
        };

        // 指令 0: FRAME (无源码)
        func.Instructions.Add(DebugInstr.Frame(2));

        // 指令 1: LDI R8, 0 (行 3)
        var i1 = DebugInstr.I8(OpCode.LDC, 8, 0);
        i1.SrcFile = "main.st";
        i1.SrcLine = 3;
        func.Instructions.Add(i1);

        // 指令 2: SETL R8, L0 (行 3)
        var i2 = DebugInstr.Slot(OpCode.SETL, 8, 0);
        i2.SrcFile = "main.st";
        i2.SrcLine = 3;
        func.Instructions.Add(i2);

        // 指令 3: GETL R9, L0 (行 5)
        var i3 = DebugInstr.Slot(OpCode.GETL, 9, 0);
        i3.SrcFile = "main.st";
        i3.SrcLine = 5;
        func.Instructions.Add(i3);

        // 指令 4: RET (无源码)
        func.Instructions.Add(DebugInstr.Op(OpCode.RET));

        dm.Functions.Add(func);
        var map = dm.BuildSourceMap();

        Assert.Equal(3, map.InstructionMap.Count);
        Assert.Equal(new SourceMap.SourceLoc("main.st", 3), map.InstructionMap[3]);
        Assert.Equal(new SourceMap.SourceLoc("main.st", 3), map.InstructionMap[9]);
        Assert.Equal(new SourceMap.SourceLoc("main.st", 5), map.InstructionMap[13]);

        // PC 0 和 PC 17 不应该出现
        Assert.False(map.InstructionMap.ContainsKey(0));
        Assert.False(map.InstructionMap.ContainsKey(17));
    }

    // ══════════════════════════════════════════
    //  9. BuildSourceMap PC 连续
    // ══════════════════════════════════════════

    [Fact]
    public void BuildSourceMap_PcIsSequential()
    {
        var dm = new DebugModule();

        // 函数1：3 条指令
        var f1 = new DebugFunction { Name = "A", TotalLocals = 1 };
        var a1 = DebugInstr.Op(OpCode.FRAME); a1.SrcFile = "a.st"; a1.SrcLine = 1;
        var a2 = DebugInstr.Op(OpCode.LEAVE); a2.SrcFile = "a.st"; a2.SrcLine = 2;
        var a3 = DebugInstr.Op(OpCode.RET);
        f1.Instructions.AddRange(new[] { a1, a2, a3 });
        dm.Functions.Add(f1);

        // 函数2：2 条指令
        var f2 = new DebugFunction { Name = "B", TotalLocals = 1 };
        var b1 = DebugInstr.Op(OpCode.FRAME); b1.SrcFile = "b.st"; b1.SrcLine = 10;
        var b2 = DebugInstr.Op(OpCode.RET);
        f2.Instructions.AddRange(new[] { b1, b2 });
        dm.Functions.Add(f2);

        var map = dm.BuildSourceMap();

        // PC 跨函数连续：函数A=PC 0-4，函数B=PC 5-8
        Assert.Equal(new SourceMap.SourceLoc("a.st", 1), map.InstructionMap[0]);
        Assert.Equal(new SourceMap.SourceLoc("a.st", 2), map.InstructionMap[3]);
        Assert.Equal(new SourceMap.SourceLoc("b.st", 10), map.InstructionMap[5]);

        // PC 4 和 8 没有源码（RET 无源码）
        Assert.False(map.InstructionMap.ContainsKey(4));
        Assert.False(map.InstructionMap.ContainsKey(8));
    }

    // ══════════════════════════════════════════
    //  10. BuildSourceMap 函数范围
    // ══════════════════════════════════════════

    [Fact]
    public void BuildSourceMap_FunctionRanges_CorrectEntryPc()
    {
        var dm = new DebugModule();

        var f1 = new DebugFunction
        {
            Name = "Main",
            SourceFile = "main.st",
            SourceStartLine = 1,
            SourceEndLine = 20,
            TotalLocals = 1,
        };
        f1.Instructions.Add(DebugInstr.Op(OpCode.RET));
        dm.Functions.Add(f1);

        var f2 = new DebugFunction
        {
            Name = "helper",
            SourceFile = "helper.st",
            SourceStartLine = 5,
            SourceEndLine = 12,
            TotalLocals = 1,
        };
        f2.Instructions.Add(DebugInstr.Op(OpCode.RET));
        dm.Functions.Add(f2);

        var map = dm.BuildSourceMap();

        Assert.Equal(2, map.FunctionRanges.Count);

        Assert.Equal("Main", map.FunctionRanges[0].Name);
        Assert.Equal("main.st", map.FunctionRanges[0].File);
        Assert.Equal(1, map.FunctionRanges[0].StartLine);
        Assert.Equal(20, map.FunctionRanges[0].EndLine);
        Assert.Equal(0, map.FunctionRanges[0].EntryPc);

        Assert.Equal("helper", map.FunctionRanges[1].Name);
        Assert.Equal("helper.st", map.FunctionRanges[1].File);
        Assert.Equal(5, map.FunctionRanges[1].StartLine);
        Assert.Equal(12, map.FunctionRanges[1].EndLine);
        Assert.Equal(1, map.FunctionRanges[1].EntryPc); // 函数2从PC 1开始
    }

    // ══════════════════════════════════════════
    //  11. SourceMap.FindByPc 精确命中
    // ══════════════════════════════════════════

    [Fact]
    public void FindByPc_ExactMatch_ReturnsCorrect()
    {
        var map = new SourceMap();
        map.InstructionMap[5] = new SourceMap.SourceLoc("main.st", 10);
        map.InstructionMap[10] = new SourceMap.SourceLoc("main.st", 20);
        map.InstructionMap[15] = new SourceMap.SourceLoc("main.st", 30);

        var result = map.FindByPc(10);

        Assert.NotNull(result);
        Assert.Equal("main.st", result.Value.File);
        Assert.Equal(20, result.Value.Line);
    }

    // ══════════════════════════════════════════
    //  12. SourceMap.FindByPc 最近查找
    // ══════════════════════════════════════════

    [Fact]
    public void FindByPc_BetweenEntries_ReturnsNearestBelow()
    {
        var map = new SourceMap();
        map.InstructionMap[5] = new SourceMap.SourceLoc("main.st", 10);
        map.InstructionMap[10] = new SourceMap.SourceLoc("main.st", 20);
        map.InstructionMap[15] = new SourceMap.SourceLoc("main.st", 30);

        // PC 12 介于 10 和 15 之间，应该返回 PC 10 的映射
        var result = map.FindByPc(12);

        Assert.NotNull(result);
        Assert.Equal("main.st", result.Value.File);
        Assert.Equal(20, result.Value.Line);
    }

    [Fact]
    public void FindByPc_BeforeFirstEntry_ReturnsNull()
    {
        var map = new SourceMap();
        map.InstructionMap[5] = new SourceMap.SourceLoc("main.st", 10);

        var result = map.FindByPc(3);

        Assert.Null(result);
    }

    // ══════════════════════════════════════════
    //  13. SourceMap.FindByPc 无匹配
    // ══════════════════════════════════════════

    [Fact]
    public void FindByPc_EmptyMap_ReturnsNull()
    {
        var map = new SourceMap();

        var result = map.FindByPc(0);

        Assert.Null(result);
    }

    [Fact]
    public void FindByPc_AfterAllEntries_ReturnsLast()
    {
        var map = new SourceMap();
        map.InstructionMap[5] = new SourceMap.SourceLoc("main.st", 10);
        map.InstructionMap[10] = new SourceMap.SourceLoc("main.st", 20);

        // PC 99 超出所有条目，应该返回 PC 10 的映射
        var result = map.FindByPc(99);

        Assert.NotNull(result);
        Assert.Equal(20, result.Value.Line);
    }

    // ══════════════════════════════════════════
    //  14. SourceMap.ToJson 格式
    // ══════════════════════════════════════════

    [Fact]
    public void ToJson_EmptyMap_ProducesValidJson()
    {
        var map = new SourceMap();
        string json = map.ToJson();

        // 验证是合法 JSON
        var doc = JsonDocument.Parse(json);
        Assert.NotNull(doc);

        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("functions", out _));
        Assert.True(root.TryGetProperty("map", out _));
    }

    [Fact]
    public void ToJson_ProducesValidJson_WithContent()
    {
        var map = new SourceMap();
        map.FunctionRanges.Add(new SourceMap.FunctionRange(
            "Main", "main.st", 1, 20, 0));
        map.InstructionMap[0] = new SourceMap.SourceLoc("main.st", 3);
        map.InstructionMap[2] = new SourceMap.SourceLoc("main.st", 5);

        string json = map.ToJson();

        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // 验证 functions 数组
        var funcs = root.GetProperty("functions");
        Assert.Equal(1, funcs.GetArrayLength());

        var f0 = funcs[0];
        Assert.Equal("Main", f0.GetProperty("name").GetString());
        Assert.Equal("main.st", f0.GetProperty("file").GetString());
        Assert.Equal(1, f0.GetProperty("start").GetInt32());
        Assert.Equal(20, f0.GetProperty("end").GetInt32());
        Assert.Equal(0, f0.GetProperty("pc").GetInt32());

        // 验证 map 数组
        var mapArr = root.GetProperty("map");
        Assert.Equal(2, mapArr.GetArrayLength());

        var m0 = mapArr[0];
        Assert.Equal(0, m0.GetProperty("pc").GetInt32());
        Assert.Equal("main.st", m0.GetProperty("file").GetString());
        Assert.Equal(3, m0.GetProperty("line").GetInt32());

        var m1 = mapArr[1];
        Assert.Equal(2, m1.GetProperty("pc").GetInt32());
        Assert.Equal(5, m1.GetProperty("line").GetInt32());
    }

    // ══════════════════════════════════════════
    //  15. SourceMap.ToJson 内容完整
    // ══════════════════════════════════════════

    [Fact]
    public void ToJson_MultipleFunctions_AllIncluded()
    {
        var map = new SourceMap();
        map.FunctionRanges.Add(new SourceMap.FunctionRange(
            "Main", "main.st", 1, 20, 0));
        map.FunctionRanges.Add(new SourceMap.FunctionRange(
            "add", "math.st", 5, 8, 3));

        string json = map.ToJson();
        var doc = JsonDocument.Parse(json);
        var funcs = doc.RootElement.GetProperty("functions");

        Assert.Equal(2, funcs.GetArrayLength());
        Assert.Equal("Main", funcs[0].GetProperty("name").GetString());
        Assert.Equal("add", funcs[1].GetProperty("name").GetString());
    }

    [Fact]
    public void ToJson_MapEntries_SortedByPc()
    {
        var map = new SourceMap();
        // 故意乱序添加
        map.InstructionMap[10] = new SourceMap.SourceLoc("a.st", 20);
        map.InstructionMap[5] = new SourceMap.SourceLoc("a.st", 10);
        map.InstructionMap[0] = new SourceMap.SourceLoc("a.st", 1);

        string json = map.ToJson();
        var doc = JsonDocument.Parse(json);
        var mapArr = doc.RootElement.GetProperty("map");

        // 验证 JSON 中的 PC 是递增的
        int prevPc = -1;
        foreach (var entry in mapArr.EnumerateArray())
        {
            int pc = entry.GetProperty("pc").GetInt32();
            Assert.True(pc > prevPc, $"PC {pc} should be > {prevPc}");
            prevPc = pc;
        }
    }

    // ══════════════════════════════════════════
    //  额外：DebugInstr 格式化覆盖
    // ══════════════════════════════════════════

    [Fact]
    public void DebugInstr_Format_SymbolicJump()
    {
        var instr = DebugInstr.Jmp("loop_start");
        Assert.Contains("JMP loop_start", instr.Format());
    }

    [Fact]
    public void DebugInstr_Format_SymbolicBz()
    {
        var instr = DebugInstr.Bz(8, "end");
        Assert.Equal("BZ R8, end", instr.Format());
    }

    [Fact]
    public void DebugInstr_Format_SymbolicCall()
    {
        var instr = DebugInstr.Call("factorial");
        Assert.Equal("CALL factorial", instr.Format());
    }

    [Fact]
    public void DebugInstr_Format_RegisterOperations()
    {
        Assert.Contains("IADD R10, R11, R12",
            DebugInstr.R3(OpCode.IADD, 10, 11, 12).Format());
        Assert.Contains("MOV R4, R10",
            DebugInstr.R2(OpCode.MOV, 4, 10).Format());
        Assert.Contains("LNIL R8",
            DebugInstr.R1(OpCode.LNIL, 8).Format());
        Assert.Contains("LDC R8, 42",
            DebugInstr.LdcInt(8, 42).Format());
        Assert.Contains("FRAME 5",
            DebugInstr.Frame(5).Format());
        Assert.Contains("GETL R10, L0",
            DebugInstr.Slot(OpCode.GETL, 10, 0).Format());
        Assert.Contains("LDC R8, 3.14",
            DebugInstr.Ldc(8, 3.14).Format());
        Assert.Contains("LDC R9, \"hello\"",
            DebugInstr.LdcStr(9, "hello").Format());
    }

    [Fact]
    public void DebugInstr_Format_WithSourceLocation()
    {
        var instr = DebugInstr.R1(OpCode.LNIL, 8);
        instr.SrcFile = "test.st";
        instr.SrcLine = 42;

        // Format 本身不包含源码位置（由 Dump 处理）
        Assert.Contains("LNIL R8", instr.Format());

        // 但字段正确设置
        Assert.Equal("test.st", instr.SrcFile);
        Assert.Equal(42, instr.SrcLine);
    }

    // ── 新增：结构体指令格式化 ──

    [Fact]
    public void DebugInstr_Format_Snew()
    {
        var instr = DebugInstr.Snew(10, 4, 2);
        string f = instr.Format();

        Assert.Contains("SNEW", f);
        Assert.Contains("R10", f);
        Assert.Contains("4", f);
    }

    [Fact]
    public void DebugInstr_Format_Sget()
    {
        var instr = DebugInstr.Sget(10, 8, 2);
        string f = instr.Format();

        Assert.Contains("SGET", f);
        Assert.Contains("R10", f);
        Assert.Contains("R8", f);
        Assert.Contains("2", f);
    }

    [Fact]
    public void DebugInstr_Format_Sset()
    {
        var instr = DebugInstr.Sset(11, 10, 1);
        string f = instr.Format();

        Assert.Contains("SSET", f);
        Assert.Contains("R11", f);
        Assert.Contains("R10", f);
        Assert.Contains("1", f);
    }

}
