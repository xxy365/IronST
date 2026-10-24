namespace IronST.Frontend.Analysis;

public static class IECStandardLibrary
{
    /// <summary>完整 IEC 标准库声明（数学 + 字符串）。行为与既有版本一致。</summary>
    public static ExternalLibrary Create() => new()
    {
        Name = "IEC 61131-3 Standard",
        Functions = [.. MathFunctions, .. StringFunctions],
    };

    /// <summary>
    /// 仅字符串标准函数的声明（不含数学函数）。
    ///
    /// <para>
    /// CLI 的默认宿主环境只为字符串函数接好了宿主实现（见 <c>StringStandardLibrary</c>）；
    /// 数学函数在 IR 层降级为 C extern（sqrt/sin/…），而字节码后端要求每个 extern 都有宿主
    /// 处理函数（F13，未解）。所以 CLI 不能整张 <see cref="Create"/> 注入 —— 那只会把
    /// 「未声明的函数 'SQRT'」换成一条指不到用户代码的后端「has no host handler」报错。
    /// </para>
    /// </summary>
    public static ExternalLibrary CreateStringFunctions() => new()
    {
        Name = "IEC 61131-3 String Functions",
        Functions = [.. StringFunctions],
    };

    // ── 声明来源：数学与字符串各一处，供 Create / CreateStringFunctions 共享 ──

    // A5：标准功能块声明（检查表 A5 验收：标准库形式提供，前端声明 + 字节码/IR 实现，不要求进 VM 指令集）。
    // 当前实现：仅提供声明（供编译期识别），运行时行为由宿主注册提供简单模拟（非完整 PLC 行为）。
    public static ExternalLibrary CreateStandardFunctionBlocks() => new()
    {
        Name = "IEC 61131-3 Standard Function Blocks (A5)",
        FunctionBlocks = [
            // TON — 定时器开启延时
            FB("TON", input: [P("IN", "BOOL"), P("PT", "TIME")],
                 output: [P("Q", "BOOL"), P("ET", "TIME")],
                 vars: [("Counter", "TIME")]),
            // TOF — 定时器关闭延时
            FB("TOF", input: [P("IN", "BOOL"), P("PT", "TIME")],
                 output: [P("Q", "BOOL"), P("ET", "TIME")],
                 vars: [("Counter", "TIME")]),
            // CTU — 上升沿计数器
            FB("CTU", input: [P("CU", "BOOL"), P("R", "BOOL"), P("PV", "INT")],
                 output: [P("Q", "BOOL"), P("CV", "INT")],
                 vars: [("Count", "INT")]),
            // CTD — 下降沿计数器
            FB("CTD", input: [P("CD", "BOOL"), P("LD", "BOOL"), P("PV", "INT")],
                 output: [P("Q", "BOOL"), P("CV", "INT")],
                 vars: [("Count", "INT")]),
            // R_TRIG — 上升沿检测
            FB("R_TRIG", input: [P("CLK", "BOOL")],
                 output: [P("Q", "BOOL")],
                 vars: [("PrevCLK", "BOOL")]),
            // F_TRIG — 下降沿检测
            FB("F_TRIG", input: [P("CLK", "BOOL")],
                 output: [P("Q", "BOOL")],
                 vars: [("PrevCLK", "BOOL")]),
            // SR — 置位优先
            FB("SR", input: [P("S1", "BOOL"), P("R", "BOOL")],
                 output: [P("Q1", "BOOL")],
                 vars: []),
            // RS — 复位优先
            FB("RS", input: [P("S", "BOOL"), P("R1", "BOOL")],
                 output: [P("Q", "BOOL")],
                 vars: []),
        ],
    };

    // A5：标准功能块简便构造辅助（P = 参数对，FB = 功能块构造）。
    private static (string, string) P(string name, string typeName) => (name, typeName);

    private static ExternalFunctionBlock FB(
        string name,
        List<(string Name, string TypeName)>? input = null,
        List<(string Name, string TypeName)>? output = null,
        List<(string Name, string TypeName)>? inout = null,
        List<(string Name, string TypeName)>? vars = null) => new()
        {
            Name = name,
            InputVars = (input ?? []).Select(p => new ExternalParameter { Name = p.Name, TypeName = p.TypeName }).ToList(),
            OutputVars = (output ?? []).Select(p => new ExternalParameter { Name = p.Name, TypeName = p.TypeName }).ToList(),
            InOutVars = (inout ?? []).Select(p => new ExternalParameter { Name = p.Name, TypeName = p.TypeName }).ToList(),
            InternalVars = (vars ?? []).Select(p => (Name: p.Name, TypeName: p.TypeName)).ToList(),
        };

    private static ExternalFunction[] MathFunctions =>
    [
        Fn("ABS",   "DINT",  ("input", "DINT")),
        Fn("SQRT",  "LREAL", ("input", "LREAL")),
        Fn("LN",    "LREAL", ("input", "LREAL")),
        Fn("LOG",   "LREAL", ("input", "LREAL")),
        Fn("EXP",   "LREAL", ("input", "LREAL")),
        Fn("SIN",   "LREAL", ("input", "LREAL")),
        Fn("COS",   "LREAL", ("input", "LREAL")),
        Fn("TAN",   "LREAL", ("input", "LREAL")),
        Fn("ASIN",  "LREAL", ("input", "LREAL")),
        Fn("ACOS",  "LREAL", ("input", "LREAL")),
        Fn("ATAN",  "LREAL", ("input", "LREAL")),
        Fn("ATAN2", "LREAL", ("x", "LREAL"), ("y", "LREAL")),
        Fn("EXPT",  "LREAL", ("base", "LREAL"), ("exponent", "LREAL")),
    ];

    private static ExternalFunction[] StringFunctions =>
    [
        Fn("LEN",    "INT",  ("input", "STRING")),
        Fn("LEFT",   "STRING", ("input", "STRING"), ("length", "INT")),
        Fn("RIGHT",  "STRING", ("input", "STRING"), ("length", "INT")),
        Fn("MID",    "STRING", ("input", "STRING"), ("length", "INT"), ("start", "INT")),
        Fn("CONCAT", "STRING", ("in1", "STRING"), ("in2", "STRING")),
        Fn("INSERT", "STRING", ("in1", "STRING"), ("in2", "STRING"), ("position", "INT")),
        Fn("DELETE", "STRING", ("input", "STRING"), ("length", "INT"), ("start", "INT")),
        Fn("REPLACE","STRING", ("in1", "STRING"), ("in2", "STRING"), ("length", "INT"), ("start", "INT")),
        Fn("FIND",   "INT",    ("in1", "STRING"), ("in2", "STRING")),
    ];

    // ── 便捷构造 ──

    private static ExternalFunction Fn(
        string name, string returnType, params (string, string)[] parameters) =>
        new()
        {
            Name = name,
            ReturnTypeName = returnType,
            Parameters = parameters
                .Select(p => new ExternalParameter { Name = p.Item1, TypeName = p.Item2 })
                .ToList(),
        };
}
