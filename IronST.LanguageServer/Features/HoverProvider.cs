using IronST.Frontend.Analysis;
using IronST.LanguageServer.Protocol;
using IronST.LanguageServer.Server;
using System.Text.Json.Nodes;

namespace IronST.LanguageServer.Features;

/// <summary>悬停：变量 / 函数 / 功能块 / 类型的签名与说明。</summary>
internal static class HoverProvider
{
    public static JsonObject? Hover(AnalysisSnapshot snap, int offset)
    {
        var ident = Identifiers.FindAt(snap.Index, offset);
        if (ident == null)
            return null;

        var (name, start, end) = ident.Value;
        var contents = BuildContents(snap.Result, name);
        if (contents == null)
            return null;

        var (sl, sc) = snap.Index.PositionOf(start);
        var (el, ec) = snap.Index.PositionOf(end);

        return Json.Obj(
            ("contents", Json.Obj(("kind", Lsp.MarkupMarkdown), ("value", contents))),
            ("range", Json.Range(sl, sc, el, ec)));
    }

    internal static string? BuildContents(AnalysisResult result, string name)
    {
        // 1. 变量
        var v = result.Variables.FirstOrDefault(
            x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (v != null)
            return $"```iecst\n{name} : {v.TypeName}\n```\n\n**{v.VarBlockKind}** · `{v.POUName}`";

        // 2. POU
        var p = result.POUs.FirstOrDefault(
            x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (p != null)
        {
            var header = p.Kind switch
            {
                POUKind.Function => $"FUNCTION {p.Name} : {p.ReturnTypeName ?? "VOID"}",
                POUKind.FunctionBlock => $"FUNCTION_BLOCK {p.Name}",
                _ => $"PROGRAM {p.Name}",
            };

            var sb = new System.Text.StringBuilder();
            sb.Append("```iecst\n").Append(header).Append('\n');

            if (p.Parameters.Count > 0)
            {
                sb.Append("VAR_INPUT\n");
                foreach (var par in p.Parameters)
                    sb.Append($"    {par.Name} : {par.TypeName}   // {par.Direction}\n");
                sb.Append("END_VAR\n");
            }
            sb.Append("```");

            return sb.ToString();
        }

        // 3. 类型
        var t = result.CustomTypes.FirstOrDefault(
            x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (t != null)
        {
            var sb = new System.Text.StringBuilder();

            if (t.Kind == TypeKind.Struct && t.Fields != null)
            {
                sb.Append("```iecst\nTYPE ").Append(t.Name).Append(" : STRUCT\n");
                foreach (var f in t.Fields)
                    sb.Append($"    {f.Name} : {f.TypeName};\n");
                sb.Append("END_STRUCT; END_TYPE\n```");
            }
            else if (t.Kind == TypeKind.Enum && t.EnumMembers != null)
            {
                sb.Append("```iecst\nTYPE ").Append(t.Name).Append(" : (");
                sb.Append(string.Join(", ", t.EnumMembers));
                sb.Append(");\nEND_TYPE\n```");
            }
            else
            {
                sb.Append("```iecst\n").Append(t.Name)
                  .Append(" = ").Append(t.UnderlyingType ?? "?").Append("\n```");
            }

            return sb.ToString();
        }

        return null;
    }
}
