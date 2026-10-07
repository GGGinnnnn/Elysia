using ElysiaPet.Services;

namespace ElysiaPet.Views.Pages;

/// <summary>功能说明页：纯静态内容，无逻辑。</summary>
public partial class HelpPage : PageBase
{
    public HelpPage(IDashboardHost host) : base(host)
    {
        InitializeComponent();
    }
}
