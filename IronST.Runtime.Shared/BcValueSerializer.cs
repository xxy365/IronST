// BcValueSerializer.cs — BcValue 的二进制编码（`.isb` 与 `.stmod` 共用同一份实现）。
//
// 为什么独立出来：常量池编码此前在 BytecodeFileHelper（写）与 BytecodeModule（读）
// 各有一份。`.stmod` 需要同一编码，若无单一事实来源就会变成第三份 —— 任何一侧
// 改动都会让两种容器悄悄不兼容。本类型的字节输出与迁移前**逐字节一致**。
using System.Text;

namespace IronST.Runtime.Shared;

/// <summary>
/// 统一常量池值（<see cref="BcValue"/>）的定长头 + 变长负载编码。
/// </summary>
/// <remarks>
/// 单值布局（小端）：
/// <code>
///   byte  tag            // ValueTag
///   byte  padding[7]     // 保留，写 0
///   ...   payload        // Int64: 8B; Float64: 8B; String: int32 字节数 + UTF-8 + 8 字节对齐填充
/// </code>
/// </remarks>
public static class BcValueSerializer
{
    /// <summary>单个字符串常量允许的最大 UTF-8 字节数（既有 .isb 读取限制）。</summary>
    public const int MaxSingleStringLen = 64 * 1024;

    /// <summary>写出一个值。</summary>
    public static void Write(BinaryWriter bw, BcValue val)
    {
        bw.Write((byte)val.Tag);
        bw.Write(new byte[7]);

        switch (val.Tag)
        {
            case ValueTag.Int64:
                bw.Write(val.IntValue);
                break;

            case ValueTag.Float64:
                bw.Write(val.FloatValue);
                break;

            case ValueTag.String:
            {
                byte[] bytes = Encoding.UTF8.GetBytes(val.StringValue ?? "");
                bw.Write(bytes.Length);
                bw.Write(bytes);

                int remainder = bytes.Length % 8;
                if (remainder > 0)
                    bw.Write(new byte[8 - remainder]);
                break;
            }

            default:
                bw.Write(0L);
                break;
        }
    }

    /// <summary>读出一个值；长度越界或 tag 未知时抛 <see cref="InvalidDataException"/>。</summary>
    /// <param name="index">仅用于错误信息（常量池下标）。</param>
    public static BcValue Read(BinaryReader br, int index)
    {
        byte tag = br.ReadByte();
        br.ReadBytes(7);

        switch ((ValueTag)tag)
        {
            case ValueTag.Int64:
                return new BcValue(br.ReadInt64());

            case ValueTag.Float64:
                return new BcValue(br.ReadDouble());

            case ValueTag.String:
            {
                int byteLen = br.ReadInt32();
                if (byteLen < 0 || byteLen > MaxSingleStringLen)
                    throw new InvalidDataException(
                        $"ConstantPool string {index} length {byteLen} exceeds limit {MaxSingleStringLen}");

                byte[] utf8 = br.ReadBytes(byteLen);
                var value = new BcValue(Encoding.UTF8.GetString(utf8));

                int padding = (8 - (byteLen % 8)) % 8;
                if (padding > 0)
                    br.ReadBytes(padding);

                return value;
            }

            default:
                throw new InvalidDataException($"ConstantPool[{index}]: unknown tag {tag}");
        }
    }
}
