using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace SubtitleStudio.App.Services;

/// <summary>几个小而常用的对话框，省得每处各写一套。</summary>
public static class Dialogs
{
    /// <summary>是/否确认框。没有窗口时直接返回 false（宁可不做也不误删）。</summary>
    public static async Task<bool> ConfirmAsync(
        Window? owner,
        string title,
        string message,
        string? yes = null,
        string? no = null)
    {
        if (owner is null)
            return false;

        yes ??= Loc.T("Common.OK");
        no ??= Loc.T("Common.Cancel");

        var result = false;
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#1B1B1F"))
        };

        var yesButton = new Button { Content = yes, MinWidth = 92, IsDefault = true };
        var noButton = new Button { Content = no, MinWidth = 92, IsCancel = true };

        yesButton.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };
        noButton.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10,
                    Children = { noButton, yesButton }
                }
            }
        };

        await dialog.ShowDialog(owner);
        return result;
    }

    /// <summary>取承载窗口（控件 → TopLevel）。</summary>
    public static Window? OwnerOf(Visual? visual) => TopLevel.GetTopLevel(visual) as Window;
}
