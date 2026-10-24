// STModFormat.cs — `.stmod` 模块格式的常量、枚举与内存模型。
//
// 规范正文见 docs/stmod格式规范.md。本文件只定义"是什么"，不负责读写。
//
// 与 .isb 的关系：
//   · .isb  —— 最终可执行字节码（魔数 0x49525354，无区段表），由 VM 直接执行。
//   · .stmod —— 编译器读取/链接的**模块输入**（魔数 "STMD"，带区段表与元数据），
//              默认不允许 `ironst run` 直接执行。
// 两者**不复用文件头**，也互不伪装：扩展名与魔数都不同，读取器会拒绝对方的文件。
using System.Buffers.Binary;
using System.Text;

namespace IronST.Runtime.Shared.Modules;

/// <summary>`.stmod` 格式的结构性错误（非法/截断/越界/缺少必需区段等）。</summary>
/// <remarks>
/// 不继承 <see cref="InvalidDataException"/>：该类型在 .NET 中已 sealed。
/// 调用方按 <see cref="Exception"/> 处理即可；<see cref="STModReader"/> 内部
/// 抛出的其它 <see cref="InvalidDataException"/>（如常量池解码）都会被包装成本类型。
/// </remarks>
public sealed class STModFormatException : Exception
{
    public STModFormatException(string message) : base(message) { }
}

/// <summary>格式级常量。所有整数字段一律 <b>Little Endian</b>。</summary>
public static class STModFormat
{
    /// <summary>魔数 ASCII <c>"STMD"</c>（字节 0x53 0x54 0x4D 0x44）。</summary>
    public const uint Magic = 0x444D5453;   // "STMD" 的小端 uint32 读法

    /// <summary>魔数原始字节（用于逐字节比较与写出）。</summary>
    public static ReadOnlySpan<byte> MagicBytes => "STMD"u8;

    /// <summary>当前写入的主版本。读取器只接受此主版本。</summary>
    public const ushort FormatVersionMajor = 1;

    /// <summary>当前写入的次版本。读取器接受同主版本的任意次版本（向前兼容）。</summary>
    public const ushort FormatVersionMinor = 0;

    /// <summary>固定文件头长度（字节）。</summary>
    public const uint HeaderSize = 48;

    /// <summary>区段表条目长度（字节）。</summary>
    public const uint SectionEntrySize = 32;

    /// <summary>元数据记录头长度（字节）。</summary>
    public const uint MetadataRecordHeaderSize = 12;

    /// <summary>函数索引条目定长头长度（字节），其后紧跟 name 与 signature 的 UTF-8 字节。</summary>
    public const uint FunctionEntryHeaderSize = 48;

    /// <summary>区段条数上限（防御异常分配）。</summary>
    public const uint MaxSectionCount = 4096;

    /// <summary>元数据 Key 的 UTF-8 字节上限。</summary>
    public const uint MaxKeyBytes = 1024;

    /// <summary>元数据记录的条数上限（防御异常分配）。</summary>
    public const uint MaxMetadataRecords = 65536;

    /// <summary>函数索引的条目上限（防御异常分配）。</summary>
    public const uint MaxFunctionEntries = 1_000_000;

    /// <summary>名称/签名/Key 字符串的 UTF-8 字节上限。</summary>
    public const uint MaxStringBytes = 1024 * 1024;

    /// <summary>各区段的文件内对齐（2 的幂）。</summary>
    public const uint SectionAlignment = 8;
}

/// <summary>区段类型。1..8 已明确预留；0x8000–0xFFFF 留给私有扩展。</summary>
public enum STModSectionType : ushort
{
    /// <summary>元数据（Key-Value）。必需。</summary>
    Metadata = 1,

    /// <summary>函数索引（真实代码范围）。必需。</summary>
    FunctionIndex = 2,

    /// <summary>字节码。必需。</summary>
    Code = 3,

    /// <summary>统一常量池（BcValue 编码，与 .isb 相同）。必需。</summary>
    ConstantPool = 4,

    /// <summary>导入符号。v1 由链接协议（docs/stmod链接协议.md §4）定义；未解析导入 ⇒ 模块不可直接执行。</summary>
    ImportSymbols = 5,

    /// <summary>导出符号（链接协议 §5）。</summary>
    ExportSymbols = 6,

    /// <summary>重定位记录（RelocationTable 既有编码）。可选。</summary>
    Relocations = 7,

    /// <summary>调试信息（信息性，v1 可忽略）。</summary>
    DebugInfo = 8,
}

/// <summary>区段标志。</summary>
[Flags]
public enum STModSectionFlags : ushort
{
    None = 0,

    /// <summary>
    /// 必需区段：读取器不认识或无法处理时必须**拒绝整个文件**，不得静默忽略。
    /// </summary>
    Required = 1,
}

/// <summary>文件头 Flags。</summary>
[Flags]
public enum STModHeaderFlags : uint
{
    None = 0,

    /// <summary>文件中存在入口点；此时 <c>module.entryPC</c> 元数据必须存在且在代码范围内。</summary>
    HasEntryPoint = 1,
}

/// <summary>函数索引条目的标志。</summary>
[Flags]
public enum STModFunctionFlags : uint
{
    None = 0,

    /// <summary>模块公开（可被其它模块导入）。</summary>
    Exported = 1,

    /// <summary>仅声明、无函数体（例如宿主函数声明）。</summary>
    Declaration = 2,

    /// <summary>模块入口函数。</summary>
    EntryPoint = 4,
}

/// <summary>元数据值类型。</summary>
public enum STModValueType : byte
{
    String = 1,
    Boolean = 2,
    Int32 = 3,
    Int64 = 4,
    UInt32 = 5,
    UInt64 = 6,
    ByteArray = 7,
}

/// <summary>已知元数据 Key 与"必需 Key"集合。</summary>
public static class STModMetadataKeys
{
    public const string ModuleName = "module.name";
    public const string ModuleVersion = "module.version";
    public const string ModuleDescription = "module.description";
    public const string ModuleAuthor = "module.author";
    public const string ModuleLicense = "module.license";
    public const string CompilerVersion = "compiler.version";
    public const string CompilerTarget = "compiler.target";
    public const string ModuleDebug = "module.debug";
    public const string RequiredFeatures = "module.requiredFeatures";
    public const string EntryFunction = "module.entryFunction";
    public const string EntryPC = "module.entryPC";

    /// <summary>链接协议 §3：模块是否已完全链接（Boolean；缺失时按 §1.4 推断）。</summary>
    public const string ModuleLinked = "module.linked";

    /// <summary>链接协议 §3：模块全局槽位总数（UInt32；缺失时按 §1.5 回退）。</summary>
    public const string GlobalCount = "module.globalCount";

    /// <summary>链接协议 §3：信息性导入符号名（String，<c>;</c> 分隔）。</summary>
    public const string ModuleImports = "module.imports";

    /// <summary>链接协议 §3：信息性导出符号名（String，<c>;</c> 分隔）。</summary>
    public const string ModuleExports = "module.exports";

    /// <summary>
    /// 读取器强制的必需 Key。缺失即拒绝 —— 但"模块版本"（<c>module.version</c>）与
    /// <b>二进制格式版本</b>（文件头）是两件事，不得混用。
    /// </summary>
    public static readonly string[] Required =
    [
        ModuleName,
        ModuleVersion,
        CompilerVersion,
    ];
}

/// <summary>`module.requiredFeatures` 使用的特性名。</summary>
public static class STModFeatures
{
    public const string Bytecode = "bytecode";
    public const string ConstantPool = "constant-pool";
    public const string Relocations = "relocations";

    /// <summary>v1 读取器能处理的全部特性。</summary>
    public static readonly string[] Supported =
    [
        Bytecode,
        ConstantPool,
        Relocations,
    ];
}

/// <summary>一条元数据记录。</summary>
public sealed class STModMetadataEntry
{
    public required string Key { get; init; }
    public required STModValueType ValueType { get; init; }
    public required byte[] Value { get; init; }

    public string AsString() => Encoding.UTF8.GetString(Value);
    public bool AsBoolean() => Value.Length == 1 && Value[0] != 0;
    public int AsInt32() => BinaryPrimitives.ReadInt32LittleEndian(Value);
    public long AsInt64() => BinaryPrimitives.ReadInt64LittleEndian(Value);
    public uint AsUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Value);
    public ulong AsUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Value);

    public static STModMetadataEntry FromString(string key, string value) => new()
    {
        Key = key,
        ValueType = STModValueType.String,
        Value = Encoding.UTF8.GetBytes(value),
    };

    public static STModMetadataEntry FromBoolean(string key, bool value) => new()
    {
        Key = key,
        ValueType = STModValueType.Boolean,
        Value = [value ? (byte)1 : (byte)0],
    };

    public static STModMetadataEntry FromInt32(string key, int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return new() { Key = key, ValueType = STModValueType.Int32, Value = bytes };
    }

    public static STModMetadataEntry FromInt64(string key, long value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        return new() { Key = key, ValueType = STModValueType.Int64, Value = bytes };
    }

    public static STModMetadataEntry FromUInt32(string key, uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return new() { Key = key, ValueType = STModValueType.UInt32, Value = bytes };
    }

    public static STModMetadataEntry FromUInt64(string key, ulong value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        return new() { Key = key, ValueType = STModValueType.UInt64, Value = bytes };
    }

    public static STModMetadataEntry FromBytes(string key, byte[] value) => new()
    {
        Key = key,
        ValueType = STModValueType.ByteArray,
        Value = value,
    };
}

/// <summary>函数索引条目：真实编译结果中的函数布局。</summary>
/// <remarks>
/// <see cref="CodeOffset"/> 是**在 Code 区段内**的相对偏移；
/// 文件内绝对位置 = <c>CodeSection.Offset + CodeOffset</c>。
/// </remarks>
public sealed class STModFunctionEntry
{
    public required uint FunctionId { get; init; }
    public STModFunctionFlags Flags { get; init; }

    /// <summary>所属代码区段的区内 Id；由写入器赋值、读取器回填。</summary>
    public uint CodeSectionId { get; set; }

    public required string Name { get; init; }

    /// <summary>签名文本，例如 <c>DINT (DINT, DINT)</c>（来自 IR 函数类型）。</summary>
    public required string Signature { get; init; }

    public uint ParamCount { get; init; }

    /// <summary>帧内局部槽位数（来自真实发射的 FRAME 操作数）。</summary>
    public uint LocalCount { get; init; }

    /// <summary>现有 VM 为定长帧栈，无逐函数 max stack；该字段保留且恒为 0。</summary>
    public uint MaxStack { get; init; }

    public ulong CodeOffset { get; init; }
    public ulong CodeLength { get; init; }

    public bool IsExported => (Flags & STModFunctionFlags.Exported) != 0;
    public bool IsDeclaration => (Flags & STModFunctionFlags.Declaration) != 0;
    public bool IsEntryPoint => (Flags & STModFunctionFlags.EntryPoint) != 0;
}

/// <summary>链接协议的符号种类（docs/stmod链接协议.md §4）。</summary>
public enum STModSymbolKind : ushort
{
    /// <summary>模块间函数符号：由链接器跨模块解析到唯一实现。</summary>
    Function = 1,

    /// <summary>宿主函数符号：由运行期 HostBridge 按名提供实现。</summary>
    HostFunction = 2,
}

/// <summary>符号标志。</summary>
[Flags]
public enum STModSymbolFlags : ushort
{
    None = 0,

    /// <summary>导入必需（未解析则拒绝执行 / 拒绝链接）。</summary>
    Required = 1,

    /// <summary>导出（可被其它模块导入）。</summary>
    Exported = 2,
}

/// <summary>
/// 一条链接符号（`ImportSymbols` / `ExportSymbols` 区段条目）。
/// </summary>
/// <remarks>
/// 符号身份 = <see cref="Name"/>，与 <see cref="STModFunctionEntry.Name"/> 逐字节相同、
/// 序数比较。导出符号额外的 <see cref="FunctionId"/> 指向本模块函数索引。
/// </remarks>
public sealed class STModSymbol
{
    public required string Name { get; init; }

    /// <summary>IR 文本签名，例如 <c>DINT (DINT, DINT)</c>。</summary>
    public string Signature { get; init; } = "";

    public STModSymbolKind Kind { get; init; } = STModSymbolKind.Function;

    public STModSymbolFlags Flags { get; init; }

    /// <summary>导出符号：本模块 <c>FunctionIndex</c> 中的 FunctionId；导入符号为 0。</summary>
    public uint FunctionId { get; init; }

    public bool IsRequired => (Flags & STModSymbolFlags.Required) != 0;
    public bool IsExported => (Flags & STModSymbolFlags.Exported) != 0;
}

/// <summary>读出的区段表条目。</summary>
public sealed class STModSectionInfo
{
    public required uint Id { get; init; }
    public required STModSectionType Type { get; init; }
    public required STModSectionFlags Flags { get; init; }
    public required ulong Offset { get; init; }
    public required ulong Size { get; init; }
    public required uint Alignment { get; init; }

    public bool IsRequired => (Flags & STModSectionFlags.Required) != 0;

    public override string ToString() =>
        $"[{Id}] {Type} ({(IsRequired ? "必需" : "可选")}) offset=0x{Offset:X8} size={Size} align={Alignment}";
}

/// <summary>已解析的 `.stmod` 模块。</summary>
public sealed class STModModule
{
    public required IReadOnlyDictionary<string, STModMetadataEntry> Metadata { get; init; }
    public required IReadOnlyList<STModFunctionEntry> Functions { get; init; }
    public required byte[] Code { get; init; }
    public required IReadOnlyList<BcValue> ConstantPool { get; init; }
    public required IReadOnlyList<STModSectionInfo> Sections { get; init; }

    public bool HasEntryPoint { get; init; }
    public uint EntryPC { get; init; }
    public RelocationTable? Relocations { get; init; }

    /// <summary>导入符号（链接协议 §4）。空 = 无外部依赖。</summary>
    public IReadOnlyList<STModSymbol> Imports { get; init; } = [];

    /// <summary>导出符号（链接协议 §5）。</summary>
    public IReadOnlyList<STModSymbol> Exports { get; init; } = [];

    /// <summary>
    /// 模块是否已完全链接（无未解析导入）。缺失 <c>module.linked</c> 时按协议 §1.4 推断。
    /// </summary>
    public bool IsLinked { get; init; } = true;

    /// <summary>全局槽位总数（协议 §1.5 / §3）。</summary>
    public int GlobalCount { get; init; }

    /// <summary>
    /// 模块二进制格式版本。读取时来自文件头；内存中新建的模块默认按**当前**格式
    /// 版本（写入器恒写 <see cref="STModFormat.FormatVersionMajor"/>，二者一致）。
    /// </summary>
    public ushort FormatVersionMajor { get; init; } = STModFormat.FormatVersionMajor;
    public ushort FormatVersionMinor { get; init; } = STModFormat.FormatVersionMinor;

    /// <summary>元数据字符串（缺失或类型不符时返回 <c>null</c>）。</summary>
    public string? GetString(string key) =>
        Metadata.TryGetValue(key, out var e) && e.ValueType == STModValueType.String
            ? e.AsString()
            : null;
}
