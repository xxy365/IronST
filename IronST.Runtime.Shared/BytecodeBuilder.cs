// ═══════════════════════════════════════════════════════════════
//  BytecodeBuilder.cs — 字节码构建器 (适配新版 Shared 统一常量池)
// ═══════════════════════════════════════════════════════════════
using System.Text;
using IronST.Runtime.Shared;

namespace IronST.Runtime.Shared;

public sealed class BytecodeBuilder
{
    private readonly List<byte> _code = new();

    // ── 统一常量池 ───────────────────────────────────────────
    private readonly List<BcValue> _constPool = new();
    private int _globalCount;

    // ── 寄存器名 ─────────────────────────────────────────────
    public const byte ZERO = 0,
        SP = 1,
        FP = 2,
        LR = 3;
    public const byte A0 = 4,
        A1 = 5,
        A2 = 6,
        A3 = 7;
    public const byte R8 = 8,
        R9 = 9,
        R10 = 10,
        R11 = 11;
    public const byte R16 = 16,
        R17 = 17,
        R18 = 18,
        R19 = 19;
    public const byte R20 = 20,
        R21 = 21,
        R22 = 22,
        R23 = 23;

    /// <summary>当前已发射的代码字节数 (= 下一条指令的 PC)。</summary>
    public int PC => _code.Count;

    /// <summary>
    /// 统一常量池当前条目数（0B 步骤 3 · 切片 4 引入）。
    /// </summary>
    /// <remarks>
    /// 供回填前校验"写入的常量索引确实落在本模块池内"。池只追加不去重
    /// （<see cref="AddConst(double)"/> / <see cref="AddString"/>），
    /// 故索引一经分配终身不变。
    /// </remarks>
    public int ConstPoolCount => _constPool.Count;

    // ══════════════════════════════════════════════════════════
    //  配置 & 常量池
    // ══════════════════════════════════════════════════════════
    public BytecodeBuilder Globals(int count)
    {
        _globalCount = count;
        return this;
    }

    /// <summary>向统一常量池添加 Int64，返回索引。</summary>
    public int AddConst(long value)
    {
        _constPool.Add(new BcValue(value));
        return _constPool.Count - 1;
    }

    /// <summary>向统一常量池添加 Float64，返回索引。</summary>
    public int AddConst(double value)
    {
        _constPool.Add(new BcValue(value));
        return _constPool.Count - 1;
    }

    /// <summary>向统一常量池添加 String，返回索引。</summary>
    public int AddString(string value)
    {
        _constPool.Add(new BcValue(value));
        return _constPool.Count - 1;
    }

    // ══════════════════════════════════════════════════════════
    //  修补 (前向引用)
    // ══════════════════════════════════════════════════════════
    public BytecodeBuilder PatchByte(int offset, byte value)
    {
        _code[offset] = value;
        return this;
    }

    public BytecodeBuilder PatchUInt32(int offset, uint value)
    {
        _code[offset] = (byte)value;
        _code[offset + 1] = (byte)(value >> 8);
        _code[offset + 2] = (byte)(value >> 16);
        _code[offset + 3] = (byte)(value >> 24);
        return this;
    }

    /// <summary>
    /// 按小端覆盖 4 字节有符号立即数（0B 步骤 3 · 切片 4 引入）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="LE32"/>（<c>LDC</c> 的发射路径）字节序一致，使回填写回的字节
    /// 与发射时逐位相同。合法常量索引为非负，与 <see cref="PatchUInt32"/> 在该区间
    /// 字节等价；本方法保留符号语义，供后续有符号 32 位立即数复用。
    /// </remarks>
    public BytecodeBuilder PatchInt32(int offset, int value)
    {
        _code[offset] = (byte)value;
        _code[offset + 1] = (byte)(value >> 8);
        _code[offset + 2] = (byte)(value >> 16);
        _code[offset + 3] = (byte)(value >> 24);
        return this;
    }

    public BytecodeBuilder PatchInt16(int offset, short value)
    {
        _code[offset] = (byte)(value & 0xFF);
        _code[offset + 1] = (byte)((value >> 8) & 0xFF);
        return this;
    }

    /// <summary>
    /// 按小端覆盖 2 字节<b>无符号</b>立即数（0B 步骤 3 · 切片 5 引入）。
    /// </summary>
    /// <remarks>
    /// 与 <c>HCALL</c> 的发射路径（<c>BytecodeBuilder.cs:213-219</c>：手工
    /// <c>(byte)(id &amp; 0xFF)</c> / <c>(byte)((id >> 8) &amp; 0xFF)</c>，
    /// 对 <c>ushort</c> 做<b>无符号</b>移位）字节序一致，使回填写回的字节与发射时逐位相同。
    /// <see cref="PatchInt16"/> 对任意 <c>short</c> 也能写出正确的小端两字节，
    /// 本方法保留无符号语义以匹配 <c>id16</c> 编码，避免调用点出现符号转换。
    /// </remarks>
    public BytecodeBuilder PatchUInt16(int offset, ushort value)
    {
        _code[offset] = (byte)(value & 0xFF);
        _code[offset + 1] = (byte)((value >> 8) & 0xFF);
        return this;
    }

    // ══════════════════════════════════════════════════════════
    //  底层字节发射
    // ══════════════════════════════════════════════════════════
    private BytecodeBuilder B(OpCode op)
    {
        _code.Add((byte)op);
        return this;
    }

    private BytecodeBuilder B(byte b)
    {
        _code.Add(b);
        return this;
    }

    private BytecodeBuilder LE16(short v)
    {
        _code.Add((byte)(v & 0xFF));
        _code.Add((byte)((v >> 8) & 0xFF));
        return this;
    }

    private BytecodeBuilder LE32(int v)
    {
        _code.Add((byte)v);
        _code.Add((byte)(v >> 8));
        _code.Add((byte)(v >> 16));
        _code.Add((byte)(v >> 24));
        return this;
    }

    private BytecodeBuilder U32(uint v)
    {
        _code.Add((byte)v);
        _code.Add((byte)(v >> 8));
        _code.Add((byte)(v >> 16));
        _code.Add((byte)(v >> 24));
        return this;
    }

    // ══════════════════════════════════════════════════════════
    //  指令发射 — 使用 OpCode 枚举
    // ══════════════════════════════════════════════════════════

    // ── 1 字节 [op] ─────────────────────────────────────────
    public BytecodeBuilder NOP() => B(OpCode.NOP);
    public BytecodeBuilder HALT() => B(OpCode.HALT);
    public BytecodeBuilder BRK() => B(OpCode.BRK);
    public BytecodeBuilder LEAVE() => B(OpCode.LEAVE);
    public BytecodeBuilder RET() => B(OpCode.RET);
    public BytecodeBuilder VMRET() => B(OpCode.VMRET);

    // ── 2 字节 [op][byte] ───────────────────────────────────
    public BytecodeBuilder SYSCALL(byte func) => B(OpCode.SYSCALL).B(func);
    public BytecodeBuilder LNIL(byte rd) => B(OpCode.LNIL).B(rd);
    public BytecodeBuilder LTRUE(byte rd) => B(OpCode.LTRUE).B(rd);
    public BytecodeBuilder LFALSE(byte rd) => B(OpCode.LFALSE).B(rd);
    public BytecodeBuilder PUSH(byte rs) => B(OpCode.PUSH).B(rs);
    public BytecodeBuilder POP(byte rd) => B(OpCode.POP).B(rd);
    public BytecodeBuilder JMPR(byte rs) => B(OpCode.JMPR).B(rs);
    public BytecodeBuilder CALLR(byte rs) => B(OpCode.CALLR).B(rs);
    public BytecodeBuilder IINC(byte rd) => B(OpCode.IINC).B(rd);
    public BytecodeBuilder IDEC(byte rd) => B(OpCode.IDEC).B(rd);
    public BytecodeBuilder VMCALL(byte id) => B(OpCode.VMCALL).B(id);

    // ── 3 字节 [op][rd][rs] — 双寄存器 ─────────────────────
    public BytecodeBuilder MOV(byte d, byte s) => B(OpCode.MOV).B(d).B(s);
    public BytecodeBuilder SWAP(byte d, byte s) => B(OpCode.SWAP).B(d).B(s);
    public BytecodeBuilder INEG(byte d, byte s) => B(OpCode.INEG).B(d).B(s);
    public BytecodeBuilder FNEG(byte d, byte s) => B(OpCode.FNEG).B(d).B(s);
    public BytecodeBuilder NOT(byte d, byte s) => B(OpCode.NOT).B(d).B(s);
    public BytecodeBuilder CLZ(byte d, byte s) => B(OpCode.CLZ).B(d).B(s);
    public BytecodeBuilder TYPE(byte d, byte s) => B(OpCode.TYPE).B(d).B(s);
    public BytecodeBuilder GETR(byte d, byte s) => B(OpCode.GETR).B(d).B(s);
    public BytecodeBuilder SETR(byte d, byte s) => B(OpCode.SETR).B(d).B(s);
    public BytecodeBuilder BOUNDS(byte idx, byte max) => B(OpCode.BOUNDS).B(idx).B(max);

    /// <summary>引用判空：R[reg] 为 Void 时抛 NullReference。</summary>
    public BytecodeBuilder NULLCK(byte reg) => B(OpCode.NULLCK).B(reg);
    public BytecodeBuilder GETGI(byte d, byte s) => B(OpCode.GETGI).B(d).B(s);
    public BytecodeBuilder SETGI(byte d, byte s) => B(OpCode.SETGI).B(d).B(s);
    public BytecodeBuilder TOINT(byte d, byte s) => B(OpCode.TOINT).B(d).B(s);
    public BytecodeBuilder TOFLT(byte d, byte s) => B(OpCode.TOFLT).B(d).B(s);
    public BytecodeBuilder TOSTR(byte d, byte s) => B(OpCode.TOSTR).B(d).B(s);
    public BytecodeBuilder TOBOOL(byte d, byte s) => B(OpCode.TOBOOL).B(d).B(s);

    // ── 3 字节 [op][lo][hi] — 16 位操作数 ───────────────────
    public BytecodeBuilder PUSHN(short n) => B(OpCode.PUSHN).LE16(n);
    public BytecodeBuilder POPN(short n) => B(OpCode.POPN).LE16(n);
    public BytecodeBuilder FRAME(short n) => B(OpCode.FRAME).LE16(n);

    /// <summary>HCALL id16。发射 3 字节 [0xB0][id_lo][id_hi]。</summary>
    public BytecodeBuilder HCALL(ushort id)
    {
        B(OpCode.HCALL);
        _code.Add((byte)(id & 0xFF));
        _code.Add((byte)((id >> 8) & 0xFF));
        return this;
    }

    // ── 3 字节 [op][rs][off8] — 单条件分支 ──────────────────
    public BytecodeBuilder BZ(byte rs, sbyte off) => B(OpCode.BZ).B(rs).B((byte)off);
    public BytecodeBuilder BNZ(byte rs, sbyte off) => B(OpCode.BNZ).B(rs).B((byte)off);

    // ── 4 字节 [op][rd][s16lo][s16hi] — 变量访问 ───────────
    public BytecodeBuilder GETL(byte rd, short slot) => B(OpCode.GETL).B(rd).LE16(slot);
    public BytecodeBuilder SETL(byte rs, short slot) => B(OpCode.SETL).B(rs).LE16(slot);
    public BytecodeBuilder GETG(byte rd, short slot) => B(OpCode.GETG).B(rd).LE16(slot);
    public BytecodeBuilder SETG(byte rs, short slot) => B(OpCode.SETG).B(rs).LE16(slot);

    // ── 4 字节 [op][rd][rs1][rs2] — 整数算术 ────────────────
    public BytecodeBuilder IADD(byte d, byte s1, byte s2) => B(OpCode.IADD).B(d).B(s1).B(s2);
    public BytecodeBuilder ISUB(byte d, byte s1, byte s2) => B(OpCode.ISUB).B(d).B(s1).B(s2);
    public BytecodeBuilder IMUL(byte d, byte s1, byte s2) => B(OpCode.IMUL).B(d).B(s1).B(s2);
    public BytecodeBuilder IDIV(byte d, byte s1, byte s2) => B(OpCode.IDIV).B(d).B(s1).B(s2);

    // ── 4 字节 [op][rd][rs1][rs2] — 浮点算术 ────────────────
    public BytecodeBuilder FADD(byte d, byte s1, byte s2) => B(OpCode.FADD).B(d).B(s1).B(s2);
    public BytecodeBuilder FSUB(byte d, byte s1, byte s2) => B(OpCode.FSUB).B(d).B(s1).B(s2);
    public BytecodeBuilder FMUL(byte d, byte s1, byte s2) => B(OpCode.FMUL).B(d).B(s1).B(s2);
    public BytecodeBuilder FDIV(byte d, byte s1, byte s2) => B(OpCode.FDIV).B(d).B(s1).B(s2);

    // ── 4 字节 [op][rd][rs1][rs2] — 位运算 ──────────────────
    public BytecodeBuilder AND(byte d, byte s1, byte s2) => B(OpCode.AND).B(d).B(s1).B(s2);
    public BytecodeBuilder OR(byte d, byte s1, byte s2) => B(OpCode.OR).B(d).B(s1).B(s2);
    public BytecodeBuilder XOR(byte d, byte s1, byte s2) => B(OpCode.XOR).B(d).B(s1).B(s2);
    public BytecodeBuilder SHL(byte d, byte s1, byte s2) => B(OpCode.SHL).B(d).B(s1).B(s2);
    public BytecodeBuilder SHR(byte d, byte s1, byte s2) => B(OpCode.SHR).B(d).B(s1).B(s2);
    public BytecodeBuilder SAR(byte d, byte s1, byte s2) => B(OpCode.SAR).B(d).B(s1).B(s2);
    public BytecodeBuilder ROL(byte d, byte s1, byte s2) => B(OpCode.ROL).B(d).B(s1).B(s2);
    public BytecodeBuilder ROR(byte d, byte s1, byte s2) => B(OpCode.ROR).B(d).B(s1).B(s2);

    // ── 4 字节 [op][rd][rs1][rs2] — 比较 ────────────────────
    public BytecodeBuilder CMP(byte d, byte s1, byte s2) => B(OpCode.CMP).B(d).B(s1).B(s2);
    public BytecodeBuilder IEQ(byte d, byte s1, byte s2) => B(OpCode.IEQ).B(d).B(s1).B(s2);
    public BytecodeBuilder INE(byte d, byte s1, byte s2) => B(OpCode.INE).B(d).B(s1).B(s2);
    public BytecodeBuilder FEQ(byte d, byte s1, byte s2) => B(OpCode.FEQ).B(d).B(s1).B(s2);

    // ── 4 字节 [op][rd][rs][imm8] — 立即数位运算 ────────────
    public BytecodeBuilder ANDI(byte d, byte s, byte imm) => B(OpCode.ANDI).B(d).B(s).B(imm);
    public BytecodeBuilder ORI(byte d, byte s, byte imm) => B(OpCode.ORI).B(d).B(s).B(imm);
    public BytecodeBuilder XORI(byte d, byte s, byte imm) => B(OpCode.XORI).B(d).B(s).B(imm);
    public BytecodeBuilder SHLI(byte d, byte s, byte imm) => B(OpCode.SHLI).B(d).B(s).B(imm);
    public BytecodeBuilder SHRI(byte d, byte s, byte imm) => B(OpCode.SHRI).B(d).B(s).B(imm);

    // ── 4 字节 [op][rd][rs][tag8] — ISTYPE ─────────────────
    public BytecodeBuilder ISTYPE(byte rd, byte rs, byte tag) =>
        B(OpCode.ISTYPE).B(rd).B(rs).B(tag);
    public BytecodeBuilder ISTYPE(byte rd, byte rs, ValueTag tag) =>
        B(OpCode.ISTYPE).B(rd).B(rs).B((byte)tag);

    // ── 5 字节 结构体 SNEW [op][rd][typeId][count:2 LE] ────
    public BytecodeBuilder SNEW(byte rd, byte typeId, ushort count)
    {
        B(OpCode.SNEW).B(rd).B(typeId);
        _code.Add((byte)(count & 0xFF));
        _code.Add((byte)((count >> 8) & 0xFF));
        return this;
    }

    // ── 4 字节 结构体 SGET/SSET ────────────────────────────
    public BytecodeBuilder SGET(byte d, byte obj, byte field) =>
        B(OpCode.SGET).B(d).B(obj).B(field);
    public BytecodeBuilder SSET(byte src, byte obj, byte field) =>
        B(OpCode.SSET).B(src).B(obj).B(field);

    // ── 4 字节 [op][rs1][rs2][off8] — 双条件分支 ───────────
    public BytecodeBuilder BEQ(byte s1, byte s2, sbyte o) => B(OpCode.BEQ).B(s1).B(s2).B((byte)o);
    public BytecodeBuilder BNE(byte s1, byte s2, sbyte o) => B(OpCode.BNE).B(s1).B(s2).B((byte)o);
    public BytecodeBuilder BLT(byte s1, byte s2, sbyte o) => B(OpCode.BLT).B(s1).B(s2).B((byte)o);
    public BytecodeBuilder BGE(byte s1, byte s2, sbyte o) => B(OpCode.BGE).B(s1).B(s2).B((byte)o);
    public BytecodeBuilder BGT(byte s1, byte s2, sbyte o) => B(OpCode.BGT).B(s1).B(s2).B((byte)o);
    public BytecodeBuilder BLE(byte s1, byte s2, sbyte o) => B(OpCode.BLE).B(s1).B(s2).B((byte)o);

    // ── 5 字节 [op][addr32] ─────────────────────────────────
    public int JMP(uint addr)
    {
        int offset = _code.Count;
        B(OpCode.JMP).U32(addr);
        return offset;
    }

    public int CALL(uint addr)
    {
        int offset = _code.Count;
        B(OpCode.CALL).U32(addr);
        return offset;
    }

    // ── 6 字节 [op][rd][idx32] — 统一常量池加载 ─────────────
    public BytecodeBuilder LDC(byte rd, int idx) => B(OpCode.LDC).B(rd).LE32(idx);

    // ══════════════════════════════════════════════════════════
    //  代码校验
    // ══════════════════════════════════════════════════════════
    private void ValidateCode()
    {
        int pc = 0;
        while (pc < _code.Count) {
            OpCode op = (OpCode)_code[pc];
            if (!VMBase.InstrLength.TryGetValue(op, out var len) || len == 0)
                throw new InvalidOperationException(
                    $"Invalid opcode 0x{(byte)op:X2} at offset {pc}");
            pc += len;
        }
        if (pc != _code.Count)
            throw new InvalidOperationException(
                $"Code size mismatch: walked {pc} bytes but code has {_code.Count} bytes");
    }

    // ══════════════════════════════════════════════════════════
    //  Build → BytecodeModule
    // ══════════════════════════════════════════════════════════
    public BytecodeModule Build() => BuildModule(0);

    public BytecodeModule BuildModule(uint entryPC)
    {
        ValidateCode();
        return BytecodeModule.FromBuilder(
            entryPC,
            _code.ToArray(),
            _constPool.ToArray(),
            _globalCount,
            debugInfo: null
        );
    }

    // ══════════════════════════════════════════════════════════
    //  序列化 → .irst 文件 (统一常量池格式)
    // ══════════════════════════════════════════════════════════
    public byte[] Serialize(uint entryPC = 0)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // ── 头部 (28 bytes) ──
        bw.Write(0x49525354u);
        bw.Write((byte)1);
        bw.Write((byte)0);
        bw.Write((byte)0);
        bw.Write((byte)0);
        bw.Write(entryPC);
        bw.Write((uint)_code.Count);
        bw.Write(_constPool.Count);
        bw.Write(_globalCount);
        bw.Write(0u);

        // ── Code ──
        bw.Write(_code.ToArray());

        // ── ConstantPool (统一 BcValue 序列化) ──
        foreach (var v in _constPool) {
            bw.Write((byte)v.Tag);
            bw.Write(new byte[7]);

            switch (v.Tag) {
                case ValueTag.Int64:
                    bw.Write(v.IntValue);
                    break;
                case ValueTag.Float64:
                    bw.Write(v.FloatValue);
                    break;
                case ValueTag.String: {
                        byte[] utf8 = Encoding.UTF8.GetBytes(v.StringValue ?? "");
                        bw.Write(utf8.Length);
                        bw.Write(utf8);
                        int padding = (8 - (utf8.Length % 8)) % 8;
                        if (padding > 0) bw.Write(new byte[padding]);
                        break;
                    }
                default:
                    throw new InvalidOperationException($"Unsupported const pool tag: {v.Tag}");
            }
        }

        bw.Flush();
        return ms.ToArray();
    }

    public void SaveTo(string path, uint entryPC = 0)
    {
        File.WriteAllBytes(path, Serialize(entryPC));
    }

    /// <summary>发射原始字节 (仅测试用)。</summary>
    public BytecodeBuilder EmitRaw(params byte[] bytes)
    {
        _code.AddRange(bytes);
        return this;
    }

    /// <summary>获取已发射的原始代码字节 (调试用)。</summary>
    public byte[] GetRawCode() => _code.ToArray();
}