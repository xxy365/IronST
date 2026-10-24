using IronST.Frontend.Analysis;
using IronST.LanguageServer.Protocol;
using IronST.LanguageServer.Server;
using System.Text.Json.Nodes;

namespace IronST.LanguageServer.Features;

/// <summary>转到定义 / 查找所有引用。</summary>
internal static class NavigationProvider
{
    /// <summary>转到定义：找同名符号的 Declaration 引用位置。</summary>
    public static JsonNode? Definition(AnalysisSnapshot snap, int offset)
    {
        var ident = Identifiers.FindAt(snap.Index, offset);
        if (ident == null)
            return null;

        var name = ident.Value.Name;

        var decl = snap.Result.References.FirstOrDefault(
            r => r.SymbolName.Equals(name, StringComparison.OrdinalIgnoreCase)
                 && r.Usage == ReferenceUsage.Declaration);

        // References 里没有（如外部库函数），退回到 POU/类型的声明行
        var location = decl != null
            ? Locate(snap, decl.SymbolName, decl.Line, decl.FilePath)
            : LocateFromInfo(snap, name);

        return location;
    }

    /// <summary>查找所有引用（含声明）。</summary>
    public static JsonArray References(AnalysisSnapshot snap, int offset)
    {
        var result = new JsonArray();
        var ident = Identifiers.FindAt(snap.Index, offset);
        if (ident == null)
            return result;

        var name = ident.Value.Name;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var r in snap.Result.References)
        {
            if (!r.SymbolName.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;

            var uri = string.IsNullOrEmpty(r.FilePath) ? snap.Uri : r.FilePath!;
            var loc = BuildLocation(snap, uri, r.SymbolName, r.Line);
            if (loc == null)
                continue;

            var key = loc.ToJsonString();
            if (seen.Add(key))
                result.Add(loc);
        }

        return result;
    }

    // ── 工具 ──

    private static JsonNode? LocateFromInfo(AnalysisSnapshot snap, string name)
    {
        var pou = snap.Result.POUs.FirstOrDefault(
            p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (pou != null)
            return BuildLocation(snap, pou.FilePath ?? snap.Uri, pou.Name, pou.Line);

        var type = snap.Result.CustomTypes.FirstOrDefault(
            t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (type != null)
            return BuildLocation(snap, type.FilePath ?? snap.Uri, type.Name, type.Line);

        return null;
    }

    private static JsonNode? Locate(AnalysisSnapshot snap, string symbolName, int line, string? filePath)
        => BuildLocation(snap, string.IsNullOrEmpty(filePath) ? snap.Uri : filePath!, symbolName, line);

    /// <summary>
    /// 在 <paramref name="uri"/> 对应的文本里定位 <paramref name="symbolName"/>。
    /// 本文件用快照里的 TextIndex；其他文件用磁盘内容。
    /// </summary>
    private static JsonObject? BuildLocation(AnalysisSnapshot snap, string uri, string symbolName, int line)
    {
        if (uri == snap.Uri)
        {
            var found = snap.Index.FindWordOnLine(line, symbolName);
            var range = found != null
                ? RangeFromOffsets(snap.Index, found.Value.Start, found.Value.End)
                : LineRange(snap.Index, line);
            return Json.Location(uri, range);
        }

        // 其他文件：读盘（跨文件跳转）
        var text = TryReadFile(uri);
        if (text == null)
        {
            // 读不到就退化为「该行整行」，客户端仍能跳过去
            return Json.Location(uri, Json.Range(Math.Max(0, line - 1), 0, Math.Max(0, line - 1), 0));
        }

        var index = new TextIndex(text);
        var other = index.FindWordOnLine(line, symbolName);
        var r = other != null
            ? RangeFromOffsets(index, other.Value.Start, other.Value.End)
            : LineRange(index, line);
        return Json.Location(uri, r);
    }

    private static string? TryReadFile(string uri)
    {
        var path = PathFromUri(uri);
        if (path == null || !File.Exists(path))
            return null;
        try
        {
            return File.ReadAllText(path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>file:// URI → 本地路径（Windows 盘符与分隔符已规范化）。</summary>
    internal static string? PathFromUri(string uri)
    {
        if (!uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            return null;

        var path = Uri.UnescapeDataString(uri["file://".Length..]);

        // Windows: file:///C:/x/y.st → /C:/x/y.st → C:/x/y.st
        bool isWindowsPath =
            path.Length >= 3 && path[0] == '/' && char.IsAsciiLetter(path[1]) && path[2] == ':';

        if (isWindowsPath)
        {
            path = path[1..];
            if (OperatingSystem.IsWindows())
                path = path.Replace('/', '\\');
        }

        return path;
    }

    internal static JsonObject RangeFromOffsets(TextIndex index, int start, int end)
    {
        var (sl, sc) = index.PositionOf(start);
        var (el, ec) = index.PositionOf(end);
        return Json.Range(sl, sc, el, ec);
    }

    private static JsonObject LineRange(TextIndex index, int line)
    {
        int line0 = Math.Max(0, line - 1);
        int start = index.LineStartOffset(line0);
        int end = index.LineEndOffset(line0);
        var (sl, sc) = index.PositionOf(start);
        var (el, ec) = index.PositionOf(end);
        return Json.Range(sl, sc, el, ec);
    }
}
