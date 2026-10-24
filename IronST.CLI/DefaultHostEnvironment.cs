// DefaultHostEnvironment.cs — CLI 默认宿主环境。
//
// 为什么存在：ST 语言里唯一的输出手段是宿主函数 Print；文件/文本处理则依赖字符串
// 标准函数。CLI 的 run 若不带宿主环境，这些程序连前端都过不去（函数未声明），
// "运行结果"也就无从断言。
//
// 关键约束：编译期与运行期必须成对同源 ——
//   · Libraries   → 前端声明（语义分析认得 Print 与字符串标准函数）
//   · FunctionMap → ByteCodeGenerator 的 名→HCALL ID
//   · Register    → 运行期 HostBridge 注册同一 ID
// 三者缺一就会"编译通过、运行时 HCALL unregistered"。
//
// 边界：只接线**已有宿主实现**的函数。数学函数在 IR 层降级为 C extern 且无宿主实现
// （F13，未解），故这里只取 IEC 字符串子集 —— 见 IECStandardLibrary.CreateStringFunctions。
using IronST.Frontend;
using IronST.Frontend.Analysis;
using IronST.Runtime.Shared;

namespace IronST.CLI;

internal static class DefaultHostEnvironment
{
    /// <summary>Print 的 HCALL ID（与 <see cref="FunctionMap"/> / <see cref="Register"/> 同一常量）。</summary>
    private const ushort PrintId = HostCallIds.PRINT_STR;

    /// <summary>
    /// 前端声明：<c>Print</c> + IEC 字符串标准函数（LEN/LEFT/RIGHT/MID/CONCAT/
    /// INSERT/DELETE/REPLACE/FIND）。不含数学声明（F13 未解）。
    /// </summary>
    public static IReadOnlyList<ExternalLibrary> Libraries { get; } =
        [HostBuiltins.Create(), IECStandardLibrary.CreateStringFunctions()];

    /// <summary>传给 <c>ByteCodeGenerator</c> 的 函数名 → HCALL ID 映射。</summary>
    public static IReadOnlyDictionary<string, ushort> FunctionMap { get; } = BuildFunctionMap();

    /// <summary>运行期注册实现（ID 必须与 <see cref="FunctionMap"/> 一致）。</summary>
    public static void Register(HostBridge bridge)
    {
        bridge.Register(PrintId, ctx =>
        {
            // BcValue.ToString() 按 Tag 自适应格式化 Int64 / Float64 / String，
            // 故语义层无需区分 Print 的实参类型。
            Console.WriteLine(ctx.ReadReg(BytecodeBuilder.A0).ToString());
            return BcValue.Void;
        });

        // 同一份 ID 表对应同一份实现：字符串标准函数的 9 个 ID 由 StringStandardLibrary 注册。
        StringStandardLibrary.Register(bridge);

        // A4：时间类型时钟源可注入接口（检查表 A4 验收：时钟源可注入，便于测试与确定性）。
        // 默认实现：返回当前 UTC 毫秒时间戳（i64）；测试时可覆盖桥接以注入固定值。
        bridge.RegisterIfAbsent(HostCallIds.CLOCK, ctx =>
        {
            // 返回值：Int64 毫秒时间戳（与 TIME 类型映射为 i64 一致）。
            return new BcValue(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        });

        // A5：标准功能块简单行为注册（检查表 A5 验收：标准库形式提供，不要求完整 PLC 行为）。
        // 仅提供基本接口可运行（非完整工业控制行为），供编译期识别与基本边界测试使用。
        // 标准 FB 的完整状态行为（计时、计数、边沿状态机）由用户自建宿主实现覆盖，
        // 或随模块 B 链接协议（B1）与标准库路线图一并完善。
        bridge.RegisterIfAbsent(0x0100, ctx => BcValue.Void); // FB_TON 保留 ID（简单占位）
        bridge.RegisterIfAbsent(0x0101, ctx => BcValue.Void); // FB_TOF 保留 ID
        bridge.RegisterIfAbsent(0x0102, ctx => BcValue.Void); // FB_CTU 保留 ID
        bridge.RegisterIfAbsent(0x0103, ctx => BcValue.Void); // FB_CTD 保留 ID
        bridge.RegisterIfAbsent(0x0104, ctx => BcValue.Void); // FB_R_TRIG 保留 ID
        bridge.RegisterIfAbsent(0x0105, ctx => BcValue.Void); // FB_F_TRIG 保留 ID
        bridge.RegisterIfAbsent(0x0106, ctx => BcValue.Void); // FB_SR 保留 ID
        bridge.RegisterIfAbsent(0x0107, ctx => BcValue.Void); // FB_RS 保留 ID
    }

    /// <summary>
    /// Print 与字符串标准函数的 名→ID 表。字符串函数的 ID 取自
    /// <see cref="StringStandardLibrary.Functions"/>（运行期实现的唯一来源），
    /// 因此编译期映射与运行期注册天然一致。
    /// </summary>
    private static Dictionary<string, ushort> BuildFunctionMap()
    {
        var map = new Dictionary<string, ushort>(StringComparer.Ordinal) { ["Print"] = PrintId };
        foreach (var (name, id) in StringStandardLibrary.Functions)
            map[name] = id;
        return map;
    }
}
