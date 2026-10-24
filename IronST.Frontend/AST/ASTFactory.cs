using IronST.Common.Diagnostics;
using IronST.Common.Types;

namespace IronST.Frontend.AST;

/// <summary>
/// AST 节点工厂 —— 集中处理 Span 设置与 ResolvedType 默认赋值，
/// 消除 Visitor 中的重复样板代码。
/// </summary>
public static class ASTFactory
{
    // ═══════════════════════════════════════════
    //  字面量
    // ═══════════════════════════════════════════

    public static LiteralExprNode Int(SourceSpan s, string raw, long val) =>
        new()
        {
            Span = s,
            Kind = LiteralKind.Integer,
            RawText = raw,
            ParsedValue = val,
            ResolvedType = IECType.DInt,
        };

    public static LiteralExprNode BasedInt(SourceSpan s, string raw, long val) =>
        new()
        {
            Span = s,
            Kind = LiteralKind.BasedInteger,
            RawText = raw,
            ParsedValue = val,
            ResolvedType = IECType.DInt,
        };

    public static LiteralExprNode Real(SourceSpan s, string raw, double val) =>
        new()
        {
            Span = s,
            Kind = LiteralKind.Real,
            RawText = raw,
            ParsedValue = val,
            ResolvedType = IECType.Real,
        };

    public static LiteralExprNode Bool(SourceSpan s, bool val) =>
        new()
        {
            Span = s,
            Kind = LiteralKind.Boolean,
            RawText = val ? "TRUE" : "FALSE",
            ParsedValue = val,
            ResolvedType = IECType.Bool,
        };

    public static LiteralExprNode Str(SourceSpan s, string raw, string content) =>
        new()
        {
            Span = s,
            Kind = LiteralKind.String,
            RawText = raw,
            ParsedValue = content,
            ResolvedType = IECType.DefaultString,
        };

    public static LiteralExprNode Time(SourceSpan s, string raw) =>
        new()
        {
            Span = s,
            Kind = LiteralKind.Time,
            RawText = raw,
            ParsedValue = null,
            ResolvedType = IECType.Time,
        };

    public static LiteralExprNode Date(SourceSpan s, string raw) =>
        new()
        {
            Span = s,
            Kind = LiteralKind.Date,
            RawText = raw,
            ParsedValue = null,
            ResolvedType = IECType.Date,
        };

    public static LiteralExprNode Tod(SourceSpan s, string raw) =>
        new()
        {
            Span = s,
            Kind = LiteralKind.TimeOfDay,
            RawText = raw,
            ParsedValue = null,
            ResolvedType = IECType.Tod,
        };

    public static LiteralExprNode Dt(SourceSpan s, string raw) =>
        new()
        {
            Span = s,
            Kind = LiteralKind.DateTime,
            RawText = raw,
            ParsedValue = null,
            ResolvedType = IECType.Dt,
        };

    /// <summary>通用字面量 —— 用于 SubrangeBound 等需要自定义 ResolvedType 的场景</summary>
    public static LiteralExprNode Literal(
        SourceSpan s,
        LiteralKind kind,
        string raw,
        object? parsed,
        IECType? resolved = null
    ) =>
        new()
        {
            Span = s,
            Kind = kind,
            RawText = raw,
            ParsedValue = parsed,
            ResolvedType = resolved,
        };

    // ═══════════════════════════════════════════
    //  Typed 字面量
    // ═══════════════════════════════════════════

    public static TypedLiteralExprNode TypedLiteral(
        SourceSpan s,
        string typeName,
        string rawValue
    ) =>
        new()
        {
            Span = s,
            TypeName = typeName,
            RawValue = rawValue,
        };

    // ═══════════════════════════════════════════
    //  二元 / 一元表达式
    // ═══════════════════════════════════════════

    public static BinaryExprNode Binary(BinaryOp op, ExpressionNode left, ExpressionNode right) =>
        new(op, left, right) { Span = SpanBetween(left, right) };

    public static UnaryExprNode Unary(SourceSpan s, UnaryOp op, ExpressionNode operand) =>
        new(op, operand) { Span = s };

    // ═══════════════════════════════════════════
    //  复合语句
    // ═══════════════════════════════════════════

    public static IfNode If(SourceSpan s, ExpressionNode condition) => new(condition) { Span = s };

    public static ForNode For(
        SourceSpan s,
        string variable,
        ExpressionNode from,
        ExpressionNode to
    ) => new(variable, from, to) { Span = s };

    public static WhileNode While(SourceSpan s, ExpressionNode condition) =>
        new(condition) { Span = s };

    public static CaseNode Case(SourceSpan s, ExpressionNode selector) =>
        new(selector) { Span = s };

    // ═══════════════════════════════════════════
    //  赋值与调用
    // ═══════════════════════════════════════════

    public static AssignmentNode Assignment(
        SourceSpan s,
        ExpressionNode target,
        ExpressionNode value
    ) => new(target, value) { Span = s };

    public static RefAssignmentNode RefAssignment(
        SourceSpan s,
        ExpressionNode target,
        ExpressionNode value
    ) => new(target, value) { Span = s };

    public static CallParamNode Param(SourceSpan s, ExpressionNode value) =>
        new(value) { Span = s };

    // ═══════════════════════════════════════════
    //  变量声明与类型
    // ═══════════════════════════════════════════

    public static VarDeclItemNode VarDeclItem(SourceSpan s, DataTypeNode typeName) =>
        new(typeName) { Span = s };

    public static TypeDeclNode TypeDecl(SourceSpan s, string name, TypeDefNode definition) =>
        new(name, definition) { Span = s };

    public static SimpleTypeDefNode SimpleTypeDef(DataTypeNode underlyingType) =>
        new(underlyingType);

    public static ArrayDataTypeNode ArrayDataType(SourceSpan s, DataTypeNode elementType) =>
        new(elementType) { Span = s };

    // ═══════════════════════════════════════════
    //  简单叶子语句
    // ═══════════════════════════════════════════

    public static ReturnNode Return(SourceSpan s) => new() { Span = s };

    public static ExitNode Exit(SourceSpan s) => new() { Span = s };

    // ═══════════════════════════════════════════
    //  内部工具
    // ═══════════════════════════════════════════

    private static SourceSpan SpanBetween(ASTNode from, ASTNode to) =>
        new(
            from.Span.Line,
            from.Span.Column,
            to.Span.EndLine,
            to.Span.EndColumn,
            from.Span.FileName
        );
}
