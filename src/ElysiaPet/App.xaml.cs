using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ElysiaPet.Services;
using ElysiaPet.Views;

namespace ElysiaPet;

/// <summary>
/// 应用入口。旧版把一切都塞在 <c>if __name__ == '__main__'</c> 里，
/// 这里明确划分：全局异常兜底 -> 装配服务 -> 打开桌宠窗口 -> 启动后台调度。
/// </summary>
public partial class App : Application
{
    private ConfigService _configService = null!;
    private AiClient _aiClient = null!;
    private PetWindow? _pet;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 任何未捕获异常都写进日志并友好提示，绝不让程序静默消失（旧版打包后出问题完全无迹可查）
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Error("未处理的致命异常", args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString()));
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Error("未观察的任务异常", args.Exception);
            args.SetObserved();
        };

        AppLog.Info("======== 爱莉希雅桌宠启动 ========");
        AppLog.Info($"程序目录: {AppPaths.ExecutableDirectory}");
        AppLog.Info($"素材目录: {AppPaths.AssetDirectory}");

        _configService = new ConfigService();
        await _configService.LoadAsync();
        AppLog.Info($"配置载入完成: {_configService.FilePath}");

        _aiClient = new AiClient();

        // 自检模式：跑完检查直接退出，不进入正常交互
        if (e.Args.Any(arg => string.Equals(arg, "--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            AppLog.Info("进入自检模式 (--selftest)");
            var exitCode = await SelfTest.RunAsync(_configService, _aiClient);
            _aiClient.Dispose();
            AppLog.Shutdown();
            Shutdown(exitCode);
            return;
        }

        // GIF 渲染诊断模式：只放一张动图，用于判断动画是否真的在播
        if (e.Args.Any(arg => string.Equals(arg, "--giftest", StringComparison.OrdinalIgnoreCase)))
        {
            AppLog.Info("进入 GIF 渲染诊断模式 (--giftest)");
            var window = GifTest.Create(seconds: 6);
            window.Show();
            return;
        }

        // 界面截图诊断模式：把管理台与桌宠渲染成 PNG，便于人工确认配色与排版
        if (e.Args.Any(arg => string.Equals(arg, "--shot", StringComparison.OrdinalIgnoreCase)))
        {
            AppLog.Info("进入界面截图模式 (--shot)");
            UiShot.Capture(_configService, _aiClient, Path.Combine(AppPaths.ExecutableDirectory, "shots"));
            return;
        }

        // GIF 逐帧导出诊断：把每一帧单独存成 PNG，确认动画到底有没有在播
        if (e.Args.Any(arg => string.Equals(arg, "--gifframes", StringComparison.OrdinalIgnoreCase)))
        {
            AppLog.Info("进入 GIF 逐帧导出模式 (--gifframes)");
            GifFrames.Dump(Path.Combine(AppPaths.ExecutableDirectory, "shots"));
            return;
        }

        // 图标字形诊断：挑出单色可用的图标，避免彩色 emoji 破坏粉色主题
        if (e.Args.Any(arg => string.Equals(arg, "--icontest", StringComparison.OrdinalIgnoreCase)))
        {
            IconTest.Run();
            return;
        }

        _pet = new PetWindow(_configService, _aiClient);
        _pet.Show();

        // 首次运行（或配置被重建）时把默认配置落盘，方便用户直接编辑
        if (!System.IO.File.Exists(_configService.FilePath))
            await _configService.SaveAsync();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("界面线程未处理异常", e.Exception);
        e.Handled = true;

        try
        {
            MessageBox.Show(
                "妖精小姐遇到了一个小问题，已经记录到 elysia.log 里啦：\n\n" + e.Exception.Message,
                "爱莉希雅桌宠",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            AppLog.Error("展示错误提示时又出错了", ex);
        }
    }

    /// <summary>由桌宠窗口调用的统一退出路径：彻底释放托盘、AI 连接和日志。</summary>
    public void ShutdownApplication()
    {
        AppLog.Info("======== 爱莉希雅桌宠退出 ========");
        // Dispose 内部会先卸载托盘图标（否则任务栏会残留一个点不掉的僵尸图标）
        _pet?.Dispose();
        _pet = null;
        _aiClient?.Dispose();
        AppLog.Shutdown();
        Shutdown();
    }
}
