using IronST.Frontend.Analysis;
using IronST.LanguageServer.Protocol;
using IronST.LanguageServer.Server;
using System.Text.Json.Nodes;

namespace IronST.LanguageServer.Features;

/// <summary>补全：变量 / 函数 / 功能块 / 类型 / 关键字 / 标准函数；以及 <c>.</c> 后的成员补全。</summary>
internal static class CompletionProvider
{
    private static readonly string[] Keywords =
    [
        "PROGRAM", "END_PROGRAM", "FUNCTION", "END_FUNCTION",
        "FUNCTION_BLOCK", "END_FUNCTION_BLOCK", "ACTION", "END_ACTION",
        "TYPE", "END_TYPE", "STRUCT", "END_STRUCT", "ARRAY", "OF", "REFERENCE", "TO",
        "VAR", "END_VAR", "VAR_INPUT", "VAR_OUTPUT", "VAR_IN_OUT", "VAR_GLOBAL",
        "VAR_EXTERNAL", "VAR_TEMP", "VAR_ACCESS", "VAR_CONFIG", "VAR_CONSTANT",
        "VAR_RETAIN", "VAR_NON_RETAIN", "CONSTANT", "RETAIN", "NON_RETAIN", "AT",
        "IF", "THEN", "ELSIF", "ELSE", "END_IF",
        "FOR", "TO", "BY", "DO", "END_FOR",
        "WHILE", "END_WHILE", "REPEAT", "UNTIL", "END_REPEAT",
        "CASE", "OF", "END_CASE", "ELSE",
        "RETURN", "EXIT",
        "AND", "OR", "XOR", "NOT", "MOD",
        "TRUE", "FALSE",
        "CONFIGURATION", "END_CONFIGURATION", "RESOURCE", "END_RESOURCE", "TASK", "WITH", "ON",
    ];

    /// <summary>
    /// 标准函数（ABS/SQRT/LEN/CONCAT/...）+ 宿主内建函数（Print）。
    /// 直接由 <see cref="IECStandardLibrary"/> 与 <see cref="HostBuiltins"/> 生成，
    /// 保证与语义分析器实际认的函数表一致。
    /// </summary>
    private static readonly (string Name, string Signature)[] StdFunctions =
        BuildStdFunctions();

    private static (string Name, string Signature)[] BuildStdFunctions()
    {
        // 两张表分开取：IECStandardLibrary 走后端降级，HostBuiltins 走 HCALL，
        // 语义分析器两边都认，补全也得两边都出。
        var libs = new[] { IECStandardLibrary.Create(), HostBuiltins.Create() };
        return libs
            .SelectMany(lib => lib.Functions)
            .Select(f =>
            {
                var parameters = string.Join(
                    ", ", f.Parameters.Select(p => $"{p.Name} : {p.TypeName}"));
                var ret = f.ReturnTypeName == null ? "VOID" : f.ReturnTypeName;
                return (f.Name, $"{f.Name}({parameters}) : {ret}");
            })
            .ToArray();
    }

    /// <summary>
    /// kind：null = 普通补全；非空 = 成员补全（限定名）。
    /// </summary>
    public static JsonArray Complete(AnalysisSnapshot snap, int offset, string? qualifier)
    {
        var items = new JsonArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (qualifier != null)
        {
            AddMemberCompletions(snap, qualifier, items, seen);
            return items;
        }

        // ── 变量 ──
        foreach (var v in snap.Result.Variables)
        {
            if (!seen.Add(v.Name))
                continue;
            items.Add(Item(v.Name, Lsp.KindVariable, v.TypeName,
                $"{v.VarBlockKind} · {v.POUName}"));
        }

        // ── POU ──
        foreach (var p in snap.Result.POUs)
        {
            if (p.IsExternal || !seen.Add(p.Name))
                continue;

            var kind = p.Kind switch
            {
                POUKind.Function => Lsp.KindFunction,
                POUKind.FunctionBlock => Lsp.KindClass,
                _ => Lsp.KindModule,
            };
            items.Add(Item(p.Name, kind, SignatureOf(p), POUKindText(p.Kind)));
        }

        // ── 自定义类型 ──
        foreach (var t in snap.Result.CustomTypes)
        {
            if (!seen.Add(t.Name))
                continue;

            var kind = t.Kind switch
            {
                TypeKind.Enum => Lsp.KindEnum,
                TypeKind.Struct => Lsp.KindStruct,
                _ => Lsp.KindTypeParameter,
            };
            items.Add(Item(t.Name, kind, t.Kind.ToString(), TypeKindText(t.Kind)));
        }

        // ── IEC 标准函数 ──
        foreach (var (name, sig) in StdFunctions)
        {
            if (seen.Add(name))
                items.Add(Item(name, Lsp.KindFunction, sig, "IEC 61131-3 标准函数"));
        }

        // ── 关键字 ──
        foreach (var kw in Keywords)
        {
            if (seen.Add(kw))
                items.Add(Item(kw, Lsp.KindKeyword, null, "关键字"));
        }

        return items;
    }

    /// <summary>限定名补全：查限定名指向的结构体字段 / 枚举成员。</summary>
    private static void AddMemberCompletions(
        AnalysisSnapshot snap, string qualifier, JsonArray items, HashSet<string> seen)
    {
        // 1. 限定名是变量 → 用它的类型名找 CustomType
        var typeName = snap.Result.Variables
            .Where(v => v.Name.Equals(qualifier, StringComparison.OrdinalIgnoreCase))
            .Select(v => v.TypeName)
            .FirstOrDefault();

        // 2. 限定名本身可能是类型名（如 Color.Red）
        if (typeName == null && snap.Result.CustomTypes.Any(
                t => t.Name.Equals(qualifier, StringComparison.OrdinalIgnoreCase)))
        {
            EmitMembers(snap, qualifier, items, seen);
            return;
        }

        if (typeName == null)
            return;

        EmitMembers(snap, typeName, items, seen);
    }

    private static void EmitMembers(
        AnalysisSnapshot snap, string typeName, JsonArray items, HashSet<string> seen)
    {
        var type = snap.Result.CustomTypes
            .FirstOrDefault(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
        if (type == null)
            return;

        if (type.Fields != null)
        {
            foreach (var f in type.Fields)
            {
                if (!seen.Add(f.Name))
                    continue;
                items.Add(Item(f.Name, Lsp.KindField, f.TypeName, $"{type.Name} 字段"));
            }
        }

        if (type.EnumMembers != null)
        {
            foreach (var m in type.EnumMembers)
            {
                if (!seen.Add(m))
                    continue;
                var value = type.EnumMemberValues != null
                            && type.EnumMemberValues.TryGetValue(m, out var v)
                    ? $" = {v}"
                    : "";
                items.Add(Item(m, Lsp.KindEnumMember, $"{m}{value}", $"{type.Name} 枚举成员"));
            }
        }
    }

    // ── 工具 ──

    internal static string SignatureOf(POUInfo p)
    {
        var prefix = p.Kind switch
        {
            POUKind.Function => $"FUNCTION {p.Name} : {p.ReturnTypeName ?? "VOID"}",
            POUKind.FunctionBlock => $"FUNCTION_BLOCK {p.Name}",
            _ => $"PROGRAM {p.Name}",
        };

        if (p.Parameters.Count == 0)
            return prefix;

        var parameters = string.Join("; ", p.Parameters.Select(x => $"{x.Name} : {x.TypeName}"));
        return $"{prefix}({parameters})";
    }

    private static string POUKindText(POUKind k) => k switch
    {
        POUKind.Function => "函数",
        POUKind.FunctionBlock => "功能块",
        _ => "程序",
    };

    private static string TypeKindText(TypeKind k) => k switch
    {
        TypeKind.Struct => "结构体",
        TypeKind.Enum => "枚举",
        TypeKind.Subrange => "子范围",
        _ => "类型别名",
    };

    internal static JsonObject Item(string label, int kind, string? detail, string? description)
    {
        var o = Json.Obj(("label", label), ("kind", kind));
        if (!string.IsNullOrEmpty(detail))
            o["detail"] = detail;
        if (!string.IsNullOrEmpty(description))
            o["documentation"] = Json.Obj(
                ("kind", Lsp.MarkupPlainText), ("value", description));
        return o;
    }
}
