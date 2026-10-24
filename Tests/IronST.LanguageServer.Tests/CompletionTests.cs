using IronST.LanguageServer.Features;

namespace IronST.LanguageServer.Tests;

public class CompletionTests
{
    private const string Source = """
        TYPE Point :
        STRUCT
            X : REAL;
            Y : REAL;
        END_STRUCT;
        END_TYPE;

        FUNCTION DistanceSq : REAL
        VAR_INPUT
            a : Point;
            b : Point;
        END_VAR
        VAR
            dx : REAL;
            dy : REAL;
        END_VAR
        dx := a.X - b.X;
        DistanceSq := dx * dx + dy * dy;
        END_FUNCTION

        PROGRAM Main
        VAR
            p1 : Point;
            total : DINT;
            counter : INT;
        END_VAR
        total := 0;
        END_PROGRAM
        """;

    [Fact]
    public void 普通补全_包含变量()
    {
        var snap = TestHelpers.Snapshot(Source);
        Assert.True(snap.Result.Success, string.Join("; ", snap.Result.Diagnostics));

        var offset = TestHelpers.OffsetOf(Source, "total := 0");
        var items = CompletionProvider.Complete(snap, offset, qualifier: null);

        var labels = items.Select(i => i!["label"]!.GetValue<string>()).ToList();
        Assert.Contains("counter", labels);
        Assert.Contains("p1", labels);
        Assert.Contains("DistanceSq", labels);
        Assert.Contains("Point", labels);
    }

    [Fact]
    public void 普通补全_包含关键字与标准函数()
    {
        var snap = TestHelpers.Snapshot(Source);
        var items = CompletionProvider.Complete(snap, 0, qualifier: null);
        var labels = items.Select(i => i!["label"]!.GetValue<string>()).ToList();

        Assert.Contains("END_IF", labels);
        Assert.Contains("WHILE", labels);
        Assert.Contains("SQRT", labels);   // 来自 IECStandardLibrary
        Assert.Contains("CONCAT", labels);
    }

    [Fact]
    public void 成员补全_返回结构体字段()
    {
        var snap = TestHelpers.Snapshot(Source);

        // 光标放在 "total := 0" 上，限定名传 p1
        var offset = TestHelpers.OffsetOf(Source, "total := 0");
        var items = CompletionProvider.Complete(snap, offset, qualifier: "p1");

        var labels = items.Select(i => i!["label"]!.GetValue<string>()).ToList();
        Assert.Contains("X", labels);
        Assert.Contains("Y", labels);
        Assert.Equal(2, labels.Count);
    }

    [Fact]
    public void 成员补全_限定名可以是类型名本身()
    {
        var snap = TestHelpers.Snapshot(Source);
        var items = CompletionProvider.Complete(snap, 0, qualifier: "Point");
        var labels = items.Select(i => i!["label"]!.GetValue<string>()).ToList();
        Assert.Contains("X", labels);
        Assert.Contains("Y", labels);
    }

    [Fact]
    public void 成员补全_未知限定名返回空()
    {
        var snap = TestHelpers.Snapshot(Source);
        var items = CompletionProvider.Complete(snap, 0, qualifier: "不存在的变量");
        Assert.Empty(items);
    }
}
