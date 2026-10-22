using Avalonia.Controls;
using Avalonia.Controls.Templates;
using SubtitleStudio.App.ViewModels;

namespace SubtitleStudio.App;

/// <summary>按命名约定把 ViewModel 映射到 View（Dock 面板内容靠它渲染）。</summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null)
            return null;

        var name = data.GetType().FullName!.Replace("ViewModels", "Views", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        if (type is null)
            return new TextBlock { Text = "Not Found: " + name };

        return (Control)Activator.CreateInstance(type)!;
    }

    /// <summary>Dock 的面板模型都来自 ViewModels 命名空间；AppState 自己不是面板内容。</summary>
    public bool Match(object? data) =>
        data is not null
        && data is not AppState
        && data.GetType().Namespace == typeof(AppState).Namespace;
}
