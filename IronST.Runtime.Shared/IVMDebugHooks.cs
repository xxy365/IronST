// ═══════════════════════════════════════════════════════════════
//  IVMDebugHooks.cs — 调试钩子接口
//
//  发布模式: 不实现此接口，hooks 为 null → JIT 消除全部分支
//  开发模式: 实现此接口，注入完整调试逻辑
// ═══════════════════════════════════════════════════════════════
namespace IronST.Runtime.Shared;

/// <summary>
/// 虚拟机调试钩子。
/// VMBase 在执行循环中以 null-check 方式调用，发布模式无开销。
/// </summary>
public interface IVMDebugHooks
{
    /// <summary>
    /// 每条指令执行前调用。
    /// </summary>
    /// <param name="vm">VM 实例 (可调用 Pause、访问 Code/Context)</param>
    /// <param name="pc">当前程序计数器</param>
    /// <returns>true = 继续执行，false = 暂停 (断点/单步)</returns>
    bool PreStep(VMBase vm, uint pc);

    /// <summary>BRK 指令命中时调用。</summary>
    void OnBreak(VMBase vm, uint pc);

    /// <summary>运行时错误发生时调用。</summary>
    void OnError(RuntimeErrorException ex);
}
