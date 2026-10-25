using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SubtitleStudio.Core.Media;

namespace SubtitleStudio.App.Services;

/// <summary>
/// slim 版不打包 ffmpeg。缺失时给用户一条明路：官方下载页、GitHub 加速镜像，
/// 或者直接把 ffmpeg.exe / ffprobe.exe 放进程序目录旁边的 tools\ffmpeg\bin。
/// </summary>
public static class FfmpegSetup
{
    // 官方下载页会列出 Windows 构建（gyan.dev / BtbN）；
    // 镜像走 GitHub 加速，指向 BtbN 的 Windows 构建发布页。
    public const string OfficialUrl = "https://ffmpeg.org/download.html";
    public const string MirrorUrl = "https://gh-proxy.com/https://github.com/BtbN/FFmpeg-Builds/releases";

    /// <summary>程序实际会搜索的目录；优先已有目录，避免开发时提示到 bin\Debug 下面去。</summary>
    public static string TargetFolder
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; dir is not null && i < 6; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "tools", "ffmpeg", "bin");
                if (Directory.Exists(candidate))
                    return candidate;
            }

            return Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "bin");
        }
    }

    /// <summary>缺 ffmpeg 就弹提示；用户重新放好后返回 true。</summary>
    public static async Task<bool> PromptAsync(Window owner)
    {
        if (FfmpegTools.Available)
            return true;

        var result = false;
        var dialog = new Window
        {
            Title = Loc.T("Ffmpeg.Title"),
            Width = 560,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#1B1B1F"))
        };

        var status = new TextBlock
        {
            Text = string.Empty,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#9FB4CD")),
            TextWrapping = TextWrapping.Wrap
        };

        var pathBox = new TextBox
        {
            Text = TargetFolder,
            IsReadOnly = true,
            FontSize = 11.5,
            FontFamily = new FontFamily("Consolas")
        };

        Button MakeButton(string key, string? url = null)
        {
            var button = new Button
            {
                Content = Loc.T(key),
                MinWidth = 96,
                Padding = new Thickness(10, 3),
                FontSize = 12
            };

            if (url is not null)
            {
                button.Click += async (_, _) =>
                {
                    try
                    {
                        if (TopLevel.GetTopLevel(owner)?.Launcher is { } launcher)
                            await launcher.LaunchUriAsync(new Uri(url));
                    }
                    catch (Exception ex)
                    {
                        status.Text = ex.Message;
                    }
                };
            }

            return button;
        }

        var official = MakeButton("Ffmpeg.Download.Official", OfficialUrl);
        var mirror = MakeButton("Ffmpeg.Download.Mirror", MirrorUrl);

        var openFolder = new Button
        {
            Content = Loc.T("Ffmpeg.OpenFolder"),
            MinWidth = 96,
            Padding = new Thickness(10, 3),
            FontSize = 12
        };
        openFolder.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(TargetFolder);
                Process.Start(new ProcessStartInfo(TargetFolder) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
            }
        };

        var recheck = new Button
        {
            Content = Loc.T("Ffmpeg.Recheck"),
            MinWidth = 96,
            Padding = new Thickness(10, 3),
            FontSize = 12
        };
        recheck.Click += (_, _) =>
        {
            if (!FfmpegTools.Available)
            {
                status.Text = Loc.T("Ffmpeg.StillMissing");
                status.Foreground = new SolidColorBrush(Color.Parse("#FFBEBE"));
                return;
            }

            result = true;
            dialog.Close();
        };

        var later = new Button
        {
            Content = Loc.T("Ffmpeg.Later"),
            MinWidth = 96,
            Padding = new Thickness(10, 3),
            FontSize = 12,
            IsCancel = true
        };
        later.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 14,
            Children =
            {
                new TextBlock
                {
                    Text = Loc.T("Ffmpeg.Message"),
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap
                },
                pathBox,
                new TextBlock
                {
                    Text = Loc.T("Ffmpeg.Hint"),
                    FontSize = 11.5,
                    Foreground = new SolidColorBrush(Color.Parse("#8A8A93")),
                    TextWrapping = TextWrapping.Wrap
                },
                status,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { official, mirror }
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { openFolder, recheck, later }
                }
            }
        };

        await dialog.ShowDialog(owner);
        return result || FfmpegTools.Available;
    }
}
