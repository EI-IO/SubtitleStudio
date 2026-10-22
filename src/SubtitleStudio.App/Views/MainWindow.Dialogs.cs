using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace SubtitleStudio.App.Views;

/// <summary>跟系统打交道的部分：选文件、选目录、在资源管理器里定位。</summary>
public partial class MainWindow
{
    /// <summary>AppState 选媒文件时回调这里。</summary>
    private async Task<string?> PickMediaAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择视频或音频文件",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("媒体文件")
                {
                    Patterns = new[]
                    {
                        "*.mp4", "*.mkv", "*.mov", "*.avi", "*.webm", "*.flv", "*.wmv", "*.m4v", "*.ts",
                        "*.mp3", "*.wav", "*.m4a", "*.aac", "*.flac", "*.ogg", "*.opus"
                    }
                },
                FilePickerFileTypes.All
            }
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <summary>导出时选目标目录。</summary>
    private async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择导出位置",
            AllowMultiple = false
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    /// <summary>在资源管理器里选中文件（或打开目录）。</summary>
    private static Task RevealAsync(string path)
    {
        try
        {
            var full = System.IO.Path.GetFullPath(path);
            var arguments = System.IO.Directory.Exists(full) ? $"\"{full}\"" : $"/select,\"{full}\"";

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", arguments)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // 打开资源管理器失败不提示
        }

        return Task.CompletedTask;
    }
}
