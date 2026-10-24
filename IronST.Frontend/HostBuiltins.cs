namespace IronST.Frontend.Analysis;

/// <summary>
/// 宿主函数的**前端声明** —— 只管让语义分析、LSP 补全"认得"这些名字；
/// 真正的实现挂在运行时的 HostBridge 上（IDE 侧见 <c>Services/DebugHostBridge</c>）。
///
/// 与 <see cref="IECStandardLibrary"/> 的区别：
/// <list type="bullet">
///   <item>那边的函数会在 IR 层降级成 C extern 名（sqrt/sin/fabs…），
///         要求后端有对应实现；</item>
///   <item>这边是 HCALL，要求宿主按 ID 注册过处理函数。</item>
/// </list>
/// 两条链路互不相干，可以并存。
///
/// 刻意与 <see cref="IECStandardLibrary"/> 分成两张表：那张在字节码后端跑不通，
/// 调用方必须能只挑这一张传给 <c>FrontendPipeline.Analyze</c>。
/// </summary>
public static class HostBuiltins
{
    public static ExternalLibrary Create() => new()
    {
        Name = "IronST Host Builtins",
        Functions =
        [
            // 形参写成 STRING 只是为了有签名可查：语义分析器对外部函数只校验
            // 函数名是否已声明，不校验实参个数与类型，所以 Print(sum)、
            // Print(dist)、Print('x') 都能通过编译；实参由 BcValue 自适应格式化。
            new ExternalFunction
            {
                Name = "Print",
                ReturnTypeName = null,
                Parameters = [new ExternalParameter { Name = "value", TypeName = "STRING" }],
            },
        ],
    };
}
