// src/IronST.Common/Types/IECType.cs
namespace IronST.Common.Types;

/// <summary>
/// IEC 61131-3 类型体系。
/// 内置类型用单例，复合类型通过工厂方法创建。
/// </summary>
public sealed class IECType
{
    // ── 类型种类 ──
    public enum KindTag
    {
        BOOL,
        SINT,
        INT,
        DINT,
        LINT,
        USINT,
        UINT,
        UDINT,
        ULINT,
        BYTE,
        WORD,
        DWORD,
        LWORD,
        REAL,
        LREAL,
        TIME,
        DATE,
        TOD,
        DT,
        STRING,
        WSTRING,
        ARRAY,
        STRUCT,
        ENUM,
        REFERENCE,
        VOID,
    }

    public KindTag Kind { get; init; }
    public string Name { get; init; } = "";
    public int BitWidth { get; init; }
    public bool IsSigned { get; init; } = true;

    // STRING / WSTRING
    public int? MaxStringLength { get; init; }
    public bool IsWideString { get; init; }

    // ARRAY
    public IECType? ElementType { get; init; }
    public List<(int Lower, int Upper)>? Dimensions { get; init; }

    // STRUCT
    public Dictionary<string, IECType>? Fields { get; init; }

    // ENUM
    public List<string>? EnumValues { get; init; }

    // 枚举成员值映射
    public Dictionary<string, long>? EnumMemberValues { get; init; }

    // REFERENCE
    public IECType? ReferencedType { get; init; }

    // REFERENCE 的目标类型名（源码中书写的名字，如 "Target"）。
    //
    // 前向引用（目标类型声明在引用者之后）时，类型布局的不动点会先解析引用者，
    // 此时目标仍是预声明占位符 Void。**名字**始终可靠 —— 前端在不动点收敛后
    // 按名字回填真实目标（见 SemanticAnalyzer.BackfillReferenceTargets）。
    public string? ReferenceTargetName { get; init; }

    // ── 分类辅助 ──
    public bool IsInteger =>
        Kind
            is KindTag.BOOL
                or KindTag.SINT
                or KindTag.INT
                or KindTag.DINT
                or KindTag.LINT
                or KindTag.USINT
                or KindTag.UINT
                or KindTag.UDINT
                or KindTag.ULINT
                or KindTag.BYTE
                or KindTag.WORD
                or KindTag.DWORD
                or KindTag.LWORD
                // A4：时间类型映射为 Int64（i64 占位），参与类型化运算与比较时按整数域处理。
                or KindTag.TIME
                or KindTag.DATE
                or KindTag.TOD
                or KindTag.DT;

    public bool IsReal => Kind is KindTag.REAL or KindTag.LREAL;

    public bool IsNumeric => IsInteger || IsReal;

    public bool IsUnsigned =>
        Kind
            is KindTag.USINT
                or KindTag.UINT
                or KindTag.UDINT
                or KindTag.ULINT
                or KindTag.BYTE
                or KindTag.WORD
                or KindTag.DWORD
                or KindTag.LWORD;

    // ── 内置类型单例 ──
    public static readonly IECType Bool = new()
    {
        Kind = KindTag.BOOL,
        Name = "BOOL",
        BitWidth = 1,
        IsSigned = false,
    };
    public static readonly IECType Byte = new()
    {
        Kind = KindTag.BYTE,
        Name = "BYTE",
        BitWidth = 8,
        IsSigned = false,
    };
    public static readonly IECType Word = new()
    {
        Kind = KindTag.WORD,
        Name = "WORD",
        BitWidth = 16,
        IsSigned = false,
    };
    public static readonly IECType DWord = new()
    {
        Kind = KindTag.DWORD,
        Name = "DWORD",
        BitWidth = 32,
        IsSigned = false,
    };
    public static readonly IECType LWord = new()
    {
        Kind = KindTag.LWORD,
        Name = "LWORD",
        BitWidth = 64,
        IsSigned = false,
    };
    public static readonly IECType SInt = new()
    {
        Kind = KindTag.SINT,
        Name = "SINT",
        BitWidth = 8,
    };
    public static readonly IECType Int = new()
    {
        Kind = KindTag.INT,
        Name = "INT",
        BitWidth = 16,
    };
    public static readonly IECType DInt = new()
    {
        Kind = KindTag.DINT,
        Name = "DINT",
        BitWidth = 32,
    };
    public static readonly IECType LInt = new()
    {
        Kind = KindTag.LINT,
        Name = "LINT",
        BitWidth = 64,
    };
    public static readonly IECType USInt = new()
    {
        Kind = KindTag.USINT,
        Name = "USINT",
        BitWidth = 8,
        IsSigned = false,
    };
    public static readonly IECType UInt = new()
    {
        Kind = KindTag.UINT,
        Name = "UINT",
        BitWidth = 16,
        IsSigned = false,
    };
    public static readonly IECType UDInt = new()
    {
        Kind = KindTag.UDINT,
        Name = "UDINT",
        BitWidth = 32,
        IsSigned = false,
    };
    public static readonly IECType ULInt = new()
    {
        Kind = KindTag.ULINT,
        Name = "ULINT",
        BitWidth = 64,
        IsSigned = false,
    };
    public static readonly IECType Real = new()
    {
        Kind = KindTag.REAL,
        Name = "REAL",
        BitWidth = 32,
    };
    public static readonly IECType LReal = new()
    {
        Kind = KindTag.LREAL,
        Name = "LREAL",
        BitWidth = 64,
    };
    public static readonly IECType Time = new()
    {
        Kind = KindTag.TIME,
        Name = "TIME",
        BitWidth = 64,
    };
    public static readonly IECType Date = new()
    {
        Kind = KindTag.DATE,
        Name = "DATE",
        BitWidth = 64,
    };
    public static readonly IECType Tod = new()
    {
        Kind = KindTag.TOD,
        Name = "TOD",
        BitWidth = 64,
    };
    public static readonly IECType Dt = new()
    {
        Kind = KindTag.DT,
        Name = "DT",
        BitWidth = 64,
    };
    public static readonly IECType Void = new()
    {
        Kind = KindTag.VOID,
        Name = "VOID",
        BitWidth = 0,
    };
    public static readonly IECType Enum = new()
    {
        Kind = KindTag.ENUM,
        Name = "ENUM",
        BitWidth = 32,
        IsSigned = false,
    };

    public static readonly IECType DefaultString = new()
    {
        Kind = KindTag.STRING,
        Name = "STRING",
        BitWidth = 8,
        MaxStringLength = 254,
        IsWideString = false,
    };

    public static readonly IECType DefaultWString = new()
    {
        Kind = KindTag.WSTRING,
        Name = "WSTRING",
        BitWidth = 16,
        MaxStringLength = 254,
        IsWideString = true,
    };

    // ── 名称 → 单例查找表 ──
    private static readonly Dictionary<string, IECType> Builtins = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["BOOL"] = Bool,
        ["BYTE"] = Byte,
        ["WORD"] = Word,
        ["DWORD"] = DWord,
        ["LWORD"] = LWord,
        ["SINT"] = SInt,
        ["INT"] = Int,
        ["DINT"] = DInt,
        ["LINT"] = LInt,
        ["USINT"] = USInt,
        ["UINT"] = UInt,
        ["UDINT"] = UDInt,
        ["ULINT"] = ULInt,
        ["REAL"] = Real,
        ["LREAL"] = LReal,
        ["TIME"] = Time,
        ["T"] = Time,
        ["DATE"] = Date,
        ["D"] = Date,
        ["TIME_OF_DAY"] = Tod,
        ["TOD"] = Tod,
        ["DATE_AND_TIME"] = Dt,
        ["DT"] = Dt,
        ["STRING"] = DefaultString,
        ["WSTRING"] = DefaultWString,
    };

    public static IECType? LookupBuiltin(string name) => Builtins.GetValueOrDefault(name);

    public static IECType MakeString(int maxLen, bool wide = false) =>
        new()
        {
            Kind = wide ? KindTag.WSTRING : KindTag.STRING,
            Name = wide ? $"WSTRING({maxLen})" : $"STRING({maxLen})",
            BitWidth = wide ? 16 : 8,
            MaxStringLength = maxLen,
            IsWideString = wide,
        };

    public static IECType MakeArray(IECType element, List<(int Lower, int Upper)> dims)
    {
        var dimStr = string.Join(",", dims.Select(d => $"{d.Lower}..{d.Upper}"));
        return new()
        {
            Kind = KindTag.ARRAY,
            Name = $"ARRAY[{dimStr}] OF {element.Name}",
            ElementType = element,
            Dimensions = dims,
        };
    }

    public static IECType MakeStruct(string name, Dictionary<string, IECType> fields) =>
        new()
        {
            Kind = KindTag.STRUCT,
            Name = name,
            Fields = fields,
        };

    public static IECType MakeEnum(string name, List<string> values) =>
        new()
        {
            Kind = KindTag.ENUM,
            Name = name,
            EnumValues = values,
            BitWidth = 32,
            IsSigned = false,
        };

    public static IECType MakeEnum(
        string name,
        List<string> values,
        Dictionary<string, long> memberValues
    ) =>
        new()
        {
            Kind = KindTag.ENUM,
            Name = name,
            EnumValues = values,
            EnumMemberValues = memberValues,
            BitWidth = 32,
            IsSigned = false,
        };

    /// <summary>
    /// 创建引用类型。
    /// </summary>
    /// <param name="target">
    /// 目标类型。前向引用场景下可能是占位符 <see cref="Void"/>，
    /// 真实目标由前端在类型不动点收敛后回填。
    /// </param>
    /// <param name="targetName">
    /// 源码中书写的目标类型名。前向引用的占位符场景靠它回填真实目标；
    /// 省略时退化为 <c>target.Name</c>。
    /// </param>
    public static IECType MakeReference(IECType target, string? targetName = null) =>
        new()
        {
            Kind = KindTag.REFERENCE,
            Name = $"REFERENCE TO {target.Name}",
            ReferencedType = target,
            ReferenceTargetName = targetName ?? target.Name,
        };
}
