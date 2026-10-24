using IronST.Frontend.Analysis;
using IronST.LanguageServer.Protocol;
using IronST.LanguageServer.Server;
using System.Text.Json.Nodes;

namespace IronST.LanguageServer.Features;

/// <summary>
/// 参数提示：光标在某个函数调用的括号内时，给出被调函数的签名与当前参数位置。
/// 调用识别用文本扫描（跳过注释与字符串，维护括号栈），
/// 这样即使源码有语法错误也能用。
/// </summary>
internal static class SignatureHelpProvider
{
    public static JsonObject? SignatureHelp(AnalysisSnapshot snap, int offset)
    {
        var call = Identifiers.FindEnclosingCall(snap.Index, offset);
        if (call == null || string.IsNullOrEmpty(call.Value.Callee))
            return null;

        var (callee, _, activeParameter) = call.Value;

        var signature = FindSignature(snap.Result, callee);
        if (signature == null)
            return null;

        var (label, documentation, parameters) = signature.Value;

        var signatureObj = Json.Obj(
            ("label", label),
            ("documentation", documentation == null
                ? null
                : Json.Obj(("kind", Lsp.MarkupMarkdown), ("value", documentation))),
            ("parameters", parameters));

        return Json.Obj(
            ("signatures", Json.Arr(signatureObj)),
            ("activeSignature", 0),
            ("activeParameter", Math.Min(activeParameter, Math.Max(0, parameters.Count - 1))));
    }

    private static (string Label, string? Documentation, JsonArray Parameters)? FindSignature(
        AnalysisResult result, string callee)
    {
        // 1. 源码里定义的 POU
        var p = result.POUs.FirstOrDefault(
            x => x.Name.Equals(callee, StringComparison.OrdinalIgnoreCase));
        if (p != null)
        {
            var parameters = new JsonArray();
            foreach (var par in p.Parameters)
                parameters.Add(Json.Obj(
                    ("label", $"{par.Name} : {par.TypeName}"),
                    ("documentation", $"{par.Direction} · {par.TypeName}")));

            var label = p.Parameters.Count == 0
                ? $"{p.Name}()"
                : $"{p.Name}({string.Join(", ", p.Parameters.Select(x => $"{x.Name} : {x.TypeName}"))})";

            var doc = p.Kind == POUKind.Function
                ? $"**FUNCTION {p.Name} : {p.ReturnTypeName ?? "VOID"}**"
                : $"**{p.Kind} {p.Name}**";

            return (label, doc, parameters);
        }

        // 2. IEC 标准函数
        var std = IECStandardLibrary.Create()
            .Functions.FirstOrDefault(
                f => f.Name.Equals(callee, StringComparison.OrdinalIgnoreCase));
        if (std != null)
        {
            var parameters = new JsonArray();
            foreach (var par in std.Parameters)
                parameters.Add(Json.Obj(
                    ("label", $"{par.Name} : {par.TypeName}"),
                    ("documentation", par.TypeName)));

            var label = $"{std.Name}({string.Join(", ", std.Parameters.Select(x => $"{x.Name} : {x.TypeName}"))}) : {std.ReturnTypeName ?? "VOID"}";

            return (label, $"**IEC 61131-3 标准函数 {std.Name}**", parameters);
        }

        return null;
    }
}
