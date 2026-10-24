using IronST.IRBuilder.IR;
using IronST.Frontend.AST;
using IronST.Common.Diagnostics;

namespace IronST.LLVMCodeGen.CodeGen;

/// <summary>
/// 内置函数统一识别与分发中枢。
/// 按类别分发到 TypeConversionFunctions / MathFunctions / StringFunctions。
/// </summary>
public static class BuiltinFunctions
{
    private static readonly HashSet<string> _typeConversions = new(StringComparer.OrdinalIgnoreCase)
    {
        "BOOL", "SINT", "INT", "DINT", "LINT",
        "USINT", "UINT", "UDINT", "ULINT",
        "BYTE", "WORD", "DWORD", "LWORD",
        "REAL", "LREAL",
        "TIME", "DATE", "TOD", "DT",
    };

    private static readonly HashSet<string> _mathFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "ABS", "SQRT", "LN", "LOG", "EXP",
        "SIN", "COS", "TAN",
        "ASIN", "ACOS", "ATAN", "ATAN2",
        "EXPT",
    };

    private static readonly HashSet<string> _stringFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "LEN", "LEFT", "RIGHT", "MID",
        "CONCAT", "INSERT", "DELETE", "REPLACE",
        "FIND",
    };

    /// <summary>
    /// 判断是否为已知内置函数。
    /// </summary>
    public static bool IsBuiltin(string name)
        => _typeConversions.Contains(name)
        || _mathFunctions.Contains(name)
        || _stringFunctions.Contains(name);

    /// <summary>
    /// 尝试以内置函数方式发射。返回 null 表示不是内置函数，应走常规函数调用路径。
    /// </summary>
    public static Value? TryEmit(
        CodeGenContext ctx,
        ExpressionEmitter exprEmitter,
        string name,
        List<CallParamNode> arguments,
        SourceSpan span)
    {
        if (_typeConversions.Contains(name))
            return TypeConversionFunctions.Emit(ctx, exprEmitter, name, arguments, span);

        if (_mathFunctions.Contains(name))
            return MathFunctions.Emit(ctx, exprEmitter, name, arguments, span);

        if (_stringFunctions.Contains(name))
            return StringFunctions.Emit(ctx, exprEmitter, name, arguments, span);

        return null;
    }
}