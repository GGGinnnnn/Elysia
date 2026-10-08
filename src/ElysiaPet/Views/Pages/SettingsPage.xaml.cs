using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using ElysiaPet.Models;
using ElysiaPet.Services;

namespace ElysiaPet.Views.Pages;

/// <summary>
/// 系统设置页。所有输入都经过统一清洗（<see cref="TextParsers"/>），
/// 不再出现旧版那种「输入汉字导致 int() 抛异常、然后悄悄什么都不保存」的情况。
/// </summary>
public partial class SettingsPage : PageBase
{
    private readonly AiClient _tester = new();
    private bool _loading;

    public SettingsPage(IDashboardHost host) : base(host)
    {
        InitializeComponent();

        // 事件必须在 InitializeComponent 之后挂：否则 XAML 解析 Slider 的
        // Minimum/Maximum 时就会触发 ValueChanged，而那时后面的 TextBlock 还没创建。
        PetWidthSlider.ValueChanged += OnPetWidthChanged;
        BubbleFontSlider.ValueChanged += OnBubbleFontChanged;

        OnActivated();
    }

    public override void OnActivated()
    {
        _loading = true;
        try
        {
            var config = Config;

            ApiKeyBox.Text = config.ApiKey;
            ApiUrlBox.Text = config.ApiBaseUrl;
            ModelBox.Text = config.Model;
            PresetBox.Text = config.RolePreset;
            UserNameBox.Text = config.UserInfo.Name;

            LevelCombo.SelectedIndex = config.Level switch
            {
                WindowLevel.AlwaysOnTop => 0,
                WindowLevel.AboveNormal => 1,
                WindowLevel.DesktopOnly => 2,
                _ => 0,
            };

            PetWidthSlider.Value = Math.Clamp(config.PetWidth, PetWidthSlider.Minimum, PetWidthSlider.Maximum);
            BubbleFontSlider.Value = Math.Clamp(config.BubbleFontSize, BubbleFontSlider.Minimum, BubbleFontSlider.Maximum);
            UpdateSliderLabels();

            OpacityCheck.IsChecked = config.OpacityEnabled;
            OpacityTimeoutBox.Text = config.OpacityTimeout.ToString();
            OpacityValueBox.Text = config.OpacityValue.ToString("0.0#");

            TrayCheck.IsChecked = config.TrayIconEnabled;

            MaxChatBox.Text = config.MaxChatHistory.ToString();
            MaxBubbleBox.Text = config.MaxBubbleHistory.ToString();
            ContextBox.Text = config.ContextLinkCount.ToString();
            IdleMinutesBox.Text = Math.Max(1, config.IdleTimeout / 60).ToString();
            AiIdleCheck.IsChecked = config.AiIdleEnabled;

            AutostartRegCheck.IsChecked = config.AutostartEnabled;
            AutostartLnkCheck.IsChecked = config.ShortcutStartEnabled;
        }
        finally
        {
            _loading = false;
        }

        RefreshAutostartState();
    }

    private void UpdateSliderLabels()
    {
        PetWidthText.Text = $"{PetWidthSlider.Value:0} px";
        BubbleFontText.Text = $"{BubbleFontSlider.Value:0} px";
    }

    private void OnPetWidthChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        UpdateSliderLabels();
    }

    private void OnBubbleFontChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        UpdateSliderLabels();
    }

    // ==================== 保存 ====================

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        var config = Config;

        config.ApiKey = ApiKeyBox.Text.Trim();
        config.ApiBaseUrl = string.IsNullOrWhiteSpace(ApiUrlBox.Text)
            ? "https://api.deepseek.com/v1/chat/completions"
            : ApiUrlBox.Text.Trim();
        config.Model = string.IsNullOrWhiteSpace(ModelBox.Text) ? "deepseek-flash" : ModelBox.Text.Trim();
        config.RolePreset = string.IsNullOrWhiteSpace(PresetBox.Text) ? PersonaDefaults.RolePreset : PresetBox.Text.Trim();
        config.UserInfo.Name = string.IsNullOrWhiteSpace(UserNameBox.Text) ? "主人" : UserNameBox.Text.Trim();

        config.Level = LevelCombo.SelectedIndex switch
        {
            1 => WindowLevel.AboveNormal,
            2 => WindowLevel.DesktopOnly,
            _ => WindowLevel.AlwaysOnTop,
        };

        config.PetWidth = PetWidthSlider.Value;
        config.BubbleFontSize = BubbleFontSlider.Value;
        config.OpacityEnabled = OpacityCheck.IsChecked == true;

        // 容错：随便输入汉字也不会把设置写坏，抽不出数字就用原值
        config.OpacityTimeout = TextParsers.ExtractInt(OpacityTimeoutBox.Text) is { } timeout
            ? Math.Clamp(timeout, 1, 3600)
            : Math.Max(config.OpacityTimeout, 30);
        config.OpacityValue = TextParsers.ExtractDouble(OpacityValueBox.Text) is { } opacity
            ? Math.Clamp(opacity, 0.1, 1.0)
            : config.OpacityValue;

        config.TrayIconEnabled = TrayCheck.IsChecked == true;

        config.MaxChatHistory = TextParsers.ExtractInt(MaxChatBox.Text) is { } maxChat
            ? Math.Clamp(maxChat, 1, 500) : config.MaxChatHistory;
        config.MaxBubbleHistory = TextParsers.ExtractInt(MaxBubbleBox.Text) is { } maxBubble
            ? Math.Clamp(maxBubble, 1, 500) : config.MaxBubbleHistory;
        config.ContextLinkCount = TextParsers.ExtractInt(ContextBox.Text) is { } context
            ? Math.Clamp(context, 1, config.MaxChatHistory) : Math.Min(config.ContextLinkCount, config.MaxChatHistory);

        config.IdleTimeout = TextParsers.ExtractInt(IdleMinutesBox.Text) is { } minutes
            ? Math.Clamp(minutes, 1, 1440) * 60
            : config.IdleTimeout;
        config.AiIdleEnabled = AiIdleCheck.IsChecked == true;

        // 两种自启动方式互斥，避免同时写入两份启动项
        if (AutostartRegCheck.IsChecked == true && AutostartLnkCheck.IsChecked == true)
        {
            AutostartLnkCheck.IsChecked = false;
            Status("两种自启动方式只需选一种，已保留注册表方式。");
        }

        config.AutostartEnabled = AutostartRegCheck.IsChecked == true;
        config.ShortcutStartEnabled = AutostartLnkCheck.IsChecked == true;

        AutostartService.SetRegistryAutostart(config.AutostartEnabled, out var regMessage);
        AutostartService.SetShortcutAutostart(config.ShortcutStartEnabled, out var lnkMessage);

        await Host.SaveAndApplyAsync();

        // 保存后回填一次，把钳制后的真实值展示给用户
        OnActivated();
        RefreshAutostartState();

        Status($"{regMessage}；{lnkMessage}");
    }

    private void OnRevert(object sender, RoutedEventArgs e)
    {
        OnActivated();
        Status("已放弃尚未保存的修改");
    }

    private async void OnRestoreDefaults(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "将把「角色预设」「待机语录」「整点报时语录」恢复成内置的爱莉希雅版本。\n" +
            "对话记录、日程和快捷启动不会被清除。确定继续吗？",
            "恢复默认人设",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        var config = Config;
        config.RolePreset = PersonaDefaults.RolePreset;
        config.IdleMessages = new System.Collections.Generic.List<string>(PersonaDefaults.IdleMessages);
        config.HourlyMessages = PersonaDefaults.CreateHourlyMessages();

        await Host.SaveAndApplyAsync();
        OnActivated();
        Status("已恢复内置的爱莉希雅人设与语录 ♪");
    }

    private void OnOpenConfigFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{AppPaths.ExecutableDirectory}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppLog.Warn($"打开配置目录失败: {ex.Message}");
            Status("打开配置目录失败，请手动前往程序所在文件夹");
        }
    }

    // ==================== 连接测试 ====================

    private async void OnTestConnection(object sender, RoutedEventArgs e)
    {
        var config = Config;

        // 用界面上的当前输入临时测试，未保存也能先验证
        var probe = new AppConfig
        {
            ApiKey = ApiKeyBox.Text.Trim(),
            ApiBaseUrl = string.IsNullOrWhiteSpace(ApiUrlBox.Text) ? config.ApiBaseUrl : ApiUrlBox.Text.Trim(),
            Model = string.IsNullOrWhiteSpace(ModelBox.Text) ? config.Model : ModelBox.Text.Trim(),
            RolePreset = string.IsNullOrWhiteSpace(PresetBox.Text) ? config.RolePreset : PresetBox.Text.Trim(),
            UserInfo = new UserInfo { Name = string.IsNullOrWhiteSpace(UserNameBox.Text) ? "主人" : UserNameBox.Text.Trim() },
        };

        TestResultText.Text = "正在连接……";
        Status("正在测试大模型连接……");

        try
        {
            var reply = await _tester.IdleLineAsync(probe, TimeSpan.FromSeconds(15));
            var (text, state) = TextParsers.ParseReply(TextParsers.StripThinking(reply));
            TestResultText.Text = $"✓ 连接成功！模型回复：{text}（心情标签：{state}）";
            Status("连接测试通过 ✓");
        }
        catch (AiException ex)
        {
            TestResultText.Text = $"✖ {ex.Message}";
            Status("连接测试失败，请检查 API Key 与接口地址");
        }
        catch (Exception ex)
        {
            AppLog.Error("连接测试异常", ex);
            TestResultText.Text = $"✖ 未预期错误：{ex.Message}";
            Status("连接测试失败");
        }
    }

    // ==================== 自启动状态 ====================

    private void OnRefreshAutostart(object sender, RoutedEventArgs e) => RefreshAutostartState();

    private void RefreshAutostartState()
    {
        var (registry, shortcut) = AutostartService.GetCurrentState();
        AutostartStateText.Text =
            $"系统当前状态：注册表启动项 {(registry ? "已存在 ✓" : "不存在")}；启动文件夹快捷方式 {(shortcut ? "已存在 ✓" : "不存在")}。\n" +
            $"启动命令：{AutostartService.ResolveLaunchCommand()}";
    }

    /// <summary>页面实例与窗口同生命周期，这里顺手释放测试用连接池。</summary>
    public void DisposeTester() => _tester.Dispose();
}
