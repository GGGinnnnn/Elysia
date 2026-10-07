using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ElysiaPet.Models;
using ElysiaPet.Services;

namespace ElysiaPet.Views.Pages;

/// <summary>日程提醒页：列表 + 手动增删 + 清理已过期。</summary>
public partial class RemindersPage : PageBase
{
    public RemindersPage(IDashboardHost host) : base(host)
    {
        InitializeComponent();
        OnActivated();
    }

    public override void OnActivated()
    {
        ReminderList.Items.Clear();

        var reminders = Config.Reminders;
        if (reminders.Count == 0)
        {
            ReminderList.Items.Add(new ListBoxItem
            {
                Content = "还没有任何日程。试试对桌宠说：「12:00提醒我吃饭」",
                IsEnabled = false,
            });
            return;
        }

        // 按时间排序展示，但保留原始索引以便删除时对得上
        var ordered = reminders
            .Select((reminder, index) => (reminder, index))
            .OrderBy(pair => pair.reminder.Time, StringComparer.Ordinal)
            .ToList();

        foreach (var (reminder, index) in ordered)
        {
            var expired = IsPast(reminder.Time);
            ReminderList.Items.Add(new ListBoxItem
            {
                Content = $"{(expired ? "✅" : "⏰")}  {reminder.Time}   {reminder.Content}" +
                          (expired ? "   （今天已过时间点）" : string.Empty),
                Tag = index,
            });
        }
    }

    private static bool IsPast(string time)
    {
        if (!TextParsers.IsValidTime(time)) return false;
        return string.CompareOrdinal(time, DateTime.Now.ToString("HH:mm")) < 0;
    }

    private async void OnAdd(object sender, RoutedEventArgs e)
    {
        var time = TextParsers.NormalizeTime(TimeBox.Text.Trim());
        var content = ContentBox.Text.Trim();

        if (!TextParsers.IsValidTime(time))
        {
            Status("时间格式不对，请填 14:30 这样的格式");
            return;
        }

        if (content.Length == 0)
        {
            Status("提醒内容不能为空");
            return;
        }

        Config.Reminders.Add(new Reminder { Time = time, Content = content });
        TimeBox.Clear();
        ContentBox.Clear();

        await Host.SaveAndApplyAsync();
        OnActivated();
        Status($"已添加日程：{time} {content}");
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (ReminderList.SelectedItem is not ListBoxItem { Tag: int index })
        {
            Status("请先在上面的列表里选中一条日程");
            return;
        }

        if (index < 0 || index >= Config.Reminders.Count) return;

        var removed = Config.Reminders[index];
        Config.Reminders.RemoveAt(index);

        await Host.SaveAndApplyAsync();
        OnActivated();
        Status($"已删除日程：{removed.Time} {removed.Content}");
    }

    private async void OnClearPast(object sender, RoutedEventArgs e)
    {
        var now = DateTime.Now.ToString("HH:mm");
        var kept = Config.Reminders.Where(r => string.CompareOrdinal(r.Time, now) >= 0).ToList();
        var removed = Config.Reminders.Count - kept.Count;

        if (removed == 0)
        {
            Status("没有已过时间点的日程");
            return;
        }

        var confirm = MessageBox.Show(
            $"将删除 {removed} 条已经过了时间点的日程，确定吗？",
            "清理过期日程",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        Config.Reminders = new List<Reminder>(kept);
        await Host.SaveAndApplyAsync();
        OnActivated();
        Status($"已清理 {removed} 条过期日程");
    }
}
