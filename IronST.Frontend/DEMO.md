// tools/IronST.Demo/Program.cs
using IronST.Frontend;
using IronST.Frontend.Analysis;

var source = @"
TYPE Point :
STRUCT
    X : REAL;
    Y : REAL;
END_STRUCT
;
END_TYPE;

TYPE Color : (Red, Green, Blue);
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
dy := a.Y - b.Y;
DistanceSq := dx * dx + dy * dy;
END_FUNCTION

FUNCTION_BLOCK PID
VAR_INPUT
    setpoint : REAL;
    measured : REAL;
END_VAR
VAR_OUTPUT
    controlOutput : REAL;
END_VAR
VAR_IN_OUT
    integralState : REAL;
END_VAR
VAR
    kp : REAL;
    ki : REAL;
END_VAR
controlOutput := kp * (setpoint - measured);
END_FUNCTION_BLOCK

PROGRAM Main
VAR
    p1 : Point;
    p2 : Point;
    dist : REAL;
    pid : PID;
    sp : REAL;
    pv : REAL;
    output : REAL;
    integral : REAL;
END_VAR
dist := DistanceSq(p1, p2);
pid(setpoint := sp, measured := pv, controlOutput => output, integralState := integral);
END_PROGRAM
";

// ═══════════════════════════════════════════
//  分析
// ═══════════════════════════════════════════

Console.OutputEncoding = System.Text.Encoding.UTF8;

var result = FrontendPipeline.Analyze(source, "demo.st");

// ═══════════════════════════════════════════
//  1. 诊断信息
// ═══════════════════════════════════════════

Console.WriteLine("╔══════════════════════════════════════════╗");
Console.WriteLine("║         1. DIAGNOSTICS 诊断信息          ║");
Console.WriteLine("╚══════════════════════════════════════════╝");

if (result.Diagnostics.Count == 0)
{
    Console.WriteLine("  (无诊断信息，分析成功)");
}
else
{
    foreach (var d in result.Diagnostics)
    {
        var loc = d.Span?.ToString() ?? "?";
        Console.WriteLine($"  [{d.Severity,-7}] {loc} {d.Code}: {d.Message}");
    }
}

Console.WriteLine();

// ═══════════════════════════════════════════
//  2. POUs
// ═══════════════════════════════════════════

Console.WriteLine("╔══════════════════════════════════════════╗");
Console.WriteLine("║         2. POUs 程序组织单元             ║");
Console.WriteLine("╚══════════════════════════════════════════╝");

foreach (var pou in result.POUs)
{
    var retStr = pou.ReturnTypeName != null ? $" : {pou.ReturnTypeName}" : "";
    Console.WriteLine($"\n  {pou.Kind} {pou.Name}{retStr}");

    if (pou.Parameters.Count > 0)
    {
        Console.WriteLine("  参数:");
        foreach (var p in pou.Parameters)
        {
            var arrow = p.Direction switch
            {
                "VAR_INPUT"  => "← IN ",
                "VAR_OUTPUT" => "→ OUT",
                "VAR_IN_OUT" => "↔ I/O",
                _            => "  VAR"
            };
            Console.WriteLine($"    {arrow}  {p.Name,-16} : {p.TypeName}");
        }
    }
    else
    {
        Console.WriteLine("  参数: (无)");
    }
}

Console.WriteLine();

// ═══════════════════════════════════════════
//  3. Variables
// ═══════════════════════════════════════════

Console.WriteLine("╔══════════════════════════════════════════╗");
Console.WriteLine("║         3. VARIABLES 变量清单            ║");
Console.WriteLine("╚══════════════════════════════════════════╝");

var grouped = result.Variables.GroupBy(v => v.POUName);
foreach (var group in grouped)
{
    Console.WriteLine($"\n  ┌─ {group.Key} ─");
    foreach (var v in group)
    {
        Console.WriteLine($"  │  {v.VarBlockKind,-14} {v.Name,-16} : {v.TypeName}");
    }
    Console.WriteLine("  └─");
}

Console.WriteLine();

// ═══════════════════════════════════════════
//  4. CustomTypes
// ═══════════════════════════════════════════

Console.WriteLine("╔══════════════════════════════════════════╗");
Console.WriteLine("║         4. CUSTOM TYPES 自定义类型       ║");
Console.WriteLine("╚══════════════════════════════════════════╝");

foreach (var t in result.CustomTypes)
{
    Console.WriteLine($"\n  TYPE {t.Name} ({t.Kind})");

    if (t.Kind == TypeKind.Struct && t.Fields != null)
    {
        Console.WriteLine("  STRUCT");
        foreach (var f in t.Fields)
            Console.WriteLine($"    {f.Name,-16} : {f.TypeName}");
        Console.WriteLine("  END_STRUCT");
    }
    else if (t.Kind == TypeKind.Enum && t.EnumMembers != null)
    {
        Console.WriteLine($"  ({string.Join(", ", t.EnumMembers)})");
    }
    else if (t.Kind == TypeKind.SimpleType)
    {
        Console.WriteLine($"  := {t.UnderlyingType}");
    }
}

Console.WriteLine();

// ═══════════════════════════════════════════
//  5. CallSites
// ═══════════════════════════════════════════

Console.WriteLine("╔══════════════════════════════════════════╗");
Console.WriteLine("║         5. CALL SITES 调用关系           ║");
Console.WriteLine("╚══════════════════════════════════════════╝");

foreach (var call in result.CallSites)
{
    Console.WriteLine($"\n  {call.CallerName} → {call.CalleeName}  (行 {call.Line}, {call.ArgumentCount} 个参数)");

    if (call.Arguments.Count == 0)
    {
        Console.WriteLine("    (无参数)");
        continue;
    }

    Console.WriteLine("    ┌──────────────┬────────┬─────────┬──────────┬──────────────────┐");
    Console.WriteLine("    │ 形参名       │ 方向   │ 绑定    │ 形参类型 │ 实参             │");
    Console.WriteLine("    ├──────────────┼────────┼─────────┼──────────┼──────────────────┤");

    foreach (var arg in call.Arguments)
    {
        var dir = arg.Direction switch
        {
            ParameterDirection.Input  => "INPUT ",
            ParameterDirection.Output => "OUTPUT",
            ParameterDirection.InOut  => "IN/OUT",
            _                         => "  ?   "
        };

        var bind = arg.Binding switch
        {
            ArgumentBindingStyle.NamedInput  => "  :=   ",
            ArgumentBindingStyle.NamedOutput => "  =>   ",
            ArgumentBindingStyle.Positional  => "  pos  ",
            _                                => "  ?    "
        };

        var name   = (arg.ParameterName     ?? "?").PadRight(12);
        var pType  = (arg.ParameterTypeName  ?? "?").PadRight(8);
        var summary = arg.ExpressionSummary.PadRight(16);

        Console.WriteLine($"    │ {name} │ {dir} │ {bind} │ {pType} │ {summary} │");
    }

    Console.WriteLine("    └──────────────┴────────┴─────────┴──────────┴──────────────────┘");
}

Console.WriteLine();

// ═══════════════════════════════════════════
//  6. References
// ═══════════════════════════════════════════

Console.WriteLine("╔══════════════════════════════════════════╗");
Console.WriteLine("║         6. REFERENCES 符号引用           ║");
Console.WriteLine("╚══════════════════════════════════════════╝");

var refGrouped = result.References
    .GroupBy(r => r.SymbolName)
    .OrderBy(g => g.Key);

foreach (var group in refGrouped)
{
    Console.WriteLine($"\n  {group.Key} ({group.First().Kind})");
    foreach (var r in group)
    {
        var usage = r.Usage switch
        {
            ReferenceUsage.Declaration => "定义",
            ReferenceUsage.Read        => "读取",
            ReferenceUsage.Write       => "写入",
            ReferenceUsage.Call        => "调用",
            ReferenceUsage.TypeUsage   => "类型",
            _ => "?"
        };
        var loc = $"行{r.Line}:列{r.Column}";
        Console.WriteLine($"    {usage,-4}  {loc,-12}  POU={r.POUName ?? "(全局)"}");
    }
}

Console.WriteLine();

// ═══════════════════════════════════════════
//  7. 交互式测试
// ═══════════════════════════════════════════

Console.WriteLine("╔══════════════════════════════════════════╗");
Console.WriteLine("║         7. INTERACTIVE 交互测试          ║");
Console.WriteLine("╚══════════════════════════════════════════╝");
Console.WriteLine();
Console.WriteLine("  输入符号名查询信息（输入 q 退出）：");

while (true)
{
    Console.Write("\n  > ");
    var input = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(input) || input == "q") break;

    var found = false;

    // 查变量
    var v = result.Variables.FirstOrDefault(
        x => x.Name.Equals(input, StringComparison.OrdinalIgnoreCase));
    if (v != null)
    {
        Console.WriteLine($"  [变量] {v.Name} : {v.TypeName}");
        Console.WriteLine($"         {v.VarBlockKind} | POU={v.POUName} | 行 {v.Line}");
        found = true;
    }

    // 查 POU
    var p = result.POUs.FirstOrDefault(
        x => x.Name.Equals(input, StringComparison.OrdinalIgnoreCase));
    if (p != null)
    {
        var ret = p.ReturnTypeName != null ? $" : {p.ReturnTypeName}" : "";
        Console.WriteLine($"  [POU]  {p.Kind} {p.Name}{ret}");
        foreach (var param in p.Parameters)
            Console.WriteLine($"         {param.Direction,-14} {param.Name} : {param.TypeName}");
        found = true;
    }

    // 查类型
    var t = result.CustomTypes.FirstOrDefault(
        x => x.Name.Equals(input, StringComparison.OrdinalIgnoreCase));
    if (t != null)
    {
        Console.WriteLine($"  [类型] {t.Kind} {t.Name}");
        if (t.Fields != null)
            foreach (var f in t.Fields)
                Console.WriteLine($"         字段: {f.Name} : {f.TypeName}");
        if (t.EnumMembers != null)
            Console.WriteLine($"         成员: {string.Join(", ", t.EnumMembers)}");
        if (t.UnderlyingType != null)
            Console.WriteLine($"         底层类型: {t.UnderlyingType}");
        found = true;
    }

    // 查引用
    var refs = result.References.Where(
        r => r.SymbolName.Equals(input, StringComparison.OrdinalIgnoreCase)).ToList();
    if (refs.Count > 0)
    {
        if (!found) Console.WriteLine($"  [符号] {input}");
        Console.WriteLine($"         共 {refs.Count} 处引用:");
        foreach (var r in refs)
        {
            var usage = r.Usage switch
            {
                ReferenceUsage.Declaration => "定义",
                ReferenceUsage.Read        => "读取",
                ReferenceUsage.Write       => "写入",
                ReferenceUsage.Call        => "调用",
                ReferenceUsage.TypeUsage   => "类型",
                _ => "?"
            };
            Console.WriteLine($"           {usage} @ 行{r.Line}:列{r.Column} (POU={r.POUName ?? "全局"})");
        }
        found = true;
    }

    if (!found)
        Console.WriteLine($"  未找到符号 '{input}'");
}
