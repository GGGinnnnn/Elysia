using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ElysiaPet.Services;
using ElysiaPet.Views;

namespace ElysiaPet;

/// <summary>
/// 界面截图诊断模式（命令行加 <c>--shot</c>）。
/// 把管理台真正渲染出来并保存成 PNG，用于人工确认配色与排版，
/// 免得每次都靠"猜"用户看到的界面长什么样。正常使用不会触发。
/// </summary>
internal static class UiShot
{
    public static void Capture(ConfigService configService, AiClient aiClient, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        var pet = new PetWindow(configService, aiClient);
        pet.Show();

        var dashboard = new DashboardWindow(pet);

        // 等窗口完成布局，再逐页截图
        var windows = new Window[] { dashboard, pet };
        var names = new[] { "dashboard", "pet" };

        var pages = new[]
        {
            DashboardPage.Settings,
            DashboardPage.ChatHistory,
            DashboardPage.Chime,
            DashboardPage.Reminders,
            DashboardPage.QuickApps,
            DashboardPage.Help,
            DashboardPage.About,
            DashboardPage.BubbleHistory,
        };

        var index = 0;
        var pageIndex = 0;
        var petStatesDone = 0;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        timer.Tick += (_, _) =>
        {
            try
            {
                if (index < windows.Length)
                {
                    Save(windows[index], Path.Combine(outputDirectory, names[index] + ".png"));
                    index++;
                    return;
                }

                // 桌宠的三种状态：待机 / 气泡 / 输入框，用于人工核对样式
                if (petStatesDone < 3)
                {
                    switch (petStatesDone)
                    {
                        case 0:
                            Save(pet, Path.Combine(outputDirectory, "pet-1-idle.png"));
                            break;
                        case 1:
                            pet.ShowMessage("今天的你也超好看呢，要不要一起喝杯茶呀？");
                            // 截图模式不进消息循环，打字机的延时回调不会跑，这里直接把全文亮出来
                            pet.CompleteTypewriter();
                            pet.SetBubbleOpacityImmediately(1.0);
                            Save(pet, Path.Combine(outputDirectory, "pet-2-bubble.png"));
                            break;
                        case 2:
                            pet.ShowInputBox();
                            Save(pet, Path.Combine(outputDirectory, "pet-3-input.png"));
                            break;
                    }

                    AppLog.Info($"[布局] 状态{petStatesDone}: {pet.DescribeLayout()}");
                    petStatesDone++;
                    return;
                }

                // 之后每一拍换一个分页并截图
                if (pageIndex >= pages.Length)
                {
                    timer.Stop();
                    dashboard.ForceClose();
                    pet.Dispose();
                    AppLog.Info("[截图] 全部完成");
                    AppLog.Shutdown();
                    Application.Current.Shutdown(0);
                    return;
                }

                var page = pages[pageIndex];
                dashboard.OpenPage(page);
                Save(dashboard, Path.Combine(outputDirectory, $"page-{pageIndex}-{page}.png"));
                pageIndex++;
            }
            catch (Exception ex)
            {
                AppLog.Error("[截图] 失败", ex);
                timer.Stop();
                Application.Current.Shutdown(1);
            }
        };

        dashboard.Show();
        dashboard.OpenPage(DashboardPage.Settings);
        timer.Start();
    }

    private static void Save(Window window, string path)
    {
        if (window is PetWindow pw) AppLog.Info($"[渲染前] {pw.DescribeLayout()}");

        var width = (int)Math.Ceiling(window.ActualWidth > 0 ? window.ActualWidth : window.Width);
        var height = (int)Math.Ceiling(window.ActualHeight > 0 ? window.ActualHeight : window.Height);
        if (width <= 0 || height <= 0)
        {
            AppLog.Warn($"[截图] 窗口尺寸异常，跳过 {path}");
            return;
        }

        // 让 WPF 把当前布局真正画出来，再由 RenderTargetBitmap 取像素
        window.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        try
        {
            bitmap.Render(window);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            AppLog.Warn($"[截图] 渲染窗口失败: {ex.Message}");
            return;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(path);
        encoder.Save(stream);

        var info = new FileInfo(path);
        AppLog.Info($"[截图] 已保存 {Path.GetFileName(path)} ({width}x{height}, {info.Length / 1024} KB)");
    }
}
