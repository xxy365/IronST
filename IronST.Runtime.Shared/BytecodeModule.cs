// ═══════════════════════════════════════════════════════════════
//  BytecodeModule.cs — 字节码模块加载 (ISA §13.4 头部)
// ═══════════════════════════════════════════════════════════════
namespace IronST.Runtime.Shared;

/// <summary>
/// 从 byte[] 解析后的字节码模块，包含代码段、统一常量池和全局变量计数。
/// </summary>
public sealed class BytecodeModule
{
    public uint EntryPC { get; }
    public byte[] Code { get; }
    public BcValue[] ConstantPool { get; }
    public int GlobalCount { get; }
    public byte[]? DebugInfo { get; }

    private BytecodeModule(uint entry, byte[] code, BcValue[] cp, int gc, byte[]? dbg)
    {
        EntryPC = entry;
        Code = code;
        ConstantPool = cp;
        GlobalCount = gc;
        DebugInfo = dbg;
    }

    public static BytecodeModule FromBuilder(
        uint entryPC,
        byte[] code,
        BcValue[] constantPool,
        int globalCount,
        byte[]? debugInfo
    )
    {
        return new BytecodeModule(entryPC, code, constantPool, globalCount, debugInfo);
    }

    public static BytecodeModule Load(byte[] data)
    {
        using var br = new BinaryReader(new MemoryStream(data));

        // ── 头部 ──
        uint magic = br.ReadUInt32();
        if (magic != 0x49525354) // "IRST"
            throw new InvalidDataException($"Bad magic: 0x{magic:X8}");

        byte verMajor = br.ReadByte();
        byte verMinor = br.ReadByte();
        byte verPatch = br.ReadByte();
        br.ReadByte(); // flags (reserved)

        if (verMajor != 1)
            throw new InvalidDataException($"Unsupported version {verMajor}.{verMinor}.{verPatch}");

        uint entryPC = br.ReadUInt32();
        uint codeSize = br.ReadUInt32();
        int constPoolCount = br.ReadInt32();
        int globalCount = br.ReadInt32();
        uint debugInfoSize = br.ReadUInt32();

        // ── 大小限制 ──
        const int MaxCodeSize = 16 * 1024 * 1024;      // 16 MB

        if (codeSize > MaxCodeSize)
            throw new InvalidDataException($"Code size {codeSize} exceeds limit {MaxCodeSize}");

        // ── entryPC 校验 ──
        if (entryPC >= codeSize)
            throw new InvalidDataException($"EntryPC {entryPC} >= codeSize {codeSize}");

        // ── Code ──
        byte[] code = br.ReadBytes((int)codeSize);

        // ── ConstantPool (统一 BcValue 序列化格式) ──
        // 编解码与 .stmod 共用 BcValueSerializer（单一事实来源，字节不变）。
        var cp = new BcValue[constPoolCount];
        for (int i = 0; i < constPoolCount; i++)
            cp[i] = BcValueSerializer.Read(br, i);

        // ── DebugInfo (optional) ──
        byte[]? dbg = debugInfoSize > 0 ? br.ReadBytes((int)debugInfoSize) : null;

        return new BytecodeModule(entryPC, code, cp, globalCount, dbg);
    }
}