namespace IronST.IRBuilder.Parser;

/// <summary>词法单元类型</summary>
enum TokenKind
{
    IntLit,         // 整数字面量: 42, -1
    FloatLit,       // 浮点字面量: 3.14
    StringLit,      // 字符串: "hello"
    LocalIdent,     // 局部标识符: %foo, %1
    GlobalIdent,    // 全局标识符: @foo, @1
    IntType,        // 整数类型: i32, i64
    Ident,          // 标识符/关键字: define, add, ...
    LBrace, RBrace, // { }
    LParen, RParen, // ( )
    LBrack, RBrack, // [ ]
    LAngle, RAngle, // < >
    Eq,             // =
    Comma,          // ,
    Star,           // *
    Colon,          // :
    Ellipsis,       // ...
    Bang,           // !
    Eof
}

/// <summary>词法单元</summary>
class Token
{
    public TokenKind Kind { get; set; }
    public string Text { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public override string ToString() => $"{Kind}: '{Text}' @ {Line}:{Column}";
}