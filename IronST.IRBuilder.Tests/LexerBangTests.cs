using IronST.IRBuilder.Parser;
using Xunit;

namespace IronST.IRBuilder.Tests;

/// <summary>
/// Lexer 对 ! 符号的词法分析测试
/// </summary>
public class LexerBangTests
{
    [Fact]
    public void Lexer_单独感叹号()
    {
        var lexer = new Lexer("!");
        var tokens = lexer.Tokenize();

        Assert.True(tokens.Count >= 2);
        Assert.Equal(TokenKind.Bang, tokens[0].Kind);
        Assert.Equal("!", tokens[0].Text);
    }

    [Fact]
    public void Lexer_叹号后跟整数()
    {
        var lexer = new Lexer("!42");
        var tokens = lexer.Tokenize();

        Assert.True(tokens.Count >= 3);
        Assert.Equal(TokenKind.Bang, tokens[0].Kind);
        Assert.Equal(TokenKind.IntLit, tokens[1].Kind);
        Assert.Equal("42", tokens[1].Text);
    }

    [Fact]
    public void Lexer_叹号后跟标识符()
    {
        var lexer = new Lexer("!llvm.dbg.cu");
        var tokens = lexer.Tokenize();

        Assert.True(tokens.Count >= 3);
        Assert.Equal(TokenKind.Bang, tokens[0].Kind);
        Assert.Equal(TokenKind.Ident, tokens[1].Kind);
    }

    [Fact]
    public void Lexer_叹号后跟字符串()
    {
        var lexer = new Lexer("!\"hello\"");
        var tokens = lexer.Tokenize();

        Assert.True(tokens.Count >= 3);
        Assert.Equal(TokenKind.Bang, tokens[0].Kind);
        Assert.Equal(TokenKind.StringLit, tokens[1].Kind);
    }

    [Fact]
    public void Lexer_叹号不影响其他Token()
    {
        var lexer = new Lexer("define i32 @f() { ret i32 0, !dbg !0 }");
        var tokens = lexer.Tokenize();

        var bangs = tokens.Where(t => t.Kind == TokenKind.Bang).ToList();
        Assert.Equal(2, bangs.Count);
    }
}