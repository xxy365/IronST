// ═══════════════════════════════════════════════════════════════
//  VMDevOptions.cs — 开发模式配置
// ═══════════════════════════════════════════════════════════════
using IronST.Runtime.Shared;

namespace IronST.Runtime.Debug;

public sealed class VMDevOptions : VMOptions
{
    /// <summary>调试会话。必填。</summary>
    public required VMDebugSession DebugSession { get; init; }
}
