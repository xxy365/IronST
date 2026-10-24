using IronST.Runtime.Shared;

namespace IronST.Runtime;

/// <summary>
/// 发布模式虚拟机。不包含任何调试功能，性能最优。
/// </summary>
public sealed class VM : VMBase
{
    public VM(BytecodeModule module, VMOptions? options = null)
        : base(module, options ?? new VMOptions(), hooks: null) { }
}
