// STModWriter.cs — `.stmod` 写入器。
//
// 布局策略：先构造各区段负载，再按固定顺序分配区段 Id 并做 8 字节对齐排布。
// 区段 Id 与类型一一对应（1..5），因此 FunctionIndex 里的 CodeSectionId 恒为 3。
// 元数据按键名序数排序输出，保证同一输入产生逐字节相同的文件。
using System.Text;

namespace IronST.Runtime.Shared.Modules;

/// <summary>把内存中的 <see cref="STModModule"/> 写成 `.stmod` 字节流。</summary>
public static class STModWriter
{
    /// <summary>Metadata 区段 Id（同时也是文件头 <c>MetadataSectionId</c> 的取值）。</summary>
    public const uint MetadataSectionId = 1;

    /// <summary>FunctionIndex 区段 Id。</summary>
    public const uint FunctionIndexSectionId = 2;

    /// <summary>Code 区段 Id。</summary>
    public const uint CodeSectionId = 3;

    /// <summary>ConstantPool 区段 Id。</summary>
    public const uint ConstantPoolSectionId = 4;

    /// <summary>Relocations 区段 Id（可选区段）。</summary>
    public const uint RelocationsSectionId = 5;

    /// <summary>
    /// ImportSymbols 区段 Id（可选；区段类型为 5）。
    /// </summary>
    /// <remarks>
    /// Id 与类型**不要求相等**（读取器按类型定位）。此处 Id 取 6 是为了不与既有的
    /// <see cref="RelocationsSectionId"/>=5 冲突，从而让"有重定位无导入"的旧模块
    /// 字节布局保持不变。
    /// </remarks>
    public const uint ImportSymbolsSectionId = 6;

    /// <summary>ExportSymbols 区段 Id（可选；区段类型为 6）。</summary>
    public const uint ExportSymbolsSectionId = 7;

    public static void Save(string path, STModModule module) =>
        File.WriteAllBytes(path, Write(module));

    /// <summary>
    /// 原子写出：先写同目录临时文件再替换目标，失败时**不留下**看似有效的半成品
    /// （链接协议 §8 步骤 14）。
    /// </summary>
    public static void SaveAtomic(string path, STModModule module) =>
        SaveBytesAtomic(path, Write(module));

    /// <summary>原子写出已序列化字节（<see cref="Write"/> 的产物）。</summary>
    public static void SaveBytesAtomic(string path, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(bytes);

        string full = Path.GetFullPath(path);
        string dir = Path.GetDirectoryName(full) ?? ".";
        Directory.CreateDirectory(dir);

        string temp = Path.Combine(dir, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, full, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
            throw;
        }
    }

    /// <summary>序列化；失败时抛 <see cref="STModFormatException"/>（写侧同样不产出非法文件）。</summary>
    public static byte[] Write(STModModule module)
    {
        Validate(module);

        var sections = new List<(uint Id, STModSectionType Type, STModSectionFlags Flags, byte[] Payload)>
        {
            (MetadataSectionId, STModSectionType.Metadata, STModSectionFlags.Required,
                BuildMetadata(module.Metadata)),
            (FunctionIndexSectionId, STModSectionType.FunctionIndex, STModSectionFlags.Required,
                BuildFunctionIndex(module.Functions)),
            (CodeSectionId, STModSectionType.Code, STModSectionFlags.Required,
                module.Code),
            (ConstantPoolSectionId, STModSectionType.ConstantPool, STModSectionFlags.Required,
                BuildConstantPool(module.ConstantPool)),
        };

        if (module.Relocations is { Count: > 0 })
        {
            sections.Add((RelocationsSectionId, STModSectionType.Relocations,
                STModSectionFlags.None, module.Relocations.Serialize()));
        }

        // 链接协议 §1.2：新增区段一律以**可选**标志写出，旧读取器忽略即可。
        if (module.Imports.Count > 0)
        {
            sections.Add((ImportSymbolsSectionId, STModSectionType.ImportSymbols,
                STModSectionFlags.None, BuildSymbols(module.Imports, isExport: false)));
        }

        if (module.Exports.Count > 0)
        {
            sections.Add((ExportSymbolsSectionId, STModSectionType.ExportSymbols,
                STModSectionFlags.None, BuildSymbols(module.Exports, isExport: true)));
        }

        // ── 排版 ──
        uint sectionCount = (uint)sections.Count;
        ulong tableOffset = STModFormat.HeaderSize;
        ulong tableEnd = tableOffset + (ulong)sectionCount * STModFormat.SectionEntrySize;

        var offsets = new ulong[sections.Count];
        ulong cursor = AlignUp(tableEnd);
        for (int i = 0; i < sections.Count; i++)
        {
            offsets[i] = cursor;
            cursor = AlignUp(cursor + (ulong)sections[i].Payload.Length);
        }

        ulong totalSize = cursor;
        var headerFlags = module.HasEntryPoint
            ? STModHeaderFlags.HasEntryPoint
            : STModHeaderFlags.None;

        using var ms = new MemoryStream(checked((int)totalSize));
        using var bw = new BinaryWriter(ms);

        // ── 固定头（48 字节，全部小端）──
        bw.Write(STModFormat.MagicBytes);            // 0..3
        bw.Write(STModFormat.FormatVersionMajor);    // 4..5
        bw.Write(STModFormat.FormatVersionMinor);    // 6..7
        bw.Write((uint)headerFlags);                 // 8..11
        bw.Write(STModFormat.HeaderSize);            // 12..15
        bw.Write(sectionCount);                      // 16..19
        bw.Write(0u);                                // 20..23 reserved0
        bw.Write(tableOffset);                       // 24..31
        bw.Write(MetadataSectionId);                 // 32..35
        bw.Write(0u);                                // 36..39 reserved1
        bw.Write(0ul);                               // 40..47 reserved2

        // ── 区段表 ──
        for (int i = 0; i < sections.Count; i++)
        {
            bw.Write(sections[i].Id);
            bw.Write((ushort)sections[i].Type);
            bw.Write((ushort)sections[i].Flags);
            bw.Write(offsets[i]);
            bw.Write((ulong)sections[i].Payload.Length);
            bw.Write(STModFormat.SectionAlignment);
            bw.Write(0u);                            // reserved
        }

        // ── 区段负载（间隙填 0，保证对齐且无重叠）──
        for (int i = 0; i < sections.Count; i++)
        {
            PadTo(bw, (long)offsets[i]);
            bw.Write(sections[i].Payload);
        }
        PadTo(bw, (long)totalSize);

        bw.Flush();
        return ms.ToArray();
    }

    // ══════════════════════════════════════════════════════════
    //  区段负载
    // ══════════════════════════════════════════════════════════

    private static byte[] BuildMetadata(IReadOnlyDictionary<string, STModMetadataEntry> metadata)
    {
        var ordered = metadata.Values.OrderBy(e => e.Key, StringComparer.Ordinal).ToList();

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((uint)ordered.Count);
        bw.Write(0u);
        bw.Write(0u);
        bw.Write(0u);

        foreach (var e in ordered)
        {
            byte[] key = Encoding.UTF8.GetBytes(e.Key);
            bw.Write((uint)key.Length);
            bw.Write((byte)e.ValueType);
            bw.Write((byte)0);          // flags
            bw.Write((ushort)0);        // reserved
            bw.Write((uint)e.Value.Length);
            bw.Write(key);
            bw.Write(e.Value);
        }

        bw.Flush();
        return ms.ToArray();
    }

    private static byte[] BuildFunctionIndex(IReadOnlyList<STModFunctionEntry> functions)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((uint)functions.Count);
        bw.Write(0u);
        bw.Write(0u);
        bw.Write(0u);

        foreach (var f in functions)
        {
            byte[] name = Encoding.UTF8.GetBytes(f.Name);
            byte[] sig = Encoding.UTF8.GetBytes(f.Signature);

            bw.Write(f.FunctionId);
            bw.Write((uint)f.Flags);
            bw.Write(CodeSectionId);
            bw.Write((uint)name.Length);
            bw.Write((uint)sig.Length);
            bw.Write(f.ParamCount);
            bw.Write(f.LocalCount);
            bw.Write(f.MaxStack);
            bw.Write(f.CodeOffset);
            bw.Write(f.CodeLength);
            bw.Write(name);
            bw.Write(sig);
        }

        bw.Flush();
        return ms.ToArray();
    }

    private static byte[] BuildSymbols(IReadOnlyList<STModSymbol> symbols, bool isExport)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((uint)symbols.Count);
        bw.Write(0u);

        foreach (var s in symbols)
        {
            byte[] name = Encoding.UTF8.GetBytes(s.Name);
            byte[] sig = Encoding.UTF8.GetBytes(s.Signature);

            if (isExport)
            {
                bw.Write(s.FunctionId);
                bw.Write((uint)name.Length);
                bw.Write((uint)sig.Length);
                bw.Write((ushort)s.Kind);
                bw.Write((ushort)s.Flags);
            }
            else
            {
                bw.Write((uint)name.Length);
                bw.Write((uint)sig.Length);
                bw.Write((ushort)s.Kind);
                bw.Write((ushort)s.Flags);
            }

            bw.Write(name);
            bw.Write(sig);
        }

        bw.Flush();
        return ms.ToArray();
    }

    private static byte[] BuildConstantPool(IReadOnlyList<BcValue> pool)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((uint)pool.Count);
        bw.Write(0u);
        foreach (var value in pool)
            BcValueSerializer.Write(bw, value);

        bw.Flush();
        return ms.ToArray();
    }

    // ══════════════════════════════════════════════════════════
    //  写侧校验（不产出非法文件）
    // ══════════════════════════════════════════════════════════

    private static void Validate(STModModule module)
    {
        if (module.Functions.Count > STModFormat.MaxFunctionEntries)
            throw new STModFormatException(
                $"函数数量 {module.Functions.Count} 超过上限 {STModFormat.MaxFunctionEntries}");

        if (module.Metadata.Count > STModFormat.MaxMetadataRecords)
            throw new STModFormatException(
                $"元数据条数 {module.Metadata.Count} 超过上限 {STModFormat.MaxMetadataRecords}");

        foreach (string key in STModMetadataKeys.Required)
        {
            if (!module.Metadata.TryGetValue(key, out var entry))
                throw new STModFormatException($"缺少必需元数据 '{key}'");
            if (entry.ValueType != STModValueType.String)
                throw new STModFormatException($"必需元数据 '{key}' 必须是 String 类型");
        }

        var ids = new HashSet<uint>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in module.Functions)
        {
            if (!ids.Add(f.FunctionId))
                throw new STModFormatException($"重复的 functionId {f.FunctionId}");
            if (!names.Add(f.Name))
                throw new STModFormatException($"重复的函数名 '{f.Name}'");
            if (f.Name.Length == 0)
                throw new STModFormatException("函数名不能为空");
            if (f.CodeLength == 0)
                throw new STModFormatException($"函数 '{f.Name}' 的 CodeLength 为 0");
            if (f.CodeOffset > (ulong)module.Code.Length
                || f.CodeLength > (ulong)module.Code.Length - f.CodeOffset)
            {
                throw new STModFormatException(
                    $"函数 '{f.Name}' 的代码范围 0x{f.CodeOffset:X}+0x{f.CodeLength:X} "
                    + $"越过 Code 区段（{module.Code.Length} 字节）");
            }
        }

        if (module.HasEntryPoint && module.EntryPC >= (uint)module.Code.Length)
            throw new STModFormatException(
                $"入口 PC {module.EntryPC} 越过 Code 区段（{module.Code.Length} 字节）");

        ValidateSymbols(module.Imports, ids, isExport: false);
        ValidateSymbols(module.Exports, ids, isExport: true);
    }

    /// <summary>
    /// 写侧符号校验（链接协议 §4.1 / §5.1）：不产出读取器会拒绝的文件。
    /// </summary>
    private static void ValidateSymbols(
        IReadOnlyList<STModSymbol> symbols, HashSet<uint> functionIds, bool isExport)
    {
        string what = isExport ? "导出符号" : "导入符号";
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var s in symbols)
        {
            if (string.IsNullOrEmpty(s.Name))
                throw new STModFormatException($"模块{what}名不能为空");

            if (!names.Add(s.Name))
                throw new STModFormatException($"重复的模块{what}名 '{s.Name}'");

            if (!Enum.IsDefined(s.Kind))
                throw new STModFormatException($"{what} '{s.Name}' 的 Kind={(ushort)s.Kind} 未知");

            if (isExport)
            {
                if (!functionIds.Contains(s.FunctionId))
                    throw new STModFormatException(
                        $"{what} '{s.Name}' 指向不存在的 FunctionId {s.FunctionId}");
            }
            else if (s.IsRequired)
            {
                // 协议 §1.3：v1 不写出 Required 的导入区段（旧读取器必须拒绝）。
                throw new STModFormatException(
                    $"{what} '{s.Name}' 不得标记 Required（v1 不产生必需的导入区段）");
            }
        }
    }

    // ══════════════════════════════════════════════════════════
    //  小工具
    // ══════════════════════════════════════════════════════════

    private static ulong AlignUp(ulong value)
    {
        uint align = STModFormat.SectionAlignment;
        ulong rem = value % align;
        return rem == 0 ? value : value + (align - rem);
    }

    private static void PadTo(BinaryWriter bw, long target)
    {
        while (bw.BaseStream.Position < target)
            bw.Write((byte)0);
    }
}
