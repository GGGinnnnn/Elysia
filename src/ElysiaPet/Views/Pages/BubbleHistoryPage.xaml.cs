using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ElysiaPet.Services;

namespace ElysiaPet.Views.Pages;

/// <summary>自动冒泡记录页：与对话历史同理，但数据源是 bubble_history。</summary>
public partial class BubbleHistoryPage : PageBase
{
    private int _selectedIndex = -1;

    public BubbleHistoryPage(IDashboardHost host) : base(host)
    {
        InitializeComponent();
        OnActivated();
    }

    public override void OnActivated()
    {
        var bubbles = Config.BubbleHistory;
        CountText.Text = bubbles.Count.ToString();

        BubbleList.Items.Clear();
        for (var i = bubbles.Count - 1; i >= 0; i--)
        {
            var turn = bubbles[i];
            BubbleList.Items.Add(new ListBoxItem
            {
                Content = $"冒泡 {i + 1}   {turn.Time}\n{Truncate(turn.Pet, 24)}",
                Tag = i,
            });
        }

        UpdateDetail();
    }

    private void UpdateDetail()
    {
        if (_selectedIndex < 0 || _selectedIndex >= Config.BubbleHistory.Count)
        {
            DetailBox.Text = "这里记录桌宠主动说出口的话：待机唠嗑、AI 即兴冒泡、整点报时。\n\n" +
                             "它们不会进入对话上下文，所以不会「带偏」模型的记忆。";
            return;
        }

        var turn = Config.BubbleHistory[_selectedIndex];
        DetailBox.Text = $"【时间】{turn.Time}\n【来源】{turn.User}\n\n{turn.Pet}";
    }

    private void OnBubbleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BubbleList.SelectedItem is ListBoxItem { Tag: int index })
        {
            _selectedIndex = index;
            UpdateDetail();
        }
    }

    private void OnBubbleRightClick(object sender, MouseButtonEventArgs e)
    {
        if (BubbleList.SelectedItem is not ListBoxItem { Tag: int index }) return;

        var menu = new ContextMenu();
        var item = new MenuItem { Header = "❌ 删除此条冒泡记录" };
        item.Click += async (_, _) => await DeleteAsync(index);
        menu.Items.Add(item);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private async Task DeleteAsync(int index)
    {
        if (index < 0 || index >= Config.BubbleHistory.Count) return;

        Config.BubbleHistory.RemoveAt(index);
        _selectedIndex = -1;
        await Host.SaveAndApplyAsync();
        OnActivated();
        Status($"已删除第 {index + 1} 条冒泡记录");
    }

    private async void OnClearAll(object sender, RoutedEventArgs e)
    {
        if (Config.BubbleHistory.Count == 0)
        {
            Status("当前没有任何冒泡记录");
            return;
        }

        var confirm = MessageBox.Show(
            $"确定要清空全部 {Config.BubbleHistory.Count} 条冒泡记录吗？",
            "清空冒泡记录",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        Config.BubbleHistory.Clear();
        _selectedIndex = -1;
        await Host.SaveAndApplyAsync();
        OnActivated();
        Status("已清空全部冒泡记录");
    }

    private static string Truncate(string value, int max)
    {
        var single = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return single.Length <= max ? single : single[..max] + "…";
    }
}
