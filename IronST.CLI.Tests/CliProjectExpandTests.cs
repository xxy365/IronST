// CliProjectExpandTests.cs — 附加功能：ironst expand <file.stproj> 源码展开。
//
// 展开是**独立**功能：build / run 都不依赖它，也不存在"先生成合并 .st 再编译"的路径。
// 本文件验证两件事：
//   1. 展开产物是单个 .st 文件，且**实际能被既有单文件编译路径编译并执行**；
//   2. 执行结果与原项目运行一致（不是"假定语义正确"）。
using IronST.CLI.Commands;

namespace IronST.CLI.Tests;

[Collection("cli-console")]
public class CliProjectExpandTests : IDisposable
{
    private readonly string _dir;

    public CliProjectExpandTests()
    {
        _dir = Path.Combine(
            Path.GetTempPath(), "ironst-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // 清理失败不影响用例结论
        }
    }

    // ── 基础设施 ──────────────────────────────────────────────

    private string WriteIn(string relativePath, string content)
    {
        var path = Path.Combine(_dir, relativePath);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, content);
        return path;
    }

    private static (int Exit, string Out, string Err) Invoke(params string[] args)
    {
        var outW = new StringWriter();
        var errW = new StringWriter();
        var prevOut = Console.Out;
        var prevErr = Console.Error;
        try
        {
            Console.SetOut(outW);
            Console.SetError(errW);
            int exit = CliRouter.Run(args);
            return (exit, outW.ToString(), errW.ToString());
        }
        finally
        {
            Console.SetOut(prevOut);
            Console.SetError(prevErr);
        }
    }

    private static string[] Lines(string output) =>
        output.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

    private static void AssertLinesInOrder(string output, params string[] expected)
    {
        int idx = 0;
        foreach (var line in Lines(output))
        {
            if (idx < expected.Length && line == expected[idx])
                idx++;
        }
        Assert.Equal(expected.Length, idx);
    }

    // ── 源文件片段（与 CliProjectTests 同构，刻意重复以保持用例自足）──

    private const string MathSt = """
        FUNCTION Add : DINT
        VAR_INPUT a : DINT; b : DINT; END_VAR
        Add := a + b;
        END_FUNCTION
        """;

    private const string TypesSt = """
        TYPE Point : STRUCT
        x : DINT;
        y : DINT;
        END_STRUCT;
        END_TYPE;
        """;

    private const string MainSt = """
        PROGRAM Main
        VAR
            p : Point;
            total : DINT;
            s : STRING;
        END_VAR
            p.x := 40;
            p.y := 2;
            Print(p.x);
            total := Add(40, 2);
            Print(total);
            s := CONCAT('proj', 'ok');
            Print(s);
        END_PROGRAM
        """;

    private static readonly string[] ProjectOutput = ["40", "42", "projok"];

    private const string AlphaSt = """
        PROGRAM Alpha
        VAR a : DINT; END_VAR
        a := 111; Print(a);
        END_PROGRAM
        """;

    private const string BetaSt = """
        PROGRAM Beta
        VAR b : DINT; END_VAR
        b := 222; Print(b);
        END_PROGRAM
        """;

    private string WriteSampleProject(string projectFileName = "MyScript.stproj")
    {
        WriteIn("src/Main.st", MainSt);
        WriteIn("src/Math.st", MathSt);
        WriteIn("src/Types.st", TypesSt);
        return WriteIn(projectFileName, """
            <?xml version="1.0" encoding="utf-8"?>
            <STProject Version="1">
              <PropertyGroup>
                <Name>MyScript</Name>
                <StartupProgram>Main</StartupProgram>
                <OutputPath>dist/MyScript.isb</OutputPath>
              </PropertyGroup>
              <ItemGroup>
                <STCompile Include="src/Main.st" />
                <STCompile Include="src/Math.st" />
                <STCompile Include="src/Types.st" />
              </ItemGroup>
            </STProject>
            """);
    }

    // ═══════════════════════════════════════════════════════════
    //  1. 展开为单个 .st 文件
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Expand_ValidProject_WritesSingleSourceFile()
    {
        var proj = WriteSampleProject();
        var outFile = Path.Combine(_dir, "merged.st");

        var (exit, @out, err) = Invoke("expand", proj, "-o", outFile);

        Assert.Equal(0, exit);
        Assert.Equal("", err);
        Assert.True(File.Exists(outFile));
        Assert.Contains("展开成功", @out);

        var text = File.ReadAllText(outFile);
        // 三个源文件的声明都必须在展开结果里。
        Assert.Contains("FUNCTION Add", text);
        Assert.Contains("TYPE Point", text);
        Assert.Contains("PROGRAM Main", text);
    }

    // ═══════════════════════════════════════════════════════════
    //  2. 展开产物必须能独立编译执行，且行为与原项目一致
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Expand_Result_CompilesAndRunsIdenticallyToProject()
    {
        var proj = WriteSampleProject();
        var outFile = Path.Combine(_dir, "merged.st");

        var (eExit, _, eErr) = Invoke("expand", proj, "-o", outFile);
        Assert.Equal(0, eExit);
        Assert.Equal("", eErr);

        // 走既有单文件编译路径实际执行展开产物。
        var (sExit, sOut, sErr) = Invoke("run", outFile);
        var (pExit, pOut, _) = Invoke("run", proj);

        Assert.Equal(0, sExit);
        Assert.Equal("", sErr);
        AssertLinesInOrder(sOut, ProjectOutput);

        Assert.Equal(0, pExit);
        AssertLinesInOrder(pOut, ProjectOutput);
    }

    // ═══════════════════════════════════════════════════════════
    //  3. 多 PROGRAM 项目：展开产物配合 --entry 仍可执行
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Expand_MultiProgramProject_RunsWithExplicitEntry()
    {
        WriteIn("src/alpha.st", AlphaSt);
        WriteIn("src/beta.st", BetaSt);
        var proj = WriteIn("two.stproj", """
            <?xml version="1.0" encoding="utf-8"?>
            <STProject Version="1">
              <PropertyGroup>
                <Name>Two</Name>
                <StartupProgram>Alpha</StartupProgram>
              </PropertyGroup>
              <ItemGroup>
                <STCompile Include="src/alpha.st" />
                <STCompile Include="src/beta.st" />
              </ItemGroup>
            </STProject>
            """);
        var outFile = Path.Combine(_dir, "two.st");

        var (eExit, _, _) = Invoke("expand", proj, "-o", outFile);
        Assert.Equal(0, eExit);

        // ST 无入口指令，多 PROGRAM 的单文件需显式 --entry（与多文件语义一致）。
        var (rExit, rOut, rErr) = Invoke("run", outFile, "--entry", "Alpha");

        Assert.Equal(0, rExit);
        Assert.Equal("", rErr);
        AssertLinesInOrder(rOut, "111");
        Assert.DoesNotContain("222", rOut);
    }

    // ═══════════════════════════════════════════════════════════
    //  4. 默认输出路径 = 项目目录 / <Name>.st
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Expand_WithoutDashO_WritesNextToProjectFile()
    {
        var proj = WriteSampleProject();

        var (exit, @out, err) = Invoke("expand", proj);

        Assert.Equal(0, exit);
        Assert.Equal("", err);
        Assert.True(File.Exists(Path.Combine(_dir, "MyScript.st")));
        Assert.Contains("展开成功", @out);
    }

    // ═══════════════════════════════════════════════════════════
    //  5. 输入校验
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Expand_NonProjectInput_FailsClearly()
    {
        var st = WriteIn("plain.st", MainSt);

        var (exit, _, err) = Invoke("expand", st);

        Assert.Equal(1, exit);
        Assert.Contains(".stproj", err);
    }

    [Fact]
    public void Expand_ProjectFileMissing_FailsClearly()
    {
        var ghost = Path.Combine(_dir, "ghost.stproj");

        var (exit, _, err) = Invoke("expand", ghost);

        Assert.Equal(1, exit);
        Assert.Contains("ghost.stproj", err);
    }
}
