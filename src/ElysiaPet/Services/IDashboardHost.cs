using System;
using System.Threading.Tasks;
using ElysiaPet.Models;

namespace ElysiaPet.Services;

/// <summary>
/// 管理台分页需要的全部能力。分页只依赖这个接口，不直接摸桌宠或窗口对象，
/// 所以每个页面都能单独拿出来实例化/测试。
/// </summary>
public interface IDashboardHost
{
    AppConfig Config { get; }

    IPetHost Pet { get; }

    /// <summary>把配置写回磁盘并让桌宠立即生效。</summary>
    Task SaveAndApplyAsync();

    /// <summary>在底部状态栏提示一句话。</summary>
    void SetStatus(string message);

    /// <summary>往实时日志面板追加一行。</summary>
    void AppendLog(string text);

    /// <summary>切换分页（用于页面内的跳转按钮）。</summary>
    void Navigate(DashboardPage page);
}
