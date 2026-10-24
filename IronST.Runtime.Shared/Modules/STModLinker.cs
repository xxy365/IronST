// STModLinker.cs — `.stmod` 静态链接器（链接协议 docs/stmod链接协议.md §8）。
//
// 职责：把 N 个 `.stmod` 真正合并为一个**自足**的 `.stmod`。
//   · 合并 Code / ConstantPool / 全局槽位 / FunctionIndex；
//   · 对每一类重定位做**有边界检查**的回填（§6）；
//   · 解析所有导入符号，检测重复定义与签名不一致；
//   · 重建元数据（module.linked = true 等）。
//
// 边界：
//   · 只处理**已读入**的 STModModule；文件读写由调用方（CLI）负责。
//   · 不改变调用约定、不改指令集、不重排函数体内字节：只做地址/索引回填。
//   · 函数体在模块内**连续拷贝**，因此模块内任意局部 PC 都平移同一个基址；
//     这一点让"标签（JMP 目标）"与"本模块函数"都能用同一套加法重定基。
//   · 绝不截断位移、绝不静默跳过不支持的重定位：无法处理一律**拒绝**。
using System.Buffers.Binary;
using IronST.Runtime.Shared;

namespace IronST.Runtime.Shared.Modules;

/// <summary>静态链接失败（可安全展示给用户）。</summary>
public sealed class STModLinkException : Exception
{
    public STModLinkException(string message) : base(message) { }
}

/// <summary>`mod link` 的选项。</summary>
public sealed class STModLinkOptions
{
    /// <summary>显式入口函数名（短名）；<c>null</c> 时沿用输入模块的入口。</summary>
    public string? EntryName { get; init; }

    /// <summary>输出模块名；<c>null</c> 时取首个输入模块名。</summary>
    public string? ModuleName { get; init; }

    /// <summary>
    /// 当前宿主环境（宿主函数名 → 最终 ID）。用于 <c>HostIdx</c> 的按名重解析（§6.2）；
    /// <c>null</c> 表示没有可用宿主环境（此时若遇到 HostIdx 记录即拒绝）。
    /// </summary>
    public IReadOnlyDictionary<string, ushort>? HostFunctions { get; init; }

    /// <summary>信息性来源（写入 <c>module.sources</c>）。</summary>
    public IReadOnlyList<string>? SourceNames { get; init; }
}

/// <summary>把多个 `.stmod` 链接为一个完整模块。</summary>
public static class STModLinker
{
    /// <summary>入口桩：<c>CALL &lt;entry&gt;</c>（5 字节）+ <c>SYSCALL EXIT</c>（2 字节）。</summary>
    private const int EntryStubLength = 7;

    /// <summary>模块间代码块对齐（保持与区段对齐一致）。</summary>
    private const int CodeAlignment = 8;

    /// <summary>
    /// 链接为内存中的 <see cref="STModModule"/>。所有校验通过后才返回；
    /// 任何不一致都抛 <see cref="STModLinkException"/>（不产出半成品）。
    /// </summary>
    public static STModModule Link(
        IReadOnlyList<STModModule> modules, STModLinkOptions? options = null)
        => LinkWithLayout(modules, options).Module;

    /// <summary>
    /// 链接，并额外返回<b>每个输入模块的代码基址</b>（§8 步骤 6 的那个 <c>codeBase</c>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 存在的理由：链接把各模块的机器码<b>逐字节原样</b>搬到 <c>codeBase[i]</c> 处
    /// （只有操作数里的绝对地址经重定位表改写），所以输入模块 i 里 PC=<c>p</c> 的
    /// <b>指令本身</b>在产物里就是 <c>codeBase[i] + p</c>。
    /// </para>
    /// <para>
    /// 于是「PC → (文件, 行)」的源映射也能用同一个平移保下来：
    /// <c>新PC = codeBase[i] + 旧PC</c>。这是让 <c>run linked.stmod</c> 还能报源行号的前提
    /// —— 否则链接后 PC 空间变了，模块自带的映射全部失效。
    /// </para>
    /// </remarks>
    public static (STModModule Module, int[] CodeBase) LinkWithLayout(
        IReadOnlyList<STModModule> modules, STModLinkOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(modules);
        options ??= new STModLinkOptions();

        if (modules.Count == 0)
            throw new STModLinkException("没有输入模块：`mod link` 至少需要一个 .stmod");

        // ── §8 步骤 1/2：结构复检（读取器已做；此处复检版本门槛）──
        for (int mi = 0; mi < modules.Count; mi++)
        {
            if (modules[mi].FormatVersionMajor != STModFormat.FormatVersionMajor)
            {
                throw new STModLinkException(
                    $"模块 {Label(modules, mi)} 的格式主版本 "
                    + $"{modules[mi].FormatVersionMajor} 不受支持"
                    + $"（只接受 {STModFormat.FormatVersionMajor}）");
            }
        }

        // ── §8 步骤 3：定义表 + 重复定义检测 ──
        var definitions =
            new Dictionary<string, (int Module, STModFunctionEntry Fn)>(StringComparer.Ordinal);
        var definitionModules = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        for (int mi = 0; mi < modules.Count; mi++)
        {
            foreach (var f in modules[mi].Functions)
            {
                if (f.IsDeclaration)
                    continue;

                if (!definitions.ContainsKey(f.Name))
                    definitions[f.Name] = (mi, f);

                if (!definitionModules.TryGetValue(f.Name, out var owners))
                    definitionModules[f.Name] = owners = new List<int>();
                owners.Add(mi);
            }
        }

        // ── 入口解析（§7.2）：先于重复定义检查，使"同名多实现"在**指定入口**时
        //    报"入口歧义"并列出模块限定候选，而不是笼统的"重复定义"。──
        var (entryModule, entryFunction) =
            ResolveEntry(modules, definitions, definitionModules, options.EntryName);

        // ── §8 步骤 3（续）：重复定义检测 ──
        var duplicates = definitionModules.Where(kv => kv.Value.Count > 1).ToList();
        if (duplicates.Count > 0)
        {
            string report = string.Join("；", duplicates.Select(kv =>
                $"'{kv.Key}'（{string.Join("、", kv.Value.Select(mi => Label(modules, mi)))}）"));
            throw new STModLinkException(
                $"重复定义的函数：{report}。同名函数在多个模块中都有实现，"
                + "链接器不得静默任选；请重命名或拆分模块。");
        }

        // ── §8 步骤 4/5：解析导入 + 签名一致性 ──
        for (int mi = 0; mi < modules.Count; mi++)
        {
            foreach (var import in modules[mi].Imports)
            {
                if (import.Kind == STModSymbolKind.HostFunction)
                {
                    if (options.HostFunctions is null
                        || !options.HostFunctions.ContainsKey(import.Name))
                    {
                        throw new STModLinkException(
                            $"模块 {Label(modules, mi)} 导入宿主函数 '{import.Name}'，"
                            + "但当前宿主环境未提供该函数");
                    }
                    continue;
                }

                if (!definitions.TryGetValue(import.Name, out var def))
                {
                    throw new STModLinkException(
                        $"未解析的外部引用 '{import.Name}'（模块 {Label(modules, mi)}）："
                        + "没有任何输入模块提供其实现");
                }

                if (!string.Equals(import.Signature, def.Fn.Signature, StringComparison.Ordinal))
                {
                    throw new STModLinkException(
                        $"签名不一致：'{import.Name}' 在 {Label(modules, mi)} 中导入为 "
                        + $"'{import.Signature}'，在 {Label(modules, def.Module)} 中定义为 "
                        + $"'{def.Fn.Signature}'");
                }
            }
        }

        // ── §8 步骤 6：代码基址 ──
        var codeBase = new int[modules.Count];
        int cursor = entryFunction is not null ? EntryStubLength : 0;
        for (int mi = 0; mi < modules.Count; mi++)
        {
            cursor = AlignUp(cursor);
            codeBase[mi] = cursor;
            cursor += modules[mi].Code.Length;
        }
        int totalCode = AlignUp(cursor);

        var mergedCode = new byte[totalCode];
        for (int mi = 0; mi < modules.Count; mi++)
        {
            Array.Copy(
                modules[mi].Code, 0, mergedCode, codeBase[mi], modules[mi].Code.Length);
        }

        // 入口桩的 CALL 立即数稍后经重定位表回填（保持"可再链接"）。
        var relocations = new RelocationTable();

        uint entryStartPC = 0;
        if (entryFunction is not null)
        {
            mergedCode[0] = (byte)OpCode.CALL;
            mergedCode[5] = (byte)OpCode.SYSCALL;
            mergedCode[6] = (byte)SyscallFunc.EXIT;
            entryStartPC = (uint)(codeBase[entryModule] + (long)entryFunction.CodeOffset);
        }

        // ── §8 步骤 7：合并函数索引（按名去重：声明被实现遮蔽）──
        var outputFunctions = new List<(int Module, STModFunctionEntry Fn)>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        for (int mi = 0; mi < modules.Count; mi++)
        {
            foreach (var f in modules[mi].Functions)
            {
                if (f.IsDeclaration && definitions.ContainsKey(f.Name))
                    continue;   // 该名字已有实现，声明条目被遮蔽
                if (!seenNames.Add(f.Name))
                    continue;   // 跨模块重复的宿主声明：只保留首个
                outputFunctions.Add((mi, f));
            }
        }

        var newIds = new Dictionary<string, uint>(StringComparer.Ordinal);
        for (int i = 0; i < outputFunctions.Count; i++)
            newIds[outputFunctions[i].Fn.Name] = (uint)i;

        var mergedStart = new Dictionary<string, uint>(StringComparer.Ordinal);
        for (int mi = 0; mi < modules.Count; mi++)
        {
            foreach (var f in modules[mi].Functions)
            {
                if (f.IsDeclaration)
                    continue;
                mergedStart[f.Name] =
                    (uint)(codeBase[mi] + (long)f.CodeOffset);
            }
        }

        var outIndex = new List<STModFunctionEntry>(outputFunctions.Count);
        foreach (var (mi, f) in outputFunctions)
        {
            var flags = f.Flags & ~STModFunctionFlags.EntryPoint;
            if (entryFunction is not null && string.Equals(f.Name, entryFunction.Name, StringComparison.Ordinal))
                flags |= STModFunctionFlags.EntryPoint;

            outIndex.Add(new STModFunctionEntry
            {
                FunctionId = newIds[f.Name],
                Flags = flags,
                Name = f.Name,
                Signature = f.Signature,
                ParamCount = f.ParamCount,
                LocalCount = f.LocalCount,
                MaxStack = f.MaxStack,
                CodeOffset = (ulong)(codeBase[mi] + (long)f.CodeOffset),
                CodeLength = f.CodeLength,
            });
        }

        // ── §8 步骤 8：合并常量池 ──
        var constBase = new int[modules.Count];
        int constCursor = 0;
        var mergedPool = new List<BcValue>();
        for (int mi = 0; mi < modules.Count; mi++)
        {
            constBase[mi] = constCursor;
            mergedPool.AddRange(modules[mi].ConstantPool);
            constCursor += modules[mi].ConstantPool.Count;
        }

        // ── §8 步骤 9：合并全局槽位 ──
        var globalBase = new int[modules.Count];
        int globalCursor = 0;
        for (int mi = 0; mi < modules.Count; mi++)
        {
            globalBase[mi] = globalCursor;
            globalCursor += Math.Max(0, modules[mi].GlobalCount);
        }
        if (globalCursor > RelocationRules.MaxGlobalSlots)
        {
            throw new STModLinkException(
                $"合并后全局槽位总数 {globalCursor} 超过上限 "
                + $"{RelocationRules.MaxGlobalSlots}");
        }

        // ── §8 步骤 10：逐条重定位回填 ──
        RelocateAddr32EntryStub(mergedCode, relocations, entryFunction, entryStartPC);

        for (int mi = 0; mi < modules.Count; mi++)
        {
            if (modules[mi].Relocations is not { } src)
                continue;

            foreach (var record in src.Records)
            {
                ApplyRecord(modules, options, definitions, mergedStart,
                    codeBase, constBase, globalBase, mi, record, mergedCode, relocations);
            }
        }

        // ── §8 步骤 11：重建元数据 ──
        var exports = outputFunctions
            .Where(x => x.Fn.IsExported)
            .Select(x => new STModSymbol
            {
                Name = x.Fn.Name,
                Signature = x.Fn.Signature,
                Kind = STModSymbolKind.Function,
                Flags = STModSymbolFlags.Exported,
                FunctionId = newIds[x.Fn.Name],
            })
            .ToList();

        var metadata = BuildMetadata(modules, options, entryFunction, exports, globalCursor);

        // ── §8 步骤 12：复检无未解析强制重定位 ──
        // 已在上文逐条解析：任何无法解析的符号都已抛 STModLinkException。

        return (new STModModule
        {
            Metadata = metadata,
            Functions = outIndex,
            Code = mergedCode,
            ConstantPool = mergedPool,
            Sections = [],
            HasEntryPoint = entryFunction is not null,
            EntryPC = 0,
            Relocations = relocations,
            Imports = [],
            Exports = exports,
            IsLinked = true,
            GlobalCount = globalCursor,
            FormatVersionMajor = STModFormat.FormatVersionMajor,
            FormatVersionMinor = STModFormat.FormatVersionMinor,
        }, codeBase);
    }

    /// <summary>链接并序列化，随后**复读**校验产物（§8 步骤 13）。</summary>
    public static byte[] LinkToBytes(
        IReadOnlyList<STModModule> modules, STModLinkOptions? options = null)
        => LinkToBytesWithLayout(modules, options).Bytes;

    /// <summary>
    /// 同 <see cref="LinkToBytes"/>，但额外返回各输入模块的代码基址
    /// （见 <see cref="LinkWithLayout"/>）——调用方据此把源映射平移到产物 PC 空间。
    /// </summary>
    public static (byte[] Bytes, int[] CodeBase) LinkToBytesWithLayout(
        IReadOnlyList<STModModule> modules, STModLinkOptions? options = null)
    {
        var (module, codeBase) = LinkWithLayout(modules, options);
        byte[] bytes = STModWriter.Write(module);
        STModReader.Read(bytes);   // 复读：确保产物自身合法
        return (bytes, codeBase);
    }

    // ══════════════════════════════════════════════════════════
    //  逐类重定位
    // ══════════════════════════════════════════════════════════

    private static void RelocateAddr32EntryStub(
        byte[] code, RelocationTable table,
        STModFunctionEntry? entry, uint entryStartPC)
    {
        if (entry is null)
            return;

        BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(1, 4), entryStartPC);
        table.Add(RelocationRecord.Create(
            RelocationKind.Addr32,
            RelocationSection.Code,
            offset: 1,
            symbolName: entry.Name,
            addend: (int)entryStartPC));
    }

    private static void ApplyRecord(
        IReadOnlyList<STModModule> modules,
        STModLinkOptions options,
        Dictionary<string, (int Module, STModFunctionEntry Fn)> definitions,
        Dictionary<string, uint> mergedStart,
        int[] codeBase, int[] constBase, int[] globalBase,
        int moduleIndex,
        RelocationRecord record,
        byte[] code,
        RelocationTable output)
    {
        if (record.Section != RelocationSection.Code)
        {
            throw new STModLinkException(
                $"不支持的重定位段 {record.Section}（模块 {Label(modules, moduleIndex)}，"
                + $"偏移 0x{record.Offset:X}）");
        }

        int offset = codeBase[moduleIndex] + record.Offset;
        if (offset < 0 || offset > code.Length)
        {
            throw new STModLinkException(
                $"重定位偏移越界：0x{offset:X}（模块 {Label(modules, moduleIndex)}，"
                + $"Kind={record.Kind}）");
        }

        switch (record.Kind)
        {
            case RelocationKind.Addr32:
            {
                string symbol = record.SymbolName!;
                int target;

                if (mergedStart.TryGetValue(symbol, out uint start))
                {
                    // 函数符号（本模块或跨模块）：解析到合并后函数起点。
                    target = (int)start;
                }
                else if (record.Addend > 0)
                {
                    // 函数内标签：模块内局部目标 PC + 模块代码基址。
                    target = codeBase[moduleIndex] + record.Addend;
                }
                else
                {
                    throw new STModLinkException(
                        $"未解析的符号 '{symbol}'（模块 {Label(modules, moduleIndex)}，"
                        + $"代码偏移 0x{record.Offset:X}）：既不是任何模块的函数，"
                        + "也没有可用的标签加数");
                }

                EnsureRange(code, offset, 4, record, modules, moduleIndex);
                BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(offset, 4), (uint)target);
                output.Add(RelocationRecord.Create(
                    RelocationKind.Addr32, RelocationSection.Code, offset,
                    symbol, record.Source, target));
                break;
            }

            case RelocationKind.ConstIdx:
            {
                int target = constBase[moduleIndex] + record.Addend;
                EnsureRange(code, offset, 4, record, modules, moduleIndex);
                BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(offset, 4), target);
                output.Add(RelocationRecord.Create(
                    RelocationKind.ConstIdx, RelocationSection.Code, offset,
                    null, record.Source, target));
                break;
            }

            case RelocationKind.GlobalIdx:
            {
                int target = globalBase[moduleIndex] + record.Addend;
                EnsureRange(code, offset, 2, record, modules, moduleIndex);
                BinaryPrimitives.WriteInt16LittleEndian(code.AsSpan(offset, 2), (short)target);
                output.Add(RelocationRecord.Create(
                    RelocationKind.GlobalIdx, RelocationSection.Code, offset,
                    record.SymbolName, record.Source, target));
                break;
            }

            case RelocationKind.HostIdx:
            {
                if (options.HostFunctions is null
                    || !options.HostFunctions.TryGetValue(record.SymbolName!, out ushort hostId))
                {
                    throw new STModLinkException(
                        $"宿主函数 '{record.SymbolName}' 在当前宿主环境中未注册"
                        + $"（模块 {Label(modules, moduleIndex)}）");
                }

                EnsureRange(code, offset, 2, record, modules, moduleIndex);
                BinaryPrimitives.WriteUInt16LittleEndian(code.AsSpan(offset, 2), hostId);
                output.Add(RelocationRecord.Create(
                    RelocationKind.HostIdx, RelocationSection.Code, offset,
                    record.SymbolName, record.Source, hostId));
                break;
            }

            case RelocationKind.Off8:
            {
                // 函数内相对位移：链接器只在函数之间拼接、不改函数体内部字节，
                // 因此相对距离不变 —— 只重定基"记录偏移"，**不动任何字节**。
                output.Add(RelocationRecord.Create(
                    RelocationKind.Off8, RelocationSection.Code, offset,
                    record.SymbolName, record.Source, record.Addend));
                break;
            }

            default:
                throw new STModLinkException(
                    $"不支持的重定位类型 {(byte)record.Kind}（模块 "
                    + $"{Label(modules, moduleIndex)}，偏移 0x{record.Offset:X}）");
        }
    }

    private static void EnsureRange(
        byte[] code, int offset, int width,
        RelocationRecord record, IReadOnlyList<STModModule> modules, int moduleIndex)
    {
        if (offset < 0 || offset > code.Length - width)
        {
            throw new STModLinkException(
                $"重定位 {record.Kind} 的回填区间 [{offset}, {offset + width}) "
                + $"越过合并后代码（{code.Length} 字节）"
                + $"（模块 {Label(modules, moduleIndex)}）");
        }
    }

    // ══════════════════════════════════════════════════════════
    //  入口解析
    // ══════════════════════════════════════════════════════════

    private static (int Module, STModFunctionEntry? Fn) ResolveEntry(
        IReadOnlyList<STModModule> modules,
        Dictionary<string, (int Module, STModFunctionEntry Fn)> definitions,
        Dictionary<string, List<int>> definitionModules,
        string? entryName)
    {
        if (entryName is not null)
        {
            var candidates = new List<(int Module, STModFunctionEntry Fn)>();
            for (int mi = 0; mi < modules.Count; mi++)
            {
                foreach (var f in modules[mi].Functions)
                {
                    if (!f.IsDeclaration
                        && string.Equals(f.Name, entryName, StringComparison.Ordinal))
                    {
                        candidates.Add((mi, f));
                    }
                }
            }

            if (candidates.Count == 0)
            {
                // 只有声明（无实现）？
                bool declaredOnly = modules.Any(m =>
                    m.Functions.Any(f => f.IsDeclaration
                        && string.Equals(f.Name, entryName, StringComparison.Ordinal)));

                if (declaredOnly)
                {
                    throw new STModLinkException(
                        $"入口函数 '{entryName}' 只有声明、没有可执行实现，不能作为入口");
                }

                var available = definitions.Keys
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToList();
                throw new STModLinkException(
                    $"找不到入口函数 '{entryName}'。"
                    + (available.Count > 0
                        ? $"可用入口：{string.Join("、", available)}"
                        : "输入模块中没有可执行的函数"));
            }

            if (candidates.Count > 1)
            {
                string list = string.Join("、", candidates.Select(c =>
                    $"{Label(modules, c.Module)}:{c.Fn.Name}"));
                throw new STModLinkException(
                    $"入口函数名 '{entryName}' 有歧义（{candidates.Count} 个匹配）："
                    + $"{list}。同名函数在多个模块中都有实现，无法共存于一个模块；"
                    + "请重命名其中之一后重新编译，再用 --entry 指定。");
            }

            return candidates[0];
        }

        // 未指定 `--entry`：采用**首个声明了入口的输入模块**的入口（链接协议 §7.2）。
        // 因此应把主程序模块放在命令行最前；需要精确选择时请显式传 `--entry`。
        for (int mi = 0; mi < modules.Count; mi++)
        {
            if (!modules[mi].HasEntryPoint)
                continue;

            string? declared = modules[mi].GetString(STModMetadataKeys.EntryFunction);
            if (declared is not null
                && definitions.TryGetValue(declared, out var byName))
            {
                return (byName.Module, byName.Fn);
            }

            var flagged = modules[mi].Functions.FirstOrDefault(f => f.IsEntryPoint && !f.IsDeclaration);
            if (flagged is not null && definitions.ContainsKey(flagged.Name))
                return (mi, flagged);
        }

        return (-1, null);
    }

    // ══════════════════════════════════════════════════════════
    //  元数据
    // ══════════════════════════════════════════════════════════

    private static Dictionary<string, STModMetadataEntry> BuildMetadata(
        IReadOnlyList<STModModule> modules,
        STModLinkOptions options,
        STModFunctionEntry? entryFunction,
        IReadOnlyList<STModSymbol> exports,
        int globalCount)
    {
        string sourceName = modules[0].GetString(STModMetadataKeys.ModuleName) ?? "linked";
        string moduleName = options.ModuleName ?? sourceName;

        var metadata = new Dictionary<string, STModMetadataEntry>(StringComparer.Ordinal)
        {
            [STModMetadataKeys.ModuleName] =
                STModMetadataEntry.FromString(STModMetadataKeys.ModuleName, moduleName),

            [STModMetadataKeys.ModuleVersion] =
                STModMetadataEntry.FromString(
                    STModMetadataKeys.ModuleVersion,
                    modules[0].GetString(STModMetadataKeys.ModuleVersion) ?? "1.0.0"),

            [STModMetadataKeys.CompilerVersion] = STModMetadataEntry.FromString(
                STModMetadataKeys.CompilerVersion,
                modules[0].GetString(STModMetadataKeys.CompilerVersion) ?? "1.0.0.0"),

            [STModMetadataKeys.CompilerTarget] = STModMetadataEntry.FromString(
                STModMetadataKeys.CompilerTarget,
                modules[0].GetString(STModMetadataKeys.CompilerTarget) ?? "ironst-vm/1"),

            [STModMetadataKeys.ModuleDebug] =
                STModMetadataEntry.FromBoolean(STModMetadataKeys.ModuleDebug, false),

            [STModMetadataKeys.RequiredFeatures] = STModMetadataEntry.FromString(
                STModMetadataKeys.RequiredFeatures,
                string.Join(",", new[]
                {
                    STModFeatures.Bytecode,
                    STModFeatures.ConstantPool,
                    STModFeatures.Relocations,
                })),

            // 链接协议 §3：产物已是完全链接状态。
            [STModMetadataKeys.ModuleLinked] =
                STModMetadataEntry.FromBoolean(STModMetadataKeys.ModuleLinked, true),

            [STModMetadataKeys.GlobalCount] =
                STModMetadataEntry.FromUInt32(
                    STModMetadataKeys.GlobalCount, (uint)globalCount),

            ["module.sources"] = STModMetadataEntry.FromString(
                "module.sources",
                string.Join(";", options.SourceNames ?? [])),
        };

        if (exports.Count > 0)
        {
            metadata[STModMetadataKeys.ModuleExports] = STModMetadataEntry.FromString(
                STModMetadataKeys.ModuleExports,
                string.Join(";", exports.Select(e => e.Name)));
        }

        if (entryFunction is not null)
        {
            metadata[STModMetadataKeys.EntryFunction] = STModMetadataEntry.FromString(
                STModMetadataKeys.EntryFunction, entryFunction.Name);
            metadata[STModMetadataKeys.EntryPC] = STModMetadataEntry.FromUInt32(
                STModMetadataKeys.EntryPC, 0);
        }

        return metadata;
    }

    // ══════════════════════════════════════════════════════════
    //  小工具
    // ══════════════════════════════════════════════════════════

    private static string Label(IReadOnlyList<STModModule> modules, int index) =>
        modules[index].GetString(STModMetadataKeys.ModuleName) ?? $"#{index}";

    private static int AlignUp(int value)
    {
        int rem = value % CodeAlignment;
        return rem == 0 ? value : value + (CodeAlignment - rem);
    }
}
