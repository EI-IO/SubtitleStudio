using NAudio.Wave;
using SubtitleStudio.Core.Media;

namespace SubtitleStudio.App.Services;

/// <summary>
/// 预览播放：先用 ffmpeg 把选中的几条音轨解码并混成一个临时 wav，
/// 再交给 NAudio 播放，播放位置用来驱动时间轴的播放头。
/// </summary>
public sealed class AudioPlayback : IDisposable
{
    private WaveOutEvent? _output;
    private AudioFileReader? _reader;
    private string? _tempWav;

    public bool IsReady => _reader is not null;

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    public double Duration => _reader?.TotalTime.TotalSeconds ?? 0;

    public double Position
    {
        get => _reader?.CurrentTime.TotalSeconds ?? 0;
        set
        {
            if (_reader is not null)
                _reader.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(value, 0, Math.Max(0, Duration - 0.05)));
        }
    }

    /// <summary>把指定音轨解码 + 混音到临时 wav；索引为空时唱第 0 条。</summary>
    /// <param name="maxSeconds">
    /// 只解码前这么多秒（超长媒体整段解码出的临时 wav 动辄上 GB，预览播放没必要）。
    /// null 表示整段解码。
    /// </param>
    public async Task PrepareAsync(string mediaPath, IReadOnlyList<int> streamIndexes,
        CancellationToken cancellationToken, double? maxSeconds = null)
    {
        Stop();
        Release();

        var indexes = streamIndexes.Count > 0 ? streamIndexes : new[] { 0 };
        var temp = TempWorkspace.CreateFilePath(mediaPath, ".play.wav");
        var adopted = false;

        try
        {
            var args = new List<string>
            {
                "-hide_banner", "-y", "-i", mediaPath
            };

            // -t 放在输出选项里：只解前 maxSeconds 秒
            if (maxSeconds is { } cap && cap > 0)
                args.AddRange(new[] { "-t", cap.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) });

            if (indexes.Count == 1)
            {
                args.AddRange(new[] { "-map", $"0:{indexes[0]}", "-vn" });
            }
            else
            {
                var inputs = string.Concat(indexes.Select(i => $"[0:{i}]"));
                var filter = $"{inputs}amix=inputs={indexes.Count}:duration=longest:normalize=0[a]";
                args.AddRange(new[] { "-filter_complex", filter, "-map", "[a]" });
            }

            args.AddRange(new[] { "-ac", "2", "-ar", "48000", "-c:a", "pcm_s16le", temp });

            var ffmpeg = FfmpegTools.FfmpegPath
                         ?? throw new InvalidOperationException(Loc.T("Error.FfmpegMissing"));
            var (code, stderr) = await FfmpegTools.RunAsync(ffmpeg, args, cancellationToken);
            if (code != 0 || !File.Exists(temp))
                throw new InvalidOperationException(Loc.T("Error.OpenAudio", ProcessText.Tail(stderr)));

            _tempWav = temp;
            _reader = new AudioFileReader(temp);
            _output = new WaveOutEvent { DesiredLatency = 120 };
            _output.Init(_reader);
            adopted = true;
        }
        finally
        {
            // 取消（勾选变了/换文件）时 ffmpeg 可能已经写出了半个 wav，不能留在临时目录里
            if (!adopted)
                TempWorkspace.TryDelete(temp);
        }
    }

    public void Play()
    {
        if (_output is null)
            return;

        if (_reader is not null && _reader.CurrentTime >= _reader.TotalTime - TimeSpan.FromMilliseconds(50))
            _reader.CurrentTime = TimeSpan.Zero;

        _output.Play();
    }

    public void Pause() => _output?.Pause();

    /// <summary>丢掉已经解码好的混音（音轨勾选变了），下次播放按新选择重新解码。</summary>
    public void ReleasePrepared()
    {
        Stop();
        Release();
    }

    public void Stop()
    {
        try
        {
            _output?.Stop();
            if (_reader is not null)
                _reader.CurrentTime = TimeSpan.Zero;
        }
        catch
        {
            // 播放设备异常时忽略
        }
    }

    private void Release()
    {
        try
        {
            _output?.Dispose();
        }
        catch
        {
            // 忽略释放异常
        }

        _output = null;

        try
        {
            _reader?.Dispose();
        }
        catch
        {
            // 忽略释放异常
        }

        _reader = null;

        TempWorkspace.TryDelete(_tempWav);
        _tempWav = null;
    }

    public void Dispose() => Release();
}
