using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using SubtitleStudio.App.ViewModels;

namespace SubtitleStudio.App.Views;

public partial class StyleView : UserControl
{
    public StyleView()
    {
        InitializeComponent();

        // 调色板：点开就是色块网格，点一下立即生效并收起；要透明度/精确值就用旁边的十六进制框
        PrimaryPalette.Content = BuildPalette((vm, c) => vm.PrimaryColor = c, PrimaryColorButton);
        OutlinePalette.Content = BuildPalette((vm, c) => vm.OutlineColor = c, OutlineColorButton);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is StyleViewModel vm)
            vm.PickFontFile = PickFontFileAsync;
    }

    /// <summary>生成一块色板：灰阶 + 两档色相环，点色即写回 VM 并关掉弹层。</summary>
    private Control BuildPalette(Action<StyleViewModel, Color> apply, Button host)
    {
        var view = this;
        var panel = new WrapPanel { Width = 288 };

        foreach (var color in Swatches())
        {
            var swatch = new Border
            {
                Width = 20,
                Height = 20,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Color.Parse("#3A3A44")),
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            ToolTip.SetTip(swatch, $"#{color.R:X2}{color.G:X2}{color.B:X2}");

            var captured = color;
            swatch.PointerPressed += (_, _) =>
            {
                if (view.DataContext is StyleViewModel vm)
                    apply(vm, captured);
                (host.Flyout as Flyout)?.Hide();
            };
            panel.Children.Add(swatch);
        }

        return panel;
    }

    /// <summary>8 格灰阶 + 12 色相 × 鲜艳/柔和两档，共 32 色。</summary>
    private static IEnumerable<Color> Swatches()
    {
        for (var i = 0; i < 8; i++)
        {
            var v = (byte)Math.Round(i * 255.0 / 7);
            yield return Color.FromRgb(v, v, v);
        }

        for (var h = 0; h < 12; h++)
            yield return FromHsv(h * 30, 1.0, 1.0);
        for (var h = 0; h < 12; h++)
            yield return FromHsv(h * 30, 0.45, 1.0);
    }

    private static Color FromHsv(double hue, double saturation, double value)
    {
        var c = value * saturation;
        var x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = value - c;

        var (r, g, b) = hue switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private async Task<string?> PickFontFileAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return null;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Services.Loc.T("Dialog.PickFont"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(Services.Loc.T("Dialog.FontFiles")) { Patterns = new[] { "*.ttf", "*.otf", "*.ttc" } },
                FilePickerFileTypes.All
            }
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
