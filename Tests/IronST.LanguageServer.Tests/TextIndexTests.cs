using IronST.LanguageServer.Protocol;

namespace IronST.LanguageServer.Tests;

public class TextIndexTests
{
    [Fact]
    public void LspPosition_与_UTF16偏移_互转()
    {
        var index = new TextIndex("abc\ndef\nghi");
        Assert.Equal(0, index.OffsetOf(0, 0));
        Assert.Equal(2, index.OffsetOf(0, 2));
        Assert.Equal(4, index.OffsetOf(1, 0)); // 第二行起始
        Assert.Equal(5, index.OffsetOf(1, 1));
        Assert.Equal((0, 1), index.PositionOf(1));
        Assert.Equal((1, 0), index.PositionOf(4));
        Assert.Equal((2, 2), index.PositionOf(10));
    }

    [Fact]
    public void Lsp位置越界_会被夹取而不是抛异常()
    {
        var index = new TextIndex("ab\ncd");
        Assert.Equal(2, index.OffsetOf(0, 99));   // 行尾
        Assert.Equal(0, index.OffsetOf(-5, -5));  // 文件头
        Assert.Equal(5, index.OffsetOf(99, 99));  // 文件尾
    }

    [Fact]
    public void 增补平面字符_前端码位列_换算成LSP_UTF16列()
    {
        // 😀 (U+1F600) 占 1 个码位、2 个 UTF-16 码元
        // 这正是 ANTLR 的 code point 列号 与 LSP 的 UTF-16 列号 会分叉的场景
        var index = new TextIndex("😀ab");

        // 码位列 1 → 第一个字符（😀）起始，UTF-16 第 0 列
        Assert.Equal((0, 0), index.ToLspPosition(1, 1));
        // 码位列 2（'a'）→ UTF-16 第 2 列（😀 占了 0 和 1）
        Assert.Equal((0, 2), index.ToLspPosition(1, 2));
        // 码位列 3（'b'）→ UTF-16 第 3 列
        Assert.Equal((0, 3), index.ToLspPosition(1, 3));
    }

    [Fact]
    public void 中文字符_码位列与UTF16列一致()
    {
        // 中文属 BMP，1 个码位 = 1 个 UTF-16 码元，两者不分叉
        var index = new TextIndex("中文abc");
        Assert.Equal((0, 0), index.ToLspPosition(1, 1));
        Assert.Equal((0, 2), index.ToLspPosition(1, 3));
        Assert.Equal((0, 4), index.ToLspPosition(1, 5));
    }

    [Fact]
    public void 增补平面字符_行内查找仍能定位()
    {
        var index = new TextIndex("    😀target : INT;");
        var found = index.FindWordOnLine(1, "target");
        Assert.NotNull(found);
        Assert.Equal("target", index.Text[found!.Value.Start..found.Value.End]);
    }

    [Fact]
    public void 行内查找标识符_能定位精确范围()
    {
        var index = new TextIndex("PROGRAM Main\nVAR\n    counter : INT;\nEND_VAR\n");
        var found = index.FindWordOnLine(3, "counter");
        Assert.NotNull(found);
        Assert.Equal("counter", index.Text[found!.Value.Start..found.Value.End]);
    }

    [Fact]
    public void 行内查找标识符_不会命中单词的一部分()
    {
        var index = new TextIndex("my_counter := counter;");
        // "my_counter := counter;" 中独立的 counter 从下标 14 开始
        var found = index.FindWordOnLine(1, "counter");
        Assert.NotNull(found);
        Assert.Equal(14, found!.Value.Start);
    }

    [Fact]
    public void 找不到返回null()
    {
        var index = new TextIndex("abc");
        Assert.Null(index.FindWordOnLine(1, "xyz"));
        Assert.Null(index.FindWordOnLine(99, "abc"));
    }
}
