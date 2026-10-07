using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ElysiaPet.Services;

namespace ElysiaPet.Views.Pages;

/// <summary>
/// 整点报时页。旧版把语录字典压成文本再解压回来，格式一错就整段丢失；
/// 这里解析时逐行校验，坏行会被跳过并明确报出是第几行。
/// </summary>
public partial class ChimePage : PageBase
{
    private bool _loading;

    public ChimePage(IDashboardHost host) : base(host)
    {
        InitializeComponent();
        OnActivated();
    }

    public override void OnActivated()
    {
        _loading = true;
        try
        {
            ChimeCheck.IsChecked = Config.HourlyChime;
            ChimeEditor.Text = Serialize(Config.HourlyMessages);
        }
        finally
        {
            _loading = false;
        }

        UpdateSummary();
    }

    private static string Serialize(Dictionary<string, List<string>> messages)
    {
        var builder = new StringBuilder();
        foreach (var hour in messages.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            foreach (var line in messages[hour])
                builder.AppendLine($"{hour}|{line}");
        }
        return builder.ToString().TrimEnd();
    }

    private void UpdateSummary()
    {
        var hours = Config.HourlyMessages.Count;
        var lines = Config.HourlyMessages.Values.Sum(list => list.Count);
        SummaryText.Text = $"当前已配置 {hours} 个整点、共 {lines} 条语录";
    }

    private void OnChimeToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Config.HourlyChime = ChimeCheck.IsChecked == true;
        _ = Host.SaveAndApplyAsync();
        Status(Config.HourlyChime ? "整点报时已开启" : "整点报时已关闭");
    }

    /// <summary>把编辑区文本解析成字典，同时收集格式错误的行号。</summary>
    private (Dictionary<string, List<string>> Messages, List<int> BadLines) Parse()
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var badLines = new List<int>();
        var lines = ChimeEditor.Text.Replace("\r\n", "\n").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i].Trim();
            if (raw.Length == 0 || raw.StartsWith('#')) continue;

            var separator = raw.IndexOf('|');
            if (separator <= 0 || separator >= raw.Length - 1)
            {
                badLines.Add(i + 1);
                continue;
            }

            var hourText = raw[..separator].Trim();
            var message = raw[(separator + 1)..].Trim();
            if (message.Length == 0 || !int.TryParse(hourText, out var hour) || hour is < 0 or > 23)
            {
                badLines.Add(i + 1);
                continue;
            }

            var key = hour.ToString("00");
            if (!result.TryGetValue(key, out var list))
            {
                list = new List<string>();
                result[key] = list;
            }
            list.Add(message);
        }

        return (result, badLines);
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        var (messages, badLines) = Parse();

        if (messages.Count == 0)
        {
            Status("至少要保留一条合法语录，格式为  09|九点整啦~");
            return;
        }

        if (badLines.Count > 0)
        {
            var preview = string.Join("、", badLines.Take(8));
            var more = badLines.Count > 8 ? $" 等 {badLines.Count} 行" : string.Empty;
            var confirm = MessageBox.Show(
                $"第 {preview}{more} 行格式不正确，保存时会被忽略。\n继续保存吗？（正确格式示例：09|九点整啦~）",
                "存在格式问题",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;
        }

        Config.HourlyMessages = messages;
        await Host.SaveAndApplyAsync();
        OnActivated();
        Status($"报时语录已保存，共 {messages.Values.Sum(v => v.Count)} 条");
    }

    private void OnRevert(object sender, RoutedEventArgs e)
    {
        OnActivated();
        Status("已放弃修改");
    }

    private void OnValidate(object sender, RoutedEventArgs e)
    {
        var (messages, badLines) = Parse();
        if (badLines.Count == 0)
            Status($"格式全部正确，共 {messages.Values.Sum(v => v.Count)} 条语录");
        else
            Status($"发现 {badLines.Count} 行格式不正确：第 {string.Join("、", badLines.Take(10))} 行");
    }

    private void OnPreview(object sender, RoutedEventArgs e)
    {
        var (messages, _) = Parse();
        if (messages.Count == 0)
        {
            Status("没有可用语录，先按格式写一条吧");
            return;
        }

        var hour = DateTime.Now.Hour.ToString("00");
        var pool = messages.TryGetValue(hour, out var current) && current.Count > 0
            ? current
            : messages.Values.First();

        var line = pool[Random.Shared.Next(pool.Count)];
        Host.Pet.ShowMessage(line);
        Host.Pet.ChangeState("wink");
        Status($"已在桌宠上试听：{line}");
    }
}
