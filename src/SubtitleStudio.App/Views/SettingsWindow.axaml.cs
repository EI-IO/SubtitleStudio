using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SubtitleStudio.App.Services;
using SubtitleStudio.Core.Asr;
using System.Text.Json;
using SubtitleStudio.Core.Media;

namespace SubtitleStudio.App.Views;

/// <summary>
/// 设置窗口：左边是提供商列表（名称 + 状态点 + 编辑/删除，可添加 OpenAI 兼容的自定义提供商），
/// 右边是点「编辑」后对应的配置表单。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AsrSettings _settings;

    /// <summary>模型列表当前的状态指纹；只有装了/在下/半成品状态变了才重建行，进度只刷新进度条。</summary>
    private string _modelSignature = string.Empty;

    /// <summary>当前在详情面板里编辑谁：local / siliconflow / teleai / 自定义提供商的 Id。</summary>
    private string? _selectedKey;

    /// <summary>详情面板里正在编辑的自定义提供商（_settings 的副本里的那个实例）。</summary>
    private CustomProvider? _editingCustom;

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

        // 下载进度由独立服务广播：窗口开着就刷新，关掉也不影响下载继续
        ModelDownloads.Changed += OnDownloadsChanged;

        // 代码拼出来的行和状态文字要跟着语言走；XAML 文案由 DynamicResource 自己刷新
        Loc.Changed += OnLanguageChanged;

        Closed += (_, _) =>
        {
            ModelDownloads.Changed -= OnDownloadsChanged;
            Loc.Changed -= OnLanguageChanged;
        };
    }

    /// <summary>保存后回调（由调用方写盘并刷新界面）。</summary>
    public Action<AsrSettings>? Saved { get; set; }

    /// <summary>连接测试拉到模型后的回调（key = siliconflow / 自定义提供商 Id），主界面模型菜单靠它同步。</summary>
    public Action<string, IReadOnlyList<RemoteModel>>? CloudModelsFetched { get; set; }

    private void Populate()
    {
        SiliconBaseUrl.Text = _settings.SiliconFlowBaseUrl;
        SiliconKey.Text = _settings.SiliconFlowApiKey;
        TeleAiUrl.Text = TeleAiClient.BuildUrl(_settings.TeleAiBaseUrl);
        TeleAiApiKey.Text = _settings.TeleAiApiKey;
        DenoiseCheck.IsChecked = _settings.Denoise;

        ModelDirText.Text = Loc.T("Settings.ModelDir", ModelCatalog.DefaultModelDirectory);
        BuildModelList();
        BuildProviderList();

        // 打开时默认选中正在用的那家，少点一次
        SelectRow(_settings.Backend switch
        {
            AsrBackend.SiliconFlow => "siliconflow",
            AsrBackend.TeleAi => "teleai",
            AsrBackend.Custom => _settings.CustomProviderId,
            _ => "local"
        });
    }

    // ———————————————— 提供商列表 ————————————————

    private sealed record ProviderRow(string Key, string DisplayName, bool IsCustom, bool Ready, bool Active);

    /// <summary>列出内置三家 + 全部自定义提供商；状态点绿=配好了，红=缺东西。</summary>
    private void BuildProviderList()
    {
        ProviderList.Children.Clear();

        var localReady = ModelCatalog.List().Count > 0;
        AddProviderRow(new ProviderRow("local", Loc.T("Settings.Provider.Local"), false,
            localReady, _settings.Backend == AsrBackend.Local));
        AddProviderRow(new ProviderRow("siliconflow", Loc.T("Settings.Provider.SiliconFlow"), false,
            !string.IsNullOrWhiteSpace(_settings.SiliconFlowApiKey), _settings.Backend == AsrBackend.SiliconFlow));
        AddProviderRow(new ProviderRow("teleai", Loc.T("Settings.Provider.TeleAi"), false,
            !string.IsNullOrWhiteSpace(_settings.TeleAiApiKey), _settings.Backend == AsrBackend.TeleAi));

        foreach (var provider in _settings.CustomProviders)
        {
            AddProviderRow(new ProviderRow(provider.Id, provider.Name, true,
                AsrSettings.IsReady(provider),
                _settings.Backend == AsrBackend.Custom && _settings.CustomProviderId == provider.Id));
        }
    }

    /// <summary>语言切换后：代码生成的行、目录提示、状态文案全部按新语言重来。</summary>
    private void OnLanguageChanged()
    {
        if (!IsLoaded)
            return;

        ModelDirText.Text = Loc.T("Settings.ModelDir", ModelCatalog.DefaultModelDirectory);
        BuildProviderList();
        BuildModelList();
        SelectRow(_selectedKey);
    }

    private void AddProviderRow(ProviderRow row)
    {
        var nameBlock = new TextBlock
        {
            Text = row.DisplayName,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 110
        };

        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        head.Children.Add(nameBlock);

        // 自定义提供商带个标签，免得和内置的混在一起
        if (row.IsCustom)
        {
            head.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.Parse("#2E3A4E")),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = Loc.T("Settings.Provider.Custom"),
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.Parse("#9FB4CD"))
                }
            });
        }

        // 状态点：绿=配置齐全，红=还缺东西；悬停能看到原因
        var dot = new Ellipse
        {
            Width = 8,
            Height = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = new SolidColorBrush(row.Ready ? Color.Parse("#46B85A") : Color.Parse("#E06A6A"))
        };
        ToolTip.SetTip(dot, Loc.T(row.Ready ? "Settings.Provider.Ready" : "Settings.Provider.NotReady"));
        head.Children.Add(dot);

        if (row.Active)
        {
            head.Children.Add(new TextBlock
            {
                Text = Loc.T("Settings.Provider.InUse"),
                FontSize = 10,
                Opacity = 0.55,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var captured = row;

        if (row.IsCustom)
        {
            var delete = new Button
            {
                Content = Loc.T("Common.Delete"),
                Padding = new Thickness(8, 2),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#E06A6A"))
            };
            delete.Click += (_, _) => OnDeleteProvider(captured);
            buttons.Children.Add(delete);
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(head, 0);
        grid.Children.Add(head);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        // 整行就是按钮：点哪行就编辑哪行（不再单独放「编辑」键）
        var card = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#23232A")),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = grid
        };
        card.PointerPressed += (_, e) =>
        {
            // 点在「删除」按钮上不抢事件
            if (!e.Handled)
                SelectRow(captured.Key);
        };
        ProviderList.Children.Add(card);
    }

    /// <summary>选中一行：左边高亮与否不重要，重要的是右边换上对应的表单。</summary>
    private void SelectRow(string? key)
    {
        // 换表单前先把正在编辑的自定义提供商字段收回来
        CommitCustomForm();

        _selectedKey = key;
        DetailHint.IsVisible = key is null;
        LocalPanel.IsVisible = key == "local";
        SiliconPanel.IsVisible = key == "siliconflow";
        TeleAiPanel.IsVisible = key == "teleai";

        _editingCustom = key is not null && key != "local" && key != "siliconflow" && key != "teleai"
            ? _settings.CustomProviders.FirstOrDefault(p => p.Id == key)
            : null;
        CustomPanel.IsVisible = _editingCustom is not null;

        if (_editingCustom is not null)
        {
            CustomName.Text = _editingCustom.Name;
            CustomBaseUrl.Text = _editingCustom.BaseUrl;
            CustomKey.Text = _editingCustom.ApiKey;
            CustomModelText.Text = _editingCustom.Model;
            CustomStatus.Text = _editingCustom.CachedModels.Count > 0
                ? Loc.T("Settings.Status.CachedModels", _editingCustom.CachedModels.Count)
                : Loc.T("Settings.Status.NoCachedModels");
        }
    }

    /// <summary>把自定义表单里的值写回正在编辑的提供商（取消整个窗口时这份副本会被丢弃）。</summary>
    private void CommitCustomForm()
    {
        if (_editingCustom is null)
            return;

        _editingCustom.Name = string.IsNullOrWhiteSpace(CustomName.Text)
            ? Loc.T("Settings.Provider.DefaultName")
            : CustomName.Text.Trim();
        _editingCustom.BaseUrl = CustomBaseUrl.Text?.Trim() ?? string.Empty;
        _editingCustom.ApiKey = CustomKey.Text?.Trim() ?? string.Empty;
        _editingCustom.Model = CustomModelText.Text?.Trim() ?? string.Empty;
    }

    private void OnAddProvider(object? sender, RoutedEventArgs e)
    {
        var provider = new CustomProvider
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = Loc.T("Settings.Provider.NewName", _settings.CustomProviders.Count + 1)
        };
        _settings.CustomProviders.Add(provider);
        BuildProviderList();
        SelectRow(provider.Id);
    }

    private async void OnDeleteProvider(ProviderRow row)
    {
        // 删除不可恢复，二次确认
        var ok = await Dialogs.ConfirmAsync(
            this,
            Loc.T("Settings.DeleteProvider.Title"),
            Loc.T("Settings.DeleteProvider.Message", row.DisplayName),
            Loc.T("Common.Delete"),
            Loc.T("Common.Cancel"));
        if (!ok)
            return;

        _settings.CustomProviders.RemoveAll(p => p.Id == row.Key);
        if (_settings.CustomProviderId == row.Key)
            _settings.CustomProviderId = null;

        if (_selectedKey == row.Key)
            SelectRow(null);

        BuildProviderList();
    }

    // ———————————————— 本地模型下载 ————————————————

    /// <summary>本地模型列表：已装=删除，下载中=停止，下了半截=继续，其余=下载。</summary>
    private void BuildModelList()
    {
        ModelList.Children.Clear();

        var installed = ModelCatalog.List()
            .Select(m => System.IO.Path.GetFileName(m.FullPath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var incomplete = ModelDownloader.FindIncomplete()
            .Select(x => x.FileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _modelSignature = string.Empty;

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
                Text = ModelNote(model),
                FontSize = 11,
                Opacity = 0.6,
                TextWrapping = TextWrapping.Wrap
            });
            Grid.SetColumn(info, 0);
            row.Children.Add(info);

            var done = installed.Contains(model.FileName);
            var active = ModelDownloads.IsActive(model);
            var half = !done && !active && incomplete.Contains(model.FileName);

            var button = new Button
            {
                Content = Loc.T(active ? "Settings.Model.Stop" : done ? "Common.Delete" : half ? "Settings.Model.Continue" : "Settings.Model.Download"),
                MinWidth = 76,
                VerticalAlignment = VerticalAlignment.Center
            };

            var captured = model;
            button.Click += (_, _) => OnModelAction(captured);

            Grid.SetColumn(button, 1);
            row.Children.Add(button);

            ModelList.Children.Add(row);
            ModelList.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.Parse("#2A2A31")) });

            _modelSignature += $"{model.FileName}:{(done ? "done" : active ? "active" : half ? "half" : "new")};";
        }

        UpdateDownloadStatus();
    }

    /// <summary>模型说明走语言包；外部语言包没带这个键时退回 ModelCatalog 的英文兜底。</summary>
    private static string ModelNote(ModelSuggestion model)
    {
        var key = $"Settings.Model.Note.{model.Display}";
        var text = Loc.T(key);
        return string.Equals(text, key, StringComparison.Ordinal) ? model.Note : text;
    }

    /// <summary>按钮只有四种含义：下载 / 继续 / 停止 / 删除。</summary>
    private async void OnModelAction(ModelSuggestion model)
    {
        if (ModelDownloads.IsActive(model))
        {
            ModelDownloads.Cancel(model);
            DownloadStatus.Text = Loc.T("Settings.Model.Stopping", model.Display);
            return;
        }

        var installed = ModelCatalog.List().Any(m =>
            string.Equals(System.IO.Path.GetFileName(m.FullPath), model.FileName, StringComparison.OrdinalIgnoreCase));

        if (installed)
        {
            var ok = await Dialogs.ConfirmAsync(
                this,
                Loc.T("Settings.Model.DeleteTitle"),
                Loc.T("Settings.Model.DeleteMessage", model.Display, model.SizeLabel),
                Loc.T("Common.Delete"),
                Loc.T("Common.Cancel"));
            if (!ok)
                return;

            if (!ModelDownloads.Delete(model))
            {
                DownloadStatus.Text = Loc.T("Settings.Model.Downloading");
                return;
            }

            DownloadStatus.Text = Loc.T("Settings.Model.Deleted", model.Display);
            BuildModelList();
            return;
        }

        DownloadStatus.Text = Loc.T("Settings.Model.Connecting", model.Display);
        ModelDownloads.Start(model);
    }

    /// <summary>下载状态变化：状态变了就重建按钮，只是进度变了就刷新进度条。</summary>
    private void OnDownloadsChanged()
    {
        if (!IsVisible)
            return;

        var signature = BuildSignature();
        if (signature != _modelSignature)
            BuildModelList();
        else
            UpdateDownloadStatus();
    }

    private static string BuildSignature()
    {
        var installed = ModelCatalog.List()
            .Select(m => System.IO.Path.GetFileName(m.FullPath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var incomplete = ModelDownloader.FindIncomplete()
            .Select(x => x.FileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var builder = new System.Text.StringBuilder();
        foreach (var model in ModelCatalog.Suggestions)
        {
            var state = installed.Contains(model.FileName)
                ? "done"
                : ModelDownloads.IsActive(model)
                    ? "active"
                    : incomplete.Contains(model.FileName) ? "half" : "new";
            builder.Append(model.FileName).Append(':').Append(state).Append(';');
        }

        return builder.ToString();
    }

    /// <summary>进度条显示正在下的那个；没在下的就显示最近一次的结果。</summary>
    private void UpdateDownloadStatus()
    {
        var running = ModelCatalog.Suggestions
            .Select(ModelDownloads.Get)
            .FirstOrDefault(j => j is { Running: true });

        if (running is not null)
        {
            DownloadBar.Value = running.Fraction;
            DownloadStatus.Text = running.Total > 0
                ? Loc.T("Settings.Model.Progress", running.Model.Display, running.Source,
                    running.Received / 1048576.0, running.Total / 1048576.0, running.Fraction)
                : Loc.T("Settings.Model.ProgressUnknown", running.Model.Display, running.Source,
                    running.Received / 1048576.0);
            return;
        }

        var last = ModelCatalog.Suggestions
            .Select(ModelDownloads.Get)
            .Where(j => j is not null)
            .OrderByDescending(j => j!.FinishedAt)
            .FirstOrDefault();

        if (last is null)
        {
            DownloadBar.Value = 0;
            return;
        }

        DownloadBar.Value = last.Succeeded ? 1 : 0;
        DownloadStatus.Text = last.Succeeded
            ? Loc.T("Settings.Model.Downloaded", last.Model.Display, last.Model.SizeLabel)
            : Loc.T("Settings.Model.StatusLine", last.Model.Display, last.Error);
    }

    // ———————————————— 云端列表 / 测试 ————————————————

    /// <summary>硅基流动「连接测试」：能连上 + 拉到模型列表才算通，拉到的模型同步进主界面模型菜单。</summary>
    private async void OnFetchSilicon(object? sender, RoutedEventArgs e)
    {
        var key = SiliconKey.Text?.Trim() ?? string.Empty;
        if (key.Length == 0)
        {
            SiliconStatus.Text = Loc.T("Settings.Status.FillKey");
            return;
        }

        FetchSiliconButton.IsEnabled = false;
        SiliconStatus.Text = Loc.T("Settings.Status.Testing");
        try
        {
            var models = await SiliconFlowClient.ListModelsAsync(SiliconBaseUrl.Text ?? _settings.SiliconFlowBaseUrl, key);
            CloudModelsFetched?.Invoke("siliconflow", models);

            SiliconStatus.Text = models.Count == 0
                ? Loc.T("Settings.Status.SiliconEmpty")
                : Loc.T("Settings.Status.SiliconOk", models.Count);
        }
        catch (Exception ex)
        {
            SiliconStatus.Text = Loc.T("Settings.Status.ConnectFailed", ex.Message);
        }
        finally
        {
            FetchSiliconButton.IsEnabled = true;
        }
    }

    /// <summary>自定义提供商「连接测试」：OpenAI 兼容的 /models，不过滤（用户自己知道哪个是识别模型）。</summary>
    private async void OnFetchCustom(object? sender, RoutedEventArgs e)
    {
        var baseUrl = CustomBaseUrl.Text?.Trim() ?? string.Empty;
        var key = CustomKey.Text?.Trim() ?? string.Empty;
        if (baseUrl.Length == 0 || key.Length == 0)
        {
            CustomStatus.Text = Loc.T("Settings.Status.FillUrlKey");
            return;
        }

        FetchCustomButton.IsEnabled = false;
        CustomStatus.Text = Loc.T("Settings.Status.Testing");
        try
        {
            var models = await SiliconFlowClient.ListModelsAsync(baseUrl, key, audioOnly: false);
            // 先把表单值收回副本，回调里要往这个提供商身上写模型缓存
            CommitCustomForm();
            if (_editingCustom is not null)
            {
                _editingCustom.CachedModels = models.Select(m => m.Id).ToList();
                CloudModelsFetched?.Invoke(_editingCustom.Id, models);
            }

            CustomStatus.Text = models.Count == 0
                ? Loc.T("Settings.Status.CustomEmpty")
                : Loc.T("Settings.Status.CustomOk", models.Count);
        }
        catch (Exception ex)
        {
            CustomStatus.Text = Loc.T("Settings.Status.ConnectFailed", ex.Message);
        }
        finally
        {
            FetchCustomButton.IsEnabled = true;
        }
    }

    /// <summary>天翼星辰：用一段 0.5 秒静音打一次短语音识别，返回 10000 就说明 Key 可用。</summary>
    private async void OnTestTeleAi(object? sender, RoutedEventArgs e)
    {
        var apiKey = TeleAiApiKey.Text?.Trim() ?? string.Empty;
        if (apiKey.Length == 0)
        {
            TeleAiStatus.Text = Loc.T("Settings.Status.FillKey");
            return;
        }

        TestTeleAiButton.IsEnabled = false;
        TeleAiStatus.Text = Loc.T("Settings.Status.Testing");

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
                ? Loc.T("Settings.Status.TeleOk")
                : Loc.T("Settings.Status.TeleFailed", (int)response.StatusCode, Trim(body, 160));
        }
        catch (Exception ex)
        {
            TeleAiStatus.Text = Loc.T("Settings.Status.TeleError", TlsDiagnostics.Describe(ex));
        }
        finally
        {
            TestTeleAiButton.IsEnabled = true;
        }
    }

    private static string Trim(string? text, int count)
    {
        if (string.IsNullOrEmpty(text)) return Loc.T("Common.Empty");
        var flat = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return flat.Length <= count ? flat : flat[..count] + "…";
    }

    // ———————————————— 保存 / 关闭 ————————————————

    /// <summary>「写了就不管」：不管怎么关（完成按钮、右上角 ×），改动都自动落盘。</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        CommitAll();
        base.OnClosing(e);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    /// <summary>把所有表单的值收回副本并通知调用方写盘。</summary>
    private void CommitAll()
    {
        // 先把还在编辑的自定义表单收回来，再整体写回
        CommitCustomForm();

        var result = _settings.Clone();
        result.SiliconFlowApiKey = SiliconKey.Text?.Trim() ?? string.Empty;
        result.SiliconFlowBaseUrl = string.IsNullOrWhiteSpace(SiliconBaseUrl.Text)
            ? "https://api.siliconflow.cn/v1"
            : SiliconBaseUrl.Text!.Trim();

        result.TeleAiApiKey = TeleAiApiKey.Text?.Trim() ?? string.Empty;
        result.Denoise = DenoiseCheck.IsChecked == true;

        Saved?.Invoke(result);
    }
}
