using Avalonia.Controls;
using Avalonia.Interactivity;
using SubtitleStudio.App.Services;
using SubtitleStudio.App.ViewModels;

namespace SubtitleStudio.App.Views;

public partial class SubtitleListView : UserControl
{
    public SubtitleListView()
    {
        InitializeComponent();
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SubtitleListViewModel vm)
            return;

        var state = vm.State;
        if (state.Cues.Count == 0)
        {
            state.Status = "当前音轨没有字幕";
            return;
        }

        UiActions.SafeRun("清空字幕失败", async () =>
        {
            var ok = await Dialogs.ConfirmAsync(
                Dialogs.OwnerOf(this),
                Loc.T("Tracks.Clear.Title"),
                Loc.T("Tracks.Clear.Message", state.SelectedTrack?.Info.Number ?? "当前音轨", state.Cues.Count),
                Loc.T("Tracks.Clear.Yes"),
                Loc.T("Common.Cancel"));

            if (!ok)
                return;

            state.ClearCues();
        }, state);
    }
}
