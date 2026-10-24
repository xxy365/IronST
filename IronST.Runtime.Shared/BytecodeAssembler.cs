// ═══════════════════════════════════════════════════════════════
//  BytecodeAssembler.cs — 高级字节码构建器 (适配新版 Shared)
//
//  包装 BytecodeBuilder，提供:
//    • 标签系统 (自动 Patch 前向引用)
//    • 函数定义/调用 (自动 FRAME/LEAVE/RET)
//    • 全局变量命名访问
//    • 临时寄存器分配
//    • 便捷快捷方法 (Print/Exit/LoadConst 等)
//    • 底层透传 (Raw 属性访问完整 Builder)
// ═══════════════════════════════════════════════════════════════
using IronST.Runtime.Shared;

namespace IronST.Runtime.Shared;

public sealed class BytecodeAssembler
{
    private readonly BytecodeBuilder _b = new();

    // ── 标签系统 ─────────────────────────────────────────────
    private readonly Dictionary<string, int> _labelDefs = new();

    /// <summary>
    /// 待回填的 8 位相对偏移（条件分支）。
    /// </summary>
    /// <remarks>
    /// 32 位绝对地址已迁移到 <see cref="Relocations"/>（0B 步骤 3 · 切片 2）。
    /// 六类逐类迁移，本列表只保留尚未迁移的 Off8。
    /// </remarks>
    private readonly List<PatchEntry> _pendingPatches = new();

    private readonly record struct PatchEntry(int Offset, string Target);

    /// <summary>
    /// 本汇编器产出的重定位记录（0B 步骤 3）。
    /// </summary>
    /// <remarks>
    /// 当前 <see cref="RelocationKind.Addr32"/>、<see cref="RelocationKind.GlobalIdx"/>、
    /// <see cref="RelocationKind.ConstIdx"/> 三类走这张表：<b>记录 → 解析 → 回填</b>
    /// 三个环节都在表上完成，写回的字节与迁移前逐字节一致。其余类别逐类迁移，
    /// 迁移之前 <see cref="ApplyPatches"/> 会<b>显式拒绝</b>表里出现的未迁移种类，
    /// 而不是静默跳过。
    /// </remarks>
    public RelocationTable Relocations { get; } = new();

    /// <summary>
    /// 为**已解析**的函数调用也登记 <c>Addr32</c> 记录（默认 <c>false</c>）。
    /// </summary>
    /// <remarks>
    /// 单模块构建不需要它（字节相同）；但静态链接会把每个函数搬到新基址，
    /// 因此"发射时已定义"的调用同样必须可重定位。编译器（<c>ByteCodeGenerator</c>）
    /// 打开本开关；汇编器级旧用例保持 <c>false</c>，行为不变。
    /// </remarks>
    public bool RecordResolvedCalls { get; set; }

    /// <summary>
    /// 允许 <c>Addr32</c> 符号在装配期保持**未解析**（默认 <c>false</c> = 一律报错）。
    /// </summary>
    /// <remarks>
    /// 仅用于"独立编译、稍后链接"的模块：未解析的 <c>CALL</c> 立即数被写成
    /// <c>0xFFFFFFFF</c> 哨兵（<b>不是</b>合法的 0 地址），符号名记入
    /// <see cref="UnresolvedSymbols"/>，由链接器回填。未链接模块不得直接执行。
    /// </remarks>
    public bool AllowUnresolvedAddr32 { get; set; }

    private readonly List<string> _unresolvedSymbols = new();

    /// <summary>装配期未能解析的 <c>Addr32</c> 符号名（去重，保持首次出现顺序）。</summary>
    public IReadOnlyList<string> UnresolvedSymbols => _unresolvedSymbols;

    /// <summary>未解析 <c>CALL</c>/<c>JMP</c> 立即数的哨兵值。</summary>
    public const uint UnresolvedAddr32Sentinel = 0xFFFFFFFFu;

    /// <summary>
    /// 常量索引重映射：<c>模块局部索引 → 最终索引</c>（0B 步骤 3 · 切片 4）。
    /// </summary>
    /// <remarks>
    /// <b>单模块默认 <c>null</c> = 恒等映射</b>，回填出的字节与迁移前完全一致。
    /// 链接器把多个模块的常量池<b>追加合并</b>（§3.3 REL-4 已冻结：不去重）后，
    /// 需要把每个模块记录里的局部索引重编号到合并池的位置，此时设置本属性即可，
    /// <b>无需改动回填逻辑本身</b>。
    ///
    /// <para>
    /// 这正是"局部索引 ≠ 稳定身份"的落地：记录里存的永远是<b>本模块</b>的序号，
    /// 由本钩子决定它在最终产物里指向哪一条。常量没有符号名（<see cref="RelocationRules.RequiresSymbol"/>
    /// 对 <c>ConstIdx</c> 返回 false），所以重编号必须走这条显式通道，不能靠名字。
    /// </para>
    /// </remarks>
    public Func<int, int>? ConstIndexRemap { get; set; }

    /// <summary>
    /// 宿主 ID 重编号钩子（0B 步骤 3 · 切片 5）。<c>null</c> = 单模块恒等映射。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 入参是记录里的<b>符号名</b>（宿主函数的稳定身份）与该模块内的<b>局部</b>宿主索引，
    /// 出参是最终宿主 ID。链接器把各模块的局部 ID 统一重编号时设此钩子；
    /// 单模块不设即恒等 —— 回填不改变任何字节（幂等）。
    /// </para>
    /// <para>
    /// <b>与 <see cref="ConstIndexRemap"/> 的区别</b>：常量<b>没有</b>跨模块稳定身份，
    /// 只能靠显式整数映射；宿主函数<b>有</b>稳定名字（<c>SymbolName</c>），
    /// 链接期按名解析即可，本钩子只是把"按名解析出的最终 ID"接进模块内回填的入口。
    /// </para>
    /// <para>
    /// <b>静态 ABI ID 不进重定位表</b>（§5 <c>HOST-2</c> 裁定）：只有动态注册的宿主函数
    /// 走这条记录—解析—回填链路，<c>HostCallIds</c> 常量保持硬编码、不参与重编号。
    /// </para>
    /// </remarks>
    public Func<string, ushort, ushort>? HostIdRemap { get; set; }

    // ── 函数表 ───────────────────────────────────────────────
    private readonly Dictionary<string, FunctionInfo> _funcTable = new();
    private readonly record struct FunctionInfo(int PC, short ParamCount, short LocalCount);

    // ── 函数字节范围（`.stmod` 函数索引的唯一事实来源）──────────
    /// <summary>
    /// 一个已发射函数的<b>真实</b>字节范围，含 <c>FRAME</c> 与结尾 <c>LEAVE</c>/<c>RET</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="StartPC"/> 即 <see cref="DefFunc"/> 开始时的 <c>PC</c>；
    /// <see cref="EndPC"/> 是发射完 <c>RET</c> 之后的 <c>PC</c>（<b>开区间</b>）。
    /// 因此 <c>CodeLength = EndPC - StartPC</c>，且 <c>Code[StartPC] == FRAME</c>、
    /// <c>Code[EndPC-1] == RET</c> —— 这两条不变量由 `.stmod` 测试直接验证，
    /// 不靠"函数是连续排列"的推测。
    /// </para>
    /// <para>
    /// 记录在函数体发射**完成后**才追加：若发射过程中抛异常，不会留下半截范围。
    /// </para>
    /// </remarks>
    public readonly record struct FunctionRange(
        string Name, int StartPC, int EndPC, int ParamCount, int LocalCount)
    {
        /// <summary>字节长度（<c>EndPC - StartPC</c>）。</summary>
        public int ByteLength => EndPC - StartPC;
    }

    private readonly List<FunctionRange> _functionRanges = new();

    /// <summary>按发射顺序记录的函数字节范围。</summary>
    public IReadOnlyList<FunctionRange> FunctionRanges => _functionRanges;

    // ── 全局变量 ─────────────────────────────────────────────
    private readonly Dictionary<string, short> _globalNames = new();
    private short _globalCount;

    /// <summary>
    /// 全局槽位上限。有效槽位索引为 <c>0..32766</c>，最多 32767 个。
    /// 数值取自 <see cref="RelocationRules.MaxGlobalSlots"/>（单一事实来源），
    /// 重定位记录的槽位范围校验用的是同一个常量。
    ///
    /// <para>
    /// <b>F6 背景</b>：<see cref="_globalCount"/> 是 <see cref="short"/>，且 GETG/SETG 的
    /// 槽位同样按 16 位有符号编码。<c>_globalCount++</c> 在第 32768 次声明时会把 32767
    /// 回绕成 -32768，随后 <see cref="ApplyPatches"/> 把这个负数写进头部，VM 分配
    /// <c>new BcValue[负数]</c> 直接崩溃。故必须在回绕前拒绝。
    /// </para>
    /// </summary>
    public const int MaxGlobals = RelocationRules.MaxGlobalSlots;

    // ── 临时寄存器分配 ───────────────────────────────────────
    private byte _nextTemp = 8;
    private readonly Stack<byte> _freeTemps = new();

    // ══════════════════════════════════════════════════════════
    //  底层透传
    // ══════════════════════════════════════════════════════════
    public BytecodeBuilder Raw => _b;
    public int PC => _b.PC;

    // ══════════════════════════════════════════════════════════
    //  标签系统
    // ══════════════════════════════════════════════════════════
    public BytecodeAssembler Label(string name)
    {
        _labelDefs[name] = _b.PC;
        return this;
    }

    public BytecodeAssembler Jmp(string label)
    {
        int opcodeOffset = _b.JMP(0);
        RecordAddr32(opcodeOffset + 1, label);
        return this;
    }

    public BytecodeAssembler Call(string label)
    {
        int opcodeOffset = _b.CALL(0);
        RecordAddr32(opcodeOffset + 1, label);
        return this;
    }

    // ── 单条件分支 ───────────────────────────────────────────
    public BytecodeAssembler Bz(byte cond, string label)
    {
        int patchOffset = _b.PC + 2;
        _b.BZ(cond, 0);
        _pendingPatches.Add(new(patchOffset, label));
        return this;
    }

    public BytecodeAssembler Bnz(byte cond, string label)
    {
        int patchOffset = _b.PC + 2;
        _b.BNZ(cond, 0);
        _pendingPatches.Add(new(patchOffset, label));
        return this;
    }

    // ── 双条件分支 ───────────────────────────────────────────
    public BytecodeAssembler Beq(byte s1, byte s2, string label)
    {
        int patchOffset = _b.PC + 3;
        _b.BEQ(s1, s2, 0);
        _pendingPatches.Add(new(patchOffset, label));
        return this;
    }

    public BytecodeAssembler Bne(byte s1, byte s2, string label)
    {
        int patchOffset = _b.PC + 3;
        _b.BNE(s1, s2, 0);
        _pendingPatches.Add(new(patchOffset, label));
        return this;
    }

    public BytecodeAssembler Blt(byte s1, byte s2, string label)
    {
        int patchOffset = _b.PC + 3;
        _b.BLT(s1, s2, 0);
        _pendingPatches.Add(new(patchOffset, label));
        return this;
    }

    public BytecodeAssembler Bge(byte s1, byte s2, string label)
    {
        int patchOffset = _b.PC + 3;
        _b.BGE(s1, s2, 0);
        _pendingPatches.Add(new(patchOffset, label));
        return this;
    }

    public BytecodeAssembler Bgt(byte s1, byte s2, string label)
    {
        int patchOffset = _b.PC + 3;
        _b.BGT(s1, s2, 0);
        _pendingPatches.Add(new(patchOffset, label));
        return this;
    }

    public BytecodeAssembler Ble(byte s1, byte s2, string label)
    {
        int patchOffset = _b.PC + 3;
        _b.BLE(s1, s2, 0);
        _pendingPatches.Add(new(patchOffset, label));
        return this;
    }

    // ══════════════════════════════════════════════════════════
    //  函数定义 / 调用
    // ══════════════════════════════════════════════════════════
    public BytecodeAssembler DefFunc(string name, int paramCount, int extraLocals, Action body)
    {
        if (paramCount is < 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(paramCount), "0–4");

        short totalLocals = (short)(paramCount + extraLocals);
        int funcPC = _b.PC;
        _funcTable[name] = new(funcPC, (short)paramCount, totalLocals);

        _b.FRAME(totalLocals);

        byte[] argRegs = { BytecodeBuilder.A0, BytecodeBuilder.A1, BytecodeBuilder.A2, BytecodeBuilder.A3 };
        for (int i = 0; i < paramCount; i++)
            _b.SETL(argRegs[i], (short)i);

        body();

        _b.LEAVE();
        _b.RET();

        // 函数体完整发射后才登记范围：EndPC 为 RET 之后的 PC（开区间）。
        _functionRanges.Add(new FunctionRange(name, funcPC, _b.PC, paramCount, totalLocals));
        return this;
    }

    public BytecodeAssembler DefFunc(string name, int paramCount, Action body) =>
        DefFunc(name, paramCount, 0, body);

    public BytecodeAssembler DefFunc(string name, Action body) => DefFunc(name, 0, 0, body);

    public BytecodeAssembler CallFunc(string name)
    {
        if (_funcTable.TryGetValue(name, out var info)) {
            int opcodeOffset = _b.CALL((uint)info.PC);
            // 链接期需要重定基**所有**绝对 CALL（函数会随模块拼接而移动）。
            // 单模块编译默认不记录（保持汇编器级旧契约）；编译器打开本开关。
            if (RecordResolvedCalls)
                RecordAddr32(opcodeOffset + 1, name);
        } else {
            int opcodeOffset = _b.CALL(0);
            RecordAddr32(opcodeOffset + 1, name);
        }
        return this;
    }

    public BytecodeAssembler GetParam(byte rd, int paramIndex)
    {
        _b.GETL(rd, (short)paramIndex);
        return this;
    }

    public BytecodeAssembler SetLocal(byte rs, int localIndex)
    {
        _b.SETL(rs, (short)localIndex);
        return this;
    }

    public BytecodeAssembler GetLocal(byte rd, int localIndex)
    {
        _b.GETL(rd, (short)localIndex);
        return this;
    }

    // ══════════════════════════════════════════════════════════
    //  全局变量 (命名访问)
    // ══════════════════════════════════════════════════════════
    public BytecodeAssembler DeclareGlobal(string name) => DeclareGlobal(name, 1);

    /// <summary>
    /// 声明一个占用 <paramref name="slotCount"/> 个连续槽位的全局变量。
    /// </summary>
    /// <remarks>
    /// 聚合全局（结构体 / 数组）在字节码里按<b>扁平槽</b>存储：字段/元素各占一槽，
    /// 名字映射到<b>起始槽号</b>。标量仍是一个槽 ⇒ 既有程序槽位逐字节不变。
    /// </remarks>
    public BytecodeAssembler DeclareGlobal(string name, int slotCount)
    {
        if (slotCount < 1)
            throw new ArgumentOutOfRangeException(
                nameof(slotCount), slotCount, "A global must occupy at least one slot.");
        if (_globalNames.ContainsKey(name))
            throw new InvalidOperationException($"Global '{name}' already declared.");
        if (_globalCount + slotCount > MaxGlobals)
            throw new InvalidOperationException(
                $"Too many globals (max {MaxGlobals}); slot index range is 0..{MaxGlobals - 1}."
            );
        _globalNames[name] = _globalCount;
        _globalCount += (short)slotCount;
        return this;
    }

    public BytecodeAssembler GetGlobal(byte rd, string name)
    {
        short slot = _globalNames[name];
        RecordGlobalIdx(name, slot, RelocationSourceLocation.Unknown);
        _b.GETG(rd, slot);
        return this;
    }

    public BytecodeAssembler SetGlobal(byte rs, string name)
    {
        short slot = _globalNames[name];
        RecordGlobalIdx(name, slot, RelocationSourceLocation.Unknown);
        _b.SETG(rs, slot);
        return this;
    }

    /// <summary>
    /// 记录一条全局变量引用（0B 步骤 3 · 切片 3）。
    /// </summary>
    /// <remarks>
    /// <c>GETG/SETG = [op][reg][LE16]</c>：立即数在操作码 +2 处、宽 16 位，
    /// 故立即数偏移取<b>当前</b> PC + 2 —— 调用方必须在发射指令<b>之前</b>调用。
    ///
    /// <para>
    /// <b>符号身份与局部索引刻意分开</b>：<see cref="RelocationRecord.SymbolName"/> 存的是
    /// 全局变量的<b>声明名</b>（跨模块身份），<b>模块局部槽位索引</b>放在
    /// <see cref="RelocationRecord.Addend"/>。不把局部槽位伪装成符号名 —— 链接期要按名字
    /// 重编号，局部索引只在本模块内有意义。
    /// </para>
    /// </summary>
    public void RecordGlobalIdx(string name, short slot, RelocationSourceLocation source)
    {
        Relocations.Add(RelocationRecord.Create(
            RelocationKind.GlobalIdx,
            RelocationSection.Code,
            _b.PC + 2,
            name,
            source,
            addend: slot));
    }

    /// <summary>
    /// 登记一条 <see cref="RelocationKind.HostIdx"/> 记录（0B 步骤 3 · 切片 5）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>偏移 <c>_b.PC + 1</c> 的依据</b>：<c>HCALL</c> 是 <c>[0xB0][id_lo][id_hi]</c> 共 3 字节、
    /// <b>无 rd 字节</b>（<c>BytecodeBuilder.cs:213-219</c>），立即数紧随操作码；
    /// 对照 <c>GETG/SETG</c> 是 操作码 + rd + <c>LE16</c>，故 <see cref="RecordGlobalIdx"/> 用的是 <c>PC + 2</c>。
    /// </para>
    /// <para>
    /// <b>调用时机</b>：必须在发射 <c>HCALL</c> <b>之前</b>调用，此时 <c>_b.PC</c> 即操作码位置
    /// （<c>EmitInstr</c> 入口不改 PC，其 <c>_sourceMap[_asm.PC]</c> 即以此为准）。
    /// </para>
    /// <para>
    /// <paramref name="localId"/> 是<b>模块局部</b>宿主索引，由 <see cref="RelocationRecord.Addend"/> 承载；
    /// 跨模块身份是 <paramref name="hostName"/>。单模块回填写回同一 ID（幂等），
    /// 跨模块重编号走 <see cref="HostIdRemap"/> 或链接期按名解析。
    /// </para>
    /// </remarks>
    public void RecordHostIdx(string hostName, ushort localId, RelocationSourceLocation source)
    {
        Relocations.Add(RelocationRecord.Create(
            RelocationKind.HostIdx,
            RelocationSection.Code,
            _b.PC + 1,
            hostName,
            source,
            addend: localId));
    }

    // ══════════════════════════════════════════════════════════
    //  临时寄存器分配
    // ══════════════════════════════════════════════════════════
    public byte AllocTemp()
    {
        if (_freeTemps.Count > 0)
            return _freeTemps.Pop();
        if (_nextTemp > 255)
            throw new InvalidOperationException("Out of registers.");
        return _nextTemp++;
    }

    public void FreeTemp(byte reg)
    {
        if (reg >= 8)
            _freeTemps.Push(reg);
    }

    // ══════════════════════════════════════════════════════════
    //  便捷快捷方法
    // ══════════════════════════════════════════════════════════

    // ── 智能加载 (统一常量池 + LDC) ──────────────────────────

    /// <summary>
    /// 发射一条 <c>LDC</c> 并登记 <see cref="RelocationKind.ConstIdx"/> 记录
    /// （0B 步骤 3 · 切片 4）。<b>两条发射路径共用的唯一收口</b>。
    /// </summary>
    /// <remarks>
    /// <c>LDC = [op][rd][LE32]</c>：立即数在操作码 +2 处、宽 32 位、<b>有符号</b>编码
    /// （<c>BytecodeBuilder.cs:311</c>）。故立即数偏移取<b>当前</b> PC + 2 ——
    /// 必须在 <c>_b.LDC</c> 之前调用。
    ///
    /// <para>
    /// <b>符号身份与局部索引分离</b>：常量没有跨模块稳定身份，故记录<b>不带</b>
    /// <see cref="RelocationRecord.SymbolName"/>（携带会被 <see cref="RelocationRecord.Validate"/>
    /// 拒绝），模块局部常量序号放在 <see cref="RelocationRecord.Addend"/>。
    /// 链接期重编号经 <see cref="ConstIndexRemap"/> 完成。
    /// </para>
    ///
    /// <para>
    /// <paramref name="source"/> 刻意<b>不给默认值</b>：<see cref="RelocationSourceLocation"/>
    /// 是结构体，<c>default</c> 会给出行 0 —— 而 0 是合法行号，会被误当成真实源位置。
    /// 无源信息时显式传 <see cref="RelocationSourceLocation.Unknown"/>。
    /// </para>
    /// </remarks>
    public void EmitLdc(byte rd, int constIndex, RelocationSourceLocation source)
    {
        Relocations.Add(RelocationRecord.Create(
            RelocationKind.ConstIdx,
            RelocationSection.Code,
            _b.PC + 2,
            symbolName: null,
            source,
            addend: constIndex));
        _b.LDC(rd, constIndex);
    }

    /// <summary>加载整数到寄存器。返回寄存器号。</summary>
    public byte Load(long value)
    {
        byte rd = AllocTemp();
        EmitLdc(rd, _b.AddConst(value), RelocationSourceLocation.Unknown);
        return rd;
    }

    /// <summary>加载浮点数到寄存器。返回寄存器号。</summary>
    public byte Load(double value)
    {
        byte rd = AllocTemp();
        EmitLdc(rd, _b.AddConst(value), RelocationSourceLocation.Unknown);
        return rd;
    }

    /// <summary>加载字符串到寄存器。返回寄存器号。</summary>
    public byte Load(string value)
    {
        byte rd = AllocTemp();
        EmitLdc(rd, _b.AddString(value), RelocationSourceLocation.Unknown);
        return rd;
    }

    /// <summary>加载布尔值到寄存器。返回寄存器号。</summary>
    public byte Load(bool value)
    {
        byte rd = AllocTemp();
        if (value) _b.LTRUE(rd);
        else _b.LFALSE(rd);
        return rd;
    }

    /// <summary>加载 Void 到寄存器。返回寄存器号。</summary>
    public byte LoadNil()
    {
        byte rd = AllocTemp();
        _b.LNIL(rd);
        return rd;
    }

    // ── 结构体 ───────────────────────────────────────────────

    /// <summary>分配 Struct。返回存放引用的寄存器号。</summary>
    public byte StructNew(byte typeId, ushort fieldCount)
    {
        byte rd = AllocTemp();
        _b.SNEW(rd, typeId, fieldCount);
        return rd;
    }

    /// <summary>R[rd] ← R[obj].StructFields[field]。返回 rd。</summary>
    public byte StructGet(byte obj, byte field)
    {
        byte rd = AllocTemp();
        _b.SGET(rd, obj, field);
        return rd;
    }

    /// <summary>R[obj].StructFields[field] ← R[src]。</summary>
    public BytecodeAssembler StructSet(byte src, byte obj, byte field)
    {
        _b.SSET(src, obj, field);
        return this;
    }

    // ── 打印 (全部改用 HCALL) ────────────────────────────────

    /// <summary>打印字符串常量 (一步完成)。</summary>
    public BytecodeAssembler Print(string text)
    {
        byte tmp = Load(text);
        _b.MOV(BytecodeBuilder.A0, tmp);
        FreeTemp(tmp);
        _b.HCALL(HostCallIds.PRINT_STR);
        return this;
    }

    /// <summary>打印寄存器中的值 (整数)。</summary>
    public BytecodeAssembler PrintInt(byte reg)
    {
        _b.MOV(BytecodeBuilder.A0, reg);
        _b.HCALL(HostCallIds.PRINT_INT);
        return this;
    }

    /// <summary>打印寄存器中的值 (字符串)。</summary>
    public BytecodeAssembler PrintStr(byte reg)
    {
        _b.MOV(BytecodeBuilder.A0, reg);
        _b.HCALL(HostCallIds.PRINT_STR);
        return this;
    }

    /// <summary>打印字符 (ASCII 码)。</summary>
    public BytecodeAssembler PrintChar(byte reg)
    {
        _b.MOV(BytecodeBuilder.A0, reg);
        _b.HCALL(HostCallIds.PRINT_CHAR);
        return this;
    }

    /// <summary>打印换行符。</summary>
    public BytecodeAssembler PrintLn()
    {
        byte tmp = Load(10L); // '\n' as Int64
        _b.MOV(BytecodeBuilder.A0, tmp);
        FreeTemp(tmp);
        _b.HCALL(HostCallIds.PRINT_CHAR);
        return this;
    }

    // ── 整数算术便捷 ────────────────────────────────────────
    public byte IAdd(byte s1, byte s2)
    {
        byte rd = AllocTemp();
        _b.IADD(rd, s1, s2);
        return rd;
    }

    public byte ISub(byte s1, byte s2)
    {
        byte rd = AllocTemp();
        _b.ISUB(rd, s1, s2);
        return rd;
    }

    public byte IMul(byte s1, byte s2)
    {
        byte rd = AllocTemp();
        _b.IMUL(rd, s1, s2);
        return rd;
    }

    public byte IDiv(byte s1, byte s2)
    {
        byte rd = AllocTemp();
        _b.IDIV(rd, s1, s2);
        return rd;
    }

    // ── 浮点算术便捷 ────────────────────────────────────────
    public byte FAdd(byte s1, byte s2)
    {
        byte rd = AllocTemp();
        _b.FADD(rd, s1, s2);
        return rd;
    }

    public byte FSub(byte s1, byte s2)
    {
        byte rd = AllocTemp();
        _b.FSUB(rd, s1, s2);
        return rd;
    }

    public byte FMul(byte s1, byte s2)
    {
        byte rd = AllocTemp();
        _b.FMUL(rd, s1, s2);
        return rd;
    }

    public byte FDiv(byte s1, byte s2)
    {
        byte rd = AllocTemp();
        _b.FDIV(rd, s1, s2);
        return rd;
    }

    // ── 比较便捷 ────────────────────────────────────────────

    /// <summary>R[d] ← CMP(R[s1], R[s2]) → Int64(-1/0/1)。返回 d。</summary>
    public byte Compare(byte s1, byte s2)
    {
        byte rd = AllocTemp();
        _b.CMP(rd, s1, s2);
        return rd;
    }

    /// <summary>R[d] ← R[s1] == R[s2] ? 1 : 0 (Int64)。返回 d。</summary>
    public byte IEq(byte s1, byte s2)
    {
        byte rd = AllocTemp();
        _b.IEQ(rd, s1, s2);
        return rd;
    }

    /// <summary>R[d] ← R[s1] != R[s2] ? 1 : 0 (Int64)。返回 d。</summary>
    public byte INe(byte s1, byte s2)
    {
        byte rd = AllocTemp();
        _b.INE(rd, s1, s2);
        return rd;
    }

    /// <summary>R[d] ← R[s1] == R[s2] ? 1 : 0 (Float64)。返回 d。</summary>
    public byte FEq(byte s1, byte s2)
    {
        byte rd = AllocTemp();
        _b.FEQ(rd, s1, s2);
        return rd;
    }

    // ── 控制 ─────────────────────────────────────────────────

    /// <summary>停机 (SYSCALL EXIT)。</summary>
    public BytecodeAssembler Exit()
    {
        _b.SYSCALL((byte)SyscallFunc.EXIT);
        return this;
    }

    /// <summary>断点 (调试用)。</summary>
    public BytecodeAssembler Break()
    {
        _b.BRK();
        return this;
    }

    // ══════════════════════════════════════════════════════════
    //  Build — 回填标签 + 设置全局变量数量
    // ══════════════════════════════════════════════════════════
    private void ApplyPatches()
    {
        _b.Globals(_globalCount);

        // ── Addr32：已迁移到 RelocationTable（0B 步骤 3 · 切片 2）──
        // 走"记录 → 解析符号 → 回填"的完整链路；写回的字节与迁移前逐字节一致。
        for (int recordIndex = 0; recordIndex < Relocations.Records.Count; recordIndex++) {
            var record = Relocations.Records[recordIndex];
            switch (record.Kind) {
                case RelocationKind.Addr32:
                    ApplyAddr32(record, recordIndex);
                    break;
                case RelocationKind.GlobalIdx:
                    ApplyGlobalIdx(record);
                    break;
                case RelocationKind.ConstIdx:
                    ApplyConstIdx(record);
                    break;
                case RelocationKind.HostIdx:
                    ApplyHostIdx(record);
                    break;
                default:
                    // 逐类迁移：尚未迁移的种类宁可拒绝，也不静默跳过。
                    throw new InvalidOperationException(
                        $"Relocation kind '{record.Kind}' at {record.Section}+{record.Offset} "
                            + "is not handled by BytecodeAssembler yet "
                            + "(only Addr32, GlobalIdx, ConstIdx and HostIdx are migrated; "
                            + "see 0B step 3)."
                    );
            }
        }

        // ── Off8：保持原路径，待下一类迁移 ──
        foreach (var (offset, target) in _pendingPatches) {
            int targetPC = ResolveLabel(target);
            // F2：条件跳转的相对偏移按 1 字节有符号编码。目标超出 -128..127 时，
            // 此处的 (sbyte) 强转会静默回绕 —— 分支跳到完全无关的 PC 上，编译
            // 全程零提示。宁可拒绝，也不产出错的字节码。
            int rel = targetPC - (offset + 1);
            if (rel < -128 || rel > 127)
                throw new InvalidOperationException(
                    $"Branch offset {rel} out of range for 8-bit patch "
                        + $"(label '{target}': offset={offset}, target PC={targetPC}); "
                        + "allowed range is -128..127."
                );
            _b.PatchByte(offset, (byte)(sbyte)rel);
        }
    }

    /// <summary>
    /// 按一条 <see cref="RelocationKind.Addr32"/> 记录解析目标并回填 32 位 PC。
    /// </summary>
    /// <remarks>
    /// 解析成功时把<b>模块局部目标 PC</b>写回记录的 <c>Addend</c>：链接器据此重定基
    /// 函数内的绝对 <c>JMP</c>（符号是标签而非函数名）。<b>不改字节</b>，且重复装配幂等。
    /// </remarks>
    private void ApplyAddr32(RelocationRecord record, int recordIndex)
    {
        string target = record.SymbolName!;

        if (!TryResolveLabel(target, out int targetPC))
        {
            if (AllowUnresolvedAddr32)
            {
                // 明确写成哨兵，绝不伪装成"合法的 0 地址调用"。
                _b.PatchUInt32(record.Offset, UnresolvedAddr32Sentinel);
                if (!_unresolvedSymbols.Contains(target, StringComparer.Ordinal))
                    _unresolvedSymbols.Add(target);
                return;
            }

            // DG-1：未定义符号诊断必须带引用来源。此处给出代码段内的引用偏移；
            // 源码位置在本层尚不可得（记录里是 Unknown），待上层接入后补。
            throw new InvalidOperationException(
                $"Undefined label or function: '{target}' — referenced from "
                    + $"code offset {record.Offset} (Addr32 relocation); "
                    + "no such label or function exists in this module."
            );
        }

        _b.PatchUInt32(record.Offset, (uint)targetPC);
        Relocations.SetAddend(recordIndex, targetPC);
    }

    /// <summary>
    /// 按一条 <see cref="RelocationKind.GlobalIdx"/> 记录，用模块局部映射解析并回写 LE16。
    /// </summary>
    /// <remarks>
    /// 单模块内解析出的槽位与发射时写入的立即数相同 —— 回填<b>不改变</b>任何字节，
    /// 但把"写死的立即数"换成了"由模块局部映射重推导"的入口：
    /// 链接器只要把 <c>_globalNames</c> 换成合并后的全局映射，就能完成跨模块重编号。
    /// </remarks>
    private void ApplyGlobalIdx(RelocationRecord record)
    {
        string name = record.SymbolName!;

        if (!_globalNames.TryGetValue(name, out short slot))
            // DG-1：未定义符号诊断必须带引用来源（此处给到代码段内偏移）
            throw new InvalidOperationException(
                $"Undefined global variable: '{name}' — referenced from "
                    + $"code offset {record.Offset} (GlobalIdx relocation); "
                    + "no such global is declared in this module."
            );

        // 记录里的局部槽位必须与模块映射一致，否则说明记录已过期
        if (record.Addend != slot)
            throw new InvalidOperationException(
                $"GlobalIdx mismatch for '{name}': the record carries slot {record.Addend} "
                    + $"but the module mapping resolves to {slot} "
                    + $"(code offset {record.Offset})."
            );

        _b.PatchInt16(record.Offset, slot);
    }

    /// <summary>
    /// 按一条 <see cref="RelocationKind.ConstIdx"/> 记录回填 <c>LDC</c> 的 LE32 索引
    /// （0B 步骤 3 · 切片 4）。
    /// </summary>
    /// <remarks>
    /// 单模块（未设 <see cref="ConstIndexRemap"/>）时最终索引 == 记录里的局部索引 ==
    /// 发射时写入的立即数，回填<b>不改变</b>任何字节；差异只在"立即数由记录重推导"
    /// 这一事实上。设了重映射则按映射改写 —— 这就是链接期合并常量池的入口。
    ///
    /// <para>
    /// <b>写入前逐项校验索引、计数、偏移</b>，宁可拒绝也不产出会在 VM 里越界的字节码：
    /// VM 把该立即数直接当 CLR 数组下标且<b>无预检</b>（<c>VMBase.RunLoop.cs:71</c>），
    /// 负数或越界会在运行时以 <see cref="IndexOutOfRangeException"/> 崩掉，
    /// 编译全程零提示 —— 与 F2/F6 同类的"静默产出坏字节码"，必须在写侧拦。
    /// </para>
    /// </remarks>
    private void ApplyConstIdx(RelocationRecord record)
    {
        int localIndex = record.Addend;

        // ── 校验 1/3：偏移必须落在代码段内，且整条立即数可写 ──
        // 记录 Offset 是发射时的 PC+2，正常流程不可能越界；越界说明记录与代码不匹配。
        if (record.Offset < 0 || record.Offset + 4 > _b.PC)
            throw new InvalidOperationException(
                $"ConstIdx offset {record.Offset} is outside the code section "
                    + $"(code length {_b.PC}); the record does not match this module's code."
            );

        // ── 校验 2/3：单模块下，局部索引必须落在本模块常量池内 ──
        // 池只追加不去重，索引一经分配终身不变；越界说明记录已过期。
        if (ConstIndexRemap is null && (localIndex < 0 || localIndex >= _b.ConstPoolCount))
            throw new InvalidOperationException(
                $"ConstIdx index {localIndex} is outside this module's constant pool "
                    + $"(count {_b.ConstPoolCount}); referenced from code offset "
                    + $"{record.Offset}."
            );

        // ── 重编号：局部索引 → 最终索引（单模块默认恒等）──
        int finalIndex = ConstIndexRemap?.Invoke(localIndex) ?? localIndex;

        // ── 校验 3/3：最终索引必须非负且可编码 ──
        // 重映射后的值不再受本模块池约束（合并池更大），但仍必须是合法下标。
        if (finalIndex < 0 || finalIndex > RelocationRules.MaxConstIndex)
            throw new InvalidOperationException(
                $"ConstIdx remapped index {finalIndex} (from local {localIndex}) is out of "
                    + $"range 0..{RelocationRules.MaxConstIndex}; referenced from code offset "
                    + $"{record.Offset}."
            );

        _b.PatchInt32(record.Offset, finalIndex);
    }

    /// <summary>
    /// 按一条 <see cref="RelocationKind.HostIdx"/> 记录回填 <c>HCALL</c> 的 LE16 宿主 ID
    /// （0B 步骤 3 · 切片 5）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 单模块（未设 <see cref="HostIdRemap"/>）时最终 ID == 记录里的局部 ID ==
    /// 发射时写入的立即数，回填<b>不改变</b>任何字节（幂等）；差异只在"立即数由记录重推导"
    /// 这一事实上。设了重映射则按映射改写 —— 这就是链接期按宿主符号名统一重编号的入口。
    /// </para>
    /// <para>
    /// <b>写前逐项校验偏移、范围</b>，宁可拒绝也不产出会调用到错误宿主的字节码：
    /// 超出 <c>ushort</c> 的 ID 必然被截断成另一个值，运行时会<b>静默调用别的宿主函数</b>
    /// （未命中才报错，命中错目标则不报）—— 与 F2/F6 同类的"静默产出坏字节码"，必须在写侧拦。
    /// </para>
    /// <para>
    /// <b>不按 <see cref="RelocationRules.MaxAllocatedHostId"/> 拒绝</b>：那是分配器策略上限，
    /// 不是编码容量；消费侧 <c>TryInvoke</c> 未命中只抛带诊断的 RuntimeError
    /// （<c>VMBase.RunLoop.cs:618-628</c>）、不崩溃，故消费侧无硬上限（§5 口径分列）。
    /// </para>
    /// </remarks>
    private void ApplyHostIdx(RelocationRecord record)
    {
        int localId = record.Addend;

        // ── 校验 1/2：偏移必须落在代码段内，且整条 2 字节立即数可写 ──
        // 记录 Offset 是发射时的 PC+1，正常流程不可能越界；越界说明记录与代码不匹配。
        if (record.Offset < 0 || record.Offset + 2 > _b.PC)
            throw new InvalidOperationException(
                $"HostIdx offset {record.Offset} is outside the code section "
                    + $"(code length {_b.PC}); the record does not match this module's code."
            );

        // ── 校验 2/2：模块局部 ID 必须落在 16 位无符号编码范围内 ──
        if (localId < 0 || localId > ushort.MaxValue)
            throw new InvalidOperationException(
                $"HostIdx module-local id {localId} is outside the 16-bit unsigned encoding "
                    + $"range 0..{ushort.MaxValue}; referenced from code offset "
                    + $"{record.Offset}."
            );

        // ── 重编号：局部 ID → 最终 ID（单模块默认恒等）──
        // 链接器按 SymbolName 解析出的最终 ID 经 HostIdRemap 回到这条回填路径。
        ushort finalId = HostIdRemap?.Invoke(record.SymbolName!, (ushort)localId)
            ?? (ushort)localId;

        // 这里**不再**校验 finalId 是否落在 0..ushort.MaxValue：钩子签名返回 ushort，
        // 该范围由类型系统保证，再写一道检查就是永远测不到的死代码（对照 F9 定级理由）。
        // 需要额外约束的口径（如分配总额 0..254）由分配器与链接期按
        // RelocationRules.MaxAllocatedHostId 复检，不塞进这条逐记录回填。
        _b.PatchUInt16(record.Offset, finalId);
    }

    /// <summary>
    /// 记录一条 32 位绝对地址重定位（0B 切片 2：Addr32 已走记录表）。
    /// </summary>
    /// <remarks>
    /// 源位置暂为 <see cref="RelocationSourceLocation.Unknown"/>：汇编器这一层拿不到
    /// 源码行号，由上层（BytecodeGen）接入时补。
    /// </remarks>
    private void RecordAddr32(int patchOffset, string target)
        => Relocations.Add(RelocationRecord.Create(
            RelocationKind.Addr32,
            RelocationSection.Code,
            patchOffset,
            target));

    public BytecodeModule Build()
    {
        ApplyPatches();
        return _b.Build();
    }

    public void SaveTo(string path)
    {
        ApplyPatches();
        _b.SaveTo(path);
    }

    public BytecodeModule BuildModule(uint entryPC)
    {
        ApplyPatches();
        return _b.BuildModule(entryPC);
    }

    private int ResolveLabel(string name)
        => TryResolveLabel(name, out int pc)
            ? pc
            : throw new InvalidOperationException($"Undefined label or function: '{name}'");

    private bool TryResolveLabel(string name, out int pc)
    {
        if (_labelDefs.TryGetValue(name, out pc))
            return true;
        if (_funcTable.TryGetValue(name, out var func)) {
            pc = func.PC;
            return true;
        }
        pc = 0;
        return false;
    }

    // ══════════════════════════════════════════════════════════
    //  常量池 (透传)
    // ══════════════════════════════════════════════════════════
    public int AddConst(long value) => _b.AddConst(value);
    public int AddConst(double value) => _b.AddConst(value);
    public int AddString(string value) => _b.AddString(value);

    // ══════════════════════════════════════════════════════════
    //  寄存器名透传
    // ══════════════════════════════════════════════════════════
    public const byte ZERO = BytecodeBuilder.ZERO;
    public const byte SP = BytecodeBuilder.SP;
    public const byte FP = BytecodeBuilder.FP;
    public const byte LR = BytecodeBuilder.LR;
    public const byte A0 = BytecodeBuilder.A0;
    public const byte A1 = BytecodeBuilder.A1;
    public const byte A2 = BytecodeBuilder.A2;
    public const byte A3 = BytecodeBuilder.A3;
    public const byte R8 = BytecodeBuilder.R8;
    public const byte R9 = BytecodeBuilder.R9;
    public const byte R10 = BytecodeBuilder.R10;
    public const byte R11 = BytecodeBuilder.R11;
}