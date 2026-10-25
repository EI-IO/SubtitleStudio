using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SubtitleStudio.App.Views;

/// <summary>
/// Dock 面板标题栏自带「▾ 菜单」和「📌 固定」两个按钮，这个工具里用不上，统一藏掉。
/// 名字来自 Dock.Avalonia 的控件模板（PART_MenuButton / PART_PinButton）。
/// 拖出浮动窗、重置布局都会重建模板，所以不能只扫一次，窗口活着的时候持续补扫。
/// </summary>
internal static class DockChrome
{
    private static readonly string[] UnwantedButtons = { "PART_MenuButton", "PART_PinButton" };

    public static void StartCleanup(Window root)
    {
        HideUnwantedButtons(root);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            // 窗口关掉后不再持有它
            if (root.GetVisualRoot() is null)
            {
                timer.Stop();
                return;
            }

            HideUnwantedButtons(root);
        };
        timer.Start();
    }

    private static void HideUnwantedButtons(Visual root)
    {
        foreach (var control in root.GetVisualDescendants().OfType<Control>())
        {
            if (control.Name is null || !UnwantedButtons.Contains(control.Name))
                continue;

            control.IsVisible = false;
            control.IsHitTestVisible = false;
            control.Focusable = false;
        }
    }
}
