using IronST.LanguageServer.Features;
using IronST.LanguageServer.Protocol;

namespace IronST.LanguageServer.Tests;

public class NavigationAndHoverTests
{
    private const string Source = """
        FUNCTION Add : DINT
        VAR_INPUT
            a : DINT;
            b : DINT;
        END_VAR
        Add := a + b;
        END_FUNCTION

        PROGRAM Main
        VAR
            x : DINT;
            y : DINT;
            sum : DINT;
        END_VAR
        x := 1;
        y := 2;
        sum := Add(x, y);
        END_PROGRAM
        """;

    [Fact]
    public void 转到定义_跳到变量声明处()
    {
        var snap = TestHelpers.Snapshot(Source);
        Assert.True(snap.Result.Success, string.Join("; ", snap.Result.Diagnostics));

        // 光标放在 "sum := Add(x, y)" 的 sum 上（第二次出现）
        var offset = TestHelpers.OffsetOf(Source, "sum := Add") + 1;
        var def = NavigationProvider.Definition(snap, offset);

        Assert.NotNull(def);
        var range = def!["range"]!.AsObject();
        var line = range["start"]!["line"]!.GetValue<int>();
        // VAR 里 sum 声明所在的 0 基行（源码第 13 行，1 基）
        Assert.Equal(12, line);
    }

    [Fact]
    public void 查找引用_包含所有读写点()
    {
        var snap = TestHelpers.Snapshot(Source);

        // sum 出现在 VAR 声明 + 赋值目标 = 2 处
        var sumOffset = TestHelpers.OffsetOf(Source, "sum := Add") + 1;
        var sumRefs = NavigationProvider.References(snap, sumOffset);
        Assert.Equal(2, sumRefs.Count);
        Assert.Contains(sumRefs, r => r!["range"]!["start"]!["line"]!.GetValue<int>() == 12);

        // x 出现在 VAR 声明 + 赋值写入 + 调用实参读取 = 3 处
        var xOffset = TestHelpers.OffsetOf(Source, "Add(x, y)") + "Add(".Length;
        var xRefs = NavigationProvider.References(snap, xOffset);
        Assert.Equal(3, xRefs.Count);

        // Add 出现在函数声明 + 函数体内写返回值 + 调用 = 3 处
        var addOffset = TestHelpers.OffsetOf(Source, "Add(x, y)") + 1;
        var addRefs = NavigationProvider.References(snap, addOffset);
        Assert.Equal(3, addRefs.Count);
    }

    [Fact]
    public void 悬停_给出变量类型()
    {
        var snap = TestHelpers.Snapshot(Source);
        var offset = TestHelpers.OffsetOf(Source, "sum := Add") + 1;
        var hover = HoverProvider.Hover(snap, offset);

        Assert.NotNull(hover);
        var value = hover!["contents"]!["value"]!.GetValue<string>();
        Assert.Contains("sum : DINT", value);
        Assert.Contains("VAR", value);
    }

    [Fact]
    public void 悬停_给出函数签名()
    {
        var snap = TestHelpers.Snapshot(Source);
        var offset = TestHelpers.OffsetOf(Source, "Add(x, y)") + 1;
        var hover = HoverProvider.Hover(snap, offset);

        Assert.NotNull(hover);
        var value = hover!["contents"]!["value"]!.GetValue<string>();
        Assert.Contains("FUNCTION Add : DINT", value);
        Assert.Contains("a : DINT", value);
    }

    [Fact]
    public void 悬停_光标不在标识符上返回null()
    {
        var snap = TestHelpers.Snapshot(Source);
        // 光标放在文件开头（PROGRAM 之前是 FUNCTION 的 F，改放空行位置）
        var offset = Source.IndexOf('\n') + 1;
        var hover = HoverProvider.Hover(snap, offset);
        Assert.Null(hover);
    }

    [Fact]
    public void 跳转路径_能解析file协议URI()
    {
        Assert.Equal(@"C:\a\b.st", NavigationProvider.PathFromUri("file:///C:/a/b.st"));
        Assert.Equal("/home/u/a.st", NavigationProvider.PathFromUri("file:///home/u/a.st"));
        Assert.Null(NavigationProvider.PathFromUri("untitled:Untitled-1"));
    }

    [Fact]
    public void 文档大纲_列出POU与变量()
    {
        var snap = TestHelpers.Snapshot(Source);
        var symbols = DocumentSymbolProvider.Symbols(snap);

        var names = symbols.Select(s => s!["name"]!.GetValue<string>()).ToList();
        Assert.Contains("Add", names);
        Assert.Contains("Main", names);

        var main = symbols.First(s => s!["name"]!.GetValue<string>() == "Main")!.AsObject();
        var children = main["children"]!.AsArray()
            .Select(c => c!["name"]!.GetValue<string>())
            .ToList();
        Assert.Contains("sum", children);
        Assert.Contains("x", children);
    }
}
