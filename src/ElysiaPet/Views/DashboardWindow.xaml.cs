using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ElysiaPet.Models;
using ElysiaPet.Services;
using ElysiaPet.Views.Pages;

namespace ElysiaPet.Views;

/// <summary>
/// 后台管理台主窗口。左侧导航 + 右侧分页容器，分页按需创建并缓存。
/// 旧版用一个 QStackedWidget 加 7 个页面并把索引硬编码在事件里，
/// 这里改成枚举 + 字典，新增页面不会再因为「索引顺延」出错。
/// </summary>
public partial class DashboardWindow : Window, IDashboardHost
{
    private readonly IPetHost _pet;
    private readonly Dictionary<DashboardPage, UserControl> _pages = new();
    private bool _forceClose;

    public DashboardWindow(IPetHost pet)
    {
        _pet = pet;
        InitializeComponent();

        AppLog.Info("管理台窗口已创建");
    }

    public AppConfig Config => _pet.Config;

    public IPetHost Pet => _pet;

    public void OpenPage(DashboardPage page)
    {
        var control = GetOrCreatePage(page);
        PageHost.Content = control;

        // 切页时刷新数据，保证显示的是最新状态（旧版靠各按钮的回调零散刷新）
        if (control is IRefreshablePage refreshable) refreshable.OnActivated();

        SyncNavSelection(page);
        SetStatus(DescribePage(page));
    }

    /// <summary>外部（桌宠/托盘）请求刷新某个分页的数据。</summary>
    public void RefreshPanel(DashboardPage page)
    {
        if (_pages.TryGetValue(page, out var control) && control is IRefreshablePage refreshable)
            refreshable.OnActivated();
    }

    /// <summary>把最新配置回填到设置页控件（设置被外部改动后调用）。</summary>
    public void ReloadFromConfig()
    {
        if (_pages.TryGetValue(DashboardPage.Settings, out var control) && control is IRefreshablePage refreshable)
            refreshable.OnActivated();
    }

    public void AppendLiveLog(string text)
    {
        if (_pages.TryGetValue(DashboardPage.ChatHistory, out var control) && control is ChatHistoryPage chat)
            chat.AppendLive(text);
    }

    public async Task SaveAndApplyAsync()
    {
        await _pet.SaveConfigAsync();

        if (_pet is PetWindow petWindow) petWindow.ApplyRuntimeSettings();

        // 保存后把其它页面刷成最新数据
        RefreshPanel(DashboardPage.ChatHistory);
        RefreshPanel(DashboardPage.BubbleHistory);
        RefreshPanel(DashboardPage.Reminders);
        RefreshPanel(DashboardPage.QuickApps);
        RefreshPanel(DashboardPage.Chime);

        _pet.ShowMessage("✨ 设置已同步！");
        SetStatus("设置已保存并生效 ✓");
    }

    public void SetStatus(string message) => StatusText.Text = message;

    /// <summary>通用日志入口：既进实时日志区，也更新底部状态栏。</summary>
    public void AppendLog(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        AppendLiveLog(text);
        SetStatus(text);
    }

    public void Navigate(DashboardPage page) => OpenPage(page);

    /// <summary>由桌宠 Dispose 时调用，绕过「关闭即隐藏」的拦截真正销毁窗口。</summary>
    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);

        // 关闭管理台不等于退出程序：只隐藏，托盘和桌宠继续运行
        if (_forceClose) return;

        e.Cancel = true;
        Hide();
        SetStatus("管理台已最小化到后台，桌宠仍在运行 ♪");
    }

    private UserControl GetOrCreatePage(DashboardPage page)
    {
        if (_pages.TryGetValue(page, out var cached)) return cached;

        UserControl created = page switch
        {
            DashboardPage.ChatHistory => new ChatHistoryPage(this),
            DashboardPage.BubbleHistory => new BubbleHistoryPage(this),
            DashboardPage.Settings => new SettingsPage(this),
            DashboardPage.Reminders => new RemindersPage(this),
            DashboardPage.Chime => new ChimePage(this),
            DashboardPage.QuickApps => new QuickAppsPage(this),
            DashboardPage.Help => new HelpPage(this),
            DashboardPage.About => new AboutPage(this),
            _ => new HelpPage(this),
        };

        _pages[page] = created;
        return created;
    }

    private void OnNavChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag }) return;
        if (!Enum.TryParse<DashboardPage>(tag, out var page)) return;
        if (PageHost is null) return; // XAML 尚未完成初始化

        OpenPage(page);
    }

    private void SyncNavSelection(DashboardPage page)
    {
        foreach (var radio in new[] { NavChat, NavBubble, NavSettings, NavReminders, NavChime, NavApps, NavHelp, NavAbout })
        {
            if (radio.Tag is string tag && Enum.TryParse<DashboardPage>(tag, out var value) && value == page)
            {
                radio.IsChecked = true;
                return;
            }
        }
    }

    private static string DescribePage(DashboardPage page) => page switch
    {
        DashboardPage.ChatHistory => "这里是完整的历史对话，右键单条记录可以删除。",
        DashboardPage.BubbleHistory => "自动冒泡（待机语录 / 整点报时）不会混进对话历史，单独记录在这里。",
        DashboardPage.Settings => "所有改动点「保存并应用」后立即生效。",
        DashboardPage.Reminders => "日程到点后，桌宠会冒泡并在屏幕中央弹出提醒。",
        DashboardPage.Chime => "每行一条，格式为 小时|语录，例如 09|九点整啦~",
        DashboardPage.QuickApps => "添加的程序会实时出现在桌宠右键菜单的最上方。",
        DashboardPage.Help => "有任何疑问可以先看这一页。",
        DashboardPage.About => "感谢你把爱莉希雅带回家 ♪",
        _ => string.Empty,
    };
}

/// <summary>分页被激活时需要刷新数据的约定。</summary>
public interface IRefreshablePage
{
    void OnActivated();
}
