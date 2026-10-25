using Avalonia.Controls;
using Avalonia.Interactivity;
using SubtitleStudio.App.Services;
using SubtitleStudio.App.ViewModels;

namespace SubtitleStudio.App.Views;

/// <summary>字幕列表面板：改字由双向绑定完成，这里只处理「清空」的确认流程。</summary>
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
            state.Status = Loc.T("SubtitleList.None");
            return;
        }

        UiActions.SafeRun(Loc.T("Error.ClearCues"), async () =>
        {
            var ok = await Dialogs.ConfirmAsync(
                Dialogs.OwnerOf(this),
                Loc.T("Tracks.Clear.Title"),
                Loc.T("Tracks.Clear.Message", state.SelectedTrack?.Info.Number ?? Loc.T("Label.CurrentTrack"), state.Cues.Count),
                Loc.T("Tracks.Clear.Yes"),
                Loc.T("Common.Cancel"));

            if (!ok)
                return;

            state.ClearCues();
        }, state);
    }
}
