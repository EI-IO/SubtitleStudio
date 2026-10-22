using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SubtitleStudio.App.ViewModels;

namespace SubtitleStudio.App.Views;

public partial class StyleView : UserControl
{
    public StyleView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is StyleViewModel vm)
            vm.PickFontFile = PickFontFileAsync;
    }

    private async Task<string?> PickFontFileAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return null;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择字体文件",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("字体文件") { Patterns = new[] { "*.ttf", "*.otf", "*.ttc" } },
                FilePickerFileTypes.All
            }
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
