// tests/IronST.Common.Tests/SourceSpanTests.cs
using IronST.Common.Diagnostics;

namespace IronST.Common.Tests;

public class SourceSpanTests
{
    [Fact]
    public void Unknown_HasZeroCoordinates()
    {
        var u = SourceSpan.Unknown;
        Assert.Equal(0, u.Line);
        Assert.Equal(0, u.Column);
        Assert.Equal(0, u.EndLine);
        Assert.Equal(0, u.EndColumn);
        Assert.Null(u.FileName);
    }

    [Fact]
    public void ToString_WithoutFileName()
    {
        var s = new SourceSpan(10, 5, 10, 12);
        Assert.Equal("10:5", s.ToString());
    }

    [Fact]
    public void ToString_WithFileName()
    {
        var s = new SourceSpan(3, 1, 3, 8, "main.st");
        Assert.Equal("main.st:3:1", s.ToString());
    }

    [Fact]
    public void RecordEquality_Works()
    {
        var a = new SourceSpan(1, 2, 3, 4);
        var b = new SourceSpan(1, 2, 3, 4);
        Assert.Equal(a, b);
    }

    [Fact]
    public void RecordInequality_Works()
    {
        var a = new SourceSpan(1, 2, 3, 4);
        var b = new SourceSpan(1, 2, 3, 5);
        Assert.NotEqual(a, b);
    }
}
