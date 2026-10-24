using System.Runtime.InteropServices;

namespace SubtitleStudio.Core.Media;

/// <summary>
/// 找一个真正可写的工作目录。
/// 直接往 %TEMP% 写在某些受限环境（沙箱、只读 AppData）下会 Permission denied，
/// 所以优先在素材旁边的隐藏目录里干活，退而求其次才用系统临时目录。
/// </summary>
public static class TempWorkspace
{
    /// <summary>建一个可写的临时目录；<paramref name="nearFilePath"/> 附近的目录优先。</summary>
    public static string CreateDirectory(string? nearFilePath = null)
    {
        foreach (var candidate in Candidates(nearFilePath))
        {
            if (TryPrepare(candidate) is { } ready)
                return ready;
        }

        throw new InvalidOperationException(
            "找不到可写的临时目录，无法抽取音轨。请检查素材所在磁盘是否只读。");
    }

    /// <summary>在可写目录里建一个独占的临时文件名（不创建文件）。</summary>
    public static string CreateFilePath(string? nearFilePath, string extension)
    {
        var dir = CreateDirectory(nearFilePath);
        return Path.Combine(dir, $"{Guid.NewGuid():N}{extension}");
    }

    public static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // 删不掉就算了，下次会重新建
        }
    }

    private static IEnumerable<string> Candidates(string? nearFilePath)
    {
        if (!string.IsNullOrWhiteSpace(nearFilePath))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(nearFilePath));
            if (!string.IsNullOrEmpty(dir))
                yield return Path.Combine(dir, ".subtitle-studio-tmp");
        }

        yield return Path.Combine(AppContext.BaseDirectory, "temp");

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(local))
            yield return Path.Combine(local, "SubtitleStudio", "temp");

        yield return Path.Combine(Path.GetTempPath(), "subtitle-studio");
    }

    private static bool _cleaned;

    private static string? TryPrepare(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            CleanupStale(dir);
            var probe = Path.Combine(dir, $".probe-{Guid.NewGuid():N}");
            using (var fs = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1,
                       FileOptions.DeleteOnClose))
            {
                fs.WriteByte(0);
            }

            return dir;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 进程被强杀时预览 wav 来不及删（一条音轨能到几百 MB），
    /// 每次启动顺手清掉一天前的残留；还活着的会话不会碰。
    /// </summary>
    private static void CleanupStale(string dir)
    {
        if (_cleaned)
            return;

        _cleaned = true;
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-1);
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                        File.Delete(file);
                }
                catch
                {
                    // 被别的实例占用就留着
                }
            }
        }
        catch
        {
            // 列不出来就算了
        }
    }
}
