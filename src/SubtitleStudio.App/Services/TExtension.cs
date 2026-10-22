using Avalonia.Markup.Xaml;

namespace SubtitleStudio.App.Services;

/// <summary>
/// XAML 里用 <c>Text="{loc:T Menu.Settings}"</c> 取文案。
/// 目前语言切换后需要重开窗口才会刷新（后续可换成动态资源绑定）。
/// </summary>
public sealed class TExtension : MarkupExtension
{
    public TExtension()
    {
    }

    public TExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}
