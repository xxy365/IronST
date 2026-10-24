// ═══════════════════════════════════════════════════════════════
//  RelocationTable.cs — 重定位记录表（阶段 0B 步骤 3 · 切片 1）
//
//  ◆ 职责：保存"要按哪个符号、改写段内哪个偏移"的记录，
//    并提供独立于 Build()/SaveTo()/BuildModule() 的序列化与读取。
//
//  ◆ 为什么不并入 .isb 头部：
//    头部版本字段的<b>字节布局尚未冻结</b>（§7.3），且已冻结
//    "兼容策略确定前不得改动序列化头、不得借用 flags"。
//    因此本表先用<b>自有</b>的独立小格式，与模块头解耦。
//
//  ◆ 符号名比较：本表用 Ordinal。Runtime.Shared 保持零依赖，
//    统一符号键（SymbolKey）由链接器在合并期施加（§9 推迟裁定）。
// ═══════════════════════════════════════════════════════════════
using System.Text;

namespace IronST.Runtime.Shared;

public sealed class RelocationTable
{
    /// <summary>"IRLR"</summary>
    private static readonly byte[] Magic = [(byte)'I', (byte)'R', (byte)'L', (byte)'R'];

    private const byte FormatVersion = 1;

    /// <summary>魔数 4 + 版本 1 + 保留 1 + 保留 2 + 记录数 4</summary>
    private const int HeaderSize = 12;

    private readonly List<RelocationRecord> _records = new();

    /// <summary>全部记录，按添加顺序。</summary>
    public IReadOnlyList<RelocationRecord> Records => _records;

    public int Count => _records.Count;

    /// <summary>
    /// 追加一条记录。<b>会重新校验</b>，保证绕过 <see cref="RelocationRecord.Create"/>
    /// 直接构造的记录也无法把越界数据塞进表里。
    /// </summary>
    public void Add(RelocationRecord record)
    {
        record.Validate();
        _records.Add(record);
    }

    /// <summary>
    /// 替换第 <paramref name="index"/> 条记录的 <c>Addend</c>。
    /// </summary>
    /// <remarks>
    /// <b>用途</b>：<c>Addr32</c> 的符号可能是<b>函数内标签</b>（绝对 <c>JMP</c> 目标）。
    /// 汇编期解析出该标签的模块局部 PC 后，把它写进 <c>Addend</c>；
    /// 链接期据此算出 <c>模块代码基址 + Addend</c>。<b>不新增记录字段</b>，
    /// 因此不改变 `RelocationTable` 的字节格式（<c>Addend</c> 本就在序列化布局中）。
    /// </remarks>
    public void SetAddend(int index, int addend)
    {
        if ((uint)index >= (uint)_records.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        var updated = _records[index] with { Addend = addend };
        updated.Validate();
        _records[index] = updated;
    }

    /// <summary>
    /// 按符号名查找引用该符号的记录。
    /// </summary>
    /// <remarks>
    /// 使用 <c>Ordinal</c> 比较（原始名）。跨模块合并时的大小写规则由链接器施加 ——
    /// 本表刻意不依赖 <c>IronST.Common</c>。
    /// </remarks>
    public IEnumerable<RelocationRecord> FindBySymbol(string symbolName)
        => _records.Where(r => string.Equals(r.SymbolName, symbolName, StringComparison.Ordinal));

    /// <summary>序列化为独立字节流（与模块头解耦，见文件头注释）。</summary>
    public byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // ── 头部 ──
        bw.Write(Magic);            // 0..3
        bw.Write(FormatVersion);    // 4
        bw.Write((byte)0);          // 5  reserved
        bw.Write((ushort)0);        // 6..7  reserved
        bw.Write(_records.Count);   // 8..11

        // ── 记录 ──
        foreach (var r in _records)
        {
            bw.Write((byte)r.Kind);
            bw.Write((byte)r.Section);
            bw.Write(r.Offset);
            bw.Write(r.Addend);
            bw.Write((byte)r.EncodingWidthBits);
            WriteString(bw, r.SymbolName);
            WriteString(bw, r.Source.File);
            bw.Write(r.Source.Line);
            bw.Write(r.Source.Column);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// 读取字节流。损坏 / 截断 / 版本不符一律抛 <see cref="InvalidDataException"/>，
    /// <b>不会</b>绕过边界校验产出半合法的记录。
    /// </summary>
    public static RelocationTable Load(byte[] data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));

        if (data.Length < HeaderSize)
            throw new InvalidDataException(
                $"Relocation table truncated: {data.Length} bytes, header needs {HeaderSize}");

        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);

        var magic = br.ReadBytes(Magic.Length);
        if (!magic.SequenceEqual(Magic))
            throw new InvalidDataException(
                $"Bad relocation table magic: 0x{Convert.ToHexString(magic)}");

        byte version = br.ReadByte();
        if (version != FormatVersion)
            throw new InvalidDataException(
                $"Unsupported relocation table version {version}, expected {FormatVersion}");

        br.ReadByte();      // reserved
        br.ReadUInt16();    // reserved

        int count = br.ReadInt32();
        if (count < 0)
            throw new InvalidDataException($"Relocation table count is negative: {count}");

        var table = new RelocationTable();

        try
        {
            for (int i = 0; i < count; i++)
            {
                var kind = (RelocationKind)br.ReadByte();
                var section = (RelocationSection)br.ReadByte();
                int offset = br.ReadInt32();
                int addend = br.ReadInt32();
                int width = br.ReadByte();
                string? symbol = ReadString(br, "symbol name");
                string? file = ReadString(br, "source file");
                int line = br.ReadInt32();
                int column = br.ReadInt32();

                // 逐条重新校验：损坏数据不得绕过边界检查
                table.Add(new RelocationRecord(
                    kind, section, offset, symbol,
                    new RelocationSourceLocation(file, line, column),
                    width, addend));
            }
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException(
                $"Relocation table truncated after {table.Count} of {count} records", ex);
        }

        if (ms.Position != ms.Length)
            throw new InvalidDataException(
                $"Relocation table has {ms.Length - ms.Position} trailing byte(s) "
                + $"after {count} record(s)");

        return table;
    }

    private static void WriteString(BinaryWriter bw, string? value)
    {
        if (value is null)
        {
            bw.Write(-1);
            return;
        }

        var utf8 = Encoding.UTF8.GetBytes(value);
        bw.Write(utf8.Length);
        bw.Write(utf8);
    }

    private static string? ReadString(BinaryReader br, string field)
    {
        int len = br.ReadInt32();
        if (len == -1) return null;

        if (len < -1)
            throw new InvalidDataException(
                $"Relocation table {field} length is negative: {len}");

        var bytes = br.ReadBytes(len);
        if (bytes.Length != len)
            throw new InvalidDataException(
                $"Relocation table truncated while reading {field}");

        return Encoding.UTF8.GetString(bytes);
    }
}
