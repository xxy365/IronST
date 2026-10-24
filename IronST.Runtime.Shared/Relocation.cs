// ═══════════════════════════════════════════════════════════════
//  Relocation.cs — 重定位记录模型（阶段 0B 步骤 3 · 切片 1）
//
//  ◆ 设计依据：docs/阶段0B设计.md §2.2（六类冻结）、§3.1（七字段记录模型）、
//    §3.3（编码宽度与有效范围）、§2.5（DG-1 未定义符号诊断须带引用来源）
//
//  ◆ 三条硬约束：
//    1. 记录<b>不存最终数值</b> —— 只存跨模块解析所需的信息（符号名、加数、
//       引用源位置）。解析结果由链接期产生，不在这里固化；否则无法实现
//       有上下文的未定义符号诊断。
//    2. 本类型位于 IronST.Runtime.Shared，保持该项目<b>零依赖</b>：
//       不引用 IronST.Common，统一符号键由链接器在合并期施加（§9 推迟裁定）。
//    3. 切片 1 <b>不改动</b> Addr32 / Off8 / GlobalIdx / ConstIdx /
//       StructTypeId / HostIdx 的既有回填行为 —— 本文件是纯新增。
// ═══════════════════════════════════════════════════════════════
namespace IronST.Runtime.Shared;

/// <summary>重定位类型 —— 六类，已冻结。</summary>
public enum RelocationKind : byte
{
    /// <summary>REL-1：调用 / 跳转目标的 32 位 PC。</summary>
    Addr32 = 1,

    /// <summary>REL-2：条件跳转的 8 位有符号相对偏移。</summary>
    Off8 = 2,

    /// <summary>REL-3：全局变量槽位索引。</summary>
    GlobalIdx = 3,

    /// <summary>REL-4：常量池索引。</summary>
    ConstIdx = 4,

    /// <summary>REL-5：结构体类型 ID。</summary>
    StructTypeId = 5,

    /// <summary>REL-6：宿主函数 ID。</summary>
    HostIdx = 6,
}

/// <summary>被改写对象所属的段（§3.1 字段 2）。</summary>
public enum RelocationSection : byte
{
    /// <summary>代码段 —— 指令内的立即数。</summary>
    Code = 1,

    /// <summary>数据段 —— 如结构体类型注册表项。</summary>
    Data = 2,
}

/// <summary>
/// 各类重定位的编码规则：<b>编码宽度</b>与<b>是否必须携带符号</b>。
/// </summary>
/// <remarks>
/// 编码宽度来自 §3.3 的逐条源码核对（<c>GETG/SETG</c> = 操作码 + rd + <c>LE16</c>、
/// <c>LDC</c> = 操作码 + rd + <c>LE32</c>、<c>HCALL</c> = 操作码 + 2 字节）。
/// </remarks>
public static class RelocationRules
{
    /// <summary>该类重定位在字节流中的编码位宽。</summary>
    public static int EncodingWidthBits(RelocationKind kind) => kind switch
    {
        RelocationKind.Addr32 => 32,
        RelocationKind.Off8 => 8,
        RelocationKind.GlobalIdx => 16,
        RelocationKind.ConstIdx => 32,
        RelocationKind.StructTypeId => 8,
        RelocationKind.HostIdx => 16,
        _ => throw new InvalidOperationException($"Unknown relocation kind: {kind}"),
    };

    /// <summary>
    /// 全局槽位总数上限。合法槽位索引为 <c>0..32766</c>，共 32767 个。
    /// </summary>
    /// <remarks>
    /// <b>单一事实来源</b>：<see cref="BytecodeAssembler.MaxGlobals"/> 直接引用本常量，
    /// 避免两处各写一个数字。链接合并后总槽位数仍须按此值复检（§4 共享预算）。
    /// </remarks>
    public const int MaxGlobalSlots = 32767;

    /// <summary>合法全局槽位索引上限（含）。</summary>
    public const int MaxGlobalSlotIndex = MaxGlobalSlots - 1;

    /// <summary>
    /// 合法常量索引上限（含）。
    /// </summary>
    /// <remarks>
    /// <b>依据是运行时消费逻辑，不是字段类型。</b><c>LDC</c> 编码为<b>有符号</b> LE32
    /// （<c>BytecodeBuilder.cs:311</c>），若只看编码可表示负值；但 VM 拿它直接当 CLR
    /// 数组下标、<b>无预检</b>（<c>VMBase.RunLoop.cs:71</c>），负数必然越界。写侧能产生的
    /// 最大索引是常量池 <c>Count - 1</c>，而池是 <c>List&lt;BcValue&gt;</c>，其长度上限即
    /// <see cref="Array.MaxLength"/> ⇒ 合法索引为 <c>0..Array.MaxLength-1</c>。
    ///
    /// <para>
    /// 用 <see cref="Array.MaxLength"/> 而非写死数字：该值随运行时而非本仓库决定，
    /// 写死会把"未经核实的容量"变成看似冻结的事实（§8.6-2）。
    /// </para>
    /// </remarks>
    public static readonly int MaxConstIndex = Array.MaxLength - 1;

    /// <summary>
    /// 分配器<b>策略</b>上限（含）：合法已分配宿主 ID 为 <c>0..254</c>，共 255 个。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>这是分配侧策略，不是运行时容量。</b>依据是分配器行为契约
    /// （<c>HostFunctionRegistry.cs:167-168</c>：<c>_nextId == byte.MaxValue</c> 即抛，
    /// 故 <c>255</c> 永不分配），不是"消费侧只能接受这么多"——
    /// 消费侧 <c>host.TryInvoke(id, ...)</c> 查表未命中只抛<b>带诊断的 RuntimeError</b>
    /// （<c>VMBase.RunLoop.cs:618-628</c>），<b>不崩溃</b>，实际接受整个编码范围。
    /// </para>
    /// <para>
    /// 三项口径<b>分列不合并</b>（§5 切片 5 侦查补正、§8.6）：
    /// ①<b>编码能力</b> <c>0..ushort.MaxValue</c>（由 <see cref="EncodingWidthBits"/> 的 16 位隐含）；
    /// ②<b>消费侧</b>无硬上限（越界只诊断）；③<b>分配器策略上限</b>即本常量。
    /// 记录层 <see cref="RelocationRecord.Validate"/> 按 ① 校验、<b>不</b>按本常量拒绝
    /// （本常量用于分配器与链接期的分配总额复检，§5 <c>HOST-1.c</c>）。
    /// </para>
    /// <para>
    /// <b>静态保留 ID 不并入本结论</b>：<c>HostCallIds</c> 稀疏占位与顺序分配器同处一个
    /// ID 空间但互不知情，冲突风险登记为 <c>HOST-2</c>（§5），<b>不是容量问题</b>。
    /// </para>
    /// </remarks>
    public const int MaxAllocatedHostId = 254;

    /// <summary>
    /// 该类是否必须携带符号名。
    /// </summary>
    /// <remarks>
    /// <c>ConstIdx</c> 是唯一例外：它的"索引来源"是模块内常量序号，
    /// 由 <see cref="RelocationRecord.Addend"/> 承载（首版追加合并、不去重，§3.3 REL-4）。
    /// </remarks>
    public static bool RequiresSymbol(RelocationKind kind) => kind switch
    {
        RelocationKind.ConstIdx => false,
        RelocationKind.Addr32
            or RelocationKind.Off8
            or RelocationKind.GlobalIdx
            or RelocationKind.StructTypeId
            or RelocationKind.HostIdx => true,
        _ => throw new InvalidOperationException($"Unknown relocation kind: {kind}"),
    };
}

/// <summary>
/// 引用源位置 —— 未定义符号诊断<b>必须</b>包含引用来源（§2.5 硬性条件）。
/// </summary>
/// <remarks>
/// <c>Line</c> / <c>Column</c> 用 <c>-1</c> 表示未知（<c>0</c> 是合法值）。
/// 本类型刻意与 <c>IronST.Common</c> 的 <c>SourceSpan</c> 无关：Runtime.Shared
/// 保持零依赖，两者之间的映射由上层完成。
/// </remarks>
public readonly record struct RelocationSourceLocation(string? File, int Line, int Column)
{
    public static readonly RelocationSourceLocation Unknown = new(null, -1, -1);

    public bool IsUnknown => File is null && Line < 0 && Column < 0;

    public override string ToString()
        => IsUnknown ? "<unknown>" : $"{File ?? "<no file>"}:{Line}:{Column}";
}

/// <summary>
/// 一条重定位记录（§3.1 七字段）。
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><see cref="Kind"/> —— 六类之一；</item>
/// <item><see cref="Section"/> —— 被改写对象所在段；</item>
/// <item><see cref="Offset"/> —— 段内偏移，指向待回填的字节；</item>
/// <item><see cref="SymbolName"/> —— 跨模块解析用的符号身份（原始名）；</item>
/// <item><see cref="Source"/> —— 引用源位置；</item>
/// <item><see cref="EncodingWidthBits"/> —— 编码位宽，写入前校验用；</item>
/// <item><see cref="Addend"/> —— 加数或相对基准：<c>ConstIdx</c> 承载模块内常量序号，
/// <c>GlobalIdx</c> 承载<b>模块局部</b>全局槽位索引。</item>
/// </list>
/// <b>没有"已解析值"字段</b> —— 这是有意的：记录只描述"要按哪个符号改写哪里"，
/// 结果由链接期写入字节流，模型层不持有最终值。
/// </remarks>
public readonly record struct RelocationRecord(
    RelocationKind Kind,
    RelocationSection Section,
    int Offset,
    string? SymbolName,
    RelocationSourceLocation Source,
    int EncodingWidthBits,
    int Addend)
{
    /// <summary>
    /// 按 <paramref name="kind"/> 的编码规则创建记录，并立即做边界校验。
    /// </summary>
    public static RelocationRecord Create(
        RelocationKind kind,
        RelocationSection section,
        int offset,
        string? symbolName,
        RelocationSourceLocation source,
        int addend = 0)
    {
        var record = new RelocationRecord(
            kind,
            section,
            offset,
            symbolName,
            source,
            RelocationRules.EncodingWidthBits(kind),
            addend);
        record.Validate();
        return record;
    }

    /// <summary>不带源位置的重载（源位置未知时使用 <see cref="RelocationSourceLocation.Unknown"/>）。</summary>
    public static RelocationRecord Create(
        RelocationKind kind,
        RelocationSection section,
        int offset,
        string? symbolName,
        int addend = 0)
        => Create(kind, section, offset, symbolName, RelocationSourceLocation.Unknown, addend);

    /// <summary>
    /// 边界校验。违反规则一律抛 <see cref="InvalidOperationException"/>
    /// （Runtime.Shared 侧的报错约定），<b>不静默截断</b>。
    /// </summary>
    public void Validate()
    {
        if (!Enum.IsDefined(Kind))
            throw new InvalidOperationException($"Unknown relocation kind: {Kind}");

        if (!Enum.IsDefined(Section))
            throw new InvalidOperationException($"Unknown relocation section: {Section}");

        if (Offset < 0)
            throw new InvalidOperationException(
                $"Relocation offset must be non-negative, got {Offset} ({Kind})");

        int expected = RelocationRules.EncodingWidthBits(Kind);
        if (EncodingWidthBits != expected)
            throw new InvalidOperationException(
                $"Relocation {Kind} encodes {expected} bits but the record says "
                + $"{EncodingWidthBits} bits; refusing a record that would silently truncate");

        if (RelocationRules.RequiresSymbol(Kind) && string.IsNullOrEmpty(SymbolName))
            throw new InvalidOperationException(
                $"Relocation {Kind} requires a symbol name — it is needed for cross-module "
                + "resolution and for undefined-symbol diagnostics");

        if (Kind == RelocationKind.GlobalIdx
            && (Addend < 0 || Addend > RelocationRules.MaxGlobalSlotIndex))
            throw new InvalidOperationException(
                $"GlobalIdx slot index {Addend} is out of range "
                    + $"0..{RelocationRules.MaxGlobalSlotIndex}; Addend carries the "
                    + "module-local global slot index, not an arbitrary addend");

        if (Kind == RelocationKind.ConstIdx) {
            // 语义边界：常量没有稳定的符号身份，索引来源就是模块局部序号。
            // 允许写符号名等于允许把局部索引误当跨模块身份，链接期会解析到错误目标。
            if (!string.IsNullOrEmpty(SymbolName))
                throw new InvalidOperationException(
                    "ConstIdx must not carry a symbol name — a constant index has no stable "
                        + "cross-module identity; the module-local index belongs in Addend");

            if (Addend < 0 || Addend > RelocationRules.MaxConstIndex)
                throw new InvalidOperationException(
                    $"ConstIdx constant index {Addend} is out of range "
                        + $"0..{RelocationRules.MaxConstIndex}; Addend carries the "
                        + "module-local constant-pool index, not an arbitrary addend");
        }

        if (Kind == RelocationKind.HostIdx) {
            // 口径分列（§5 切片 5 侦查补正）：记录层只按**编码能力**校验 —— 立即数是
            // 16 位无符号（BytecodeBuilder.cs:213-219 小端两字节），超过 ushort.MaxValue
            // 必然被截断成另一个宿主 ID，属于"静默产出调用错目标的字节码"，须在写侧拦。
            //
            // **不**按 MaxAllocatedHostId 拒绝：那是分配器策略（0..254），不是编码容量；
            // 消费侧查表未命中只抛带诊断的 RuntimeError（VMBase.RunLoop.cs:618-628）、不崩溃，
            // 故消费侧无硬上限。把分配策略当容量写进 Validate，等于把未核实的口径
            // 冻结成事实（§8.6-2）。分配总额由分配器与链接期按 MaxAllocatedHostId 复检。
            if (Addend < 0 || Addend > ushort.MaxValue)
                throw new InvalidOperationException(
                    $"HostIdx host id {Addend} is out of the 16-bit unsigned encoding range "
                        + $"0..{ushort.MaxValue}; Addend carries the module-local host index, "
                        + "and a value outside this range would be silently truncated to a "
                        + "different host id");
        }

        if (Source.Line < -1 || Source.Column < -1)
            throw new InvalidOperationException(
                $"Source line/column must be -1 (unknown) or non-negative, got {Source}");
    }
}
