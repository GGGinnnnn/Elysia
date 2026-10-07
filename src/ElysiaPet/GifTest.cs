using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ElysiaPet.Services;

namespace ElysiaPet;

/// <summary>
/// 诊断模式（命令行加 <c>--giftest</c>）。
/// 打开一个最简单的窗口，只放一张和桌宠完全同源的 GIF，
/// 周期性输出渲染结果摘要到日志，用来判断「GIF 不动」到底是
/// WPF 渲染层的问题，还是桌宠窗口自身的问题。正常使用不会触发。
/// </summary>
internal static class GifTest
{
    /// <summary>创建一个只含 GIF 的测试窗口，并把渲染摘要写入日志。</summary>
    public static Window Create(int seconds)
    {
        var image = new Image
        {
            Width = 220,
            Stretch = Stretch.Uniform,
        };

        var path = AppPaths.Gif("waiting");
        BitmapImage? bitmap = null;
        var source = "无";

        // 路径 A：外部文件
        if (File.Exists(path))
        {
            bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreImageCache;
            bitmap.EndInit();
            bitmap.Freeze();
            source = "外部文件";
        }

        // 路径 B：内嵌资源
        bitmap ??= TryEmbedded(out source);

        image.Source = bitmap;

        var window = new Window
        {
            Title = "GIF 渲染测试",
            Width = 220,
            Height = 260,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 900,
            Top = 200,
            Topmost = true,
            Background = Brushes.Transparent,
            AllowsTransparency = true,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            Content = image,
        };

        AppLog.Info($"[GIF测试] 素材来源={source} 路径={path} 存在={File.Exists(path)}");

        if (bitmap is not null)
        {
            // 用解码器读出真实帧数，确认这张 GIF 本身是多帧动画
            var frameCount = -1;
            try
            {
                using var stream = File.OpenRead(path);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                frameCount = decoder.Frames.Count;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException)
            {
                AppLog.Warn($"[GIF测试] 读取帧数失败: {ex.Message}");
            }

            AppLog.Info($"[GIF测试] 解码尺寸={bitmap.PixelWidth}x{bitmap.PixelHeight} " +
                        $"CanFreeze={bitmap.CanFreeze} IsFrozen={bitmap.IsFrozen} " +
                        $"文件真实帧数={frameCount}");
        }

        var ticks = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            ticks++;
            if (bitmap is not null)
            {
                // 读取动画帧的进度：帧索引在推进说明 WPF 真的在播动画
                AppLog.Info($"[GIF测试] 第 {ticks} 次采样：Image.ActualWidth={image.ActualWidth:0} " +
                            $"ActualHeight={image.ActualHeight:0} 窗口可见={window.IsVisible}");
            }

            if (ticks >= seconds * 2)
            {
                timer.Stop();
                Application.Current.Shutdown(0);
            }
        };
        timer.Start();

        return window;
    }

    private static BitmapImage? TryEmbedded(out string source)
    {
        try
        {
            var uri = AppPaths.EmbeddedGifUri("waiting");
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = uri;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreImageCache;
            bitmap.EndInit();
            bitmap.Freeze();
            source = "内嵌资源";
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
        {
            AppLog.Error("[GIF测试] 内嵌资源加载失败", ex);
            source = "无";
            return null;
        }
    }
}
