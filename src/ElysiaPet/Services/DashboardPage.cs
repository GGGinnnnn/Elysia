namespace ElysiaPet.Services;

/// <summary>后台管理台的分页标识。替代旧版到处传的魔法数字索引。</summary>
public enum DashboardPage
{
    ChatHistory = 0,
    BubbleHistory,
    Settings,
    Help,
    Reminders,
    Chime,
    QuickApps,
    About,
}
