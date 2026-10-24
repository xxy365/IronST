// ── 变更：全面适配 IronST.Common.Types.IECType，支持多维嵌套数组 ──
using IronST.IRBuilder.IR;
using IronST.Common.Types;
using IronST.LLVMCodeGen.Exceptions;

namespace IronST.LLVMCodeGen.TypeMapping;

public sealed class TypeMapper
{
    private readonly Module _module;
    private readonly Dictionary<string, NamedStructType> _structCache = [];

    public TypeMapper(Module module) => _module = module;

    public IrType Map(IECType type) => type.Kind switch
    {
        IECType.KindTag.VOID      => IrType.Void,
        IECType.KindTag.BOOL      => IrType.Int1,
        IECType.KindTag.SINT      => IrType.Int8,
        IECType.KindTag.BYTE      => IrType.Int8,
        IECType.KindTag.USINT     => IrType.Int8,
        IECType.KindTag.INT       => new IntegerType(16),
        IECType.KindTag.WORD      => new IntegerType(16),
        IECType.KindTag.UINT      => new IntegerType(16),
        IECType.KindTag.DINT      => IrType.Int32,
        IECType.KindTag.DWORD     => IrType.Int32,
        IECType.KindTag.UDINT     => IrType.Int32,
        IECType.KindTag.ENUM      => IrType.Int32,
        IECType.KindTag.LINT      => IrType.Int64,
        IECType.KindTag.LWORD     => IrType.Int64,
        IECType.KindTag.ULINT     => IrType.Int64,
        IECType.KindTag.TIME      => IrType.Int64,
        IECType.KindTag.DATE      => IrType.Int64,
        IECType.KindTag.TOD       => IrType.Int64,
        IECType.KindTag.DT        => IrType.Int64,
        IECType.KindTag.REAL      => IrType.Float,
        IECType.KindTag.LREAL     => IrType.Double,
        IECType.KindTag.STRING    => MapString(type),
        IECType.KindTag.WSTRING   => MapWString(type),
        IECType.KindTag.ARRAY     => MapArray(type),
        IECType.KindTag.STRUCT    => MapStruct(type),
        IECType.KindTag.REFERENCE => IrType.Pointer,
        _ => throw new TypeMappingException(type.Kind.ToString(), "未支持的类型种类")
    };

    private IrType MapString(IECType type)
    {
        int len = type.MaxStringLength ?? 254;
        var name = $"STRING_{len}";
        if (_structCache.TryGetValue(name, out var cached))
            return cached;

        var body = new StructType([
            IrType.Int32,
            new ArrayType(IrType.Int8, len)
        ]);
        _module.AddNamedType(name, body);
        var named = _module.NamedTypes.First(n => n.Name == name);
        _structCache[name] = named;
        return named;
    }

    private IrType MapWString(IECType type)
    {
        int len = type.MaxStringLength ?? 254;
        var name = $"WSTRING_{len}";
        if (_structCache.TryGetValue(name, out var cached))
            return cached;

        var body = new StructType([
            IrType.Int32,
            new ArrayType(new IntegerType(16), len)
        ]);
        _module.AddNamedType(name, body);
        var named = _module.NamedTypes.First(n => n.Name == name);
        _structCache[name] = named;
        return named;
    }

    /// <summary>
    /// 多维数组嵌套映射。
    /// ARRAY[0..3, 0..5] OF INT → [4 x [6 x i32]]
    /// 从最内维向最外维逐层包裹 ArrayType。
    /// </summary>
    private IrType MapArray(IECType type)
    {
        var elemIrType = Map(type.ElementType!);
        if (type.Dimensions is { Count: > 0 })
        {
            for (int i = type.Dimensions.Count - 1; i >= 0; i--)
            {
                int count = type.Dimensions[i].Upper - type.Dimensions[i].Lower + 1;
                elemIrType = new ArrayType(elemIrType, count);
            }
            return elemIrType;
        }
        return new ArrayType(elemIrType, 0);
    }

    /// <summary>
    /// 结构体映射。字段按名称字母序排列（Dictionary 不保序，需排序保证确定性）。
    /// </summary>
    private IrType MapStruct(IECType type)
    {
        var name = type.Name;
        if (_structCache.TryGetValue(name, out var cached))
            return cached;

        var sortedFields = GetSortedFields(type);
        var fieldTypes = sortedFields.Select(f => Map(f.Value)).ToArray();

        var body = new StructType(fieldTypes);
        _module.AddNamedType(name, body);
        var named = _module.NamedTypes.First(n => n.Name == name);
        _structCache[name] = named;
        return named;
    }

    /// <summary>
    /// 获取结构体字段在 IR 中的索引（按名称字母序，与 MapStruct 一致）。
    /// </summary>
    public int GetFieldIndex(IECType structType, string fieldName)
    {
        var sortedFields = GetSortedFields(structType);
        for (int i = 0; i < sortedFields.Count; i++)
        {
            if (string.Equals(sortedFields[i].Key, fieldName, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        throw new TypeMappingException(structType.Name, $"结构体没有字段 '{fieldName}'");
    }

    /// <summary>
    /// 尝试获取结构体字段在 IR 中的索引（按名称字母序，与 <see cref="MapStruct"/> 一致）。
    /// 供「该字段是否存在」的判定使用（如外部 FB 的空结构体）。
    /// </summary>
    public bool TryGetFieldIndex(IECType structType, string fieldName, out int index)
    {
        var sortedFields = GetSortedFields(structType);
        for (int i = 0; i < sortedFields.Count; i++)
        {
            if (string.Equals(sortedFields[i].Key, fieldName, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                return true;
            }
        }
        index = -1;
        return false;
    }

    /// <summary>
    /// 获取结构体指定字段的 Frontend IECType。
    /// </summary>
    public IECType GetFieldType(IECType structType, string fieldName)
    {
        foreach (var kv in structType.Fields!)
        {
            if (string.Equals(kv.Key, fieldName, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }
        throw new TypeMappingException(structType.Name, $"结构体没有字段 '{fieldName}'");
    }

    private static List<KeyValuePair<string, IECType>> GetSortedFields(IECType structType)
    {
        // TODO: Frontend 的 Dictionary<string, IECType> 不保证源码顺序。
        // 按字母序排列作为确定性回退。若 Frontend 改为有序字典，可直接迭代。
        return structType.Fields!
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .ToList();
    }
}