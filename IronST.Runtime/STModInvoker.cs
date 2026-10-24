using IronST.Runtime.Shared;
using IronST.Runtime.Shared.Modules;

namespace IronST.Runtime;

/// <summary>一次按函数名调用 `.stmod` 函数的结果。</summary>
public sealed class STModInvocationResult
{
    /// <summary>被调用的函数索引项（名字、签名、参数个数等）。</summary>
    public required STModFunctionEntry Function { get; init; }

    /// <summary>执行该函数的 VM 实例，便于读取寄存器、全局和错误。</summary>
    public required VM VM { get; init; }

    /// <summary>调用约定：返回值在 A0。</summary>
    public BcValue ReturnValue => VM.Context.ReadReg(BytecodeBuilder.A0);

    public VMState State => VM.State;

    /// <summary>运行期错误；未捕获运行期错误时为 <c>null</c>。</summary>
    public RuntimeErrorException? Error => VM.LastError;
}

/// <summary><see cref="STModInvoker"/> 的调用选项。</summary>
public sealed class STModInvokeOptions
{
    /// <summary>宿主函数桥；调用不依赖宿主函数时可省略。</summary>
    public HostBridge? HostBridge { get; init; }

    public int StackSize { get; init; } = 65536;

    /// <summary>结构体类型注册表；来自 <c>CompileResult.StructRegistrations</c>。</summary>
    public IReadOnlyList<(byte TypeId, int FieldCount)>? StructTypes { get; init; }

    /// <summary>PC 到源码位置的解析器，用于运行期错误定位。</summary>
    public Func<uint, (string file, int line)?>? SourceLocator { get; init; }

    /// <summary>
    /// 为 <c>true</c>（默认）时捕获 <see cref="RuntimeErrorException"/> 并放入
    /// <see cref="STModInvocationResult.Error"/>；为 <c>false</c> 时向调用方抛出。
    /// </summary>
    public bool CaptureRuntimeErrors { get; init; } = true;
}

/// <summary>
/// C# 侧按函数名调用 `.stmod` 函数的高层辅助。
/// </summary>
/// <remarks>
/// 底层复用 <see cref="STModLoader"/>：按名定位函数、在内存代码副本尾部追加
/// <c>SYSCALL EXIT</c>，并把入口返回地址交给 VM。参数仍遵循现有 ABI：
/// 前 4 个标量写 A0..A3，第 5 个起压栈。
/// </remarks>
public static class STModInvoker
{
    /// <summary>
    /// 按函数名调用模块函数，参数使用 <see cref="BcValue"/>。
    /// </summary>
    /// <example>
    /// <code>
    /// var result = STModInvoker.Invoke(module, "Add", new BcValue(2L), new BcValue(3L));
    /// long value = result.ReturnValue.IntValue;
    /// </code>
    /// </example>
    public static STModInvocationResult Invoke(
        STModModule module,
        string functionName,
        params BcValue[] args)
        => Invoke(module, functionName, args, options: null);

    /// <summary>
    /// 按函数名调用模块函数，支持传入参数列表和调用选项。
    /// </summary>
    public static STModInvocationResult Invoke(
        STModModule module,
        string functionName,
        IReadOnlyList<BcValue> args,
        STModInvokeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(functionName);
        ArgumentNullException.ThrowIfNull(args);

        // 先解析一次：既做存在/歧义/声明检查，也拿到参数个数。
        STModFunctionEntry entry = STModLoader.ResolveEntry(module, functionName);

        if (args.Count != entry.ParamCount) {
            throw new ArgumentException(
                $"函数 '{entry.Name}' 需要 {entry.ParamCount} 个参数，实际传入 {args.Count} 个",
                nameof(args));
        }

        LoadedSTMod loaded = STModLoader.Load(module, functionName);

        var vm = new VM(loaded.Module, new VMOptions
        {
            StackSize = options?.StackSize ?? 65536,
            HostBridge = options?.HostBridge,
            StructTypes = options?.StructTypes,
            SourceLocator = options?.SourceLocator,
            EntryReturnPC = loaded.EntryReturnPC,
        });

        // 前 4 个标量参数走寄存器。
        int registerArgs = Math.Min(args.Count, 4);
        for (int i = 0; i < registerArgs; i++)
            vm.Context.WriteReg((byte)(BytecodeBuilder.A0 + i), args[i]);

        // 第 5 个起压栈；压栈顺序与 ST 调用方一致（最后一个参数先压）。
        int stackArgs = args.Count - 4;
        if (stackArgs > 0) {
            long sp = vm.Context.ReadReg(BytecodeBuilder.SP).IntValue;
            if (sp - stackArgs < 0)
                throw new InvalidOperationException(
                    $"参数过多：需要 {stackArgs} 个栈槽，但当前 SP={sp}");

            for (int i = args.Count - 1; i >= 4; i--)
                vm.Context.WriteStack((int)--sp, args[i]);

            vm.Context.WriteReg(BytecodeBuilder.SP, new BcValue(sp));
        }

        if (options?.CaptureRuntimeErrors ?? true) {
            try {
                vm.Run();
            }
            catch (RuntimeErrorException) {
                // 错误已记录在 VM.LastError，结果对象继续可用。
            }
        }
        else {
            vm.Run();
        }

        return new STModInvocationResult { Function = entry, VM = vm };
    }
}
