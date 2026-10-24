// STModLoader.cs — `.stmod` → `BytecodeModule` 的**执行装载**（链接协议 §7）。
//
// 边界：
//   · 只做结构与入口校验 + 内存映射，**不改文件**、不重编码字节码。
//   · 未链接模块（存在未解析导入）在更上层被拒绝；本层只负责入口解析与装载。
//   · 无独立执行引擎：装载结果交给现有 VM 执行，绝不绕过 VM 安全检查。
using IronST.Runtime.Shared;

namespace IronST.Runtime.Shared.Modules;

/// <summary>入口解析 / 执行装载失败（可安全展示给用户）。</summary>
public sealed class STModEntryException : Exception
{
    public STModEntryException(string message) : base(message) { }
}

/// <summary>已装载为可执行的模块。</summary>
public sealed class LoadedSTMod
{
    public required BytecodeModule Module { get; init; }

    /// <summary>
    /// 入口函数的 LR（返回地址）。<c>null</c> 表示从模块原有入口桩开始执行。
    /// </summary>
    public uint? EntryReturnPC { get; init; }

    /// <summary>实际选中的入口函数；默认入口桩时为 <c>null</c>。</summary>
    public STModFunctionEntry? EntryFunction { get; init; }
}

/// <summary>把 `.stmod` 装载为可直接交给现有 VM 执行的 <see cref="BytecodeModule"/>。</summary>
public static class STModLoader
{
    /// <summary>
    /// 停机桩：<c>SYSCALL EXIT</c>（opcode 0x03 + func 0x00）。追加在内存副本代码尾部。
    /// </summary>
    private static readonly byte[] ExitThunk =
        [(byte)OpCode.SYSCALL, (byte)SyscallFunc.EXIT];

    /// <summary>
    /// 装载模块。<paramref name="entryName"/> 为 <c>null</c> 时使用模块头部入口桩；
    /// 否则按**函数名**解析入口（唯一匹配、必须有可执行实现）。
    /// </summary>
    public static LoadedSTMod Load(STModModule module, string? entryName = null)
    {
        if (entryName is null)
        {
            if (!module.HasEntryPoint)
            {
                throw new STModEntryException(
                    ".stmod 未声明入口点（缺少 HasEntryPoint / module.entryPC），"
                    + "请用 --entry 指定入口函数。");
            }

            var bm = BytecodeModule.FromBuilder(
                module.EntryPC,
                module.Code,
                ToArray(module.ConstantPool),
                module.GlobalCount,
                debugInfo: null);
            return new LoadedSTMod { Module = bm, EntryReturnPC = null, EntryFunction = null };
        }

        var entry = ResolveEntry(module, entryName);

        // 只改**内存副本**：追加 SYSCALL EXIT 桩，把被调函数的 RET 引到这里。
        byte[] code = new byte[module.Code.Length + ExitThunk.Length];
        Array.Copy(module.Code, code, module.Code.Length);
        Array.Copy(ExitThunk, 0, code, module.Code.Length, ExitThunk.Length);

        var loaded = BytecodeModule.FromBuilder(
            (uint)entry.CodeOffset,
            code,
            ToArray(module.ConstantPool),
            module.GlobalCount,
            debugInfo: null);

        return new LoadedSTMod
        {
            Module = loaded,
            EntryReturnPC = (uint)module.Code.Length,
            EntryFunction = entry,
        };
    }

    /// <summary>
    /// 按短名解析入口函数（链接协议 §7.2）。
    /// </summary>
    /// <remarks>
    /// 规则：0 个匹配 → 报错并列出可用入口；多个匹配 → 报错并列出全部候选（**绝不**默认取第一个）；
    /// 唯一匹配但为声明（宿主 / 外部）→ 报错"不可执行"。
    /// </remarks>
    public static STModFunctionEntry ResolveEntry(STModModule module, string entryName)
    {
        var matches = module.Functions
            .Where(f => string.Equals(f.Name, entryName, StringComparison.Ordinal))
            .ToList();

        if (matches.Count == 0)
        {
            var available = module.Functions
                .Where(f => !f.IsDeclaration)
                .Select(f => f.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            throw new STModEntryException(
                $".stmod 找不到入口函数 '{entryName}'。"
                + (available.Count > 0
                    ? $"可用入口：{string.Join("、", available)}。"
                    : "模块中没有可执行的函数（全部是声明）。"));
        }

        if (matches.Count > 1)
        {
            string candidates = string.Join("、", matches.Select(
                f => $"#{f.FunctionId} '{f.Name}' ({f.Signature})"));
            throw new STModEntryException(
                $".stmod 入口函数名 '{entryName}' 有歧义（{matches.Count} 个匹配）："
                + $"{candidates}。请用更完整的函数名消歧。");
        }

        var fn = matches[0];
        if (fn.IsDeclaration)
        {
            throw new STModEntryException(
                $".stmod 函数 '{entryName}' 只有声明、没有可执行实现（Declaration），"
                + "不能作为入口。");
        }

        return fn;
    }

    private static BcValue[] ToArray(IReadOnlyList<BcValue> pool)
    {
        var array = new BcValue[pool.Count];
        for (int i = 0; i < array.Length; i++)
            array[i] = pool[i];
        return array;
    }
}
