namespace IronST.IRBuilder.IR;

/// <summary>
/// 所有 debug info 类型的基类。
/// !DIFile, !DISubprogram, !DILocation 等都继承自此类。
/// </summary>
abstract class DINode : MetadataNode { }

/// <summary>源文件信息。对应 !DIFile(filename: "calc.c", directory: "/home")</summary>
class DIFile : DINode
{
    public string Filename { get; set; } = "";
    public string Directory { get; set; } = "";
}

/// <summary>编译单元。对应 !DICompileUnit(language: DW_LANG_C99, file: !1, ...)</summary>
class DICompileUnit : DINode
{
    public int Language { get; set; }
    public MetadataNode? File { get; set; }        // 引用 DIFile
    public string? Producer { get; set; }
    public bool IsOptimized { get; set; }
    public string? EmissionKind { get; set; }      // "FullDebug" / "LineTablesOnly"
    public List<MetadataNode> Subprograms { get; } = new();
}

/// <summary>函数元信息。对应 !DISubprogram(name: "add", scope: !1, line: 3, ...)</summary>
class DISubprogram : DINode
{
    public string Name { get; set; } = "";
    public MetadataNode? Scope { get; set; }       // 通常引用 DICompileUnit 或 DIFile
    public MetadataNode? File { get; set; }        // 引用 DIFile
    public int Line { get; set; }
    public MetadataNode? Type { get; set; }        // 引用 DISubroutineType
    public int ScopeLine { get; set; }
    public bool IsLocal { get; set; }
    public bool IsDefinition { get; set; }
}

/// <summary>位置信息。对应 !DILocation(line: 4, column: 12, scope: !10)</summary>
class DILocation : DINode
{
    public int Line { get; set; }
    public int Column { get; set; }
    public MetadataNode? Scope { get; set; }
    public MetadataNode? InlinedAt { get; set; }   // 内联链，可以为 null
}

/// <summary>函数签名。对应 !DISubroutineType(types: !{...})</summary>
class DISubroutineType : DINode
{
    public MetadataNode? Types { get; set; }        // 引用 MDTuple，包含参数和返回类型
}

/// <summary>基础类型。对应 !DIBasicType(name: "int", size: 32, encoding: DW_ATE_signed)</summary>
class DIBasicType : DINode
{
    public string Name { get; set; } = "";
    public int Size { get; set; }
    public int Encoding { get; set; }              // DW_ATE_signed = 5, DW_ATE_float = 4 等
}

/// <summary>作用域块。对应 !DILexicalBlock(scope: !10, line: 5, column: 3)</summary>
class DILexicalBlock : DINode
{
    public MetadataNode? Scope { get; set; }
    public MetadataNode? File { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
}