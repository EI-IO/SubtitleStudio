using Avalonia.Controls;
using Avalonia.Interactivity;
using SubtitleStudio.App.Services;
using SubtitleStudio.App.ViewModels;
using SubtitleStudio.Core.Export;

namespace SubtitleStudio.App.Views;

public partial class PreviewView : UserControl
{
    public PreviewView()
    {
        InitializeComponent();
    }

    private AppState? State => (DataContext as PreviewViewModel)?.State;

    private void OnExportDefault(object? sender, RoutedEventArgs e)
    {
        if (State is { } state)
            UiActions.SafeRun(Loc.T("Error.Export"), () => state.ExportOverlayAsync(OverlayFormat.WebmVp9Alpha), state);
    }

    private void OnExportOverlay(object? sender, RoutedEventArgs e)
    {
        if (State is not { } state || sender is not MenuItem { Tag: string tag })
            return;

        var format = tag switch
        {
            "mov" => OverlayFormat.MovProRes4444,
            "mp4" => OverlayFormat.Mp4H264,
            _ => OverlayFormat.WebmVp9Alpha
        };

        UiActions.SafeRun(Loc.T("Error.Export"), () => state.ExportOverlayAsync(format), state);
    }

    private void OnExportSubtitle(object? sender, RoutedEventArgs e)
    {
        if (State is not { } state || sender is not MenuItem { Tag: string tag })
            return;

        UiActions.SafeRun(Loc.T("Error.ExportSubtitles"), () => state.ExportSubtitlesAsync(tag), state);
    }
}
