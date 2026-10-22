using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SubtitleStudio.Core.Asr;
using System.Text.Json;
using SubtitleStudio.Core.Media;

namespace SubtitleStudio.App.Views;

/// <summary>设置窗口：云端 Key、模型列表、本地模型下载。</summary>
public partial class SettingsWindow : Window
{
    private readonly AsrSettings _settings;
    private readonly List<Button> _downloadButtons = new();
    private bool _downloading;

    public SettingsWindow()
    {
        InitializeComponent();
        _settings = new AsrSettings();
    }

    public SettingsWindow(AsrSettings settings)
    {
        InitializeComponent();
        _settings = settings.Clone();
        Loaded += (_, _) => Populate();
    }

    /// <summary>保存后回调（由调用方写盘并刷新界面）。</summary>
    public Action<AsrSettings>? Saved { get; set; }

    private void Populate()
    {
        SiliconBaseUrl.Text = _settings.SiliconFlowBaseUrl;
        SiliconKey.Text = _settings.SiliconFlowApiKey;
        TeleAiUrl.Text = TeleAiClient.BuildUrl(_settings.TeleAiBaseUrl);
        TeleAiApiKey.Text = _settings.TeleAiApiKey;


        if (!string.IsNullOrWhiteSpace(_settings.SiliconFlowModel))
        {
            SiliconModel.Items.Add(_settings.SiliconFlowModel);
            SiliconModel.SelectedIndex = 0;
        }

        ModelDirText.Text = "下载目录：" + ModelCatalog.DefaultModelDirectory;
        BuildModelList();
    }

    /// <summary>本地模型列表：已装的显示对勾，未装的给下载按钮。</summary>
    private void BuildModelList()
    {
        ModelList.Children.Clear();
        _downloadButtons.Clear();

        var installed = ModelCatalog.List()
            .Select(m => Path.GetFileName(m.FullPath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var model in ModelCatalog.Suggestions)
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Margin = new Thickness(0, 2)
            };

            var info = new StackPanel { Spacing = 1 };
            info.Children.Add(new TextBlock
            {
                Text = $"{model.Display}   {model.SizeLabel}",
                FontWeight = FontWeight.SemiBold
            });
            info.Children.Add(new TextBlock
            {
                Text = model.Note,
                FontSize = 11,
                Opacity = 0.6,
                TextWrapping = TextWrapping.Wrap
            });
            Grid.SetColumn(info, 0);
            row.Children.Add(info);

            var done = installed.Contains(model.FileName);
            var button = new Button
            {
                Content = done ? "已安装" : "下载",
                MinWidth = 76,
                IsEnabled = !done,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (!done)
            {
                var captured = model;
                button.Click += (_, _) => Download(captured, button);
                _downloadButtons.Add(button);
            }

            Grid.SetColumn(button, 1);
            row.Children.Add(button);

            ModelList.Children.Add(row);
            ModelList.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.Parse("#2A2A31")) });
        }
    }

    private void Download(ModelSuggestion model, Button button)
    {
        if (_downloading)
        {
            DownloadStatus.Text = "还有别的模型在下，等它完成";
            return;
        }

        _downloading = true;
        button.IsEnabled = false;
        button.Content = "下载中…";
        DownloadBar.Value = 0;
        DownloadStatus.Text = $"{model.Display}：连接中…";

        var progress = new Progress<DownloadProgress>(p =>
        {
            DownloadBar.Value = p.Fraction;
            DownloadStatus.Text = p.Total > 0
                ? $"{model.Display}：{p.SourceName} · {p.Received / 1048576.0:F1} / {p.Total / 1048576.0:F1} MB（{p.Fraction:P0}）"
                : $"{model.Display}：{p.SourceName} · {p.Received / 1048576.0:F1} MB";
        });

        _ = Task.Run(async () =>
        {
            try
            {
                var path = await ModelDownloader.DownloadAsync(model, null, progress);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    DownloadBar.Value = 1;
                    DownloadStatus.Text = $"{model.Display}：已保存到 {Path.GetFileName(path)}";
                    button.Content = "已安装";
                    BuildModelList();
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    DownloadStatus.Text = $"{model.Display}：下载失败 — {ex.Message}";
                    button.Content = "重试";
                    button.IsEnabled = true;
                });
            }
            finally
            {
                _downloading = false;
            }
        });
    }

    private async void OnFetchSilicon(object? sender, RoutedEventArgs e)
    {
        var key = SiliconKey.Text?.Trim() ?? string.Empty;
        if (key.Length == 0)
        {
            SiliconStatus.Text = "先填 API Key";
            return;
        }

        FetchSiliconButton.IsEnabled = false;
        SiliconStatus.Text = "获取中…";
        try
        {
            var models = await SiliconFlowClient.ListModelsAsync(SiliconBaseUrl.Text ?? _settings.SiliconFlowBaseUrl, key);
            SiliconModel.Items.Clear();
            foreach (var model in models)
                SiliconModel.Items.Add(model.Id);

            if (models.Count == 0)
            {
                SiliconStatus.Text = "没找到语音识别模型（账户可能没开通）";
            }
            else
            {
                SiliconModel.SelectedIndex = 0;
                SiliconStatus.Text = $"拿到 {models.Count} 个语音模型";
            }
        }
        catch (Exception ex)
        {
            SiliconStatus.Text = "失败：" + ex.Message;
        }
        finally
        {
            FetchSiliconButton.IsEnabled = true;
        }
    }

    /// <summary>天翼星辰：用一段 0.5 秒静音打一次短语音识别，返回 10000 就说明 Key 可用。</summary>
    private async void OnTestTeleAi(object? sender, RoutedEventArgs e)
    {
        var apiKey = TeleAiApiKey.Text?.Trim() ?? string.Empty;
        if (apiKey.Length == 0)
        {
            TeleAiStatus.Text = "先填 API Key";
            return;
        }

        TestTeleAiButton.IsEnabled = false;
        TeleAiStatus.Text = "测试中…";

        try
        {
            var silence = new byte[(int)(AudioExtractor.SampleRate * 2 * 0.5)];
            var payload = new
            {
                req_id = Guid.NewGuid().ToString("N"),
                audio_data = Convert.ToBase64String(silence),
                sample_rate = AudioExtractor.SampleRate,
                enable_punc = true
            };

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var request = new HttpRequestMessage(HttpMethod.Post, TeleAiClient.BuildUrl(_settings.TeleAiBaseUrl));
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            var ok = response.IsSuccessStatusCode && body.Contains("\"code\":10000", StringComparison.Ordinal);
            TeleAiStatus.Text = ok
                ? "连接正常，Key 可用"
                : $"失败（HTTP {(int)response.StatusCode}）：{Trim(body, 160)}";
        }
        catch (Exception ex)
        {
            TeleAiStatus.Text = "失败：" + TlsDiagnostics.Describe(ex);
        }
        finally
        {
            TestTeleAiButton.IsEnabled = true;
        }
    }

    private static string Trim(string? text, int count)
    {
        if (string.IsNullOrEmpty(text)) return "(空)";
        var flat = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return flat.Length <= count ? flat : flat[..count] + "…";
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        var result = _settings.Clone();
        result.SiliconFlowApiKey = SiliconKey.Text?.Trim() ?? string.Empty;
        result.SiliconFlowBaseUrl = string.IsNullOrWhiteSpace(SiliconBaseUrl.Text)
            ? "https://api.siliconflow.cn/v1"
            : SiliconBaseUrl.Text!.Trim();

        if (SiliconModel.SelectedItem is string selectedModel && selectedModel.Length > 0)
            result.SiliconFlowModel = selectedModel;
        else if (SiliconModel.Text is { Length: > 0 } typed)
            result.SiliconFlowModel = typed.Trim();

        result.TeleAiApiKey = TeleAiApiKey.Text?.Trim() ?? string.Empty;

        Saved?.Invoke(result);
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
