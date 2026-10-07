using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ElysiaPet.Models;
using ElysiaPet.Services;
using Microsoft.Win32;

namespace ElysiaPet.Views.Pages;

/// <summary>快捷启动管理页：增删改序，并可直接测试启动。</summary>
public partial class QuickAppsPage : PageBase
{
    public QuickAppsPage(IDashboardHost host) : base(host)
    {
        InitializeComponent();
        OnActivated();
    }

    public override void OnActivated()
    {
        AppList.Items.Clear();

        if (Config.QuickApps.Count == 0)
        {
            AppList.Items.Add(new ListBoxItem
            {
                Content = "还没有添加快捷启动项。填写名称后点「添加程序」选择 exe 即可。",
                IsEnabled = false,
            });
            return;
        }

        for (var i = 0; i < Config.QuickApps.Count; i++)
        {
            var app = Config.QuickApps[i];
            var exists = File.Exists(app.Path);
            AppList.Items.Add(new ListBoxItem
            {
                Content = $"{(exists ? "📂" : "⚠️ 路径已失效")} {app.Name}   ➔   {app.Path}",
                Tag = i,
                ToolTip = exists ? "双击可立即启动" : "文件已不存在，请删除或重新添加",
            });
        }
    }

    private async void OnAdd(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            Status("先填写一个显示在右键菜单里的名称");
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "选择要绑定到右键菜单的程序",
            Filter = "应用程序 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true) return;

        Config.QuickApps.Add(new QuickApp { Name = name, Path = dialog.FileName });
        NameBox.Clear();

        await Host.SaveAndApplyAsync();
        OnActivated();
        Status($"已添加：{name}");
    }

    private void OnTest(object sender, RoutedEventArgs e)
    {
        if (AppList.SelectedItem is not ListBoxItem { Tag: int index })
        {
            Status("请先选中一条要测试的启动项");
            return;
        }

        var app = Config.QuickApps[index];
        if (ProcessLauncher.TryLaunchApp(app.Path, out var error))
            Status($"已启动：{app.Name}");
        else
            Status($"启动失败：{error}");
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (AppList.SelectedItem is not ListBoxItem { Tag: int index })
        {
            Status("请先选中一条要删除的启动项");
            return;
        }

        var removed = Config.QuickApps[index];
        Config.QuickApps.RemoveAt(index);

        await Host.SaveAndApplyAsync();
        OnActivated();
        Status($"已删除：{removed.Name}");
    }

    private async void OnMoveUp(object sender, RoutedEventArgs e) => await MoveAsync(-1);

    private async void OnMoveDown(object sender, RoutedEventArgs e) => await MoveAsync(1);

    private async System.Threading.Tasks.Task MoveAsync(int offset)
    {
        if (AppList.SelectedItem is not ListBoxItem { Tag: int index }) return;

        var target = index + offset;
        if (target < 0 || target >= Config.QuickApps.Count)
        {
            Status("已经到头啦");
            return;
        }

        (Config.QuickApps[index], Config.QuickApps[target]) = (Config.QuickApps[target], Config.QuickApps[index]);

        await Host.SaveAndApplyAsync();
        OnActivated();
        AppList.SelectedIndex = target;
        Status("顺序已调整，右键菜单同步更新");
    }

    /// <summary>双击列表项直接启动，属于新增的顺手操作。</summary>
    private void OnListDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => OnTest(sender, new RoutedEventArgs());
}
