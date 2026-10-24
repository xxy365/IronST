using System.Text;

namespace IronST.IRBuilder.Parser;

/// <summary>词法分析器：将 .ll 文本拆分为 Token 流</summary>
class Lexer
{
    private readonly string _src;
    private int _pos;
    private int _line = 1;
    private int _col = 1;

    public Lexer(string source) => _src = source;

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();
        Token tok;
        do { tok = Next(); tokens.Add(tok); } while (tok.Kind != TokenKind.Eof);
        return tokens;
    }

    private char Peek(int offset = 0) =>
        (_pos + offset) < _src.Length ? _src[_pos + offset] : '\0';

    private bool HasMore => _pos < _src.Length;

    private char Advance()
    {
        var c = _src[_pos++];
        if (c == '\n') { _line++; _col = 1; } else _col++;
        return c;
    }

    private void SkipWhitespaceAndComments()
    {
        while (HasMore)
        {
            if (char.IsWhiteSpace(_src[_pos])) { Advance(); continue; }
            if (_src[_pos] == ';') { while (HasMore && _src[_pos] != '\n') Advance(); continue; }
            break;
        }
    }

    private Token Next()
    {
        SkipWhitespaceAndComments();
        var sl = _line; var sc = _col;

        if (!HasMore)
            return new Token { Kind = TokenKind.Eof, Line = sl, Column = sc };

        var c = Peek();

        // ---- ... 省略号 ----
        if (c == '.' && Peek(1) == '.' && Peek(2) == '.')
        {
            Advance(); Advance(); Advance();
            return new Token { Kind = TokenKind.Ellipsis, Text = "...", Line = sl, Column = sc };
        }

        // ---- 单字符符号 ----
        switch (c)
        {
            case '{': Advance(); return Mk(TokenKind.LBrace, "{", sl, sc);
            case '}': Advance(); return Mk(TokenKind.RBrace, "}", sl, sc);
            case '(': Advance(); return Mk(TokenKind.LParen, "(", sl, sc);
            case ')': Advance(); return Mk(TokenKind.RParen, ")", sl, sc);
            case '[': Advance(); return Mk(TokenKind.LBrack, "[", sl, sc);
            case ']': Advance(); return Mk(TokenKind.RBrack, "]", sl, sc);
            case '<': Advance(); return Mk(TokenKind.LAngle, "<", sl, sc);
            case '>': Advance(); return Mk(TokenKind.RAngle, ">", sl, sc);
            case '=': Advance(); return Mk(TokenKind.Eq, "=", sl, sc);
            case ',': Advance(); return Mk(TokenKind.Comma, ",", sl, sc);
            case '*': Advance(); return Mk(TokenKind.Star, "*", sl, sc);
            case ':': Advance(); return Mk(TokenKind.Colon, ":", sl, sc);
            case '!': Advance(); return Mk(TokenKind.Bang, "!", sl, sc);
        }

        // ---- 局部标识符 %name 或 %1 ----
        if (c == '%')
        {
            Advance();
            string txt = "";
            if (HasMore && (char.IsDigit(Peek()) || (Peek() == '-' && HasMore)))
                txt = ReadInt().ToString();
            else if (HasMore && (char.IsLetter(Peek()) || Peek() == '_' || Peek() == '.'))
                txt = ReadIdent();
            return Mk(TokenKind.LocalIdent, txt, sl, sc);
        }

        // ---- 全局标识符 @name 或 @1 ----
        if (c == '@')
        {
            Advance();
            string txt = "";
            if (HasMore && char.IsDigit(Peek()))
                txt = ReadInt().ToString();
            else if (HasMore && (char.IsLetter(Peek()) || Peek() == '_' || Peek() == '.'))
                txt = ReadIdent();
            return Mk(TokenKind.GlobalIdent, txt, sl, sc);
        }

        // ---- iN 整数类型 (i1, i8, i32, i64 ...) ----
        if (c == 'i' && HasMore && char.IsDigit(Peek(1)))
        {
            Advance(); // 跳过 'i'
            var n = ReadInt();
            return Mk(TokenKind.IntType, $"i{n}", sl, sc);
        }

        // ---- 标识符 / 关键字 ----
        if (char.IsLetter(c) || c == '_')
        {
            return Mk(TokenKind.Ident, ReadIdent(), sl, sc);
        }

        // ---- 数字（含负号、0x 十六进制） ----
        if (char.IsDigit(c) || (c == '-' && HasMore && char.IsDigit(Peek(1))))
        {
            return ReadNumericToken(sl, sc);
        }

        // ---- 浮点以 . 开头 ----
        if (c == '.' && HasMore && char.IsDigit(Peek(1)))
        {
            return ReadNumericToken(sl, sc);
        }

        // ---- 字符串 ----
        if (c == '"')
        {
            return ReadStringToken(sl, sc);
        }

        // ---- 未知字符，跳过 ----
        Advance();
        return Next();
    }

    // ================================================================
    // 辅助读取方法
    // ================================================================

    private static Token Mk(TokenKind k, string t, int l, int c) =>
        new() { Kind = k, Text = t, Line = l, Column = c };

    private string ReadIdent()
    {
        var sb = new StringBuilder();
        while (HasMore && (char.IsLetterOrDigit(Peek()) || Peek() == '_' || Peek() == '.'))
            sb.Append(Advance());
        return sb.ToString();
    }

    private long ReadInt()
    {
        bool neg = HasMore && Peek() == '-';
        if (neg) Advance();
        long v = 0;
        while (HasMore && char.IsDigit(Peek()))
            v = v * 10 + (Advance() - '0');
        return neg ? -v : v;
    }

    private static bool IsHexDigit(char c) =>
        char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    /// <summary>读取数字（含 0x 十六进制浮点、十进制、科学记数法）</summary>
    private Token ReadNumericToken(int sl, int sc)
    {
        var sb = new StringBuilder();
        bool neg = HasMore && Peek() == '-';
        if (neg) { sb.Append(Advance()); }

        // 0x 十六进制浮点常量 (如 0x7FF0000000000000)
        if (HasMore && Peek() == '0' && Peek(1) == 'x')
        {
            sb.Append(Advance()); // 0
            sb.Append(Advance()); // x
            while (HasMore && IsHexDigit(Peek()))
                sb.Append(Advance());
            return Mk(TokenKind.Ident, sb.ToString(), sl, sc);
        }

        bool isFloat = false;

        // 整数部分
        while (HasMore && char.IsDigit(Peek()))
            sb.Append(Advance());

        // 小数点
        if (HasMore && Peek() == '.')
        {
            isFloat = true;
            sb.Append(Advance());
            while (HasMore && char.IsDigit(Peek()))
                sb.Append(Advance());
        }

        // 科学记数法 e/E
        if (HasMore && (Peek() == 'e' || Peek() == 'E'))
        {
            isFloat = true;
            sb.Append(Advance());
            if (HasMore && (Peek() == '+' || Peek() == '-')) sb.Append(Advance());
            while (HasMore && char.IsDigit(Peek())) sb.Append(Advance());
        }

        var txt = sb.ToString();
        if (string.IsNullOrEmpty(txt)) txt = "0";

        return Mk(isFloat ? TokenKind.FloatLit : TokenKind.IntLit, txt, sl, sc);
    }

    /// <summary>读取字符串字面量（保含转义 \XX）</summary>
    private Token ReadStringToken(int sl, int sc)
    {
        Advance(); // 跳过 "
        var sb = new StringBuilder();
        while (HasMore && Peek() != '"')
        {
            if (Peek() == '\\')
            {
                Advance();
                if (HasMore) { sb.Append('\\'); sb.Append(Advance()); }
            }
            else
            {
                sb.Append(Advance());
            }
        }
        if (HasMore) Advance(); // 跳过闭合 "
        return Mk(TokenKind.StringLit, sb.ToString(), sl, sc);
    }
}