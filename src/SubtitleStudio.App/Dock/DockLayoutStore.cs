using System.Text.Json;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using SubtitleStudio.Core.Asr;

namespace SubtitleStudio.App.Docking;

/// <summary>
/// 布局存档：自己序列化停靠树（方向/比例/每个 ToolDock 里装着哪几个面板），
/// 而不是用 Dock.Serializer 连 ViewModel 一起存——我们的 VM 攥着 AppState，根本没法走 JSON 往返。
/// 只记录主停靠树；有面板漂成浮动窗时干脆不存（浮动状态不回放），避免重启后面板「丢失」。
/// </summary>
public static class DockLayoutStore
{
    /// <summary>存档放在 settings.json 旁边，跟着可写目录的探测结果走。</summary>
    public static string LayoutPath =>
        Path.Combine(Path.GetDirectoryName(AsrSettingsStore.FilePath)!, "dock-layout.json");

    /// <summary>存档节点：proportional / splitter / tooldock 三种。</summary>
    public sealed class Node
    {
        public string Type { get; set; } = string.Empty;
        public string? Orientation { get; set; }
        public string? Alignment { get; set; }
        public double Proportion { get; set; }
        public List<string>? Tools { get; set; }
        public int Active { get; set; }
        public List<Node>? Children { get; set; }
    }

    /// <summary>目前就这四个面板，存档里出现别的 Id 一律当作存档过期。</summary>
    public static bool IsKnownTool(string? id) =>
        id is "SubtitleList" or "Timeline" or "Style" or "Preview";

    public static void Save(IRootDock root)
    {
        try
        {
            var main = root.VisibleDockables?.OfType<ProportionalDock>().FirstOrDefault();
            var node = main is null ? null : Capture(main);
            // 四个面板必须都在树里；有浮动窗时它不在主树，这次就不存
            if (node is null || !ContainsAllTools(node))
                return;

            var dir = Path.GetDirectoryName(LayoutPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(LayoutPath, JsonSerializer.Serialize(node));
        }
        catch
        {
            // 存布局失败不该拦着关窗
        }
    }

    public static Node? Load()
    {
        try
        {
            if (!File.Exists(LayoutPath))
                return null;

            var node = JsonSerializer.Deserialize<Node>(File.ReadAllText(LayoutPath));
            return node is not null && ContainsAllTools(node) ? node : null;
        }
        catch
        {
            return null;
        }
    }

    public static void TryDelete()
    {
        try
        {
            if (File.Exists(LayoutPath))
                File.Delete(LayoutPath);
        }
        catch
        {
            // 删不掉就算了，下次重置/保存会覆盖
        }
    }

    private static bool ContainsAllTools(Node node)
    {
        var tools = new HashSet<string>(StringComparer.Ordinal);
        Collect(node, tools);
        return tools.SetEquals(new[] { "SubtitleList", "Timeline", "Style", "Preview" });
    }

    private static void Collect(Node node, HashSet<string> tools)
    {
        if (node.Tools is not null)
        {
            foreach (var id in node.Tools)
            {
                if (IsKnownTool(id))
                    tools.Add(id!);
            }
        }

        if (node.Children is null)
            return;

        foreach (var child in node.Children)
            Collect(child, tools);
    }

    private static Node? Capture(IDockable dockable)
    {
        switch (dockable)
        {
            case ProportionalDock pd:
            {
                var children = pd.VisibleDockables?
                    .Select(Capture)
                    .Where(n => n is not null)
                    .Cast<Node>()
                    .ToList() ?? new List<Node>();
                if (children.Count == 0)
                    return null;

                return new Node
                {
                    Type = "proportional",
                    Orientation = pd.Orientation.ToString(),
                    Proportion = pd.Proportion,
                    Children = children
                };
            }
            case ProportionalDockSplitter splitter:
                return new Node { Type = "splitter", Proportion = splitter.Proportion };
            case ToolDock td:
            {
                var tools = td.VisibleDockables?
                    .Select(t => t.Id)
                    .Where(IsKnownTool)
                    .Cast<string>()
                    .ToList() ?? new List<string>();
                if (tools.Count == 0)
                    return null;

                var active = td.ActiveDockable is { } activeDockable ? tools.IndexOf(activeDockable.Id ?? string.Empty) : 0;
                return new Node
                {
                    Type = "tooldock",
                    Alignment = td.Alignment.ToString(),
                    Proportion = td.Proportion,
                    Tools = tools,
                    Active = Math.Max(0, active)
                };
            }
            default:
                return null;
        }
    }
}
