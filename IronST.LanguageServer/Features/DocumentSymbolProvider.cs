using IronST.Frontend.Analysis;
using IronST.LanguageServer.Protocol;
using IronST.LanguageServer.Server;
using System.Text.Json.Nodes;

namespace IronST.LanguageServer.Features;

/// <summary>文档大纲：类型声明 + POU，POU 下挂它声明的变量。</summary>
internal static class DocumentSymbolProvider
{
    public static JsonArray Symbols(AnalysisSnapshot snap)
    {
        var result = new JsonArray();

        // 只处理本文件的内容
        foreach (var type in snap.Result.CustomTypes.Where(t => BelongsTo(snap, t.FilePath)))
            result.Add(TypeSymbol(snap, type));

        foreach (var pou in snap.Result.POUs.Where(p => !p.IsExternal && BelongsTo(snap, p.FilePath)))
            result.Add(PouSymbol(snap, pou));

        return result;
    }

    private static bool BelongsTo(AnalysisSnapshot snap, string? filePath)
        => string.IsNullOrEmpty(filePath)
           || string.Equals(filePath, snap.Uri, StringComparison.OrdinalIgnoreCase);

    // ── 类型 ──

    private static JsonObject TypeSymbol(AnalysisSnapshot snap, TypeInfo t)
    {
        var kind = t.Kind switch
        {
            TypeKind.Enum => Lsp.SymEnum,
            TypeKind.Struct => Lsp.SymStruct,
            _ => Lsp.SymClass,
        };

        var (range, selection) = DeclarationRange(snap, t.Name, t.Line);
        var node = Node(t.Name, kind, range, selection, t.Kind.ToString());
        var children = node["children"]!.AsArray();

        if (t.Fields != null)
        {
            foreach (var f in t.Fields)
            {
                var (fr, fs) = DeclarationRange(snap, f.Name, f.Line);
                children.Add(Node(f.Name, Lsp.SymField, fr, fs, f.TypeName));
            }
        }
        else if (t.EnumMembers != null)
        {
            foreach (var m in t.EnumMembers)
            {
                var value = t.EnumMemberValues != null && t.EnumMemberValues.TryGetValue(m, out var v)
                    ? v.ToString()
                    : "";
                children.Add(Node(m, Lsp.SymEnumMember, range, selection, value));
            }
        }

        return node;
    }

    // ── POU ──

    private static JsonObject PouSymbol(AnalysisSnapshot snap, POUInfo p)
    {
        var kind = p.Kind switch
        {
            POUKind.Function => Lsp.SymFunction,
            POUKind.FunctionBlock => Lsp.SymClass,
            _ => Lsp.SymModule,
        };

        var (range, selection) = DeclarationRange(snap, p.Name, p.Line);
        var node = Node(p.Name, kind, range, selection, CompletionProvider.SignatureOf(p));
        var children = node["children"]!.AsArray();

        // 参数
        foreach (var par in p.Parameters)
        {
            var (pr, ps) = DeclarationRange(snap, par.Name, par.Line);
            children.Add(Node(par.Name, Lsp.SymVariable, pr, ps, $"{par.Direction} : {par.TypeName}"));
        }

        // 本 POU 的变量
        foreach (var v in snap.Result.Variables.Where(
                     x => x.POUName.Equals(p.Name, StringComparison.OrdinalIgnoreCase)
                          && BelongsTo(snap, x.FilePath)))
        {
            var (vr, vs) = DeclarationRange(snap, v.Name, v.Line);
            children.Add(Node(v.Name, Lsp.SymVariable, vr, vs, $"{v.VarBlockKind} : {v.TypeName}"));
        }

        return node;
    }

    // ── 工具 ──

    private static JsonObject Node(
        string name, int kind, JsonObject range, JsonObject selection, string? detail)
    {
        var o = Json.Obj(
            ("name", name),
            ("kind", kind),
            ("range", range.DeepClone()),
            ("selectionRange", selection.DeepClone()));
        if (!string.IsNullOrEmpty(detail))
            o["detail"] = detail;
        o["children"] = new JsonArray();
        return o;
    }

    /// <summary>
    /// 声明处的范围。优先用 References 里的精确列；前端把 Column 填 0 时，
    /// 用 TextIndex 在该行扫出标识符的精确区间。
    /// </summary>
    private static (JsonObject Range, JsonObject Selection) DeclarationRange(
        AnalysisSnapshot snap, string name, int line)
    {
        var decl = snap.Result.References.FirstOrDefault(
            r => r.SymbolName.Equals(name, StringComparison.OrdinalIgnoreCase)
                 && r.Usage == ReferenceUsage.Declaration
                 && r.Line == line);

        if (decl != null)
        {
            var found = snap.Index.FindWordOnLine(line, name);
            if (found != null)
            {
                var r = NavigationProvider.RangeFromOffsets(
                    snap.Index, found.Value.Start, found.Value.End);
                return (WholeLineRange(snap.Index, line), r);
            }
        }

        // 兜底：整行
        var lineRange = WholeLineRange(snap.Index, line);
        return (lineRange, (JsonObject)lineRange.DeepClone());
    }

    private static JsonObject WholeLineRange(TextIndex index, int line)
    {
        int line0 = Math.Max(0, Math.Min(line - 1, index.LineCount - 1));
        int start = index.LineStartOffset(line0);
        int end = index.LineEndOffset(line0);
        return NavigationProvider.RangeFromOffsets(index, start, end);
    }
}
