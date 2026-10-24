using Avalonia.Controls;
using Avalonia.Layout;
using SubtitleStudio.App.Services;

namespace SubtitleStudio.App.Views;

/// <summary>
/// 语言选择窗：每种可用语言一块平铺按钮（含运行时新放进 lang/ 的语言包）。
/// 首次启动自动弹出；以后可以点状态栏语言下拉里的「更多语言…」再次打开。
/// </summary>
public partial class LanguagePickerWindow : Window
{
    public LanguagePickerWindow()
    {
        InitializeComponent();

        // 运行中补装的语言包也能立刻出现在列表里
        Loc.LoadFromFiles();
        Build();
    }

    /// <summary>在 owner 上模态显示，返回选中的语言代码；直接关窗返回 null。</summary>
    public static async Task<string?> PickAsync(Window owner)
    {
        var picker = new LanguagePickerWindow();
        return await picker.ShowDialog<string?>(owner);
    }

    private void Build()
    {
        LanguageButtons.Children.Clear();

        foreach (var language in Loc.Available)
        {
            var button = new Button
            {
                Content = language.Name,
                Width = 150,
                Height = 54,
                Margin = new Avalonia.Thickness(0, 0, 10, 10),
                FontSize = 15,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            // 当前语言给个视觉标记，一眼看出现在用的是哪个
            if (string.Equals(language.Code, Loc.Current, StringComparison.OrdinalIgnoreCase))
                button.Classes.Add("accent");

            var code = language.Code;
            button.Click += (_, _) => Close(code);
            LanguageButtons.Children.Add(button);
        }
    }
}
