using Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;

namespace SubtitleStudio.App.Docking;

/// <summary>
/// 主界面四区停靠布局：上排「字幕列表 | 时间轴」，中排样式条，下排预览。
/// 比例是调整过的经验值，面板一律不可关闭/浮动/拖动。
/// </summary>
public sealed class DockFactory : Factory
{
    private readonly ViewModels.AppState _state;

    public DockFactory(ViewModels.AppState state) => _state = state;

    public override IRootDock CreateLayout()
    {
        var list = new ViewModels.SubtitleListViewModel(_state) { Id = "SubtitleList", Title = "字幕列表" };
        var timeline = new ViewModels.TimelineViewModel(_state) { Id = "Timeline", Title = "音频与字幕轨道" };
        var style = new ViewModels.StyleViewModel(_state) { Id = "Style", Title = "字幕样式" };
        var preview = new ViewModels.PreviewViewModel(_state) { Id = "Preview", Title = "字幕渲染预览" };

        // 四个面板固定不动，不要关闭叉
        foreach (var tool in new Tool[] { list, timeline, style, preview })
        {
            tool.CanClose = false;
            tool.CanFloat = false;
            tool.CanDrag = false;
        }

        var listDock = new ToolDock
        {
            Id = "ListDock",
            Title = "字幕列表",
            Proportion = 0.27,
            Alignment = Alignment.Left,
            VisibleDockables = CreateList<IDockable>(list),
            ActiveDockable = list
        };

        var timelineDock = new ToolDock
        {
            Id = "TimelineDock",
            Title = "时间轴",
            Proportion = 0.73,
            Alignment = Alignment.Right,
            VisibleDockables = CreateList<IDockable>(timeline),
            ActiveDockable = timeline
        };

        var topRow = new ProportionalDock
        {
            Id = "TopRow",
            Orientation = Orientation.Horizontal,
            Proportion = 0.52,
            VisibleDockables = CreateList<IDockable>(
                listDock,
                new ProportionalDockSplitter { Id = "SplitTop", Proportion = 0.005 },
                timelineDock)
        };

        var styleDock = new ToolDock
        {
            Id = "StyleDock",
            Title = "字幕样式",
            Proportion = 0.14,
            VisibleDockables = CreateList<IDockable>(style),
            ActiveDockable = style
        };

        var previewDock = new ToolDock
        {
            Id = "PreviewDock",
            Title = "字幕渲染预览",
            Proportion = 0.34,
            VisibleDockables = CreateList<IDockable>(preview),
            ActiveDockable = preview
        };

        var mainLayout = new ProportionalDock
        {
            Id = "MainLayout",
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>(
                topRow,
                new ProportionalDockSplitter { Id = "Split1", Proportion = 0.005 },
                styleDock,
                new ProportionalDockSplitter { Id = "Split2", Proportion = 0.005 },
                previewDock)
        };

        var root = CreateRootDock();
        root.Id = "Root";
        root.Title = "Root";
        root.VisibleDockables = CreateList<IDockable>(mainLayout);
        root.ActiveDockable = mainLayout;
        root.DefaultDockable = mainLayout;
        return root;
    }
}




