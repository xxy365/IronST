using IronST.LanguageServer.Features;
using IronST.LanguageServer.Protocol;

namespace IronST.LanguageServer.Tests;

public class SignatureHelpTests
{
    [Fact]
    public void 光标在括号内_给出被调函数签名()
    {
        var source = """
            FUNCTION Add : DINT
            VAR_INPUT
                a : DINT;
                b : DINT;
            END_VAR
            Add := a + b;
            END_FUNCTION

            PROGRAM Main
            VAR
                sum : DINT;
            END_VAR
            sum := Add(1, 2);
            END_PROGRAM
            """;

        var snap = TestHelpers.Snapshot(source);
        Assert.True(snap.Result.Success, string.Join("; ", snap.Result.Diagnostics));

        // 光标放在 "Add(1, 2)" 的第二个参数上
        var offset = TestHelpers.OffsetOf(source, "Add(1, 2)") + "Add(1, ".Length;
        var help = SignatureHelpProvider.SignatureHelp(snap, offset);

        Assert.NotNull(help);
        var signatures = help!["signatures"]!.AsArray();
        Assert.Single(signatures);
        Assert.Contains("Add", signatures[0]!["label"]!.GetValue<string>());
        Assert.Equal(1, help!["activeParameter"]!.GetValue<int>());
    }

    [Fact]
    public void 光标在第一个参数_返回0()
    {
        var source = """
            PROGRAM Main
            VAR
                r : DINT;
            END_VAR
            r := ABS(-5);
            END_PROGRAM
            """;

        var snap = TestHelpers.Snapshot(source);
        var offset = TestHelpers.OffsetOf(source, "ABS(-5)") + "ABS(".Length;
        var help = SignatureHelpProvider.SignatureHelp(snap, offset);

        Assert.NotNull(help);
        Assert.Equal(0, help!["activeParameter"]!.GetValue<int>());
        Assert.Contains("ABS", help!["signatures"]!.AsArray()[0]!["label"]!.GetValue<string>());
    }

    [Fact]
    public void 光标不在调用中_返回null()
    {
        var source = """
            PROGRAM Main
            VAR
                r : DINT;
            END_VAR
            r := 1;
            END_PROGRAM
            """;

        var snap = TestHelpers.Snapshot(source);
        var offset = TestHelpers.OffsetOf(source, "r := 1") + 2;
        Assert.Null(SignatureHelpProvider.SignatureHelp(snap, offset));
    }

    [Fact]
    public void 注释里的括号_不会被误认为调用()
    {
        var source = """
            PROGRAM Main
            VAR
                r : DINT;
            END_VAR
            // 这里有个括号 (
            r := 1;
            END_PROGRAM
            """;

        var snap = TestHelpers.Snapshot(source);
        // 光标放在注释的括号之后那一行的行首
        var offset = TestHelpers.OffsetOf(source, "r := 1");
        Assert.Null(SignatureHelpProvider.SignatureHelp(snap, offset));
    }

    [Fact]
    public void 字符串里的括号_不会被误认为调用()
    {
        var source = """
            PROGRAM Main
            VAR
                s : STRING;
            END_VAR
            s := 'a(b';
            END_PROGRAM
            """;

        var snap = TestHelpers.Snapshot(source);
        // 光标放在字符串之后
        var offset = TestHelpers.OffsetOf(source, "s := 'a(b';") + "s := 'a(b';".Length;
        Assert.Null(SignatureHelpProvider.SignatureHelp(snap, offset));
    }
}
