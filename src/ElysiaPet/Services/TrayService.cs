using System;
using System.Drawing;
using DrawingFont = System.Drawing.Font;
using DrawingFontFamily = System.Drawing.FontFamily;
using DrawingFontStyle = System.Drawing.FontStyle;
using System.Windows;
using System.Windows.Forms;
using ElysiaPet.Views;

namespace ElysiaPet.Services;

/// <summary>托盘菜单命令的宿主，由桌宠主窗口实现，避免托盘直接依赖窗口内部字段。</summary>
public interface ITrayHost
{
    void OpenPanel(DashboardPage page);

    void ResetPosition();

    void Shutdown();
}

/// <summary>
/// 系统托盘图标。WPF 没有内置 NotifyIcon，所以复用 WinForms 的稳定实现；
/// 生命周期严格跟随配置开关，反复开关不会残留僵尸图标。
/// </summary>
public sealed class TrayService : IDisposable
{
    private NotifyIcon? _icon;
    private readonly ITrayHost _host;

    public TrayService(ITrayHost host) => _host = host;

    public bool IsVisible => _icon?.Visible == true;

    /// <summary>按配置开关托盘图标。</summary>
    public void SetEnabled(bool enabled)
    {
        if (enabled) Show();
        else Hide();
    }

    private void Show()
    {
        if (_icon is not null) return;

        var icon = LoadIcon();
        if (icon is null)
        {
            AppLog.Warn("托盘图标素材缺失，已跳过托盘初始化");
            return;
        }

        var menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            Renderer = new ElysiaPet.Controls.PinkMenuRenderer(),
            BackColor = Color.FromArgb(0xFF, 0xFB, 0xFD),
            Font = new DrawingFont(new DrawingFontFamily("幼圆"), 10.5f, DrawingFontStyle.Regular, GraphicsUnit.Point),
            Padding = new Padding(2, 6, 2, 6),
        };

        var reset = new ToolStripMenuItem("↻ 重置位置与强制置顶");
        reset.Click += (_, _) => OnUi(() => _host.ResetPosition());
        menu.Items.Add(reset);
        menu.Items.Add(new ToolStripSeparator());

        AddPanel(menu, "❝ 历史对话面板", DashboardPage.ChatHistory);
        AddPanel(menu, "❀ 自动冒泡记录", DashboardPage.BubbleHistory);
        AddPanel(menu, "⚙ 系统设置", DashboardPage.Settings);
        AddPanel(menu, "⏰ 提醒事项", DashboardPage.Reminders);
        AddPanel(menu, "♪ 报时设置", DashboardPage.Chime);
        AddPanel(menu, "➤ 快捷启动", DashboardPage.QuickApps);

        menu.Items.Add(new ToolStripSeparator());
        var exit = new ToolStripMenuItem("✖ 退出应用");
        exit.Click += (_, _) => OnUi(() => _host.Shutdown());
        menu.Items.Add(exit);

        _icon = new NotifyIcon
        {
            Icon = icon,
            Text = "爱莉希雅桌宠 ♪",
            ContextMenuStrip = menu,
            Visible = true,
        };

        _icon.DoubleClick += (_, _) => OnUi(() => _host.OpenPanel(DashboardPage.Settings));
    }

    private void AddPanel(ContextMenuStrip menu, string text, DashboardPage page)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => OnUi(() => _host.OpenPanel(page));
        menu.Items.Add(item);
    }

    private static void OnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    private static Icon? LoadIcon()
    {
        try
        {
            // 1) 优先用 exe 同目录的外部图标文件
            var path = AppPaths.Icon;
            if (System.IO.File.Exists(path)) return new Icon(path);

            // 2) 回退到内嵌图标资源（单文件 exe 且旁边没有 Assets 目录时走这条）
            var embedded = TryLoadEmbeddedIcon();
            if (embedded is not null) return embedded;

            // 3) 兜底：直接抽取可执行文件自身的图标，保证托盘永远不会是空白
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var extracted = Icon.ExtractAssociatedIcon(exe);
                if (extracted is not null) return extracted;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException)
        {
            AppLog.Warn($"加载托盘图标失败: {ex.Message}");
        }

        return SystemIcons.Application;
    }

    private static Icon? TryLoadEmbeddedIcon()
    {
        try
        {
            var stream = System.Windows.Application.GetResourceStream(AppPaths.EmbeddedIconUri)?.Stream;
            if (stream is null) return null;

            using (stream)
            {
                return new Icon(stream);
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or ArgumentException or UriFormatException)
        {
            AppLog.Warn($"读取内嵌图标失败: {ex.Message}");
            return null;
        }
    }

    private void Hide()
    {
        if (_icon is null) return;
        _icon.Visible = false;
        _icon.Dispose();
        _icon = null;
    }

    public void Dispose() => Hide();
}
