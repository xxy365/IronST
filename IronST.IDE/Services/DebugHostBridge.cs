// Services/DebugHostBridge.cs — 让 ST 程序能把结果打到输出面板
//
// 存在的理由：ST 程序算出的值原本无处可看 —— 语言里没有 PRINT，监视窗口
// 又是空壳，跑完只有一句"程序正常停机"。这里注册一个 Print() 打通这条链。
//
// 分工：Print 的**签名**在前端（IronST.Frontend/HostBuiltins.cs），供语义分析、
// 波浪线与 LSP 补全认得这个名字；本类只负责**实现**——HCALL 的 ID 映射与处理函数。
using IronST.Runtime.Shared;

namespace IronST.IDE.Services;

/// <summary>
/// 调试 / 无调试运行时共用的宿主桥。
///
/// 参数约定（见 <c>FunctionEmitter.EmitCall</c> 与 <c>HostCallIds</c>）：
/// 调用的前 4 个实参落在寄存器 A0–A3（即 R[4]–R[7]），返回值写回 A0。
/// </summary>
public sealed class DebugHostBridge
{
    private readonly ViewModels.OutputViewModel _output;
    private readonly Action<Action> _onUi;

    /// <param name="onUi">把写入动作切回 UI 线程。VM 跑在后台线程，
    /// 而 ObservableCollection 只能在 UI 线程上改。</param>
    public DebugHostBridge(ViewModels.OutputViewModel output, Action<Action> onUi)
    {
        _output = output;
        _onUi = onUi;
    }

    /// <summary>
    /// ST 函数名 → HCALL ID。必须与 <see cref="Create"/> 里注册的 ID 对上，
    /// 否则后端会抛 "External function 'X' has no host handler"。
    ///
    /// 函数名须与前端 <c>HostBuiltins</c> 里声明的一致，否则要么前端报"未声明"、
    /// 要么后端报 "no host handler"。
    /// </summary>
    public static IReadOnlyDictionary<string, ushort> HostFunctions { get; } =
        new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["Print"] = HostCallIds.PRINT_STR,
        };

    public HostBridge Create()
    {
        var bridge = new HostBridge();

        // PRINT_STR 是 Print 的实际入口。另外两个 ID 一并注册，
        // 免得字节码里若出现它们会以 "HCALL unregistered" 的形式炸在运行时。
        bridge.Register(HostCallIds.PRINT_STR, ctx => Write(ctx.ReadReg(4)));
        bridge.Register(HostCallIds.PRINT_INT, ctx => Write(ctx.ReadReg(4)));
        bridge.Register(HostCallIds.PRINT_CHAR, ctx => WriteChar(ctx.ReadReg(4)));

        return bridge;
    }

    // BcValue.ToString() 按 Tag 自适应格式化 Int64 / Float64 / String，
    // 所以无需在语义层区分 Print 的实参类型。
    private BcValue Write(BcValue value)
    {
        var text = value.ToString();
        _onUi(() => _output.Info(text));
        return BcValue.Void;
    }

    private BcValue WriteChar(BcValue value)
    {
        var text = ((char)value.AsInt64()).ToString();
        _onUi(() => _output.Info(text));
        return BcValue.Void;
    }
}
