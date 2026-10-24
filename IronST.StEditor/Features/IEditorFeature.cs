using ICSharpCode.AvalonEdit;

namespace IronST.StEditor.Features;

/// <summary>
/// 编辑器功能模块接口。
/// 只处理 TextEntered 事件，TextEntering 由 MainWindow 直接处理。
/// </summary>
public interface IEditorFeature
{
    /// <summary>文本输入后调用。返回 true 表示已处理（不影响其他模块）。</summary>
    bool OnTextEntered(TextEditor editor, string inputText);
}

/// <summary>功能模块基类：默认不处理。</summary>
public abstract class EditorFeatureBase : IEditorFeature
{
    public virtual bool OnTextEntered(TextEditor editor, string inputText) => false;
}
