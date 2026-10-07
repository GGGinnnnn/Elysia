using System;
using System.Threading.Tasks;
using ElysiaPet.Models;

namespace ElysiaPet.Services;

/// <summary>
/// 桌宠对外暴露的能力，供托盘与管理台调用。
/// 有了这层接口，界面代码不再直接翻桌宠对象的内部字段（旧版 MainDashboard 就是这么干的），
/// 谁调谁改一目了然，也方便单独测试。
/// </summary>
public interface IPetHost
{
    AppConfig Config { get; }

    /// <summary>保存配置（写盘失败会记日志，不抛异常到 UI）。</summary>
    Task SaveConfigAsync();

    /// <summary>弹出气泡说一句话。</summary>
    void ShowMessage(string text);

    /// <summary>切换表情动画。</summary>
    void ChangeState(string state);

    /// <summary>在桌宠上发一条消息（走完整链路：斜杠指令 / 日程识别 / AI 对话）。</summary>
    void SubmitInput(string text);

    /// <summary>往管理台的实时日志区追加一行（旧版的 chat_display）。</summary>
    void AppendLiveLog(string text);

    /// <summary>刷新管理台某个分页的数据。</summary>
    void RefreshPanel(DashboardPage page);

    /// <summary>让管理台把当前配置写进各控件（设置页重新显示前调用）。</summary>
    void ReloadSettingsIntoDashboard();
}
