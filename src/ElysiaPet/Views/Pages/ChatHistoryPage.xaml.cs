using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ElysiaPet.Models;
using ElysiaPet.Services;

namespace ElysiaPet.Views.Pages;

/// <summary>
/// 历史对话页：完整对话列表 + 详情 + 实时日志 + 删除/清空/复制。
/// </summary>
public partial class ChatHistoryPage : PageBase
{
    private int _selectedIndex = -1;

    public ChatHistoryPage(IDashboardHost host) : base(host)
    {
        InitializeComponent();
        OnActivated();
    }

    public override void OnActivated()
    {
        var history = Config.History;
        CountText.Text = history.Count.ToString();

        // 最新的一轮排在最上面，符合查看习惯
        HistoryList.Items.Clear();
        for (var i = history.Count - 1; i >= 0; i--)
        {
            var turn = history[i];
            var preview = Truncate(turn.User, 22);
            HistoryList.Items.Add(new ListBoxItem
            {
                Content = $"对谈 {i + 1}   {turn.Time}\n{preview}",
                Tag = i,
            });
        }

        if (HistoryList.Items.Count > 0 && _selectedIndex < 0)
            HistoryList.SelectedIndex = 0;

        UpdateDetail();
    }

    /// <summary>桌宠实时追加的日志（旧版的 chat_display）。</summary>
    public void AppendLive(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        // 详情区处于「未选择具体记录」状态时展示实时流，选中记录后展示记录内容
        if (_selectedIndex < 0)
        {
            DetailBox.AppendText(text + Environment.NewLine);
            DetailBox.ScrollToEnd();
        }
    }

    private void UpdateDetail()
    {
        if (_selectedIndex < 0 || _selectedIndex >= Config.History.Count)
        {
            DetailBox.Text = "◀ 从左侧选择一条对话查看完整内容。\n\n" +
                             "也可以直接在桌宠上和她聊天，这里会实时滚动显示。";
            return;
        }

        var turn = Config.History[_selectedIndex];
        DetailBox.Text = $"【时间】{turn.Time}\n\n【你】\n{turn.User}\n\n【爱莉希雅】\n{turn.Pet}";
    }

    private void OnHistorySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryList.SelectedItem is ListBoxItem { Tag: int index })
        {
            _selectedIndex = index;
            UpdateDetail();
        }
    }

    private void OnHistoryRightClick(object sender, MouseButtonEventArgs e)
    {
        if (HistoryList.SelectedItem is not ListBoxItem { Tag: int index }) return;

        var menu = new ContextMenu();
        var item = new MenuItem { Header = "✖ 删除此条对话记录" };
        item.Click += async (_, _) => await DeleteAsync(index);
        menu.Items.Add(item);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private async Task DeleteAsync(int index)
    {
        if (index < 0 || index >= Config.History.Count) return;

        Config.History.RemoveAt(index);
        _selectedIndex = -1;
        await Host.SaveAndApplyAsync();
        OnActivated();
        Status($"已删除第 {index + 1} 条对话记录");
    }

    private async void OnClearAll(object sender, RoutedEventArgs e)
    {
        if (Config.History.Count == 0)
        {
            Status("当前没有任何对话记录");
            return;
        }

        var confirm = MessageBox.Show(
            $"确定要清空全部 {Config.History.Count} 条对话记录吗？此操作不可撤销。",
            "清空历史对话",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        Config.History.Clear();
        _selectedIndex = -1;
        await Host.SaveAndApplyAsync();
        OnActivated();
        Status("已清空全部对话记录");
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (_selectedIndex < 0 || _selectedIndex >= Config.History.Count)
        {
            Status("先选一条对话再复制哦");
            return;
        }

        var turn = Config.History[_selectedIndex];
        var builder = new StringBuilder()
            .AppendLine($"【时间】{turn.Time}")
            .AppendLine()
            .AppendLine($"【你】{turn.User}")
            .AppendLine()
            .AppendLine($"【爱莉希雅】{turn.Pet}");

        Clipboard.SetText(builder.ToString());
        Status("已复制到剪贴板 ♪");
    }

    private static string Truncate(string value, int max)
    {
        var single = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return single.Length <= max ? single : single[..max] + "…";
    }
}
