// DemoCommand.cs — demo：内置演示（结构体 + 内部函数 + C# 宿主函数混合）
using IronST.Frontend;
using IronST.Frontend.Analysis;
using IronST.Runtime.Shared;

namespace IronST.CLI.Commands;

public static class DemoCommand
{
    public static int Run(string[] args)
    {
        var libraries = new List<ExternalLibrary>
        {
            IECStandardLibrary.Create(),
            new()
            {
                Name = "HostFunctions",
                Functions =
                [
                    new()
                    {
                        Name = "Square",
                        ReturnTypeName = "DINT",
                        Parameters = [new() { Name = "n", TypeName = "DINT" }],
                    },
                    new()
                    {
                        Name = "AbsVal",
                        ReturnTypeName = "DINT",
                        Parameters = [new() { Name = "x", TypeName = "DINT" }],
                    },
                    new()
                    {
                        Name = "CSharpPrint",
                        ReturnTypeName = "DINT",
                        Parameters = [new() { Name = "n", TypeName = "DINT" }],
                    },
                    new()
                    {
                        Name = "PrintPoint",
                        ReturnTypeName = "DINT",
                        Parameters =
                        [
                            new() { Name = "px", TypeName = "DINT" },
                            new() { Name = "py", TypeName = "DINT" },
                        ],
                    },
                ],
            },
        };

        var reg = new HostFunctionRegistry();

        reg.Register(
                "Square",
                (args) => new BcValue(args[0].IntValue * args[0].IntValue)
            )
            .Register(
                "AbsVal",
                (args) =>
                {
                    long x = args.Int(0);
                    return new BcValue(x < 0 ? -x : x);
                }
            )
            .Register(
                "CSharpPrint",
                (args) =>
                {
                    Console.WriteLine($"  [C#] CSharpPrint({args.Int(0)})");
                    return new BcValue(1L);
                }
            )
            .Register(
                "PrintPoint",
                (args) =>
                {
                    var list = args.ToList(2);
                    Console.WriteLine($"  [C#] PrintPoint(x={list[0].IntValue}, y={list[1].IntValue})");
                    return new BcValue(1L);
                }
            );

        const string source = """
            TYPE Point : STRUCT
                x : INT;
                y : INT;
            END_STRUCT; END_TYPE;

            PROGRAM Main
            VAR
                p1       : Point;
                p2       : Point;
                pSum     : Point;
                result   : DINT;
                bigVal   : DINT;
                i        : INT;
            END_VAR

            p1.x := 10;
            p1.y := 20;
            p2.x := 30;
            p2.y := 40;

            pSum.x := p1.x + p2.x;
            pSum.y := p1.y + p2.y;

            result := Square( pSum.x );
            CSharpPrint( result );

            bigVal := p1.x;
            CSharpPrint( bigVal );

            bigVal := p1.x + p2.x;
            CSharpPrint( bigVal );

            result := 0;
            IF bigVal > 50 THEN
                result := 1;
            END_IF
            CSharpPrint( result );

            PrintPoint( pSum.x, pSum.y );

            FOR i := 1 TO 5 BY 1 DO
                p1.x := p1.x + i;
                p1.y := p1.y + i;
            END_FOR

            PrintPoint( p1.x, p1.y );

            bigVal := 999;
            CSharpPrint( bigVal );
            END_PROGRAM
            """;

        try
        {
            var compiled = IronRuntime.Compile(
                source,
                sourceFileName: "struct_demo.st",
                emitDebugInfo: true,
                hostFunctions: reg.ToFunctionMap(),
                libraries: libraries
            );

            IronRuntime.Run(compiled, bridge => reg.BindAll(bridge.Register));
        }
        catch (CompilationException ex)
        {
            Console.Error.WriteLine($"编译失败: {ex.Message}");
            return 1;
        }
        catch (RuntimeErrorException ex)
        {
            // 源位置已由 VM 按 SourceLocator 回填进异常 Message。
            Console.Error.WriteLine($"运行时错误: {ex.Message}");
            return 1;
        }

        Console.WriteLine("✓ Demo 执行完成");
        return 0;
    }
}
