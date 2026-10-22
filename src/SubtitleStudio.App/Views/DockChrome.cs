using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SubtitleStudio.App.Views;

/// <summary>
/// Dock 面板标题栏自带「▾ 菜单」和「📌 固定」两个按钮，这个工具里用不上，统一藏掉。
/// 名字来自 Dock.Avalonia 的控件模板（PART_MenuButton / PART_PinButton）。
/// </summary>
internal static class DockChrome
{
    private static readonly string[] UnwantedButtons = { "PART_MenuButton", "PART_PinButton" };

    /// <summary>面板是渐进创建出来的，启动后多扫几轮，直到所有标题栏都清理干净。</summary>
    public static void StartCleanup(Visual root, int rounds = 12, TimeSpan? interval = null)
    {
        var left = rounds;
        var timer = new DispatcherTimer { Interval = interval ?? TimeSpan.FromMilliseconds(400) };

        timer.Tick += (_, _) =>
        {
            HideUnwantedButtons(root);

            if (--left <= 0)
                timer.Stop();
        };

        timer.Start();
    }

    private static void HideUnwantedButtons(Visual root)
    {
        foreach (var control in root.GetVisualDescendants().OfType<Control>())
        {
            if (control.Name is not null && UnwantedButtons.Contains(control.Name))
                control.IsVisible = false;
        }
    }
}
