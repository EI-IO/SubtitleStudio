using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SubtitleStudio.Core.Media;
using SubtitleStudio.Core.Models;

namespace SubtitleStudio.App.ViewModels;

/// <summary>时间轴上的一个音轨：自带波形、字幕和识别状态，互不干扰。</summary>
public sealed partial class TrackItem : ObservableObject
{
    public TrackItem(AudioStreamInfo info)
    {
        Info = info;
        Cues.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasCues));
            OnPropertyChanged(nameof(Label));
        };
    }

    public AudioStreamInfo Info { get; }

    public int Number => Info.Ordinal;

    public string Label => Info.Describe();

    public ObservableCollection<CueItem> Cues { get; } = new();

    public bool HasCues => Cues.Count > 0;

    [ObservableProperty] private WaveformData? _waveform;

    /// <summary>识别状态短句，例如「完成 · 12 条 · zh」。</summary>
    [ObservableProperty] private string _status = "等待识别";

    /// <summary>整段语言，识别完才有值。</summary>
    [ObservableProperty] private string? _language;

    [ObservableProperty] private double _progress;

    /// <summary>导出透明字幕视频时是否包含这条音轨。</summary>
    [ObservableProperty] private bool _exportEnabled = true;

    /// <summary>单独导出这条音轨的音频时的进度；-1 表示没在导出。</summary>
    [ObservableProperty] private double _audioProgress = -1;

    /// <summary>正在导出这条音轨的音频（防止重复点击）。</summary>
    public bool IsExportingAudio => AudioProgress >= 0;

    partial void OnAudioProgressChanged(double value) => OnPropertyChanged(nameof(IsExportingAudio));
}
