// ConstantEvaluator.cs — 编译期常量求值（F3：变量显式初始值）
//
// 缺陷登记簿 §3.1：初始值在**前端边界**就丢了 —— AST 里有 `InitialValue`，
// 但语义层没有承载它的字段，IR 层一律零初始化。
//
// 本文件补上「表达式 → 编译期常量」这一段：变量声明的初值必须是常量表达式
// （IEC 61131-3 对 VAR_GLOBAL 的硬性要求），因此在语义分析期就把值算出来，
// 供 IR 层直接落成常量 —— 不必等到字节码生成期再解释表达式。
using IronST.Common.Types;
using IronST.Frontend.AST;

namespace IronST.Frontend.Analysis;

/// <summary>
/// 编译期常量求值器。
/// </summary>
public static class ConstantEvaluator
{
    /// <summary>
    /// 求值为编译期常量并按 <paramref name="targetType"/> 归一。
    /// </summary>
    /// <param name="expr">待求值表达式（调用方应先做过 <c>InferType</c>）。</param>
    /// <param name="targetType">变量的声明类型，决定常量落在整数域还是实数域。</param>
    /// <param name="isConstant">
    /// 出参：表达式本身是否为编译期常量。
    /// <c>true</c> 但返回 <c>null</c> ⇒ 是常量，但目标类型暂不支持（字符串 / 数组 / 结构体）。
    /// <c>false</c> ⇒ 根本不是常量（如引用了其它变量）。
    /// </param>
    public static ConstantInitializer? TryEvaluate(
        ExpressionNode expr,
        IECType targetType,
        out bool isConstant)
    {
        var raw = TryEvaluateRawCore(expr);
        isConstant = raw is not null;
        if (raw is null)
            return null;

        return raw.Value.Kind switch
        {
            ConstantKind.Integer => targetType.IsReal
                ? ConstantInitializer.Real(targetType, raw.Value.LongValue)
                : ConstantInitializer.Int(targetType, raw.Value.LongValue),

            ConstantKind.Real => targetType.IsReal
                ? ConstantInitializer.Real(targetType, raw.Value.RealValue)
                // 实数 → 整数：向零取整（与 §3.9 的 FPToSI 语义一致）
                : ConstantInitializer.Int(targetType, (long)Math.Truncate(raw.Value.RealValue)),

            ConstantKind.Boolean => targetType.IsReal
                ? ConstantInitializer.Real(targetType, raw.Value.BoolValue ? 1.0 : 0.0)
                : ConstantInitializer.Int(targetType, raw.Value.BoolValue ? 1 : 0),

            // 字符串 / 时间：暂不支持作为变量初值
            _ => null,
        };
    }

    /// <summary>
    /// 求值为原始常量值。表达式不是编译期常量时返回 <c>null</c>。
    /// </summary>
    private static ConstantValue? TryEvaluateRawCore(ExpressionNode expr)
    {
        switch (expr)
        {
            case LiteralExprNode lit:
                return lit.Kind switch
                {
                    LiteralKind.Integer or LiteralKind.BasedInteger =>
                        lit.ParsedValue is long l ? ConstantValue.FromLong(l) : null,
                    LiteralKind.Real =>
                        lit.ParsedValue is double d ? ConstantValue.FromReal(d) : null,
                    LiteralKind.Boolean =>
                        lit.ParsedValue is bool b ? ConstantValue.FromBool(b) : null,
                    LiteralKind.String =>
                        lit.ParsedValue is string s ? ConstantValue.FromString(s) : null,
                    _ => null,
                };

            // 一元负号：`-5` / `-2.5`
            case UnaryExprNode { Op: UnaryOp.Neg } unary:
                return TryEvaluateRawCore(unary.Operand) switch
                {
                    { Kind: ConstantKind.Integer } v => ConstantValue.FromLong(-v.LongValue),
                    { Kind: ConstantKind.Real } v => ConstantValue.FromReal(-v.RealValue),
                    _ => null,
                };

            // 类型化字面量：`DINT#5` / `REAL#2.5` / `BOOL#TRUE`
            case TypedLiteralExprNode typed:
                return TryEvaluateTypedLiteral(typed);

            // 枚举成员：`Color.Red`
            case VariableExprNode varExpr:
                return TryEvaluateEnumMember(varExpr);

            default:
                return null;
        }
    }

    /// <summary>
    /// 类型化字面量（<c>DINT#5</c> / <c>REAL#2.5</c> / <c>BOOL#TRUE</c>）的常量值。
    /// </summary>
    /// <remarks>
    /// 只认**内建**类型名 —— 自定义 TYPE 的类型化字面量（<c>MyEnum#A</c>）不在此处理。
    /// </remarks>
    private static ConstantValue? TryEvaluateTypedLiteral(TypedLiteralExprNode typed)
    {
        var type = IECType.LookupBuiltin(typed.TypeName);
        if (type is null)
            return null;

        if (type.Kind == IECType.KindTag.BOOL)
        {
            if (bool.TryParse(typed.RawValue, out var bv))
                return ConstantValue.FromBool(bv);
            if (long.TryParse(typed.RawValue, out var bn))
                return ConstantValue.FromBool(bn != 0);
            return null;
        }

        if (type.IsInteger)
        {
            return long.TryParse(typed.RawValue, out var iv)
                ? ConstantValue.FromLong(iv)
                : null;
        }

        if (type.IsReal)
        {
            return double.TryParse(
                typed.RawValue,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var dv)
                ? ConstantValue.FromReal(dv)
                : null;
        }

        return null;
    }

    /// <summary>
    /// 枚举成员常量。依赖 <c>InferType</c> 已把 <c>ResolvedType</c> 解析成 ENUM 类型。
    /// </summary>
    private static ConstantValue? TryEvaluateEnumMember(VariableExprNode varExpr)
    {
        if (varExpr.ResolvedType is not { Kind: IECType.KindTag.ENUM } enumType)
            return null;
        if (enumType.EnumMemberValues is not { } members)
            return null;
        if (varExpr.Segments.Count == 0)
            return null;

        var memberName = varExpr.Segments[^1].Name;
        return members.TryGetValue(memberName, out var value)
            ? ConstantValue.FromLong(value)
            : null;
    }

    /// <summary>原始常量值。</summary>
    private readonly record struct ConstantValue(
        ConstantKind Kind,
        long LongValue = 0,
        double RealValue = 0,
        bool BoolValue = false,
        string? StringValue = null)
    {
        public static ConstantValue FromLong(long v) => new(ConstantKind.Integer, LongValue: v);

        public static ConstantValue FromReal(double v) => new(ConstantKind.Real, RealValue: v);

        public static ConstantValue FromBool(bool v) => new(ConstantKind.Boolean, BoolValue: v);

        public static ConstantValue FromString(string v) =>
            new(ConstantKind.String, StringValue: v);
    }

    private enum ConstantKind
    {
        Integer,
        Real,
        Boolean,
        String,
    }
}
