// ProjectWorkspaceCommandTests.cs — IDE 工程「生成 / 运行」（阶段 III）。
//
// 契约：
//   · 「生成工程」与「运行工程」必须复用 CLI 的 build / run（同一套编译与执行），
//     不许在 IDE 里另建第二套管线；
//   · 编译失败要如实返回非 0，并带上诊断文本，且**不留** .isb；
//   · 没有打开工程时调用 → ProjectFileException。
using System.IO;
using IronST.CLI;
using IronST.IDE.Services;

namespace IronST.IDE.Tests;

/// <summary>
/// 这些用例会让 <see cref="ProjectWorkspace"/> 在进程内跑 CLI 命令（会重定向 Console），
/// 放进独立集合以免与其它用例并行时互相抢 Console。
/// </summary>
[Collection("ide-console")]
public class ProjectWorkspaceCommandTests : IDisposable
{
    private readonly string _dir;

    public ProjectWorkspaceCommandTests()
    {
        _dir = Path.Combine(
            Path.GetTempPath(), "ironst-ide-project-cmd", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
    }

    private ProjectWorkspace NewProject(string name)
    {
        var workspace = new ProjectWorkspace();
        workspace.Create(Path.Combine(_dir, name, name + ".stproj"));
        return workspace;
    }

    // ═══════════════════════════════════════════════════════════
    //  生成工程
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Build_WritesRealIsb_ThatCanBeLoadedBack()
    {
        var workspace = NewProject("Built");
        var isb = Path.Combine(_dir, "Built", "Built.isb");

        var result = workspace.Build();

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(isb), $"应写出 {isb}");
        Assert.Contains("构建成功", result.Output);

        // 产物是真的字节码：能被加载回 BytecodeModule。
        var module = IronRuntime.LoadModule(isb);
        Assert.NotNull(module);
        Assert.True(module.Code.Length > 0);
    }

    [Fact]
    public void Build_CompileError_ReturnsFailure_AndWritesNoIsb()
    {
        var workspace = NewProject("Broken");

        // 把生成的入口源文件改成编不过的程序。
        var main = Path.Combine(_dir, "Broken", "Main.st");
        File.WriteAllText(main, "PROGRAM Main\nVAR x : DINT; END_VAR\n    x := ;\nEND_PROGRAM\n");

        var result = workspace.Build();

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(
            File.Exists(Path.Combine(_dir, "Broken", "Broken.isb")),
            "编译失败不得留下 .isb");
        Assert.NotEqual("", (result.Output + result.Error).Trim());
    }

    // ═══════════════════════════════════════════════════════════
    //  运行工程
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Run_ExecutesProject_AndSurfacesProgramOutput()
    {
        var workspace = NewProject("Ran");

        var result = workspace.Run();

        Assert.Equal(0, result.ExitCode);
        // 生成的模板 Main.st 打印 42；工程的宿主输出必须回到调用方。
        Assert.Contains("42", result.Output);
    }

    [Fact]
    public void Run_CompileError_ReturnsFailure_WithDiagnostics()
    {
        var workspace = NewProject("BadRun");
        File.WriteAllText(
            Path.Combine(_dir, "BadRun", "Main.st"),
            "PROGRAM Main\nVAR x : DINT; END_VAR\n    x := ;\nEND_PROGRAM\n");

        var result = workspace.Run();

        Assert.NotEqual(0, result.ExitCode);
        Assert.NotEqual("", (result.Output + result.Error).Trim());
    }

    // ═══════════════════════════════════════════════════════════
    //  没有打开工程
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Build_WithoutOpenProject_Throws()
    {
        var workspace = new ProjectWorkspace();

        Assert.Throws<ProjectFileException>(() => workspace.Build());
    }

    [Fact]
    public void Run_WithoutOpenProject_Throws()
    {
        var workspace = new ProjectWorkspace();

        Assert.Throws<ProjectFileException>(() => workspace.Run());
    }
}
