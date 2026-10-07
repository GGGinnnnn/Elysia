using System;
using System.Reflection;
using ElysiaPet.Services;

namespace ElysiaPet.Views.Pages;

/// <summary>关于页：显示版本与运行环境信息，方便排查问题时截图。</summary>
public partial class AboutPage : PageBase
{
    public AboutPage(IDashboardHost host) : base(host)
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.0.0";
        VersionText.Text = $"版本 {version}   ·   目标框架 net10.0-windows (WPF)";
        RuntimeText.Text = $".NET 运行时：{Environment.Version}    系统：{Environment.OSVersion.VersionString}";
        PathText.Text = $"程序目录：{AppPaths.ExecutableDirectory}";
    }
}
