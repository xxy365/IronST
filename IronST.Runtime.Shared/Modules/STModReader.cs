// STModReader.cs — `.stmod` 读取器（含格式校验）。
//
// 校验原则：**不因为魔数正确就信任偏移与长度**。所有偏移/长度都先做
//   · 上界检查（不越过文件/区段）
//   · 下界检查（不落进固定头）
//   · 溢出安全比较（先比较，再相减，绝不做 off+size 的无检查相加）
// 失败一律抛 STModFormatException，消息带 `.stmod` 前缀与具体原因。
using System.Text;

namespace IronST.Runtime.Shared.Modules;

/// <summary>解析 `.stmod`。读取成功只代表结构合法，<b>不执行</b>其中字节码。</summary>
public static class STModReader
{
    public static STModModule Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new STModFormatException(".stmod 缺少文件路径");

        if (!File.Exists(path))
            throw new STModFormatException($".stmod 找不到文件 '{path}'");

        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            throw new STModFormatException($".stmod 无法读取文件 '{path}': {ex.Message}");
        }

        return Read(data);
    }

    public static STModModule Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < STModFormat.HeaderSize)
        {
            throw new STModFormatException(
                $".stmod 文件被截断：固定头需要 {STModFormat.HeaderSize} 字节，实际 {file.Length} 字节");
        }

        byte[] data = file.ToArray();

        using var br = new BinaryReader(
            new MemoryStream(data, writable: false), Encoding.UTF8, leaveOpen: true);

        // ── 固定头 ──
        var magic = br.ReadBytes(4);
        if (!magic.AsSpan().SequenceEqual(STModFormat.MagicBytes))
        {
            throw new STModFormatException(
                $".stmod 魔数不匹配：期望 STMD，实际 "
                + $"0x{magic[0]:X2}{magic[1]:X2}{magic[2]:X2}{magic[3]:X2}"
                + "（.isb 是另一种文件，不能用 mod inspect 打开）");
        }

        ushort major = br.ReadUInt16();
        ushort minor = br.ReadUInt16();
        if (major != STModFormat.FormatVersionMajor)
        {
            throw new STModFormatException(
                $".stmod 不支持的格式版本 {major}.{minor}"
                + $"（本读取器支持 {STModFormat.FormatVersionMajor}.x）");
        }

        uint flagsRaw = br.ReadUInt32();
        uint headerSize = br.ReadUInt32();
        if (headerSize != STModFormat.HeaderSize)
        {
            throw new STModFormatException(
                $".stmod 固定头长度 {headerSize} 与规范 {STModFormat.HeaderSize} 不符");
        }

        uint sectionCount = br.ReadUInt32();
        if (sectionCount > STModFormat.MaxSectionCount)
        {
            throw new STModFormatException(
                $".stmod 区段数 {sectionCount} 超过上限 {STModFormat.MaxSectionCount}");
        }

        uint reserved0 = br.ReadUInt32();
        ulong tableOffset = br.ReadUInt64();
        uint metadataSectionId = br.ReadUInt32();
        uint reserved1 = br.ReadUInt32();
        ulong reserved2 = br.ReadUInt64();

        if (reserved0 != 0 || reserved1 != 0 || reserved2 != 0)
            throw new STModFormatException(".stmod 头部的保留字段必须为 0");

        // ── 区段表边界（先比较，再相减）──
        ulong tableBytes = (ulong)sectionCount * STModFormat.SectionEntrySize;
        if (tableOffset < headerSize || tableOffset > (ulong)data.Length
            || tableBytes > (ulong)data.Length - tableOffset)
        {
            throw new STModFormatException(
                $".stmod 区段表越界：offset={tableOffset}, 条目数={sectionCount}x"
                + $"{STModFormat.SectionEntrySize} 字节，文件长度 {data.Length}");
        }

        br.BaseStream.Position = (long)tableOffset;
        var sections = new List<STModSectionInfo>((int)sectionCount);
        for (uint i = 0; i < sectionCount; i++)
            sections.Add(ReadSectionInfo(br, data.Length, headerSize));

        ValidateSectionTable(sections, tableOffset, tableBytes, headerSize);

        // ── 区段负载 ──
        var metadata = ReadMetadata(data, sections.Single(s => s.Type == STModSectionType.Metadata));

        var codeSection = sections.Single(s => s.Type == STModSectionType.Code);
        byte[] code = Slice(data, codeSection);

        var constantPool = ReadConstantPool(
            data, sections.Single(s => s.Type == STModSectionType.ConstantPool));

        var functions = ReadFunctionIndex(
            data,
            sections.Single(s => s.Type == STModSectionType.FunctionIndex),
            code.Length,
            codeSection.Id);

        RelocationTable? relocations = null;
        var relocSection = sections.FirstOrDefault(s => s.Type == STModSectionType.Relocations);
        if (relocSection is not null)
        {
            try
            {
                relocations = RelocationTable.Load(Slice(data, relocSection));
            }
            catch (Exception ex)
            {
                throw new STModFormatException($".stmod Relocations 区段无效: {ex.Message}");
            }
        }

        // ── 入口点 ──
        var headerFlags = (STModHeaderFlags)flagsRaw;
        bool hasEntry = (headerFlags & STModHeaderFlags.HasEntryPoint) != 0;
        uint entryPC = 0;

        if (hasEntry)
        {
            if (!metadata.TryGetValue(STModMetadataKeys.EntryPC, out var entry)
                || entry.ValueType != STModValueType.UInt32)
            {
                throw new STModFormatException(
                    $".stmod 头部声明了入口点，但缺少 UInt32 类型的 "
                    + $"'{STModMetadataKeys.EntryPC}' 元数据");
            }

            entryPC = entry.AsUInt32();
            if ((ulong)entryPC >= (ulong)code.Length)
            {
                throw new STModFormatException(
                    $".stmod 入口 PC {entryPC} 越过 Code 区段（{code.Length} 字节）");
            }
        }

        _ = metadataSectionId;

        // ── 链接协议：导入 / 导出符号、链接状态、全局槽位数 ──
        var imports = ReadSymbols(
            data,
            sections.FirstOrDefault(s => s.Type == STModSectionType.ImportSymbols),
            isExport: false,
            functions);
        var exports = ReadSymbols(
            data,
            sections.FirstOrDefault(s => s.Type == STModSectionType.ExportSymbols),
            isExport: true,
            functions);

        // §1.4：存在"未解析的外部函数导入"或显式 module.linked=false ⇒ 未链接。
        bool linkedByMetadata = true;
        if (metadata.TryGetValue(STModMetadataKeys.ModuleLinked, out var linked)
            && linked.ValueType == STModValueType.Boolean)
        {
            linkedByMetadata = linked.AsBoolean();
        }
        bool hasUnresolvedImports = imports.Any(
            s => s.Kind == STModSymbolKind.Function);
        bool isLinked = linkedByMetadata && !hasUnresolvedImports;

        // §1.5：优先元数据；缺失时以 GlobalIdx 记录回退。
        int globalCount = 0;
        if (metadata.TryGetValue(STModMetadataKeys.GlobalCount, out var gc)
            && gc.ValueType == STModValueType.UInt32)
        {
            ulong raw = gc.AsUInt32();
            if (raw > int.MaxValue)
                throw new STModFormatException(
                    $".stmod 元数据 '{STModMetadataKeys.GlobalCount}' = {raw} 超出 int 范围");
            globalCount = (int)raw;
        }
        else if (relocations is not null)
        {
            int max = -1;
            foreach (var record in relocations.Records)
                if (record.Kind == RelocationKind.GlobalIdx && record.Addend > max)
                    max = record.Addend;
            globalCount = max + 1;
        }

        return new STModModule
        {
            Metadata = metadata,
            Functions = functions,
            Code = code,
            ConstantPool = constantPool,
            Sections = sections,
            HasEntryPoint = hasEntry,
            EntryPC = entryPC,
            Relocations = relocations,
            Imports = imports,
            Exports = exports,
            IsLinked = isLinked,
            GlobalCount = globalCount,
            FormatVersionMajor = major,
            FormatVersionMinor = minor,
        };
    }

    /// <summary>v1 必须存在的区段类型。</summary>
    public static readonly STModSectionType[] RequiredSectionTypes =
    [
        STModSectionType.Metadata,
        STModSectionType.FunctionIndex,
        STModSectionType.Code,
        STModSectionType.ConstantPool,
    ];

    // ══════════════════════════════════════════════════════════
    //  区段表自洽性
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// 区段表整体校验：Id 唯一、必需类型各恰好一次、已知但不支持的类型、
    /// 以及**区间互不重叠**（含固定头与区段表自身所占区间）。
    /// </summary>
    private static void ValidateSectionTable(
        List<STModSectionInfo> sections, ulong tableOffset, ulong tableBytes, uint headerSize)
    {
        var ids = new HashSet<uint>();
        foreach (var s in sections)
        {
            if (!ids.Add(s.Id))
                throw new STModFormatException($".stmod 重复的区段 Id {s.Id}");

            // v1 不实现模块间链接：未解析的外部符号必须明确拒绝，
            // 而不是产出一个"格式合法但运行必错"的模块。
            if (s.Type == STModSectionType.ImportSymbols && s.IsRequired)
            {
                throw new STModFormatException(
                    $".stmod 区段 [{s.Id}] 声明了必需的模块导入（ImportSymbols），"
                    + "但 v1 不支持模块间链接：未解析的外部符号不会被接受，"
                    + "请先链接为 .isb");
            }
        }

        foreach (var type in RequiredSectionTypes)
        {
            int count = sections.Count(s => s.Type == type);
            if (count == 0)
                throw new STModFormatException($".stmod 缺少必需区段 {type}");
            if (count > 1)
                throw new STModFormatException(
                    $".stmod 重复的必需区段 {type}（出现 {count} 次）");
        }

        // 区间不得重叠：把固定头与区段表也当作区间一起排序比较。
        var ranges = new List<(ulong Start, ulong End, string What)>
        {
            (0, headerSize, "固定头"),
            (tableOffset, tableOffset + tableBytes, "区段表"),
        };
        foreach (var s in sections)
            ranges.Add((s.Offset, s.Offset + s.Size, $"区段 [{s.Id}] {s.Type}"));

        var ordered = ranges.OrderBy(r => r.Start).ToList();
        for (int i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Start < ordered[i - 1].End)
            {
                throw new STModFormatException(
                    $".stmod 区段重叠：{ordered[i - 1].What} 与 {ordered[i].What} "
                    + $"在偏移 0x{ordered[i].Start:X} 处重叠");
            }
        }
    }

    // ══════════════════════════════════════════════════════════
    //  区段表
    // ══════════════════════════════════════════════════════════

    private static STModSectionInfo ReadSectionInfo(
        BinaryReader br, int fileLength, uint headerSize)
    {
        uint id = br.ReadUInt32();
        ushort typeRaw = br.ReadUInt16();
        ushort flagsRaw = br.ReadUInt16();
        ulong offset = br.ReadUInt64();
        ulong size = br.ReadUInt64();
        uint alignment = br.ReadUInt32();
        uint reserved = br.ReadUInt32();

        if (reserved != 0)
            throw new STModFormatException($".stmod 区段 [{id}] 的保留字段必须为 0");

        if (alignment == 0 || (alignment & (alignment - 1)) != 0)
            throw new STModFormatException(
                $".stmod 区段 [{id}] 的 alignment={alignment} 不是 2 的幂");

        if (offset < headerSize)
            throw new STModFormatException(
                $".stmod 区段 [{id}] 的 offset={offset} 落在固定头内（headerSize={headerSize}）");

        if (offset % alignment != 0)
            throw new STModFormatException(
                $".stmod 区段 [{id}] 的 offset={offset} 未按 alignment={alignment} 对齐");

        if (offset > (ulong)fileLength || size > (ulong)fileLength - offset)
        {
            throw new STModFormatException(
                $".stmod 区段 [{id}] 越界：offset={offset}, size={size}, 文件长度={fileLength}");
        }

        var type = (STModSectionType)typeRaw;
        var secFlags = (STModSectionFlags)flagsRaw;

        if (!Enum.IsDefined(type) && (secFlags & STModSectionFlags.Required) != 0)
        {
            throw new STModFormatException(
                $".stmod 未知区段类型 0x{typeRaw:X4} 被标记为必需，本读取器无法处理");
        }

        return new STModSectionInfo
        {
            Id = id,
            Type = type,
            Flags = secFlags,
            Offset = offset,
            Size = size,
            Alignment = alignment,
        };
    }

    private static byte[] Slice(byte[] data, STModSectionInfo section)
    {
        var slice = new byte[(int)section.Size];
        Array.Copy(data, (int)section.Offset, slice, 0, slice.Length);
        return slice;
    }

    private static BinaryReader OpenSection(byte[] data, STModSectionInfo section) =>
        new(new MemoryStream(Slice(data, section), writable: false), Encoding.UTF8);

    // ══════════════════════════════════════════════════════════
    //  Metadata
    // ══════════════════════════════════════════════════════════

    private static Dictionary<string, STModMetadataEntry> ReadMetadata(
        byte[] data, STModSectionInfo section)
    {
        try
        {
            using var br = OpenSection(data, section);

            uint count = br.ReadUInt32();
            br.ReadUInt32();
            br.ReadUInt32();
            br.ReadUInt32();

            if (count > STModFormat.MaxMetadataRecords)
            {
                throw new STModFormatException(
                    $".stmod 元数据条数 {count} 超过上限 {STModFormat.MaxMetadataRecords}");
            }

            var result = new Dictionary<string, STModMetadataEntry>(StringComparer.Ordinal);
            for (uint i = 0; i < count; i++)
            {
                uint keyLen = br.ReadUInt32();
                byte valueType = br.ReadByte();
                br.ReadByte();       // flags
                br.ReadUInt16();     // reserved
                uint valueLen = br.ReadUInt32();

                if (keyLen == 0 || keyLen > STModFormat.MaxKeyBytes)
                {
                    throw new STModFormatException(
                        $".stmod 元数据第 {i} 条的 KeyLength={keyLen} 非法"
                        + $"（允许 1..{STModFormat.MaxKeyBytes}）");
                }

                if (valueLen > STModFormat.MaxStringBytes)
                {
                    throw new STModFormatException(
                        $".stmod 元数据第 {i} 条的 ValueLength={valueLen} 超过上限 "
                        + $"{STModFormat.MaxStringBytes}");
                }

                byte[] keyBytes = ReadExact(br, keyLen);
                byte[] value = ReadExact(br, valueLen);

                var entry = new STModMetadataEntry
                {
                    Key = Encoding.UTF8.GetString(keyBytes),
                    ValueType = (STModValueType)valueType,
                    Value = value,
                };

                // 确定的重复规则：直接拒绝（不取第一个、不取最后一个、不合并）。
                if (result.ContainsKey(entry.Key))
                    throw new STModFormatException($".stmod 重复的元数据 Key '{entry.Key}'");

                foreach (char c in entry.Key)
                {
                    if (c < 0x20)
                    {
                        throw new STModFormatException(
                            $".stmod 元数据 Key 含控制字符 0x{(int)c:X2}"
                            + "（Key 必须是可打印的 UTF-8 文本）");
                    }
                }

                ValidateMetadataValue(entry);
                ValidateKnownKeyType(entry);
                result.Add(entry.Key, entry);
            }

            // 必需 Key 必须存在且类型正确 —— 但"模块版本"（module.version）与
            // 文件头里的**二进制格式版本**是两件事，不得混用。
            foreach (string key in STModMetadataKeys.Required)
            {
                if (!result.TryGetValue(key, out var required))
                    throw new STModFormatException($".stmod 缺少必需元数据 '{key}'");

                if (required.ValueType != STModValueType.String || required.Value.Length == 0)
                {
                    throw new STModFormatException(
                        $".stmod 必需元数据 '{key}' 必须是非空 String"
                        + $"（实际 {required.ValueType}，{required.Value.Length} 字节）");
                }
            }

            ValidateRequiredFeatures(result);

            return result;
        }
        catch (EndOfStreamException)
        {
            throw new STModFormatException(".stmod Metadata 区段内数据不足（疑似截断或长度字段被篡改）");
        }
    }

    /// <summary>
    /// 已知 Key 的**类型**必须精确匹配（链接协议 §3）——
    /// 例如 <c>module.linked</c> 必须是 Boolean、<c>module.globalCount</c> 必须是 UInt32。
    /// 类型不符即拒绝：读取器不得猜测语义。
    /// </summary>
    private static void ValidateKnownKeyType(STModMetadataEntry entry)
    {
        switch (entry.Key)
        {
            case STModMetadataKeys.ModuleLinked
                when entry.ValueType != STModValueType.Boolean:
                throw new STModFormatException(
                    $".stmod 元数据 '{entry.Key}' 必须是 Boolean（实际 {entry.ValueType}）");

            case STModMetadataKeys.GlobalCount
                when entry.ValueType != STModValueType.UInt32:
                throw new STModFormatException(
                    $".stmod 元数据 '{entry.Key}' 必须是 UInt32（实际 {entry.ValueType}）");
        }
    }

    /// <summary>值类型必须是已知类型，且定长类型的长度必须精确匹配。</summary>
    private static void ValidateMetadataValue(STModMetadataEntry entry)
    {
        int expected = entry.ValueType switch
        {
            STModValueType.Boolean => 1,
            STModValueType.Int32 => 4,
            STModValueType.UInt32 => 4,
            STModValueType.Int64 => 8,
            STModValueType.UInt64 => 8,
            STModValueType.String => -1,
            STModValueType.ByteArray => -1,
            _ => throw new STModFormatException(
                $".stmod 元数据 '{entry.Key}' 的 ValueType={(byte)entry.ValueType} 未知"),
        };

        if (expected >= 0 && entry.Value.Length != expected)
        {
            throw new STModFormatException(
                $".stmod 元数据 '{entry.Key}'（{entry.ValueType}）的 ValueLength="
                + $"{entry.Value.Length}，应为 {expected}");
        }

        if (entry.ValueType == STModValueType.Boolean && entry.Value[0] > 1)
        {
            throw new STModFormatException(
                $".stmod 元数据 '{entry.Key}' 的 Boolean 取值 0x{entry.Value[0]:X2} 非法"
                + "（只允许 0x00 / 0x01）");
        }
    }

    /// <summary>
    /// <c>module.requiredFeatures</c> 中不得出现本读取器不认识的必需特性 ——
    /// 旧读取器必须拒绝，而不是猜测语义。
    /// </summary>
    private static void ValidateRequiredFeatures(
        Dictionary<string, STModMetadataEntry> metadata)
    {
        if (!metadata.TryGetValue(STModMetadataKeys.RequiredFeatures, out var entry)
            || entry.ValueType != STModValueType.String)
        {
            return;
        }

        foreach (string feature in entry.AsString().Split(
            ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!STModFeatures.Supported.Contains(feature, StringComparer.Ordinal))
            {
                throw new STModFormatException(
                    $".stmod 模块声明了不支持的必需特性 '{feature}'"
                    + $"（本读取器支持：{string.Join(", ", STModFeatures.Supported)}）");
            }
        }
    }

    // ══════════════════════════════════════════════════════════
    //  FunctionIndex
    // ══════════════════════════════════════════════════════════

    private static List<STModFunctionEntry> ReadFunctionIndex(
        byte[] data, STModSectionInfo section, int codeLength, uint codeSectionId)
    {
        try
        {
            using var br = OpenSection(data, section);

            uint count = br.ReadUInt32();
            br.ReadUInt32();
            br.ReadUInt32();
            br.ReadUInt32();

            if (count > STModFormat.MaxFunctionEntries)
            {
                throw new STModFormatException(
                    $".stmod 函数条目数 {count} 超过上限 {STModFormat.MaxFunctionEntries}");
            }

            var result = new List<STModFunctionEntry>((int)count);
            var ids = new HashSet<uint>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var ranges = new List<(ulong Start, ulong End, string Name)>((int)count);

            for (uint i = 0; i < count; i++)
            {
                uint functionId = br.ReadUInt32();
                uint funcFlags = br.ReadUInt32();
                uint entryCodeSectionId = br.ReadUInt32();
                uint nameLen = br.ReadUInt32();
                uint sigLen = br.ReadUInt32();
                uint paramCount = br.ReadUInt32();
                uint localCount = br.ReadUInt32();
                uint maxStack = br.ReadUInt32();
                ulong codeOffset = br.ReadUInt64();
                ulong length = br.ReadUInt64();

                if (nameLen == 0 || nameLen > STModFormat.MaxStringBytes
                    || sigLen > STModFormat.MaxStringBytes)
                {
                    throw new STModFormatException(
                        $".stmod 函数第 {i} 条的 name/signature 长度非法"
                        + $"（name={nameLen}, signature={sigLen}）");
                }

                byte[] nameBytes = ReadExact(br, nameLen);
                byte[] signature = ReadExact(br, sigLen);
                string name = Encoding.UTF8.GetString(nameBytes);

                if (!ids.Add(functionId))
                    throw new STModFormatException($".stmod 重复的 functionId {functionId}");

                if (!names.Add(name))
                    throw new STModFormatException($".stmod 重复的函数名 '{name}'");

                if (length == 0)
                    throw new STModFormatException($".stmod 函数 '{name}' 的 CodeLength 为 0");

                if (entryCodeSectionId != codeSectionId)
                {
                    throw new STModFormatException(
                        $".stmod 函数 '{name}' 的 CodeSectionId={entryCodeSectionId} "
                        + $"未指向 Code 区段（应为 {codeSectionId}）");
                }

                // 真实布局校验：必须落在 Code 区段内（先比较，再相减，避免溢出）。
                if (codeOffset > (ulong)codeLength || length > (ulong)codeLength - codeOffset)
                {
                    throw new STModFormatException(
                        $".stmod 函数 '{name}' 的代码范围 "
                        + $"0x{codeOffset:X}+0x{length:X} 越过 Code 区段（{codeLength} 字节）");
                }

                ranges.Add((codeOffset, codeOffset + length, name));

                result.Add(new STModFunctionEntry
                {
                    FunctionId = functionId,
                    Flags = (STModFunctionFlags)funcFlags,
                    CodeSectionId = entryCodeSectionId,
                    Name = name,
                    Signature = Encoding.UTF8.GetString(signature),
                    ParamCount = paramCount,
                    LocalCount = localCount,
                    MaxStack = maxStack,
                    CodeOffset = codeOffset,
                    CodeLength = length,
                });
            }

            // 同一 Code 区段内的函数区间不得重叠：重叠说明索引不是真实布局。
            var ordered = ranges.OrderBy(r => r.Start).ToList();
            for (int i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].Start < ordered[i - 1].End)
                {
                    throw new STModFormatException(
                        $".stmod 函数 '{ordered[i - 1].Name}' 与 '{ordered[i].Name}' "
                        + "的代码区间重叠");
                }
            }

            return result;
        }
        catch (EndOfStreamException)
        {
            throw new STModFormatException(
                ".stmod FunctionIndex 区段内数据不足（疑似截断或长度字段被篡改）");
        }
    }

    // ══════════════════════════════════════════════════════════
    //  ConstantPool
    // ══════════════════════════════════════════════════════════

    private static BcValue[] ReadConstantPool(byte[] data, STModSectionInfo section)
    {
        try
        {
            using var br = OpenSection(data, section);

            uint count = br.ReadUInt32();
            br.ReadUInt32();

            if (count > STModFormat.MaxFunctionEntries)
            {
                throw new STModFormatException(
                    $".stmod 常量池条目数 {count} 超过上限 {STModFormat.MaxFunctionEntries}");
            }

            var pool = new BcValue[(int)count];
            for (int i = 0; i < pool.Length; i++)
                pool[i] = BcValueSerializer.Read(br, i);

            return pool;
        }
        catch (EndOfStreamException)
        {
            throw new STModFormatException(".stmod ConstantPool 区段内数据不足（疑似截断）");
        }
        catch (InvalidDataException ex)
        {
            throw new STModFormatException($".stmod ConstantPool 区段无效: {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════════════════
    //  ImportSymbols / ExportSymbols（链接协议 §4 / §5）
    // ══════════════════════════════════════════════════════════

    private static List<STModSymbol> ReadSymbols(
        byte[] data,
        STModSectionInfo? section,
        bool isExport,
        IReadOnlyList<STModFunctionEntry> functions)
    {
        var result = new List<STModSymbol>();
        if (section is null)
            return result;

        string what = isExport ? "ExportSymbols" : "ImportSymbols";

        try
        {
            using var br = OpenSection(data, section);

            uint count = br.ReadUInt32();
            uint reserved = br.ReadUInt32();
            if (reserved != 0)
                throw new STModFormatException($".stmod {what} 区段的保留字段必须为 0");

            if (count > STModFormat.MaxFunctionEntries)
                throw new STModFormatException(
                    $".stmod {what} 符号数 {count} 超过上限 {STModFormat.MaxFunctionEntries}");

            var names = new HashSet<string>(StringComparer.Ordinal);

            for (uint i = 0; i < count; i++)
            {
                uint functionId = 0;
                uint nameLen;
                uint sigLen;
                ushort kindRaw;
                ushort flagsRaw;

                if (isExport)
                {
                    functionId = br.ReadUInt32();
                    nameLen = br.ReadUInt32();
                    sigLen = br.ReadUInt32();
                    kindRaw = br.ReadUInt16();
                    flagsRaw = br.ReadUInt16();
                }
                else
                {
                    nameLen = br.ReadUInt32();
                    sigLen = br.ReadUInt32();
                    kindRaw = br.ReadUInt16();
                    flagsRaw = br.ReadUInt16();
                }

                if (nameLen == 0 || nameLen > STModFormat.MaxStringBytes
                    || sigLen > STModFormat.MaxStringBytes)
                {
                    throw new STModFormatException(
                        $".stmod {what} 第 {i} 条的 name/signature 长度非法"
                        + $"（name={nameLen}, signature={sigLen}）");
                }

                string name = Encoding.UTF8.GetString(ReadExact(br, nameLen));
                string signature = Encoding.UTF8.GetString(ReadExact(br, sigLen));

                if (!names.Add(name))
                    throw new STModFormatException(
                        $".stmod 重复的符号名 '{name}'（{what} 区段）");

                var kind = (STModSymbolKind)kindRaw;
                if (!Enum.IsDefined(kind))
                    throw new STModFormatException(
                        $".stmod {what} 符号 '{name}' 的 Kind={kindRaw} 未知");

                if (isExport && !functions.Any(f => f.FunctionId == functionId))
                    throw new STModFormatException(
                        $".stmod 导出符号 '{name}' 指向不存在的 FunctionId {functionId}");

                result.Add(new STModSymbol
                {
                    Name = name,
                    Signature = signature,
                    Kind = kind,
                    Flags = (STModSymbolFlags)flagsRaw,
                    FunctionId = functionId,
                });
            }

            if (br.BaseStream.Position != br.BaseStream.Length)
                throw new STModFormatException(
                    $".stmod {what} 区段在 {count} 条记录后仍有"
                    + $" {br.BaseStream.Length - br.BaseStream.Position} 字节尾随数据");

            return result;
        }
        catch (EndOfStreamException)
        {
            throw new STModFormatException(
                $".stmod {what} 区段内数据不足（疑似截断或长度字段被篡改）");
        }
    }

    // ══════════════════════════════════════════════════════════
    //  小工具
    // ══════════════════════════════════════════════════════════

    private static byte[] ReadExact(BinaryReader br, uint count)
    {
        byte[] bytes = br.ReadBytes((int)count);
        if (bytes.Length != (int)count)
            throw new EndOfStreamException();
        return bytes;
    }
}

/// <summary>
/// 宿主函数兼容性校验。
/// </summary>
/// <remarks>
/// <b>为什么不把 HCALL ID 当成恒久稳定</b>：动态注册的宿主函数 ID 来自编译时的
/// <c>hostFunctions</c> 名→ID 表（<c>RecordHostIdx</c> 记录的是<b>模块局部</b> ID）。
/// 模块被另一个宿主环境加载时，必须按<b>名字</b>重新核对 ID；不一致就拒绝，
/// 而不是让 HCALL 打到一个语义不同的实现上。
/// <para>
/// 静态 ABI ID（<c>HostCallIds</c> 常量）不进重定位表（§5 HOST-2 裁定），
/// 其兼容性由格式版本 + <c>compiler.target</c> 元数据约束。
/// </para>
/// </remarks>
public static class STModHostCompatibility
{
    /// <summary>
    /// 用给定的 名→ID 表校验模块的宿主调用。
    /// </summary>
    /// <returns>不兼容项的说明；空列表 = 兼容。</returns>
    public static IReadOnlyList<string> Check(
        STModModule module, IReadOnlyDictionary<string, ushort> hostFunctions)
    {
        var problems = new List<string>();
        if (module.Relocations is null)
            return problems;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in module.Relocations.Records)
        {
            if (record.Kind != RelocationKind.HostIdx)
                continue;

            string? name = record.SymbolName;
            if (string.IsNullOrEmpty(name))
            {
                problems.Add("存在没有符号名的 HostIdx 重定位记录（无法按名核对）");
                continue;
            }

            if (!seen.Add(name))
                continue;

            if (!hostFunctions.TryGetValue(name, out ushort current))
            {
                problems.Add($"模块依赖的宿主函数 '{name}' 在当前宿主环境中未注册");
                continue;
            }

            if (current != record.Addend)
            {
                problems.Add(
                    $"宿主函数 '{name}' 的 ID 不兼容：模块内为 {record.Addend}，当前环境为 {current}");
            }
        }

        return problems;
    }
}
