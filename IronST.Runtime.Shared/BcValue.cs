// ═══════════════════════════════════════════════════════════════
//  BcValue.cs — 24 字节 Tagged Union 值类型
// ═══════════════════════════════════════════════════════════════
//
//  ◆ 内存布局 (24 bytes, 小端):
//
//    Byte  0     : ValueTag Tag          — 类型标签枚举 (1 byte)
//    Byte  1     : byte SubTag           — 子类型标签
//                   Tag=Struct → typeId (结构体类型 ID)
//                   其他 Tag   → 0 (保留)
//    Byte  2-7   : (padding)             — 对齐填充, 无语义
//    Byte  8-15  : union { long, double }
//                   Tag=Int64   → IntValue   (有符号 64 位整数)
//                   Tag=Float64 → FloatValue (IEEE 754 双精度浮点)
//                   Tag=Struct  → slotIndex  (StructHeap Arena 偏移)
//    Byte 16-23  : object? RefValue      — 堆引用 (8 bytes, GC 可追踪)
//                   Tag=String  → System.String 实例
//                   其他 Tag    → null
//                   (Struct 字段由 StructHeap Arena 管理, 不再存于 RefValue)
//
//  ◆ 设计决策:
//    offset 16 使用 object? 引用类型 (8 bytes on x64)。
//    .NET GC 从根集合扫描时会自动追踪 object? 引用，无需自定义 GC 代码。
//
//  ◆ 外部兼容性:
//    StringValue 属性保留为 string? 强转，所有原有读写 .StringValue 的代码零改动。
//
// ═══════════════════════════════════════════════════════════════
using System.Runtime.InteropServices;

namespace IronST.Runtime.Shared;

/// <summary>
/// 24 字节 tagged union，覆盖 Void / Int64 / Float64 / String / Struct。
/// 内存布局与 ISA 规范 §2.1 完全对齐。
///
/// ◆ Tag 枚举值一览:
///   0 = Void     — 空值, 默认状态
///   1 = Int64    — 64 位有符号整数, 存于 IntValue
///   2 = Float64  — IEEE 754 双精度浮点, 存于 FloatValue
///   3 = String   — 托管字符串引用, 存于 RefValue (向下兼容可读为 StringValue)
///   5 = Struct   — IEC 结构体/函数块实例, SubTag=typeId, IntValue=StructHeap slotIndex
///
/// ◆ Struct 语义:
///   一个 Struct 类型的 BcValue 不再持有字段数组引用。
///   SubTag 存储 typeId (编译期由 TypeMapper 分配),
///   IntValue 存储 StructHeap Arena 中的 slot 偏移,
///   RefValue 始终为 null。
///   字段读写通过 StructHeap.GetField / StructHeap.SetField 完成。
///
/// ◆ GC 行为:
///   BcValue 是值类型 (struct)，寄存器/栈/全局区存放的是值副本。
///   offset 16 的 object? RefValue 仅在 Tag=String 时非 null，
///   .NET GC 从根集合扫描时自动追踪。
///   Struct 字段存放在 StructHeap 的连续 Arena 中，
///   由 StructHeap 管理生命周期，不产生额外 GC 压力。
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
public struct BcValue
{
    // ── 核心字段 (与内存布局逐字节对齐) ─────────────────────

    /// <summary>
    /// [offset 0, 1 byte] 类型标签。
    /// 决定 union 区域 (offset 8-15) 和引用区域 (offset 16) 的解释方式。
    /// </summary>
    [FieldOffset(0)]
    public ValueTag Tag;

    /// <summary>
    /// [offset 1, 1 byte] 子类型标签。
    /// Tag=Struct 时存储 typeId (结构体类型 ID)。
    /// 其他 Tag 时固定为 0。
    /// </summary>
    [FieldOffset(1)]
    internal byte SubTag;

    /// <summary>
    /// [offset 8, 8 bytes] 64 位有符号整数。
    /// Tag=Int64 时存储数值。
    /// Tag=Struct 时存储 StructHeap slotIndex。
    /// </summary>
    [FieldOffset(8)]
    public long IntValue;

    /// <summary>
    /// [offset 8, 8 bytes] IEEE 754 双精度浮点数。
    /// 仅当 Tag == ValueTag.Float64 时有语义。
    /// 与 IntValue 共享同一 8 字节区域 (union)，互斥使用。
    /// </summary>
    [FieldOffset(8)]
    public double FloatValue;

    /// <summary>
    /// [offset 16, 8 bytes] 堆引用 (GC 可追踪)。
    ///
    /// 根据 Tag 的不同，指向不同的对象:
    ///
    ///   Tag = String  → 指向 System.String 实例
    ///   Tag = Void / Int64 / Float64 / Struct → null
    /// </summary>
    [FieldOffset(16)]
    public object? RefValue;

    // ── 便捷属性 (零分配, 纯强转) ──────────────────────────

    /// <summary>
    /// Tag = String 时的字符串值。
    /// 内部强转自 RefValue，不分配内存。
    ///
    /// ◆ 向后兼容:
    ///     原版本中 offset 16 直接是 string? StringValue 字段。
    ///     改为属性后，所有读写 .StringValue 的外部代码无需修改。
    /// </summary>
    public string? StringValue
    {
        get => RefValue as string;
        set => RefValue = value;
    }

    // ════════════════════════════════════════════════════════
    //  工厂方法
    // ════════════════════════════════════════════════════════

    /// <summary>
    /// Void 值 (零初始化, 所有字段为默认值)。
    /// 等价于 default(BcValue)，即 Tag=Void, IntValue=0, RefValue=null。
    /// </summary>
    public static readonly BcValue Void = default;

    /// <summary>
    /// 创建 Int64 值。
    /// </summary>
    /// <param name="v">64 位有符号整数</param>
    public BcValue(long v)
    {
        Tag = ValueTag.Int64;
        SubTag = 0;
        IntValue = v;
        RefValue = null;
    }

    /// <summary>
    /// 创建 Float64 值。
    /// </summary>
    /// <param name="v">IEEE 754 双精度浮点数</param>
    public BcValue(double v)
    {
        Tag = ValueTag.Float64;
        SubTag = 0;
        FloatValue = v;
        RefValue = null;
    }

    /// <summary>
    /// 创建 String 值。
    /// </summary>
    /// <param name="v">托管字符串引用，允许 null</param>
    public BcValue(string v)
    {
        Tag = ValueTag.String;
        SubTag = 0;
        IntValue = 0;
        RefValue = v;
    }

    /// <summary>
    /// 创建 Struct 值 (IEC 结构体 / 函数块实例)。
    /// SubTag = typeId, IntValue = StructHeap slotIndex, RefValue = null。
    ///
    /// ◆ 示例:
    ///   int slot = structHeap.Alloc(typeId);
    ///   BcValue fb = BcValue.MakeStruct(typeId, slot);
    ///   structHeap.SetField(slot, 0, new BcValue(5));  // field 0
    /// </summary>
    /// <param name="typeId">结构体类型 ID (编译期由 TypeMapper 分配)</param>
    /// <param name="slotIndex">StructHeap Arena 中的起始偏移</param>
    /// <returns>Tag=Struct 的新 BcValue</returns>
    internal static BcValue MakeStruct(byte typeId, int slotIndex)
    {
        var v = default(BcValue);
        v.Tag = ValueTag.Struct;
        v.SubTag = typeId;
        v.IntValue = slotIndex;
        v.RefValue = null;
        return v;
    }

    // ════════════════════════════════════════════════════════
    //  真值判断 (ISA §2.3)
    // ════════════════════════════════════════════════════════

    /// <summary>
    /// 判断此值是否为 "真值" (truthy)。
    ///
    /// ◆ ISA 规范判定规则:
    ///   Void       → false  (空值恒假)
    ///   Int64(0)   → false,  其余 → true
    ///   Float64(0) → false,  其余 → true
    ///   String     → null 或 "" → false,  非空 → true
    ///   Struct     → true   (结构体实例恒真)
    /// </summary>
    public bool IsTruthy =>
        Tag switch
        {
            ValueTag.Void => false,
            ValueTag.Int64 => IntValue != 0,
            ValueTag.Float64 => FloatValue != 0.0,
            ValueTag.String => !string.IsNullOrEmpty(StringValue),
            ValueTag.Struct => true,
            _ => false,
        };

    /// <summary>
    /// IsTruthy 的逻辑取反。
    /// </summary>
    public bool IsFalsy => !IsTruthy;

    // ════════════════════════════════════════════════════════
    //  类型强制转换 (ISA §2.5 TOINT / TOFLT / TOSTR / TOBOOL)
    // ════════════════════════════════════════════════════════

    /// <summary>
    /// 强制转换为 Int64 (对应 TOINT 指令)。
    ///
    /// ◆ 转换规则:
    ///   Void      → 0L
    ///   Int64     → 直接取 IntValue
    ///   Float64   → 截断为 long (丢弃小数部分, 不四舍五入)
    ///   String    → 解析十进制整数, 失败抛出 TypeError
    ///   Struct    → 抛出 TypeError (结构体无法转换为整数)
    /// </summary>
    /// <returns>转换后的 64 位有符号整数</returns>
    /// <exception cref="RuntimeErrorException">Tag 不支持转换时抛出 TypeError</exception>
    public long AsInt64() =>
        Tag switch
        {
            ValueTag.Void => 0L,
            ValueTag.Int64 => IntValue,
            ValueTag.Float64 => (long)FloatValue,
            ValueTag.String => long.TryParse(StringValue, out var v)
                ? v
                : throw new RuntimeErrorException(
                    RuntimeErrorType.TypeError,
                    0,
                    $"Cannot parse '{StringValue}' as Int64"
                ),
            _ => throw new RuntimeErrorException(
                RuntimeErrorType.TypeError,
                0,
                $"Cannot convert {Tag} to Int64"
            ),
        };

    /// <summary>
    /// 强制转换为 Float64 (对应 TOFLT 指令)。
    ///
    /// ◆ 转换规则:
    ///   Void      → 0.0
    ///   Int64     → 转 double (精确转换, long 的范围完全被 double 覆盖)
    ///   Float64   → 直接取 FloatValue
    ///   String    → 解析十进制浮点数, 失败抛出 TypeError
    ///   Struct    → 抛出 TypeError (结构体无法转换为浮点数)
    /// </summary>
    /// <returns>转换后的 IEEE 754 双精度浮点数</returns>
    /// <exception cref="RuntimeErrorException">Tag 不支持转换时抛出 TypeError</exception>
    public double AsFloat64() =>
        Tag switch
        {
            ValueTag.Void => 0.0,
            ValueTag.Int64 => IntValue,
            ValueTag.Float64 => FloatValue,
            ValueTag.String => double.TryParse(StringValue, out var v)
                ? v
                : throw new RuntimeErrorException(
                    RuntimeErrorType.TypeError,
                    0,
                    $"Cannot parse '{StringValue}' as Float64"
                ),
            _ => throw new RuntimeErrorException(
                RuntimeErrorType.TypeError,
                0,
                $"Cannot convert {Tag} to Float64"
            ),
        };

    // ════════════════════════════════════════════════════════
    //  泛型比较 (ISA §2.4 类型提升)
    // ════════════════════════════════════════════════════════

    /// <summary>
    /// 深度相等比较 (对应 EQ / NEQ / BEQ / BNE 指令)。
    ///
    /// ◆ 比较规则:
    ///   同类型:
    ///     Void == Void           → true
    ///     Int64 vs Int64         → 比较 IntValue
    ///     Float64 vs Float64     → 比较 FloatValue
    ///     String vs String       → 引用相等或值相等 (C# string 驻留机制)
    ///     Struct vs Struct       → 比较 SubTag + IntValue (typeId + slotIndex)
    ///
    ///   跨类型数值提升:
    ///     Int64 vs Float64       → 将 Int64 提升为 Float64 后比较
    ///     Float64 vs Int64       → 将 Int64 提升为 Float64 后比较
    ///
    ///   其他跨类型组合 → false (不抛异常)
    /// </summary>
    public static bool Eq(in BcValue a, in BcValue b)
    {
        // ── 同类型快速路径 ──
        if (a.Tag == b.Tag)
            return a.Tag switch
            {
                ValueTag.Void => true,
                ValueTag.Int64 => a.IntValue == b.IntValue,
                ValueTag.Float64 => a.FloatValue == b.FloatValue,
                ValueTag.String => a.StringValue == b.StringValue,
                ValueTag.Struct => a.SubTag == b.SubTag && a.IntValue == b.IntValue,
                _ => false,
            };

        // ── 跨类型数值提升 ──
        if (a.Tag == ValueTag.Int64 && b.Tag == ValueTag.Float64)
            return a.IntValue == b.FloatValue;
        if (a.Tag == ValueTag.Float64 && b.Tag == ValueTag.Int64)
            return a.FloatValue == b.IntValue;

        // ── 其他跨类型组合 → 不相等 ──
        return false;
    }

    /// <summary>
    /// 三路比较，返回负数/零/正数 (对应 CMP / BLT / BGE / BGT / BLE 指令)。
    ///
    /// ◆ 比较规则:
    ///   同类型:
    ///     Int64 vs Int64       → a.IntValue.CompareTo(b.IntValue)
    ///     Float64 vs Float64   → a.FloatValue.CompareTo(b.FloatValue)
    ///     String vs String     → 字典序 (Ordinal)
    ///
    ///   跨类型数值提升:
    ///     Int64 vs Float64     → 都转 double 后比较
    ///     Float64 vs Int64     → 都转 double 后比较
    ///
    ///   不可比较的组合 → 抛出 TypeError:
    ///     String vs 数值、Struct vs 任何、Void vs 非Void
    /// </summary>
    /// <returns>负数 (a < b)、零 (a == b)、正数 (a > b)</returns>
    /// <exception cref="RuntimeErrorException">类型组合不支持比较时抛出 TypeError</exception>
    public static int Compare(in BcValue a, in BcValue b)
    {
        // A10 层 2 防御：Void 处理（一侧或两侧为空值时按类型零值比较，不直接抛 TypeError）
        if (a.Tag == ValueTag.Void || b.Tag == ValueTag.Void)
        {
            if (a.Tag == ValueTag.Void && b.Tag == ValueTag.Void)
                return 0;
            if (a.Tag == ValueTag.String || b.Tag == ValueTag.String || a.Tag == ValueTag.Struct || b.Tag == ValueTag.Struct)
                throw new RuntimeErrorException(RuntimeErrorType.TypeError, 0, "Cannot compare " + a.Tag + " and " + b.Tag);
            double voidVal = 0.0;
            double otherVal = (a.Tag == ValueTag.Void)
                ? (b.Tag == ValueTag.Float64 ? b.FloatValue : (b.Tag == ValueTag.Int64 ? (double)b.IntValue : 0.0))
                : (a.Tag == ValueTag.Float64 ? a.FloatValue : (a.Tag == ValueTag.Int64 ? (double)a.IntValue : 0.0));
            return (a.Tag == ValueTag.Void ? voidVal : otherVal).CompareTo((a.Tag == ValueTag.Void ? otherVal : voidVal));
        }
        // ── 同类型: Int64 ──
        if (a.Tag == ValueTag.Int64 && b.Tag == ValueTag.Int64)
            return a.IntValue.CompareTo(b.IntValue);

        // ── 同类型: Float64 ──
        if (a.Tag == ValueTag.Float64 && b.Tag == ValueTag.Float64)
            return a.FloatValue.CompareTo(b.FloatValue);

        // ── 跨类型数值提升 (都转 double) ──
        if (
            (a.Tag == ValueTag.Int64 || a.Tag == ValueTag.Float64)
            && (b.Tag == ValueTag.Int64 || b.Tag == ValueTag.Float64)
        ) {
            double va = a.Tag == ValueTag.Float64 ? a.FloatValue : a.IntValue;
            double vb = b.Tag == ValueTag.Float64 ? b.FloatValue : b.IntValue;
            return va.CompareTo(vb);
        }

        // ── 同类型: String (字典序) ──
        if (a.Tag == ValueTag.String && b.Tag == ValueTag.String)
            return string.Compare(a.StringValue, b.StringValue, StringComparison.Ordinal);

        // ── 不可比较 → TypeError ──
        throw new RuntimeErrorException(
            RuntimeErrorType.TypeError,
            0,
            $"Cannot compare {a.Tag} and {b.Tag}"
        );
    }

    // ════════════════════════════════════════════════════════
    //  辅助方法
    // ════════════════════════════════════════════════════════

    /// <summary>
    /// 人类可读的字符串表示，用于调试输出等。
    ///
    /// ◆ 输出格式:
    ///   Void      → "nil"
    ///   Int64     → "42"
    ///   Float64   → "3.14159265"  (G 格式, 自动选择定点/科学计数法)
    ///   String    → 原始字符串内容 (null → "")
    ///   Struct    → "Struct(type=5,slot=0)" (括号内为 typeId 和 slotIndex)
    /// </summary>
    public override string ToString() =>
        Tag switch
        {
            ValueTag.Void => "nil",
            ValueTag.Int64 => IntValue.ToString(),
            ValueTag.Float64 => FloatValue.ToString("G"),
            ValueTag.String => StringValue ?? "",
            ValueTag.Struct => $"Struct(type={SubTag},slot={IntValue})",
            _ => $"Unknown({Tag})",
        };
}