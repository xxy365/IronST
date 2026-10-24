// ═══════════════════════════════════════════════════════════════
//  TraceLogger.cs — 指令跟踪日志 (独立工具，不依赖 VMDebugSession)
// ═══════════════════════════════════════════════════════════════
using IronST.Runtime.Shared;

namespace IronST.Runtime.Debug;

/// <summary>
/// 记录每条执行的指令及其上下文，用于事后分析。
/// </summary>
public sealed class TraceLogger
{
    public sealed class TraceEntry
    {
        public uint PC { get; init; }
        public string Disasm { get; init; } = "";
        public BcValue[] RegSnapshot { get; init; } = Array.Empty<BcValue>();
    }

    private readonly List<TraceEntry> _entries = new();
    private readonly int[] _watchRegs;

    /// <param name="watchRegs">跟踪时快照的寄存器列表 (默认 R0-R7)</param>
    public TraceLogger(int[]? watchRegs = null)
    {
        _watchRegs = watchRegs ?? new[] { 0, 1, 2, 3, 4, 5, 6, 7 };
    }

    /// <summary>已记录的条目 (只读)。</summary>
    public IReadOnlyList<TraceEntry> Entries => _entries;
    public int Count => _entries.Count;

    /// <summary>记录一条指令。</summary>
    internal void AddEntry(uint pc, VMBase vm)
    {
        var ctx = vm.Context;

        var snap = new BcValue[_watchRegs.Length];
        for (int i = 0; i < _watchRegs.Length; i++)
            snap[i] = ctx.ReadReg(_watchRegs[i]);

        _entries.Add(
            new TraceEntry
            {
                PC = pc,
                Disasm = QuickDisasm(vm, pc),
                RegSnapshot = snap,
            }
        );
    }

    public void Clear() => _entries.Clear();

    /// <summary>导出为文本。</summary>
    public string Dump()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var e in _entries)
        {
            sb.Append($"0x{e.PC:X8}  {e.Disasm.PadRight(32)}  ");
            for (int i = 0; i < _watchRegs.Length; i++)
                sb.Append($"R{_watchRegs[i]}={e.RegSnapshot[i]} ");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>导出到文件。</summary>
    public void SaveToFile(string path) => File.WriteAllText(path, Dump());

    // ══════════════════════════════════════════════════════════
    //  内置简易反汇编
    // ══════════════════════════════════════════════════════════
    private static string QuickDisasm(VMBase vm, uint pc)
        => Disassembler.Disassemble(vm.Code, pc);
}
