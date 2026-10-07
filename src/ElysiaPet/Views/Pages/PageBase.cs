using System.Windows.Controls;
using ElysiaPet.Services;

namespace ElysiaPet.Views.Pages;

/// <summary>
/// 管理台分页基类：统一持有宿主接口，避免每个页面各写一遍字段声明。
/// 注意：继承它的页面，XAML 根元素必须写成 &lt;pages:PageBase&gt;，
/// 因为 XAML 编译器会按根元素生成基类分部声明，写 UserControl 会导致基类冲突。
/// </summary>
public abstract class PageBase : UserControl, IRefreshablePage
{
    protected PageBase(IDashboardHost host)
    {
        Host = host;
    }

    protected IDashboardHost Host { get; }

    protected Models.AppConfig Config => Host.Config;

    /// <summary>页面被切换到前台时调用，子类重写以刷新数据。</summary>
    public virtual void OnActivated()
    {
    }

    protected void Status(string message) => Host.SetStatus(message);

    protected void Navigate(DashboardPage page) => Host.Navigate(page);
}
