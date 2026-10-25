using Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;

namespace SubtitleStudio.App.Docking;

/// <summary>
/// 主界面四区停靠布局。默认是上排「字幕列表 | 时间轴」、中排样式条、下排预览；
/// 面板可以像 VSCode 一样拖到别的区域交换位置（拖出去成浮动窗也能拖回来），
/// 布局由 <see cref="DockLayoutStore"/> 存盘，下次启动原样恢复。
/// 面板不给关闭叉——关了就只能从「重置布局」找回来，干脆不让关。
/// </summary>
public sealed class DockFactory : Factory
{
    private readonly ViewModels.AppState _state;

    public DockFactory(ViewModels.AppState state) => _state = state;

    public override IRootDock CreateLayout()
    {
        // 有存档就按存档重建，坏了/缺面板就退回默认布局
        var saved = DockLayoutStore.Load();
        var main = saved is not null ? TryBuild(saved) : null;
        return WrapInRoot(main ?? DefaultLayout());
    }

    /// <summary>重置回默认布局用。</summary>
    public IRootDock CreateDefaultLayout() => WrapInRoot(DefaultLayout());

    private IRootDock WrapInRoot(IDockable mainLayout)
    {
        var root = CreateRootDock();
        root.Id = "Root";
        root.Title = "Root";
        root.VisibleDockables = CreateList<IDockable>(mainLayout);
        root.ActiveDockable = mainLayout;
        root.DefaultDockable = mainLayout;
        return root;
    }

    /// <summary>按 Id 建面板 VM；存档里冒出不认识的 Id 时返回 null，整个存档作废。</summary>
    private Tool? ToolById(string id)
    {
        Tool? tool = id switch
        {
            "SubtitleList" => new ViewModels.SubtitleListViewModel(_state) { Id = id, Title = Services.Loc.T("Panel.SubtitleList") },
            "Timeline" => new ViewModels.TimelineViewModel(_state) { Id = id, Title = Services.Loc.T("Panel.Timeline") },
            "Style" => new ViewModels.StyleViewModel(_state) { Id = id, Title = Services.Loc.T("Panel.Style") },
            "Preview" => new ViewModels.PreviewViewModel(_state) { Id = id, Title = Services.Loc.T("Panel.Preview") },
            _ => null
        };

        if (tool is null)
            return null;

        // 只禁关闭和固定：拖拽/浮动放开，拖走的面板才能换位置。
        // CanPin=false 让模板不再生成图钉按钮；菜单按钮由 DockChrome 统一隐藏。
        tool.CanClose = false;
        tool.CanPin = false;
        tool.CanFloat = true;
        tool.CanDrag = true;
        return tool;
    }

    /// <summary>从存档节点重建停靠树；任何一环对不上就返回 null 走默认。</summary>
    private IDockable? TryBuild(DockLayoutStore.Node node)
    {
        switch (node.Type)
        {
            case "splitter":
                return new ProportionalDockSplitter { Proportion = node.Proportion > 0 ? node.Proportion : 0.005 };

            case "proportional" when node.Children is { Count: > 0 }:
            {
                var children = node.Children.Select(TryBuild).ToList();
                if (children.Any(c => c is null))
                    return null;

                return new ProportionalDock
                {
                    Orientation = node.Orientation == nameof(Orientation.Horizontal)
                        ? Orientation.Horizontal
                        : Orientation.Vertical,
                    Proportion = node.Proportion > 0 ? node.Proportion : 1,
                    VisibleDockables = CreateList<IDockable>(children.Cast<IDockable>().ToArray()),
                    ActiveDockable = children.FirstOrDefault(c => c is not ProportionalDockSplitter)
                };
            }

            case "tooldock" when node.Tools is { Count: > 0 }:
            {
                var tools = node.Tools.Select(ToolById).ToList();
                if (tools.Any(t => t is null))
                    return null;

                var dock = new ToolDock
                {
                    Alignment = Enum.TryParse<Alignment>(node.Alignment, out var alignment) ? alignment : Alignment.Left,
                    Proportion = node.Proportion > 0 ? node.Proportion : 1,
                    VisibleDockables = CreateList<IDockable>(tools.Cast<IDockable>().ToArray()),
                    ActiveDockable = tools[Math.Clamp(node.Active, 0, tools.Count - 1)]
                };
                return dock;
            }

            default:
                return null;
        }
    }

    private ProportionalDock DefaultLayout()
    {
        var list = ToolById("SubtitleList")!;
        var timeline = ToolById("Timeline")!;
        var style = ToolById("Style")!;
        var preview = ToolById("Preview")!;

        var listDock = new ToolDock
        {
            Id = "ListDock",
            Title = Services.Loc.T("Panel.SubtitleList"),
            Proportion = 0.27,
            Alignment = Alignment.Left,
            VisibleDockables = CreateList<IDockable>(list),
            ActiveDockable = list
        };

        var timelineDock = new ToolDock
        {
            Id = "TimelineDock",
            Title = Services.Loc.T("Panel.Timeline"),
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
            Title = Services.Loc.T("Panel.Style"),
            Proportion = 0.14,
            VisibleDockables = CreateList<IDockable>(style),
            ActiveDockable = style
        };

        var previewDock = new ToolDock
        {
            Id = "PreviewDock",
            Title = Services.Loc.T("Panel.Preview"),
            Proportion = 0.34,
            VisibleDockables = CreateList<IDockable>(preview),
            ActiveDockable = preview
        };

        return new ProportionalDock
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
    }
}
