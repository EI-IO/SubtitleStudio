using Dock.Model.Mvvm.Controls;

namespace SubtitleStudio.App.ViewModels;

// 三个面板本身没有状态，只是把共享的 AppState 交给视图绑定；
// 带交互的样式面板在 StyleViewModel.cs。

public sealed class SubtitleListViewModel : Tool
{
    public SubtitleListViewModel(AppState state) => State = state;

    public AppState State { get; }
}

public sealed class TimelineViewModel : Tool
{
    public TimelineViewModel(AppState state) => State = state;

    public AppState State { get; }
}

public sealed class PreviewViewModel : Tool
{
    public PreviewViewModel(AppState state) => State = state;

    public AppState State { get; }
}
