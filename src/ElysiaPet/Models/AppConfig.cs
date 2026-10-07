using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ElysiaPet.Models;

/// <summary>桌宠显示的窗口层级。替换掉原先到处比较中文字符串的写法。</summary>
public enum WindowLevel
{
    /// <summary>所有应用上方（始终置顶）。</summary>
    AlwaysOnTop,

    /// <summary>非全屏应用上方。</summary>
    AboveNormal,

    /// <summary>只显示在桌面（普通窗口层级）。</summary>
    DesktopOnly,
}

/// <summary>一轮对话（用户提问 + 桌宠回复）。</summary>
public sealed class ChatTurn
{
    [JsonPropertyName("user")]
    public string User { get; set; } = string.Empty;

    [JsonPropertyName("pet")]
    public string Pet { get; set; } = string.Empty;

    /// <summary>记录时间，仅用于界面展示（旧配置里时间戳被拼在 user 文本中，这里独立成字段）。</summary>
    [JsonPropertyName("time")]
    public string Time { get; set; } = string.Empty;
}

/// <summary>一条日程提醒。</summary>
public sealed class Reminder
{
    /// <summary>触发时间，格式 HH:mm。</summary>
    [JsonPropertyName("time")]
    public string Time { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>防止同一分钟内重复触发。</summary>
    [JsonIgnore]
    public bool Fired { get; set; }
}

/// <summary>右键菜单里的快捷启动项。</summary>
public sealed class QuickApp
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
}

/// <summary>用户信息。</summary>
public sealed class UserInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "主人";
}

/// <summary>
/// 应用程序配置。<see cref="ConfigService"/> 负责与磁盘上的 config.json 互转。
/// 与旧 Python 版 pet_settings.json 结构保持一致（仅新增若干可选字段），
/// 所有属性都有默认值，因此缺字段的老配置文件也能安全读入。
/// </summary>
public sealed class AppConfig
{
    // ---------------- 模型接入 ----------------

    [JsonPropertyName("api_key")]
    public string ApiKey { get; set; } = string.Empty;

    [JsonPropertyName("api_base_url")]
    public string ApiBaseUrl { get; set; } = "https://api.deepseek.com/v1/chat/completions";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "deepseek-flash";

    [JsonPropertyName("role_preset")]
    public string RolePreset { get; set; } = PersonaDefaults.RolePreset;

    [JsonPropertyName("user_info")]
    public UserInfo UserInfo { get; set; } = new();

    // ---------------- 历史记录 ----------------

    [JsonPropertyName("history")]
    public List<ChatTurn> History { get; set; } = new();

    [JsonPropertyName("bubble_history")]
    public List<ChatTurn> BubbleHistory { get; set; } = new();

    [JsonPropertyName("max_chat_history")]
    public int MaxChatHistory { get; set; } = 20;

    [JsonPropertyName("max_bubble_history")]
    public int MaxBubbleHistory { get; set; } = 20;

    /// <summary>每次对话携带的历史轮数。</summary>
    [JsonPropertyName("context_link_count")]
    public int ContextLinkCount { get; set; } = 5;

    // ---------------- 外观与交互 ----------------

    [JsonPropertyName("tray_icon_enabled")]
    public bool TrayIconEnabled { get; set; } = true;

    [JsonPropertyName("window_level")]
    public WindowLevel Level { get; set; } = WindowLevel.AlwaysOnTop;

    [JsonPropertyName("pet_width")]
    public double PetWidth { get; set; } = 220;

    [JsonPropertyName("window_x")]
    public double? WindowX { get; set; }

    [JsonPropertyName("window_y")]
    public double? WindowY { get; set; }

    [JsonPropertyName("opacity_enabled")]
    public bool OpacityEnabled { get; set; } = true;

    /// <summary>
    /// 无操作多久后开始变透明（秒）。
    /// 默认 30 秒而不是旧版的 5 秒：5 秒太短，鼠标一停下来桌宠就变成 30% 不透明，
    /// 看上去像糊在壁纸上、连表情动画都看不清。
    /// </summary>
    [JsonPropertyName("opacity_timeout")]
    public int OpacityTimeout { get; set; } = 30;

    [JsonPropertyName("opacity_value")]
    public double OpacityValue { get; set; } = 0.3;

    [JsonPropertyName("bubble_font_size")]
    public double BubbleFontSize { get; set; } = 13;

    // ---------------- 自动冒泡与报时 ----------------

    [JsonPropertyName("idle_timeout")]
    public int IdleTimeout { get; set; } = 1200;

    [JsonPropertyName("ai_idle_enabled")]
    public bool AiIdleEnabled { get; set; }

    [JsonPropertyName("idle_messages")]
    public List<string> IdleMessages { get; set; } = new(PersonaDefaults.IdleMessages);

    [JsonPropertyName("hourly_chime")]
    public bool HourlyChime { get; set; } = true;

    [JsonPropertyName("hourly_messages")]
    public Dictionary<string, List<string>> HourlyMessages { get; set; } = PersonaDefaults.CreateHourlyMessages();

    // ---------------- 日程与快捷启动 ----------------

    [JsonPropertyName("reminders")]
    public List<Reminder> Reminders { get; set; } = new();

    [JsonPropertyName("quick_apps")]
    public List<QuickApp> QuickApps { get; set; } = new();

    // ---------------- 开机自启动 ----------------

    [JsonPropertyName("autostart_enabled")]
    public bool AutostartEnabled { get; set; }

    [JsonPropertyName("shortcut_start_enabled")]
    public bool ShortcutStartEnabled { get; set; }

    /// <summary>
    /// 读取后做一次范围钳制，避免手工改配置文件把程序玩坏（旧版把这些校验散落在各处 UI 代码里）。
    /// </summary>
    public void Normalize()
    {
        MaxChatHistory = Clamp(MaxChatHistory, 1, 500);
        MaxBubbleHistory = Clamp(MaxBubbleHistory, 1, 500);
        ContextLinkCount = Clamp(ContextLinkCount, 1, MaxChatHistory);
        IdleTimeout = Clamp(IdleTimeout, 30, 86_400);
        OpacityTimeout = Clamp(OpacityTimeout, 1, 3_600);
        OpacityValue = double.IsFinite(OpacityValue) ? System.Math.Clamp(OpacityValue, 0.1, 1.0) : 0.3;
        PetWidth = double.IsFinite(PetWidth) ? System.Math.Clamp(PetWidth, 80, 800) : 220;
        BubbleFontSize = double.IsFinite(BubbleFontSize) ? System.Math.Clamp(BubbleFontSize, 8, 40) : 13;

        UserInfo ??= new UserInfo();
        if (string.IsNullOrWhiteSpace(UserInfo.Name)) UserInfo.Name = "主人";
        if (string.IsNullOrWhiteSpace(ApiBaseUrl)) ApiBaseUrl = "https://api.deepseek.com/v1/chat/completions";
        if (string.IsNullOrWhiteSpace(Model)) Model = "deepseek-flash";
        if (string.IsNullOrWhiteSpace(RolePreset)) RolePreset = PersonaDefaults.RolePreset;

        History ??= new List<ChatTurn>();
        BubbleHistory ??= new List<ChatTurn>();
        Reminders ??= new List<Reminder>();
        QuickApps ??= new List<QuickApp>();
        if (IdleMessages is null || IdleMessages.Count == 0)
            IdleMessages = new List<string>(PersonaDefaults.IdleMessages);
        if (HourlyMessages is null || HourlyMessages.Count == 0)
            HourlyMessages = PersonaDefaults.CreateHourlyMessages();
    }

    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
}
