// ═══════════════════════════════════════════════════════════════
//  DebugVM.cs — 开发模式虚拟机
//
//  VMDebugSession 自身实现 IVMDebugHooks，直接注入。
//  断点/单步/跟踪/反汇编全部在 VMDebugSession 内部完成。
// ═══════════════════════════════════════════════════════════════
using IronST.Runtime.Shared;

namespace IronST.Runtime.Debug;

/// <summary>
/// 开发模式虚拟机。VMDebugSession 直接作为 hooks 注入。
/// </summary>
public sealed class DebugVM : VMBase
{
    private readonly VMDebugSession _session;

    public DebugVM(BytecodeModule module, VMDevOptions options)
        : base(module, options, hooks: options.DebugSession)  // ← session 自身就是 IVMDebugHooks
    {
        _session = options.DebugSession;
        _session.Attach(this);
    }

    /// <summary>调试会话 (只读)。</summary>
    public VMDebugSession DebugSession => _session;
}
