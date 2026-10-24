// AnalyzeCommand.cs — analyze <file.st>：前端分析并打印符号/调用/引用表
using IronST.Common.Diagnostics;
using IronST.Frontend;
using IronST.Frontend.Analysis;

namespace IronST.CLI.Commands;

public static class AnalyzeCommand
{
    public static int Run(string[] args)
    {
        var input = args.FirstOrDefault();
        if (input == null || !File.Exists(input))
        {
            Console.Error.WriteLine($"错误: 找不到文件 '{input}'");
            return 1;
        }

        var result = FrontendPipeline.Analyze(File.ReadAllText(input), input);

        // 1. 诊断
        Console.WriteLine("═══ 1. 诊断 ═══");
        if (result.Diagnostics.Count == 0)
            Console.WriteLine("  (无诊断信息，分析成功)");
        else
            foreach (var d in result.Diagnostics)
                Console.WriteLine($"  [{d.Severity,-7}] {d.Span} {d.Code}: {d.Message}");

        // 2. POU
        Console.WriteLine("\n═══ 2. POUs ═══");
        foreach (var pou in result.POUs)
        {
            var ret = pou.ReturnTypeName != null ? $" : {pou.ReturnTypeName}" : "";
            Console.WriteLine($"  {pou.Kind} {pou.Name}{ret}  ({pou.FilePath}:{pou.Line})");
            foreach (var p in pou.Parameters)
                Console.WriteLine($"      {p.Direction,-10} {p.Name,-16} : {p.TypeName}");
        }

        // 3. 变量
        Console.WriteLine("\n═══ 3. 变量 ═══");
        foreach (var v in result.Variables)
            Console.WriteLine($"  {v.VarBlockKind,-14} {v.Name,-20} : {v.TypeName}  ({v.POUName})");

        // 4. 自定义类型
        Console.WriteLine("\n═══ 4. 自定义类型 ═══");
        foreach (var t in result.CustomTypes)
        {
            switch (t.Kind)
            {
                case TypeKind.Struct when t.Fields != null:
                    Console.WriteLine($"  TYPE {t.Name} : STRUCT");
                    foreach (var f in t.Fields)
                        Console.WriteLine($"      {f.Name,-16} : {f.TypeName}");
                    Console.WriteLine("  END_STRUCT");
                    break;
                case TypeKind.Enum when t.EnumMembers != null:
                    Console.WriteLine($"  TYPE {t.Name} : ({string.Join(", ", t.EnumMembers)})");
                    break;
                default:
                    Console.WriteLine($"  TYPE {t.Name} ({t.Kind})");
                    break;
            }
        }

        // 5. 调用站点
        Console.WriteLine("\n═══ 5. 调用关系 ═══");
        foreach (var call in result.CallSites)
            Console.WriteLine($"  {call.CallerName} → {call.CalleeName}  (行 {call.Line}, {call.ArgumentCount} 个参数)");

        // 6. 引用
        Console.WriteLine("\n═══ 6. 符号引用 ═══");
        foreach (var group in result.References.GroupBy(r => r.SymbolName).OrderBy(g => g.Key))
        {
            Console.WriteLine($"  {group.Key} ({group.First().Kind})");
            foreach (var r in group)
                Console.WriteLine($"      {r.Usage,-12} 行{r.Line}:列{r.Column}  POU={r.POUName ?? "(全局)"}");
        }

        var errors = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        Console.WriteLine($"\n═══ 摘要: {result.POUs.Count} 个 POU, {result.Variables.Count} 个变量, {errors} 个错误 ═══");
        return errors > 0 ? 1 : 0;
    }
}
