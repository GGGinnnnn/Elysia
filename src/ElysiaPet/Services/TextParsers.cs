using System;
using System.Text.RegularExpressions;
using ElysiaPet.Models;

namespace ElysiaPet.Services;

/// <summary>
/// 文本解析工具：心情标签剥离、自然语言日程识别、用户输入数字清洗。
/// 旧版把这些正则散落在 UI 事件里，这里集中成可测试的纯函数。
/// </summary>
public static partial class TextParsers
{
    private static readonly string[] KnownStates =
    {
        "waiting", "cry", "question", "wink", "like", "speechless", "hurry",
    };

    [GeneratedRegex(@"\[\s*(waiting|cry|question|wink|like|speechless|hurry)\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex MoodTagRegex();

    [GeneratedRegex(@"\[\s*[a-zA-Z_\s]*\s*\]")]
    private static partial Regex StrayTagRegex();

    [GeneratedRegex(@"(\d{1,2})\s*[:：]\s*(\d{2})")]
    private static partial Regex TimeRegex();

    [GeneratedRegex(@"[-+]?\d*\.?\d+")]
    private static partial Regex NumberRegex();

    /// <summary>
    /// 剥离模型回复里的心情标签，返回干净文本与对应表情名。
    /// 找不到标签时回退到 <paramref name="fallback"/>（默认 wink，与旧版一致）。
    /// </summary>
    public static (string Text, string State) ParseReply(string? reply, string fallback = "wink")
    {
        var text = (reply ?? string.Empty).Trim();
        var state = fallback;

        var match = MoodTagRegex().Match(text);
        if (match.Success)
        {
            state = match.Groups[1].Value.ToLowerInvariant();
            text = MoodTagRegex().Replace(text, string.Empty);
        }

        // 模型偶尔会输出 [开心] 这种非法标签，一并清掉，避免气泡里出现方括号噪声
        text = StrayTagRegex().Replace(text, string.Empty);

        // 中文引号包裹整体回复时也去掉，旧版只处理了英文双引号
        text = text.Trim().Trim('"', '“', '”', '\'').Trim();

        // 模型有时会不自觉换行，气泡布局会被撑坏，这里压成单行
        text = Regex.Replace(text, @"\s*[\r\n]+\s*", " ");

        if (!Array.Exists(KnownStates, s => s == state)) state = fallback;
        return (text, state);
    }

    /// <summary>
    /// 尝试从「12:00提醒我吃饭」这类自然语言里解析日程。
    /// </summary>
    public static bool TryParseReminder(string? input, out string time, out string content)
    {
        time = string.Empty;
        content = string.Empty;

        var text = (input ?? string.Empty).Trim();
        if (text.Length == 0 || !text.Contains("提醒", StringComparison.Ordinal)) return false;

        var match = TimeRegex().Match(text);
        if (!match.Success) return false;

        var hour = int.Parse(match.Groups[1].Value);
        var minute = int.Parse(match.Groups[2].Value);
        if (hour > 23 || minute > 59) return false;

        time = $"{hour:00}:{minute:00}";

        // 内容取「提醒我 / 提醒」之后的部分；都没有时退化为整句
        var index = text.LastIndexOf("提醒我", StringComparison.Ordinal);
        var cut = index >= 0 ? index + "提醒我".Length : text.LastIndexOf("提醒", StringComparison.Ordinal) + "提醒".Length;
        content = cut > 0 && cut < text.Length ? text[cut..].Trim() : text;

        // 去掉内容里残留的时间片段，让提醒读起来自然
        content = TimeRegex().Replace(content, string.Empty).Trim(' ', '，', ',', '。', '的', '：', ':');
        if (content.Length == 0) content = "该做这件事啦";
        return true;
    }

    /// <summary>从自由文本里抽出第一个数字（用于「5 分钟」这种输入框容错）。</summary>
    public static int? ExtractInt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = NumberRegex().Match(text);
        if (!match.Success) return null;
        return double.TryParse(match.Value, out var value) ? (int)Math.Round(value) : null;
    }

    /// <summary>从自由文本里抽出第一个小数（用于透明度输入框容错）。</summary>
    public static double? ExtractDouble(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = NumberRegex().Match(text);
        return match.Success && double.TryParse(match.Value, out var value) ? value : null;
    }

    /// <summary>校验 HH:mm 格式。</summary>
    public static bool IsValidTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var match = TimeRegex().Match(value);
        if (!match.Success) return false;
        return int.Parse(match.Groups[1].Value) <= 23 && int.Parse(match.Groups[2].Value) <= 59;
    }

    /// <summary>规范化 HH:mm（自动补零、中文冒号转英文）。</summary>
    public static string NormalizeTime(string value)
    {
        var match = TimeRegex().Match(value);
        if (!match.Success) return value.Trim();
        return $"{int.Parse(match.Groups[1].Value):00}:{int.Parse(match.Groups[2].Value):00}";
    }

    /// <summary>把 AI 回复里可能带上的思考片段剥掉（旧版靠 prompt 约束，这里再加一道保险）。</summary>
    public static string StripThinking(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var cleaned = Regex.Replace(text, @"</?think>", string.Empty, RegexOptions.IgnoreCase);
        return cleaned.Trim();
    }

    /// <summary>状态名 -> GIF 文件名（不含扩展名）。所有已知状态都有一一对应的素材。</summary>
    public static string StateToAsset(string state) => Array.Exists(KnownStates, s => s == state) ? state : "waiting";
}
