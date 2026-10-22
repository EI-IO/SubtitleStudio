using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;
using SubtitleStudio.App.Services;
using SubtitleStudio.Core.Export;
using SubtitleStudio.Core.Models;

namespace SubtitleStudio.App.ViewModels;

/// <summary>样式面板：字体 / 字号 / 颜色 / 描边，改动直接写回 <see cref="AppState.Style"/>。</summary>
public sealed partial class StyleViewModel : Tool
{
    /// <summary>字体下拉里最后一项：导入本地字体文件。</summary>
    public const string ImportFontItem = "＋ 导入其他字体…";

    private static readonly string[] BuiltInFonts =
    {
        "Microsoft YaHei", "SimHei", "SimSun", "KaiTi", "Microsoft JhengHei", "Segoe UI", "Arial", "Consolas"
    };

    private readonly AppState _state;

    /// <summary>回填样式时置位，避免把「读出来的值」又当成用户改动写回去。</summary>
    private bool _syncing;

    public StyleViewModel(AppState state)
    {
        _state = state;
        Fonts = new ObservableCollection<string>(BuiltInFonts);

        // 之前导入过的字体（缓存在本地，重启后自动回来）
        foreach (var family in FontInstaller.Families)
        {
            if (!Fonts.Contains(family))
                Fonts.Add(family);
        }

        Fonts.Add(ImportFontItem);
        SyncFromStyle();
    }

    public AppState State => _state;

    public ObservableCollection<string> Fonts { get; }

    /// <summary>由视图注入：弹出文件选择框，返回字体文件路径。</summary>
    public Func<Task<string?>>? PickFontFile { get; set; }

    [ObservableProperty] private string _selectedFont = "Microsoft YaHei";
    [ObservableProperty] private double _fontSize = 64;
    [ObservableProperty] private bool _bold = true;
    [ObservableProperty] private Color _primaryColor = Colors.White;
    [ObservableProperty] private Color _outlineColor = Colors.Black;
    [ObservableProperty] private double _outlineWidth = 3;
    [ObservableProperty] private double _marginBottom = 64;
    [ObservableProperty] private int _maxCharsPerLine = 18;

    private void SyncFromStyle()
    {
        _syncing = true;

        var style = _state.Style;
        SelectedFont = style.FontFamily;
        FontSize = style.FontSize;
        Bold = style.Bold;
        PrimaryColor = SubtitleLayerRenderer.ParseColor(style.PrimaryColor).ToAvalonia();
        OutlineColor = SubtitleLayerRenderer.ParseColor(style.OutlineColor).ToAvalonia();
        OutlineWidth = style.OutlineWidth;
        MarginBottom = style.MarginBottom;
        MaxCharsPerLine = style.MaxCharsPerLine;

        _syncing = false;
    }

    partial void OnSelectedFontChanged(string value)
    {
        if (value == ImportFontItem)
        {
            _ = ImportFontAsync();
            return;
        }

        Apply(style => style.FontFamily = value);
    }

    private async Task ImportFontAsync()
    {
        var previous = _state.Style.FontFamily;

        var picked = PickFontFile is null ? null : await PickFontFile();
        if (string.IsNullOrWhiteSpace(picked))
        {
            SelectedFont = previous;
            return;
        }

        var family = FontInstaller.Install(picked);
        if (family is null)
        {
            SelectedFont = previous;
            _state.ReportError("字体导入失败：读不出这个文件的字体信息，换一个 .ttf / .otf 试试");
            return;
        }

        if (!Fonts.Contains(family))
            Fonts.Insert(Math.Max(0, Fonts.Count - 1), family);

        SelectedFont = family;
    }

    partial void OnFontSizeChanged(double value)
    {
        Apply(style => style.FontSize = Math.Clamp(value, 12, 200));
        OnPropertyChanged(nameof(FontSizeValue));
    }

    /// <summary>字号数字框：与滑杆共用同一个 FontSize。</summary>
    public decimal? FontSizeValue
    {
        get => (decimal)Math.Round(FontSize);
        set
        {
            if (value is not { } v || v <= 0)
                return;

            if (Math.Abs((double)v - FontSize) >= 0.5)
                FontSize = (double)v;
        }
    }

    partial void OnBoldChanged(bool value) => Apply(style => style.Bold = value);

    partial void OnOutlineWidthChanged(double value) => Apply(style => style.OutlineWidth = Math.Clamp(value, 0, 20));

    partial void OnMarginBottomChanged(double value) => Apply(style => style.MarginBottom = Math.Clamp(value, 0, 600));

    partial void OnMaxCharsPerLineChanged(int value) => Apply(style => style.MaxCharsPerLine = Math.Clamp(value, 6, 40));

    partial void OnPrimaryColorChanged(Color value)
    {
        Apply(style => style.PrimaryColor = ToHex(value));
        OnPropertyChanged(nameof(PrimaryBrush));
        OnPropertyChanged(nameof(PrimaryHex));
    }

    partial void OnOutlineColorChanged(Color value)
    {
        Apply(style => style.OutlineColor = ToHex(value));
        OnPropertyChanged(nameof(OutlineBrush));
        OnPropertyChanged(nameof(OutlineHex));
    }

    /// <summary>色块用。</summary>
    public IBrush PrimaryBrush => new SolidColorBrush(PrimaryColor);

    public IBrush OutlineBrush => new SolidColorBrush(OutlineColor);

    /// <summary>十六进制输入框：支持 #RRGGBB / #AARRGGBB。</summary>
    public string PrimaryHex
    {
        get => ToHex(PrimaryColor);
        set
        {
            if (HexColor.TryParse(value, out var color))
                PrimaryColor = color;
            else
                OnPropertyChanged();
        }
    }

    public string OutlineHex
    {
        get => ToHex(OutlineColor);
        set
        {
            if (HexColor.TryParse(value, out var color))
                OutlineColor = color;
            else
                OnPropertyChanged();
        }
    }

    private void Apply(Action<CueStyle> mutate)
    {
        if (_syncing)
            return;

        mutate(_state.Style);
        _state.InvalidateStyle();
    }

    public static string ToHex(Color color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
}
