// ModCommand.cs — `ironst mod build|link|inspect`：`.stmod` 模块的产出、链接与只读检查。
//
// 边界：
//   · build  —— 复用 IronRuntime.CompileFiles + DefaultHostEnvironment（与 compile/run 同源），
//               把真实编译产物写成 .stmod。不新增第二套编译管线。
//               `--ref m.stmod` 引用已编译模块的导出：只注入声明，保留未解析引用（P2）。
//   · link   —— 调用 STModLinker 真正静态链接多个 .stmod（P3，链接协议 §8）；
//               输出原子化，失败不留半成品。
//   · inspect —— 只解析与打印，**绝不执行**模块中的字节码。
//   · .stmod 自 P1 起可由 `run` 直接执行；存在未解析导入时 `run` 会拒绝（链接协议 §7）。
using IronST.BytecodeGen;
using IronST.Frontend.Analysis;
using IronST.Runtime.Shared;
using IronST.Runtime.Shared.Modules;

namespace IronST.CLI.Commands;

public static class ModCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine(
                "错误: 缺少 mod 子命令（用法: mod build <输入> | mod link <模块...> | mod inspect <file.stmod>）");
            return 1;
        }

        var sub = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();

        return sub switch
        {
            "build" => Build(rest),
            "link" => Link(rest),
            "inspect" => Inspect(rest),
            _ => Unknown(sub),
        };
    }

    private static int Unknown(string sub)
    {
        Console.Error.WriteLine(
            $"错误: 未知 mod 子命令 '{sub}'（用法: mod build <输入> | mod link <模块...> | mod inspect <file.stmod>）");
        return 1;
    }

    // ══════════════════════════════════════════════════════════
    //  mod build
    // ══════════════════════════════════════════════════════════

    private static int Build(string[] args)
    {
        var inputs = new List<string>();
        var refPaths = new List<string>();
        var customMetadata = new Dictionary<string, string>(StringComparer.Ordinal);
        string? entry = null;
        string? output = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--ref":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("错误: 缺少 '--ref' 的参数");
                        return 1;
                    }
                    var refPath = args[++i];
                    if (refPath.Length == 0)
                    {
                        Console.Error.WriteLine("错误: --ref 不能为空");
                        return 1;
                    }
                    if (!refPath.EndsWith(".stmod", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.Error.WriteLine(
                            $"错误: --ref 需要 .stmod 文件，实际为 '{refPath}'");
                        return 1;
                    }
                    refPaths.Add(refPath);
                    break;
                case "-o":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("错误: 缺少 '-o' 的参数");
                        return 1;
                    }
                    output = args[++i];
                    if (output.Length == 0)
                    {
                        Console.Error.WriteLine("错误: -o 不能为空");
                        return 1;
                    }
                    break;

                case "--entry":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("错误: 缺少 '--entry' 的参数");
                        return 1;
                    }
                    entry = args[++i];
                    if (entry.Length == 0)
                    {
                        Console.Error.WriteLine("错误: --entry 不能为空");
                        return 1;
                    }
                    break;

                case "--meta":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("错误: 缺少 '--meta' 的参数");
                        return 1;
                    }
                    string pair = args[++i];
                    int separator = pair.IndexOf('=');
                    if (separator <= 0)
                    {
                        Console.Error.WriteLine(
                            $"错误: --meta 需要 KEY=VALUE 格式，实际为 '{pair}'");
                        return 1;
                    }
                    string metaKey = pair[..separator].Trim();
                    string metaValue = pair[(separator + 1)..];
                    if (metaKey.Length == 0)
                    {
                        Console.Error.WriteLine("错误: --meta 的 KEY 不能为空");
                        return 1;
                    }
                    if (ReservedMetadataKeys.Contains(metaKey))
                    {
                        Console.Error.WriteLine(
                            $"错误: --meta 不能覆盖保留元数据 '{metaKey}'");
                        return 1;
                    }
                    if (!customMetadata.TryAdd(metaKey, metaValue))
                    {
                        Console.Error.WriteLine($"错误: --meta 重复指定 '{metaKey}'");
                        return 1;
                    }
                    break;

                case "--author":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("错误: 缺少 '--author' 的参数");
                        return 1;
                    }
                    if (!customMetadata.TryAdd(STModMetadataKeys.ModuleAuthor, args[++i]))
                    {
                        Console.Error.WriteLine(
                            $"错误: 重复指定 '{STModMetadataKeys.ModuleAuthor}'");
                        return 1;
                    }
                    break;

                default:
                    if (args[i].StartsWith('-'))
                    {
                        Console.Error.WriteLine($"错误: 未知选项 '{args[i]}'");
                        return 1;
                    }
                    inputs.Add(args[i]);
                    break;
            }
        }

        if (inputs.Count == 0)
        {
            Console.Error.WriteLine(
                "错误: 缺少输入（用法: mod build <file.st|file.stproj ...> [-o out.stmod]）");
            return 1;
        }

        foreach (var f in inputs)
        {
            if (!File.Exists(f))
            {
                Console.Error.WriteLine($"错误: 找不到文件 '{f}'");
                return 1;
            }
        }

        // ── 项目模式判定（与 build / run 共用 ProjectBuilder）──
        bool isProject = inputs.Count == 1
            && inputs[0].EndsWith(".stproj", StringComparison.OrdinalIgnoreCase);

        STProjectFile? project = null;
        if (isProject)
        {
            try
            {
                project = STProjectFile.Load(inputs[0]);
            }
            catch (ProjectFileException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }

            // 项目内声明的 <STModuleReference> + 命令行 --ref（按绝对路径去重）。
            foreach (var declared in project.ModuleReferences)
            {
                bool already = refPaths.Any(p => string.Equals(
                    Path.GetFullPath(p), declared, StringComparison.OrdinalIgnoreCase));
                if (!already)
                    refPaths.Add(declared);
            }
        }

        // ── 引用模块：只读取导出签名，不把它们的源码拉进本编译单元 ──
        var referenceModules = new List<STModModule>();
        foreach (var refPath in refPaths)
        {
            if (!File.Exists(refPath))
            {
                Console.Error.WriteLine($"错误: 找不到引用模块 '{refPath}'");
                return 1;
            }

            try
            {
                referenceModules.Add(STModReader.Load(refPath));
            }
            catch (STModFormatException ex)
            {
                Console.Error.WriteLine($"错误: 引用模块 '{refPath}' 无效: {ex.Message}");
                return 1;
            }
        }

        List<ExternalLibrary> referenceLibraries;
        try
        {
            referenceLibraries = STModReference.BuildLibraries(referenceModules);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");
            return 1;
        }

        string moduleName;
        string[] sourceNames;
        CompileResult result;
        string defaultOutput;

        if (isProject)
        {
            try
            {
                result = ProjectBuilder.Compile(
                    project!, entry, referenceLibraries,
                    allowExternalCalls: referenceModules.Count > 0);
            }
            catch (ProjectFileException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
            catch (CompilationException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }

            moduleName = string.IsNullOrWhiteSpace(project!.Name)
                ? Path.GetFileNameWithoutExtension(project.ProjectPath)
                : project.Name;
            sourceNames = project.SourceFiles
                .Select(p => Path.GetRelativePath(project.ProjectDirectory, p))
                .ToArray();
            defaultOutput = Path.Combine(project.ProjectDirectory, moduleName + ".stmod");
        }
        else
        {
            foreach (var f in inputs)
            {
                if (!f.EndsWith(".st", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine(
                        $"错误: mod build 需要 .st 源文件或单个 .stproj 项目文件，实际为 '{f}'");
                    return 1;
                }
            }

            var files = new List<(string Source, string FileName)>(inputs.Count);
            foreach (var f in inputs)
            {
                try
                {
                    files.Add((File.ReadAllText(f), f));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"错误: 无法读取文件 '{f}': {ex.Message}");
                    return 1;
                }
            }

            try
            {
                result = IronRuntime.CompileFiles(
                    files,
                    hostFunctions: DefaultHostEnvironment.FunctionMap,
                    libraries: DefaultHostEnvironment.Libraries
                        .Concat(referenceLibraries)
                        .ToList(),
                    entryFunctionName: entry,
                    allowExternalCalls: referenceModules.Count > 0);
            }
            catch (CompilationException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }

            moduleName = Path.GetFileNameWithoutExtension(inputs[0]);
            sourceNames = inputs.Select(f => Path.GetFileName(f) ?? f).ToArray();
            defaultOutput = Path.ChangeExtension(inputs[0], ".stmod");
        }

        IReadOnlyList<STModSymbol> imports;
        try
        {
            imports = BuildImports(result, referenceModules);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");
            return 1;
        }

        STModModule module = BuildModule(result, moduleName, sourceNames, entry, imports);

        // 链接产物的代码基址（非链接产物为 null）——用于平移源映射，见下方旁车写出。
        int[]? linkBases = null;

        // ── P5：项目文件声明了 <STModuleReference> → 真正静态链接为自足模块 ──
        // 仅当项目**显式声明**模块引用时才链接；只用 `--ref` 时保持与 `.st` 输入
        // 一致的语义（注入声明、保留外部引用，留待后续 `mod link`）。
        if (isProject && project!.ModuleReferences.Count > 0)
        {
            var toLink = new List<STModModule>(1 + referenceModules.Count) { module };
            toLink.AddRange(referenceModules);

            try
            {
                (module, linkBases) = STModLinker.LinkWithLayout(
                    toLink,
                    new STModLinkOptions
                    {
                        EntryName = entry,
                        ModuleName = moduleName,
                        HostFunctions = DefaultHostEnvironment.FunctionMap,
                        SourceNames = sourceNames,
                    });
            }
            catch (STModLinkException ex)
            {
                Console.Error.WriteLine($"错误: {ex.Message}");
                return 1;
            }
        }

        if (customMetadata.Count > 0)
            module = WithCustomMetadata(module, customMetadata);

        var outPath = output ?? defaultOutput;

        try
        {
            // 原子写出：失败不留半成品（链接协议 §8 步骤 14）。
            STModWriter.SaveAtomic(outPath, module);
        }
        catch (STModFormatException ex)
        {
            Console.Error.WriteLine($"错误: 模块格式校验失败: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: 无法写出模块 '{outPath}': {ex.Message}");
            return 1;
        }

        Console.WriteLine($"已写出模块: {outPath}");
        Console.WriteLine($"构建成功: {moduleName}（{module.Functions.Count} 个函数）");

        // 旁车源映射：让 `run <.stmod>`（以及后续 `mod link`）能把运行期错误还原成
        // 「文件:行」。非链接产物直接用本单元的映射；链接产物须把各输入模块的映射
        // 按其 codeBase 平移后合并，否则 PC 空间一变行号全部错位。
        IronRuntime.SaveSourceMap(
            outPath,
            linkBases is null
                ? result.SourceMap.ToDictionary(kv => kv.Key, kv => kv.Value)
                : SourceResolver.Merge(ModuleSourceMapParts(linkBases, result.SourceMap, refPaths)));

        return 0;
    }

    /// <summary>
    /// 「本编译单元 + 各引用模块」的源映射分段：第 0 段是本单元（代码在内存里，
    /// 映射直接来自 <paramref name="ownMap"/>），第 i+1 段是 <paramref name="refPaths"/>[i]
    /// 的旁车映射（<c>&lt;ref&gt;.stmod</c> 旁边的 <c>.map.json</c>）。
    /// </summary>
    private static IEnumerable<(int CodeBase, IReadOnlyDictionary<int, (string file, int line)> Map)>
        ModuleSourceMapParts(
            int[] codeBase,
            IReadOnlyDictionary<int, (string file, int line)> ownMap,
            IReadOnlyList<string> refPaths)
    {
        if (codeBase.Length == 0)
            yield break;

        yield return (codeBase[0], ownMap);

        for (int i = 0; i < refPaths.Count && i + 1 < codeBase.Length; i++)
            yield return (codeBase[i + 1], IronRuntime.LoadSourceMap(refPaths[i]));
    }

    /// <summary>
    /// 把编译期未解析的外部符号映射为 <c>ImportSymbols</c> 条目。
    /// 签名取自引用模块的导出（同一份 IR 文本），从而链接期可做严格一致性校验。
    /// </summary>
    private static IReadOnlyList<STModSymbol> BuildImports(
        CompileResult result, IReadOnlyList<STModModule> references)
    {
        if (result.UnresolvedSymbols.Count == 0)
            return [];

        var exported = new Dictionary<string, STModSymbol>(StringComparer.Ordinal);
        foreach (var module in references)
            foreach (var symbol in module.Exports)
                exported.TryAdd(symbol.Name, symbol);

        var imports = new List<STModSymbol>(result.UnresolvedSymbols.Count);
        foreach (string name in result.UnresolvedSymbols)
        {
            if (!exported.TryGetValue(name, out var symbol))
            {
                throw new InvalidOperationException(
                    $"外部引用 '{name}' 未在任何 --ref 模块的导出中找到。"
                    + "请确认 --ref 指向提供该函数的模块。");
            }

            imports.Add(new STModSymbol
            {
                Name = name,
                Signature = symbol.Signature,
                Kind = STModSymbolKind.Function,
                Flags = STModSymbolFlags.None,
                FunctionId = 0,
            });
        }

        return imports;
    }

    /// <summary>
    /// 把真实编译结果映射为模块模型。函数索引逐条来自
    /// <see cref="CompileResult.FunctionLayout"/>（汇编器发射时记录的真实 PC 范围）。
    /// </summary>
    private static STModModule BuildModule(
        CompileResult result, string moduleName, string[] sourceNames, string? entryOverride,
        IReadOnlyList<STModSymbol> imports)
    {
        var code = result.Module.Code;

        var functions = result.FunctionLayout
            .Select(e => new STModFunctionEntry
            {
                FunctionId = e.FunctionId,
                Flags =
                    (e.IsExported ? STModFunctionFlags.Exported : STModFunctionFlags.None)
                    | (e.IsDeclaration ? STModFunctionFlags.Declaration : STModFunctionFlags.None)
                    | (e.IsEntryPoint ? STModFunctionFlags.EntryPoint : STModFunctionFlags.None),
                Name = e.Name,
                Signature = e.Signature,
                ParamCount = (uint)Math.Max(0, e.ParamCount),
                LocalCount = (uint)Math.Max(0, e.LocalCount),
                MaxStack = 0,
                CodeOffset = (ulong)e.StartPC,
                CodeLength = (ulong)e.CodeLength,
            })
            .ToList();

        // 链接协议 §5：导出符号 = 有可执行实现且对外可见的函数。
        var exports = result.FunctionLayout
            .Where(e => e.IsExported && !e.IsDeclaration)
            .Select(e => new STModSymbol
            {
                Name = e.Name,
                Signature = e.Signature,
                Kind = STModSymbolKind.Function,
                Flags = STModSymbolFlags.Exported,
                FunctionId = e.FunctionId,
            })
            .ToList();

        // P1：无外部引用时 `mod build` 产生"完全链接"的模块。
        // P2 起按 --ref 声明保留外部引用并写出 ImportSymbols。
        bool linked = imports.Count == 0;

        var metadata = new Dictionary<string, STModMetadataEntry>(StringComparer.Ordinal)
        {
            [STModMetadataKeys.ModuleName] =
                STModMetadataEntry.FromString(STModMetadataKeys.ModuleName, moduleName),

            // 注意：这是**模块自身**的版本，与文件头里的二进制格式版本无关。
            [STModMetadataKeys.ModuleVersion] =
                STModMetadataEntry.FromString(STModMetadataKeys.ModuleVersion, "1.0.0"),

            [STModMetadataKeys.CompilerVersion] = STModMetadataEntry.FromString(
                STModMetadataKeys.CompilerVersion,
                typeof(ModCommand).Assembly.GetName().Version?.ToString() ?? "0.0.0"),

            [STModMetadataKeys.CompilerTarget] =
                STModMetadataEntry.FromString(STModMetadataKeys.CompilerTarget, "ironst-vm/1"),

            [STModMetadataKeys.ModuleDebug] =
                STModMetadataEntry.FromBoolean(STModMetadataKeys.ModuleDebug, false),

            [STModMetadataKeys.RequiredFeatures] = STModMetadataEntry.FromString(
                STModMetadataKeys.RequiredFeatures,
                string.Join(",",
                    result.Relocations.Count > 0
                        ? new[] { STModFeatures.Bytecode, STModFeatures.ConstantPool, STModFeatures.Relocations }
                        : new[] { STModFeatures.Bytecode, STModFeatures.ConstantPool })),

            [STModMetadataKeys.EntryPC] =
                STModMetadataEntry.FromUInt32(STModMetadataKeys.EntryPC, result.Module.EntryPC),

            // 链接协议 §3：链接状态与全局槽位数。
            [STModMetadataKeys.ModuleLinked] =
                STModMetadataEntry.FromBoolean(STModMetadataKeys.ModuleLinked, linked),

            [STModMetadataKeys.GlobalCount] =
                STModMetadataEntry.FromUInt32(
                    STModMetadataKeys.GlobalCount, (uint)Math.Max(0, result.Module.GlobalCount)),

            // 自定义 Key：演示"新增元数据不需要改文件头"。
            ["module.sources"] = STModMetadataEntry.FromString(
                "module.sources", string.Join(";", sourceNames)),
        };

        if (imports.Count > 0)
        {
            metadata[STModMetadataKeys.ModuleImports] = STModMetadataEntry.FromString(
                STModMetadataKeys.ModuleImports,
                string.Join(";", imports.Select(s => s.Name)));
        }

        if (exports.Count > 0)
        {
            metadata[STModMetadataKeys.ModuleExports] = STModMetadataEntry.FromString(
                STModMetadataKeys.ModuleExports,
                string.Join(";", exports.Select(s => s.Name)));
        }

        var entryName = result.FunctionLayout
            .FirstOrDefault(e => e.IsEntryPoint)?.Name
            ?? entryOverride;

        if (!string.IsNullOrEmpty(entryName))
        {
            metadata[STModMetadataKeys.EntryFunction] =
                STModMetadataEntry.FromString(STModMetadataKeys.EntryFunction, entryName);
        }

        return new STModModule
        {
            Metadata = metadata,
            Functions = functions,
            Code = code,
            ConstantPool = result.Module.ConstantPool,
            Sections = [],
            HasEntryPoint = code.Length > 0,
            EntryPC = result.Module.EntryPC,
            Relocations = result.Relocations,
            Imports = imports,
            Exports = exports,
            IsLinked = linked,
            GlobalCount = result.Module.GlobalCount,
        };
    }

    /// <summary>
    /// `mod build` 自己生成、不允许被 `--meta` 覆盖的元数据 key。
    /// </summary>
    private static readonly HashSet<string> ReservedMetadataKeys = new(StringComparer.Ordinal)
    {
        STModMetadataKeys.ModuleName,
        STModMetadataKeys.ModuleVersion,
        STModMetadataKeys.CompilerVersion,
        STModMetadataKeys.CompilerTarget,
        STModMetadataKeys.ModuleDebug,
        STModMetadataKeys.RequiredFeatures,
        STModMetadataKeys.EntryPC,
        STModMetadataKeys.EntryFunction,
        STModMetadataKeys.ModuleLinked,
        STModMetadataKeys.GlobalCount,
        STModMetadataKeys.ModuleImports,
        STModMetadataKeys.ModuleExports,
        "module.sources",
    };

    /// <summary>把 CLI 传入的自定义 key-value 合并进模块元数据。</summary>
    private static STModModule WithCustomMetadata(
        STModModule module,
        IReadOnlyDictionary<string, string> customMetadata)
    {
        var metadata = new Dictionary<string, STModMetadataEntry>(
            module.Metadata, StringComparer.Ordinal);

        foreach (var (key, value) in customMetadata)
            metadata[key] = STModMetadataEntry.FromString(key, value);

        return new STModModule
        {
            Metadata = metadata,
            Functions = module.Functions,
            Code = module.Code,
            ConstantPool = module.ConstantPool,
            Sections = module.Sections,
            HasEntryPoint = module.HasEntryPoint,
            EntryPC = module.EntryPC,
            Relocations = module.Relocations,
            Imports = module.Imports,
            Exports = module.Exports,
            IsLinked = module.IsLinked,
            GlobalCount = module.GlobalCount,
            FormatVersionMajor = module.FormatVersionMajor,
            FormatVersionMinor = module.FormatVersionMinor,
        };
    }

    // ══════════════════════════════════════════════════════════
    //  mod link
    // ══════════════════════════════════════════════════════════

    private static int Link(string[] args)
    {
        var inputs = new List<string>();
        string? entry = null;
        string? output = null;
        string? name = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("错误: 缺少 '-o' 的参数");
                        return 1;
                    }
                    output = args[++i];
                    if (output.Length == 0)
                    {
                        Console.Error.WriteLine("错误: -o 不能为空");
                        return 1;
                    }
                    break;

                case "--entry":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("错误: 缺少 '--entry' 的参数");
                        return 1;
                    }
                    entry = args[++i];
                    if (entry.Length == 0)
                    {
                        Console.Error.WriteLine("错误: --entry 不能为空");
                        return 1;
                    }
                    break;

                case "--name":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("错误: 缺少 '--name' 的参数");
                        return 1;
                    }
                    name = args[++i];
                    if (name.Length == 0)
                    {
                        Console.Error.WriteLine("错误: --name 不能为空");
                        return 1;
                    }
                    break;

                default:
                    if (args[i].StartsWith('-'))
                    {
                        Console.Error.WriteLine($"错误: 未知选项 '{args[i]}'");
                        return 1;
                    }
                    inputs.Add(args[i]);
                    break;
            }
        }

        if (inputs.Count == 0)
        {
            Console.Error.WriteLine(
                "错误: 缺少输入模块（用法: mod link <a.stmod> <b.stmod> ... [-o out.stmod]）");
            return 1;
        }

        foreach (var f in inputs)
        {
            if (!f.EndsWith(".stmod", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"错误: mod link 需要 .stmod 输入，实际为 '{f}'");
                return 1;
            }
            if (!File.Exists(f))
            {
                Console.Error.WriteLine($"错误: 找不到模块 '{f}'");
                return 1;
            }
        }

        var modules = new List<STModModule>(inputs.Count);
        foreach (var f in inputs)
        {
            try
            {
                modules.Add(STModReader.Load(f));
            }
            catch (STModFormatException ex)
            {
                Console.Error.WriteLine($"错误: 模块 '{f}' 无效: {ex.Message}");
                return 1;
            }
        }

        var options = new STModLinkOptions
        {
            EntryName = entry,
            ModuleName = name,
            HostFunctions = DefaultHostEnvironment.FunctionMap,
            SourceNames = inputs,
        };

        byte[] bytes;
        int[] linkBases;
        try
        {
            (bytes, linkBases) = STModLinker.LinkToBytesWithLayout(modules, options);
        }
        catch (STModLinkException ex)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");
            return 1;
        }
        catch (STModFormatException ex)
        {
            Console.Error.WriteLine($"错误: 链接产物格式校验失败: {ex.Message}");
            return 1;
        }

        var outPath = output ?? Path.Combine(Directory.GetCurrentDirectory(), "linked.stmod");

        try
        {
            // 原子写出：先写临时文件再替换，失败不留半成品。
            STModWriter.SaveBytesAtomic(outPath, bytes);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: 无法写出链接结果 '{outPath}': {ex.Message}");
            return 1;
        }

        // 旁车源映射：合并各输入模块的映射，按 codeBase 平移到产物 PC 空间
        // （链接只平移指令地址、不改指令边界，故映射可整体平移）。
        IronRuntime.SaveSourceMap(
            outPath,
            SourceResolver.Merge(
                inputs
                    .Select((path, i) => (i, path))
                    .Where(x => x.i < linkBases.Length)
                    .Select(x => (
                        linkBases[x.i],
                        (IReadOnlyDictionary<int, (string file, int line)>)
                            IronRuntime.LoadSourceMap(x.path)))));

        Console.WriteLine($"已写出模块: {outPath}");
        Console.WriteLine($"链接成功: {modules.Count} 个模块 → 1 个（{bytes.Length} 字节）");
        return 0;
    }

    // ══════════════════════════════════════════════════════════
    //  mod inspect
    // ══════════════════════════════════════════════════════════

    private static int Inspect(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("错误: 缺少模块文件（用法: mod inspect <file.stmod>）");
            return 1;
        }

        if (args.Length > 1)
        {
            Console.Error.WriteLine("错误: mod inspect 只接受一个 .stmod 文件");
            return 1;
        }

        var path = args[0];
        if (!path.EndsWith(".stmod", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"错误: mod inspect 需要 .stmod 文件，实际为 '{path}'");
            return 1;
        }

        STModModule module;
        try
        {
            module = STModReader.Load(path);
        }
        catch (STModFormatException ex)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");
            return 1;
        }

        Print(module, path);

        // 宿主兼容性：只读校验，不执行字节码。
        var problems = STModHostCompatibility.Check(module, DefaultHostEnvironment.FunctionMap);
        if (problems.Count > 0)
        {
            Console.Error.WriteLine("错误: 模块与当前宿主环境不兼容：");
            foreach (var problem in problems)
                Console.Error.WriteLine($"  - {problem}");
            return 1;
        }

        return 0;
    }

    private static void Print(STModModule module, string path)
    {
        Console.WriteLine($".stmod 模块: {path}");
        Console.WriteLine(
            $"格式版本: {module.FormatVersionMajor}.{module.FormatVersionMinor}"
            + $"   入口: {(module.HasEntryPoint ? $"PC {module.EntryPC}" : "(无)")}");
        Console.WriteLine($"元数据 ({module.Metadata.Count}):");

        foreach (var entry in module.Metadata.Values.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            string display = entry.ValueType switch
            {
                STModValueType.String => entry.AsString(),
                STModValueType.Boolean => entry.AsBoolean() ? "true" : "false",
                STModValueType.Int32 => entry.AsInt32().ToString(),
                STModValueType.Int64 => entry.AsInt64().ToString(),
                STModValueType.UInt32 => entry.AsUInt32().ToString(),
                STModValueType.UInt64 => entry.AsUInt64().ToString(),
                STModValueType.ByteArray => $"<{entry.Value.Length} 字节>",
                _ => $"<未知类型 {(byte)entry.ValueType}>",
            };
            Console.WriteLine($"  {entry.Key} = {display}    [{entry.ValueType}]");
        }

        Console.WriteLine($"区段 ({module.Sections.Count}):");
        foreach (var section in module.Sections)
            Console.WriteLine($"  {section}");

        // 链接协议 §7：链接状态与导入/导出符号。
        Console.WriteLine(
            $"链接状态: {(module.IsLinked ? "已链接" : "未链接（存在未解析的外部引用）")}"
            + $"   全局槽位: {module.GlobalCount}");
        Console.WriteLine($"导入符号 ({module.Imports.Count}):");
        foreach (var s in module.Imports)
            Console.WriteLine($"  {s.Name,-24} {s.Signature,-34} Kind={s.Kind} Flags={s.Flags}");
        Console.WriteLine($"导出符号 ({module.Exports.Count}):");
        foreach (var s in module.Exports)
            Console.WriteLine(
                $"  #{s.FunctionId,-6} {s.Name,-24} {s.Signature,-34} Flags={s.Flags}");

        Console.WriteLine($"函数 ({module.Functions.Count}):");
        Console.WriteLine(
            "  FunctionId  Name                 Signature"
            + "                          Flags                    CodeSection  CodeOffset  CodeLength  Params  Locals");

        foreach (var f in module.Functions)
        {
            Console.WriteLine(
                $"  {f.FunctionId,-10}  {f.Name,-20} {f.Signature,-34}"
                + $" {f.Flags,-24} {f.CodeSectionId,-11}"
                + $" 0x{f.CodeOffset:X8}  0x{f.CodeLength:X8}  {f.ParamCount,-6}  {f.LocalCount}");
        }

        if (module.Relocations is { } relocations)
        {
            var byKind = relocations.Records
                .GroupBy(r => r.Kind)
                .OrderBy(g => g.Key)
                .Select(g => $"{g.Key}={g.Count()}");
            Console.WriteLine($"重定位: {relocations.Count} 条（{string.Join(", ", byKind)}）");
        }
        else
        {
            Console.WriteLine("重定位: 0 条");
        }

        Console.WriteLine($"常量池: {module.ConstantPool.Count} 项");
        Console.WriteLine($"代码区段: {module.Code.Length} 字节");
    }
}
