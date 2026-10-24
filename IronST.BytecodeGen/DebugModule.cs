using System.Text;
using IronST.Runtime.Shared;

namespace IronST.BytecodeGen;

public sealed class DebugModule
{
    public List<string> GlobalNames { get; } = new();
    public List<DebugFunction> Functions { get; } = new();

    /// <summary>导出可读文本，用于调试。</summary>
    public string Dump()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== DebugModule ===");

        if (GlobalNames.Count > 0)
            sb.AppendLine($"Globals: {string.Join(", ", GlobalNames)}");

        foreach (var func in Functions) {
            sb.AppendLine();

            string src =
                func.SourceFile != null
                    ? $"  ; {func.SourceFile}:{func.SourceStartLine}-{func.SourceEndLine}"
                    : "";

            sb.AppendLine(
                $"-- {func.Name} (params={func.ParamCount}, locals={func.TotalLocals}){src}");

            for (int i = 0; i < func.Instructions.Count; i++) {
                foreach (var (name, idx) in func.Labels)
                    if (idx == i)
                        sb.AppendLine($"  {name}:");

                var instr = func.Instructions[i];
                string loc = instr.SrcFile != null ? $"  ; {instr.SrcFile}:{instr.SrcLine}" : "";
                sb.AppendLine($"    {instr.Format()}{loc}");
            }

            int end = func.Instructions.Count;
            foreach (var (name, idx) in func.Labels)
                if (idx == end)
                    sb.AppendLine($"  {name}:");
        }

        return sb.ToString();
    }

    /// <summary>导出源码位置映射表（PC -> SourceLocation）。</summary>
    public SourceMap BuildSourceMap()
    {
        var map = new SourceMap();
        int pc = 0;
        foreach (var func in Functions) {
            map.FunctionRanges.Add(
                new SourceMap.FunctionRange(
                    func.Name,
                    func.SourceFile ?? "",
                    func.SourceStartLine,
                    func.SourceEndLine,
                    pc));

            foreach (var instr in func.Instructions) {
                if (instr.SrcFile != null && instr.SrcLine > 0)
                    map.InstructionMap[pc] = new SourceMap.SourceLoc(instr.SrcFile, instr.SrcLine);
                byte len = VMBase.GetInstrLength(instr.Opcode);
                pc += len > 0 ? len : 1;
            }
        }
        return map;
    }
}

public sealed class DebugFunction
{
    public string Name { get; init; } = "";
    public int ParamCount { get; init; }
    public int TotalLocals { get; set; }
    public List<DebugInstr> Instructions { get; } = new();
    public Dictionary<string, int> Labels { get; } = new(StringComparer.Ordinal);

    /// <summary>函数所在源文件。</summary>
    public string? SourceFile { get; set; }
    public int SourceStartLine { get; set; }
    public int SourceEndLine { get; set; }
}

/// <summary>
/// 源码位置映射表。独立于 BytecodeModule，可序列化到 .debug.json。
/// </summary>
public sealed class SourceMap
{
    /// <summary>PC -> 源码位置（仅包含有源码信息的指令）。</summary>
    public Dictionary<int, SourceLoc> InstructionMap { get; } = new();

    /// <summary>函数级源码范围。</summary>
    public List<FunctionRange> FunctionRanges { get; } = new();

    public readonly record struct SourceLoc(string File, int Line);

    public readonly record struct FunctionRange(
        string Name,
        string File,
        int StartLine,
        int EndLine,
        int EntryPc);

    /// <summary>查找 PC 所在的最近源码位置。</summary>
    public SourceLoc? FindByPc(int pc)
    {
        int best = -1;
        foreach (var key in InstructionMap.Keys)
            if (key <= pc && key > best)
                best = key;
        return best >= 0 ? InstructionMap[best] : null;
    }

    /// <summary>导出为 JSON 字符串（可保存到 .debug.json）。</summary>
    public string ToJson()
    {
        var sb = new StringBuilder();
        sb.Append("{\"functions\":[");
        for (int i = 0; i < FunctionRanges.Count; i++) {
            var fr = FunctionRanges[i];
            if (i > 0)
                sb.Append(',');
            sb.Append(
                $"{{\"name\":\"{fr.Name}\",\"file\":\"{fr.File}\","
              + $"\"start\":{fr.StartLine},\"end\":{fr.EndLine},\"pc\":{fr.EntryPc}}}");
        }
        sb.Append("],\"map\":[");

        bool first = true;
        foreach (var (pc, loc) in InstructionMap.OrderBy(kv => kv.Key)) {
            if (!first)
                sb.Append(',');
            first = false;
            sb.Append($"{{\"pc\":{pc},\"file\":\"{loc.File}\",\"line\":{loc.Line}}}");
        }
        sb.Append("]}");
        return sb.ToString();
    }
}

public struct DebugInstr
{
    public OpCode Opcode;
    public byte Dst;
    public byte Src1;
    public byte Src2;
    public sbyte Off8;
    public short Imm16;
    public int Imm32;
    public double Float;
    public string? Str;
    public string? Label;
    public string? CallTarget;

    /// <summary>LDC 整数常量值（Imm32==1 时有效）。</summary>
    public long IntVal;

    /// <summary>源码文件名。</summary>
    public string? SrcFile;

    /// <summary>源码行号。</summary>
    public int SrcLine;

    // ═══════════════════════════════════════
    //  工厂方法
    // ═══════════════════════════════════════

    public static DebugInstr Op(OpCode op) => new() { Opcode = op };
    public static DebugInstr R1(OpCode op, byte rd) => new() { Opcode = op, Dst = rd };
    public static DebugInstr R2(OpCode op, byte rd, byte rs) =>
        new() { Opcode = op, Dst = rd, Src1 = rs };
    public static DebugInstr I8(OpCode op, byte rd, sbyte val) =>
        new() { Opcode = op, Dst = rd, Off8 = val };
    public static DebugInstr Slot(OpCode op, byte rd, short slot) =>
        new() { Opcode = op, Dst = rd, Imm16 = slot };
    public static DebugInstr Frame(short n) => new() { Opcode = OpCode.FRAME, Imm16 = n };
    public static DebugInstr R3(OpCode op, byte rd, byte rs1, byte rs2) =>
        new() { Opcode = op, Dst = rd, Src1 = rs1, Src2 = rs2 };
    public static DebugInstr I32(OpCode op, byte rd, int val) =>
        new() { Opcode = op, Dst = rd, Imm32 = val };
    public static DebugInstr Ldc(byte rd, double val) =>
        new() { Opcode = OpCode.LDC, Dst = rd, Float = val };
    public static DebugInstr LdcInt(byte rd, long val) =>
        new() { Opcode = OpCode.LDC, Dst = rd, IntVal = val, Imm32 = 1 };
    public static DebugInstr LdcStr(byte rd, string val) =>
        new() { Opcode = OpCode.LDC, Dst = rd, Str = val };
    public static DebugInstr Jmp(string label) => new() { Opcode = OpCode.JMP, Label = label };
    public static DebugInstr Bz(byte cond, string label) =>
        new() { Opcode = OpCode.BZ, Dst = cond, Label = label };
    public static DebugInstr Bnz(byte cond, string label) =>
        new() { Opcode = OpCode.BNZ, Dst = cond, Label = label };
    public static DebugInstr Branch(OpCode op, byte rs1, byte rs2, string label) =>
        new() { Opcode = op, Src1 = rs1, Src2 = rs2, Label = label };
    public static DebugInstr Call(string funcName) =>
        new() { Opcode = OpCode.CALL, CallTarget = funcName };
    public static DebugInstr Hcall(ushort index) => new() { Opcode = OpCode.HCALL, Imm16 = unchecked((short)index) };

    // 结构体 / 栈
    public static DebugInstr Snew(byte rd, byte typeId, ushort count) =>
        new() { Opcode = OpCode.SNEW, Dst = rd, Src1 = typeId, Imm16 = unchecked((short)count) };
    public static DebugInstr Sget(byte rd, byte obj, byte field) =>
        new() { Opcode = OpCode.SGET, Dst = rd, Src1 = obj, Off8 = unchecked((sbyte)field) };
    public static DebugInstr Sset(byte src, byte obj, byte field) =>
        new() { Opcode = OpCode.SSET, Src1 = src, Src2 = obj, Off8 = unchecked((sbyte)field) };
    public static DebugInstr Pushn(short n) =>
        new() { Opcode = OpCode.PUSHN, Imm16 = n };
    public static DebugInstr Popn(short n) =>
        new() { Opcode = OpCode.POPN, Imm16 = n };

    // ═══════════════════════════════════════
    //  格式化
    // ═══════════════════════════════════════

    public readonly string Format() =>
        Opcode switch
        {
            // 1 字节
            OpCode.NOP => "NOP",
            OpCode.HALT => "HALT",
            OpCode.BRK => "BRK",
            OpCode.LEAVE => "LEAVE",
            OpCode.RET => "RET",
            OpCode.VMRET => "VMRET",

            // 2 字节
            OpCode.SYSCALL => $"SYSCALL 0x{Dst:X2}",
            OpCode.LNIL => $"LNIL R{Dst}",
            OpCode.LTRUE => $"LTRUE R{Dst}",
            OpCode.LFALSE => $"LFALSE R{Dst}",
            OpCode.PUSH => $"PUSH R{Dst}",
            OpCode.POP => $"POP R{Dst}",
            OpCode.IINC => $"IINC R{Dst}",
            OpCode.IDEC => $"IDEC R{Dst}",
            OpCode.HCALL => $"HCALL 0x{unchecked((ushort)Imm16):X4}",
            OpCode.JMPR => $"JMPR R{Dst}",
            OpCode.CALLR => $"CALLR R{Dst}",
            OpCode.VMCALL => $"VMCALL {Dst}",

            // 3 字节：双寄存器
            OpCode.MOV => $"MOV R{Dst}, R{Src1}",
            OpCode.SWAP => $"SWAP R{Dst}, R{Src1}",
            OpCode.NOT => $"NOT R{Dst}, R{Src1}",
            OpCode.INEG => $"INEG R{Dst}, R{Src1}",
            OpCode.FNEG => $"FNEG R{Dst}, R{Src1}",
            OpCode.TOINT => $"TOINT R{Dst}, R{Src1}",
            OpCode.TOFLT => $"TOFLT R{Dst}, R{Src1}",
            OpCode.TOSTR => $"TOSTR R{Dst}, R{Src1}",
            OpCode.TOBOOL => $"TOBOOL R{Dst}, R{Src1}",
            OpCode.TYPE => $"TYPE R{Dst}, R{Src1}",
            OpCode.GETR => $"GETR R{Dst}, R{Src1}",
            OpCode.SETR => $"SETR R{Dst}, R{Src1}",
            OpCode.BOUNDS => $"BOUNDS R{Dst}, R{Src1}",
            OpCode.NULLCK => $"NULLCK R{Dst}",
            OpCode.GETGI => $"GETGI R{Dst}, R{Src1}",
            OpCode.SETGI => $"SETGI R{Dst}, R{Src1}",
            OpCode.CLZ => $"CLZ R{Dst}, R{Src1}",

            // 3 字节：FRAME / 跳转
            OpCode.FRAME => $"FRAME {Imm16}",
            OpCode.PUSHN => $"PUSHN {Imm16}",
            OpCode.POPN => $"POPN {Imm16}",
            OpCode.BZ => Label != null ? $"BZ R{Dst}, {Label}" : $"BZ R{Dst}, {Off8:+#;-#;0}",
            OpCode.BNZ => Label != null ? $"BNZ R{Dst}, {Label}" : $"BNZ R{Dst}, {Off8:+#;-#;0}",

            // 4 字节：变量访问
            OpCode.GETL => $"GETL R{Dst}, L{Imm16}",
            OpCode.SETL => $"SETL R{Dst}, L{Imm16}",
            OpCode.GETG => $"GETG R{Dst}, G{Imm16}",
            OpCode.SETG => $"SETG R{Dst}, G{Imm16}",

            // 常量池（统一 LDC：Int64 / Float64 / String）
            OpCode.LDC => Imm32 == 1
                ? $"LDC R{Dst}, {IntVal}"
                : Str != null
                    ? $"LDC R{Dst}, \"{Str}\""
                    : $"LDC R{Dst}, {Float:G}",

            // 4 字节：三寄存器算术
            OpCode.IADD => $"IADD R{Dst}, R{Src1}, R{Src2}",
            OpCode.ISUB => $"ISUB R{Dst}, R{Src1}, R{Src2}",
            OpCode.IMUL => $"IMUL R{Dst}, R{Src1}, R{Src2}",
            OpCode.IDIV => $"IDIV R{Dst}, R{Src1}, R{Src2}",
            OpCode.FADD => $"FADD R{Dst}, R{Src1}, R{Src2}",
            OpCode.FSUB => $"FSUB R{Dst}, R{Src1}, R{Src2}",
            OpCode.FMUL => $"FMUL R{Dst}, R{Src1}, R{Src2}",
            OpCode.FDIV => $"FDIV R{Dst}, R{Src1}, R{Src2}",
            OpCode.AND => $"AND R{Dst}, R{Src1}, R{Src2}",
            OpCode.OR => $"OR R{Dst}, R{Src1}, R{Src2}",
            OpCode.XOR => $"XOR R{Dst}, R{Src1}, R{Src2}",
            OpCode.SHL => $"SHL R{Dst}, R{Src1}, R{Src2}",
            OpCode.SHR => $"SHR R{Dst}, R{Src1}, R{Src2}",
            OpCode.SAR => $"SAR R{Dst}, R{Src1}, R{Src2}",
            OpCode.ROL => $"ROL R{Dst}, R{Src1}, R{Src2}",
            OpCode.ROR => $"ROR R{Dst}, R{Src1}, R{Src2}",

            // 4 字节：立即数位运算
            OpCode.ANDI => $"ANDI R{Dst}, R{Src1}, {Src2}",
            OpCode.ORI => $"ORI R{Dst}, R{Src1}, {Src2}",
            OpCode.XORI => $"XORI R{Dst}, R{Src1}, {Src2}",
            OpCode.SHLI => $"SHLI R{Dst}, R{Src1}, {Src2}",
            OpCode.SHRI => $"SHRI R{Dst}, R{Src1}, {Src2}",

            // 4 字节：比较
            OpCode.CMP => $"CMP R{Dst}, R{Src1}, R{Src2}",
            OpCode.IEQ => $"IEQ R{Dst}, R{Src1}, R{Src2}",
            OpCode.INE => $"INE R{Dst}, R{Src1}, R{Src2}",
            OpCode.FEQ => $"FEQ R{Dst}, R{Src1}, R{Src2}",

            // 4 字节：结构体 / 类型
            OpCode.SNEW => $"SNEW R{Dst}, type={Src1}, {unchecked((ushort)Imm16)}",
            OpCode.SGET => $"SGET R{Dst}, R{Src1}, {(byte)Off8}",
            OpCode.SSET => $"SSET R{Src1}, R{Src2}, {(byte)Off8}",
            OpCode.ISTYPE => $"ISTYPE R{Dst}, R{Src1}, {(byte)Off8}",

            // 4 字节：条件分支
            OpCode.BEQ => Label != null
                ? $"BEQ R{Src1}, R{Src2}, {Label}"
                : $"BEQ R{Src1}, R{Src2}, {Off8:+#;-#;0}",
            OpCode.BNE => Label != null
                ? $"BNE R{Src1}, R{Src2}, {Label}"
                : $"BNE R{Src1}, R{Src2}, {Off8:+#;-#;0}",
            OpCode.BLT => Label != null
                ? $"BLT R{Src1}, R{Src2}, {Label}"
                : $"BLT R{Src1}, R{Src2}, {Off8:+#;-#;0}",
            OpCode.BGE => Label != null
                ? $"BGE R{Src1}, R{Src2}, {Label}"
                : $"BGE R{Src1}, R{Src2}, {Off8:+#;-#;0}",
            OpCode.BGT => Label != null
                ? $"BGT R{Src1}, R{Src2}, {Label}"
                : $"BGT R{Src1}, R{Src2}, {Off8:+#;-#;0}",
            OpCode.BLE => Label != null
                ? $"BLE R{Src1}, R{Src2}, {Label}"
                : $"BLE R{Src1}, R{Src2}, {Off8:+#;-#;0}",

            // 函数调用
            OpCode.CALL => CallTarget != null ? $"CALL {CallTarget}" : $"CALL 0x{Imm32:X}",

            // 5 字节
            OpCode.JMP => Label != null ? $"JMP {Label}" : $"JMP 0x{Imm32:X}",

            _ => $"{Opcode} R{Dst}, R{Src1}, R{Src2}",
        };
}