// ProjectWorkspaceAddItemTests.cs — IDE 往工程里加文件（阶段 IV）。
//
// 契约：
//   · 「新增项」= 在工程目录内造一个可编译的 .st，并登记进 .stproj；
//   · 「添加现有项」= .st → 源文件、.stmod → 模块引用；工程外的文件先复制进工程目录，
//     使工程保持自足；
//   · 包装进工程后必须重新加载当前工程（解决方案树随之更新）；
//   · 没有打开工程、扩展名不支持、重复添加都要明确报错。
using System.IO;
using IronST.CLI;
using IronST.IDE.Services;

namespace IronST.IDE.Tests;

public class ProjectWorkspaceAddItemTests : IDisposable
{
    private readonly string _dir;

    public ProjectWorkspaceAddItemTests()
    {
        _dir = Path.Combine(
            Path.GetTempPath(), "ironst-ide-add", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
    }

    private (ProjectWorkspace Workspace, string ProjectPath) NewProject(string name)
    {
        var workspace = new ProjectWorkspace();
        var projectPath = workspace.Create(Path.Combine(_dir, name, name + ".stproj"));
        return (workspace, projectPath);
    }

    // ═══════════════════════════════════════════════════════════
    //  新增项
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void AddNewSource_CreatesFile_AndRegistersItInProject()
    {
        var (workspace, _) = NewProject("NewItem");
        var newFile = Path.Combine(_dir, "NewItem", "Motor.st");

        var added = workspace.AddNewSource(newFile);

        Assert.Equal(Path.GetFullPath(newFile), added);
        Assert.True(File.Exists(added));
        Assert.Contains("PROGRAM Motor", File.ReadAllText(added));

        // 当前工程已重载：两个源文件，树里也都出现。
        Assert.NotNull(workspace.Project);
        Assert.Equal(2, workspace.Project!.SourceFiles.Count);
        var root = Assert.Single(workspace.BuildTree());
        Assert.Contains(root.Children, n => n.Name == "Motor.st");
    }

    [Fact]
    public void AddNewSource_OutsideProjectDirectory_IsRejected()
    {
        var (workspace, _) = NewProject("Outside");

        Assert.Throws<ProjectFileException>(
            () => workspace.AddNewSource(Path.Combine(_dir, "Elsewhere.st")));
    }

    [Fact]
    public void AddNewSource_ExistingFile_IsRejected()
    {
        var (workspace, _) = NewProject("Already");

        Assert.Throws<ProjectFileException>(
            () => workspace.AddNewSource(Path.Combine(_dir, "Already", "Main.st")));
    }

    // ═══════════════════════════════════════════════════════════
    //  添加现有项
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void AddExistingSource_InsideProject_IsRegisteredInPlace()
    {
        var (workspace, _) = NewProject("InPlace");
        var projDir = Path.Combine(_dir, "InPlace");
        var source = Path.Combine(projDir, "Shared.st");
        File.WriteAllText(source, "FUNCTION Shared : DINT\nEND_FUNCTION\n");

        var added = workspace.AddExistingFile(source);

        Assert.Equal(Path.GetFullPath(source), added);
        Assert.Equal(2, workspace.Project!.SourceFiles.Count);
    }

    [Fact]
    public void AddExistingSource_OutsideProject_IsCopiedIntoProjectDirectory()
    {
        var (workspace, _) = NewProject("Copy");
        var projDir = Path.Combine(_dir, "Copy");
        var outside = Path.Combine(_dir, "Common.st");
        File.WriteAllText(outside, "FUNCTION Common : DINT\nCommon := 7;\nEND_FUNCTION\n");

        var added = workspace.AddExistingFile(outside);

        var expected = Path.GetFullPath(Path.Combine(projDir, "Common.st"));
        Assert.Equal(expected, added);
        Assert.True(File.Exists(expected), "工程外的文件应先复制进工程目录");
        Assert.Contains("Common := 7;", File.ReadAllText(expected));
        Assert.Equal(2, workspace.Project!.SourceFiles.Count);
    }

    [Fact]
    public void AddExistingFile_Module_BecomesModuleReference_AndShowsInTree()
    {
        var (workspace, _) = NewProject("WithModule");
        var projDir = Path.Combine(_dir, "WithModule");
        var module = Path.Combine(projDir, "Math.stmod");
        File.WriteAllBytes(module, [0x00, 0x01]);

        var added = workspace.AddExistingFile(module);

        Assert.Equal(Path.GetFullPath(module), added);
        Assert.Single(workspace.Project!.SourceFiles);              // 源文件没变（仍只有 Main.st）
        Assert.Single(workspace.Project!.ModuleReferences);

        var root = Assert.Single(workspace.BuildTree());
        var moduleNode = Assert.Single(root.Children, n => n.Name.Contains("Math.stmod"));
        Assert.Equal("", moduleNode.FilePath);                      // 模块不可当文本打开
    }

    [Fact]
    public void AddExistingFile_UnsupportedExtension_IsRejected()
    {
        var (workspace, _) = NewProject("BadExt");
        var projDir = Path.Combine(_dir, "BadExt");
        var text = Path.Combine(projDir, "notes.txt");
        File.WriteAllText(text, "hi");

        Assert.Throws<ProjectFileException>(() => workspace.AddExistingFile(text));
    }

    [Fact]
    public void AddExistingFile_Duplicate_IsRejected()
    {
        var (workspace, _) = NewProject("Twice");
        var projDir = Path.Combine(_dir, "Twice");

        Assert.Throws<ProjectFileException>(
            () => workspace.AddExistingFile(Path.Combine(projDir, "Main.st")));
    }

    // ═══════════════════════════════════════════════════════════
    //  没有打开工程
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void WithoutOpenProject_IsRejected()
    {
        var workspace = new ProjectWorkspace();
        var file = Path.Combine(_dir, "Loose.st");
        File.WriteAllText(file, "PROGRAM Loose\nEND_PROGRAM\n");

        Assert.Throws<ProjectFileException>(() => workspace.AddNewSource(file));
        Assert.Throws<ProjectFileException>(() => workspace.AddExistingFile(file));
    }
}
