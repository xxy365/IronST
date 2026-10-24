using IronST.Frontend.Analysis;
using IronST.Runtime.Shared;

namespace IronST.CLI.Tests;

/// <summary>
/// CLI 宿主参数视图（HostArgs）的栈参数回归：
/// 第 5 个参数由调用方 PUSH 到当前 SP 之上，读取必须以 SP 为基准。
/// </summary>
public class HostArgsStackTests
{
    [Fact]
    public void HostFunction_FiveArguments_FifthComesFromStack()
    {
        var library = new ExternalLibrary
        {
            Name = "HostArgsStackTests",
            Functions =
            [
                new ExternalFunction
                {
                    Name = "Sum5",
                    ReturnTypeName = "DINT",
                    Parameters = Enumerable.Range(0, 5)
                        .Select(i => new ExternalParameter { Name = $"p{i}", TypeName = "DINT" })
                        .ToList(),
                },
            ],
        };

        long seenFifth = -1;
        var registry = new HostFunctionRegistry();
        registry.Register("Sum5", args =>
        {
            seenFifth = args.Int(4);
            return new BcValue(0L);
        });

        var compiled = IronRuntime.Compile(
            """
            PROGRAM Main
            VAR x : DINT; END_VAR
                x := Sum5(1, 2, 3, 4, 5);
            END_PROGRAM
            """,
            "host-args.st",
            hostFunctions: registry.ToFunctionMap(),
            libraries: [library]);

        IronRuntime.Run(
            compiled,
            bridge => registry.BindAll(bridge.Register),
            cancellation: TestContext.Current.CancellationToken);

        Assert.Equal(5, seenFifth);
    }
}
