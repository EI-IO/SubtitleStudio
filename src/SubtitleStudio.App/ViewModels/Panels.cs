using Dock.Model.Mvvm.Controls;

namespace SubtitleStudio.App.ViewModels;

// 三个面板本身没有状态，只是把共享的 AppState 交给视图绑定；
// 带交互的样式面板在 StyleViewModel.cs。

/// <summary>字幕列表面板：只负责把共享的 <see cref="AppState"/> 交给视图。</summary>
public sealed class SubtitleListViewModel : Tool
{
    public SubtitleListViewModel(AppState state) => State = state;

    public AppState State { get; }
}

/// <summary>时间轴面板：识别/播放/模型菜单的按钮都挂在这里，状态仍来自同一份 <see cref="AppState"/>。</summary>
public sealed class TimelineViewModel : Tool
{
    public TimelineViewModel(AppState state) => State = state;

    public AppState State { get; }
}

/// <summary>预览面板：显示当前音轨的渲染效果与导出尺寸。</summary>
public sealed class PreviewViewModel : Tool
{
    public PreviewViewModel(AppState state) => State = state;

    public AppState State { get; }
}
