namespace ElysiaPet.Controls;

/// <summary>
/// 全局图标表。
///
/// 刻意选用 **Unicode 符号 / 装饰字符区**（U+2000–U+2BFF）的字形：
/// 这类字是普通矢量字体，渲染出来是单色的，会跟随 Foreground 变色，
/// 因此能和粉色主题融为一体。反过来，U+1F300 以上的 emoji 在 Windows 上
/// 会走彩色字形，无论怎么设 Foreground 都是花花绿绿的，所以全部避开。
///
/// 每个符号都配了中文说明，保证用户仍能看懂选项含义（图标 + 文字并存）。
/// </summary>
internal static class Icons
{
    // ---- 侧栏导航 ----
    public const string Chat = "❝";          // 历史对话：引号，代表对话
    public const string Bubble = "❀";        // 自动冒泡：小花
    public const string Settings = "✿";      // 系统设置：花
    public const string Reminders = "◷";     // 提醒事项：时钟
    public const string Chime = "♪";         // 报时设置：音符
    public const string QuickApps = "➤";     // 快捷启动：箭头
    public const string Help = "❓";         // 功能说明：问号
    public const string About = "❤";         // 关于：爱心
    public const string Project = "⚘";       // 项目地址：花枝

    // ---- 动作 ----
    public const string Save = "✎";          // 保存
    public const string Revert = "↺";        // 撤回 / 放弃修改
    public const string Restore = "❋";       // 恢复默认
    public const string Folder = "▤";        // 打开目录
    public const string Add = "✚";           // 添加
    public const string Delete = "✖";        // 删除
    public const string Clear = "❃";         // 清空
    public const string Copy = "❏";          // 复制
    public const string Test = "✦";          // 测试 / 试听
    public const string Refresh = "↻";       // 刷新
    public const string Up = "▲";            // 上移
    public const string Down = "▼";          // 下移
    public const string Play = "▶";          // 立即执行
    public const string Validate = "✓";      // 校验
    public const string Reset = "◈";         // 重置位置
    public const string Exit = "✕";          // 退出
    public const string Book = "❖";          // 使用说明
    public const string Net = "❍";           // 网络 / 连接

    // ---- 其它 ----
    public const string Heart = "❤";
    public const string Star = "✦";
    public const string Sparkle = "✧";
    public const string Petal = "❀";
    public const string Bullet = "•";
}
