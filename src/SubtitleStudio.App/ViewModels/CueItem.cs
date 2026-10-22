using CommunityToolkit.Mvvm.ComponentModel;
using SubtitleStudio.Core.Models;

namespace SubtitleStudio.App.ViewModels;

/// <summary>时间轴上的一条字幕，包一层以便界面绑定和编辑。</summary>
public sealed class CueItem : ObservableObject
{
    public CueItem(SubtitleCue model)
    {
        Model = model;
    }

    public SubtitleCue Model { get; }

    /// <summary>文本被编辑时通知外层刷新预览与时间轴。</summary>
    public event EventHandler? Edited;

    public double Start => Model.Start;

    public double End => Model.End;

    public string Text
    {
        get => Model.Text;
        set
        {
            if (Model.Text == value)
                return;
            Model.Text = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Preview));
            Edited?.Invoke(this, EventArgs.Empty);
        }
    }

    public string TimeLabel => $"{Format(Model.Start)} → {Format(Model.End)}";

    public string Preview => Model.Text.Length <= 28 ? Model.Text : Model.Text[..28] + "…";

    public void MoveTo(double start, double end)
    {
        Model.Start = Math.Max(0, start);
        Model.End = Math.Max(Model.Start + 0.1, end);
        RefreshTime();
    }

    public void SetBounds(double start, double end)
    {
        Model.Start = Math.Max(0, start);
        Model.End = Math.Max(Model.Start + 0.1, end);
        RefreshTime();
    }

    public void RefreshTime()
    {
        OnPropertyChanged(nameof(Start));
        OnPropertyChanged(nameof(End));
        OnPropertyChanged(nameof(TimeLabel));
    }

    private static string Format(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)ts.TotalMinutes:00}:{ts.Seconds:00}.{ts.Milliseconds / 10:00}";
    }
}
