// ProjectWorkspaceTests.cs — IDE 工程工作区（阶段 II）。
//
// 契约：
//   · 新建工程 = 复用 IronST.CLI 的 STProjectWriter（不重复实现 .stproj 格式），
//     建完即作为"当前工程"打开；
//   · 解决方案树按工程内容生成：根 = 工程，子 = 源文件（可双击打开）+ 模块引用
//     （非源文件、**不带** FilePath，避免双击把二进制 .stmod 当文本打开）；
//   · 打开非法/不存在的工程 → ProjectFileException，当前工程保持不变。
using System.IO;
using IronST.CLI;
using IronST.IDE.Services;

namespace IronST.IDE.Tests;

public class ProjectWorkspaceTests : IDisposable
{
    private readonly string _dir;

    public ProjectWorkspaceTests()
    {
        _dir = Path.Combine(
            Path.GetTempPath(), "ironst-ide-project", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
    }

    private string NewProjectPath(string name) => Path.Combine(_dir, name, name + ".stproj");

    // ═══════════════════════════════════════════════════════════
    //  1. 新建工程 → 落盘 + 成为当前工程
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Create_WritesProjectOnDisk_AndOpensItAsCurrent()
    {
        var workspace = new ProjectWorkspace();
        var projectPath = NewProjectPath("App");

        var created = workspace.Create(projectPath);

        Assert.Equal(Path.GetFullPath(projectPath), created);
        Assert.True(File.Exists(created));
        Assert.NotNull(workspace.Project);
        Assert.Equal("App", workspace.ProjectName);
        Assert.Equal(created, workspace.ProjectPath);
        Assert.True(workspace.HasProject);
    }

    [Fact]
    public void Create_RefusesExistingProject()
    {
        var workspace = new ProjectWorkspace();
        var projectPath = NewProjectPath("Twice");
        workspace.Create(projectPath);

        Assert.Throws<ProjectFileException>(() => workspace.Create(projectPath));
    }

    [Fact]
    public void Create_WithoutStprojExtension_IsRejected()
    {
        var workspace = new ProjectWorkspace();

        Assert.Throws<ProjectFileException>(
            () => workspace.Create(Path.Combine(_dir, "NoExt", "NoExt")));
        Assert.False(workspace.HasProject);
    }

    // ═══════════════════════════════════════════════════════════
    //  2. 解决方案树
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void BuildTree_RootIsProject_ChildrenAreSourceFiles()
    {
        var workspace = new ProjectWorkspace();
        var projectPath = workspace.Create(NewProjectPath("TreeApp"));

        var nodes = workspace.BuildTree();

        var root = Assert.Single(nodes);
        Assert.Equal("TreeApp", root.Name);
        Assert.True(root.IsFolder);

        var child = Assert.Single(root.Children);
        Assert.Equal("Main.st", child.Name);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(_dir, "TreeApp", "Main.st")), child.FilePath);
        Assert.False(child.IsFolder);
    }

    [Fact]
    public void BuildTree_ListsModuleReferences_WithoutOpenablePath()
    {
        // 先造一个真实的 .stmod（内容不重要，引用校验只看存在 + 扩展名）。
        var projDir = Path.Combine(_dir, "WithRef");
        Directory.CreateDirectory(Path.Combine(projDir, "lib"));
        File.WriteAllBytes(Path.Combine(projDir, "lib", "Math.stmod"), [0x00]);
        File.WriteAllText(Path.Combine(projDir, "Main.st"), "PROGRAM Main\nEND_PROGRAM\n");

        var projectPath = Path.Combine(projDir, "WithRef.stproj");
        File.WriteAllText(projectPath, STProjectWriter.WriteXml(new STProjectTemplate
        {
            Name = "WithRef",
            StartupProgram = "Main",
            ModuleReferences = ["lib/Math.stmod"],
        }));

        var workspace = new ProjectWorkspace();
        workspace.Open(projectPath);

        var root = Assert.Single(workspace.BuildTree());
        var moduleNode = Assert.Single(root.Children, n => n.Name.Contains("Math.stmod"));

        // 模块引用不是可打开的源文件：双击必须不触发"按文本打开"。
        Assert.Equal("", moduleNode.FilePath);
        Assert.False(moduleNode.IsFolder);
    }

    [Fact]
    public void BuildTree_WithoutProject_IsEmpty()
    {
        var workspace = new ProjectWorkspace();

        Assert.Empty(workspace.BuildTree());
        Assert.False(workspace.HasProject);
        Assert.Null(workspace.ProjectPath);
    }

    // ═══════════════════════════════════════════════════════════
    //  3. 打开 / 关闭
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Open_NonExistentProject_Throws_AndKeepsCurrent()
    {
        var workspace = new ProjectWorkspace();
        var projectPath = workspace.Create(NewProjectPath("Kept"));

        Assert.Throws<ProjectFileException>(
            () => workspace.Open(Path.Combine(_dir, "Ghost.stproj")));

        // 打开失败不得把已打开的工程弄丢。
        Assert.Equal(projectPath, workspace.ProjectPath);
    }

    [Fact]
    public void Open_InvalidXml_Throws()
    {
        var workspace = new ProjectWorkspace();
        var broken = Path.Combine(_dir, "Broken.stproj");
        File.WriteAllText(broken, "<STProject Version=\"1\"><PropertyGroup></STProject>");

        Assert.Throws<ProjectFileException>(() => workspace.Open(broken));
    }

    [Fact]
    public void Close_ClearsCurrentProject()
    {
        var workspace = new ProjectWorkspace();
        workspace.Create(NewProjectPath("Closable"));

        workspace.Close();

        Assert.False(workspace.HasProject);
        Assert.Null(workspace.ProjectPath);
        Assert.Empty(workspace.BuildTree());
    }
}
