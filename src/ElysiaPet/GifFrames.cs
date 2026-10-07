using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ElysiaPet.Services;

namespace ElysiaPet;

/// <summary>
/// GIF 逐帧导出诊断（命令行加 <c>--gifframes</c>）。
/// 把指定 GIF 的每一帧分别存成 PNG，用于肉眼确认：
/// 到底是 WPF 没播放动画，还是帧本身有问题、还是被摊成了一张精灵图。
/// </summary>
internal static class GifFrames
{
    public static void Dump(string outputDirectory, string state = "waiting")
    {
        Directory.CreateDirectory(outputDirectory);

        var path = AppPaths.Gif(state);
        AppLog.Info($"[帧导出] 素材: {path} 存在={File.Exists(path)}");

        // 用和程序完全相同的加载方式
        var stream = File.Exists(path)
            ? File.OpenRead(path)
            : System.Windows.Application.GetResourceStream(AppPaths.EmbeddedGifUri(state))?.Stream;

        if (stream is null)
        {
            AppLog.Error("[帧导出] 找不到素材");
            Application.Current.Shutdown(1);
            return;
        }

        using (stream)
        {
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            AppLog.Info($"[帧导出] 总帧数 = {decoder.Frames.Count}, " +
                        $"画布 = {decoder.Frames[0].PixelWidth}x{decoder.Frames[0].PixelHeight}");

            for (var i = 0; i < decoder.Frames.Count; i++)
            {
                var frame = decoder.Frames[i];
                var delay = ReadDelay(frame.Metadata as System.Windows.Media.Imaging.BitmapMetadata);
                var size = $"{frame.PixelWidth}x{frame.PixelHeight}";
                var format = frame.Format.ToString();

                // 原样导出
                SaveRaw(frame, Path.Combine(outputDirectory, $"raw-{i:00}.png"));

                // 用 WPF 渲染成 Bgra32：如果原帧带索引调色板 + 透明色，
                // 这一步会把透明色正确展开，也能看出「多帧摊在一张画布上」的现象
                var rendered = Render(frame);
                SaveRaw(rendered, Path.Combine(outputDirectory, $"frame-{i:00}.png"));

                var renderedBitmap = (BitmapSource)rendered;
                AppLog.Info($"[帧导出] 第 {i} 帧: 尺寸={size} 格式={format} 延时={delay?.ToString() ?? "无"} " +
                            $"渲染后={renderedBitmap.PixelWidth}x{renderedBitmap.PixelHeight}");
            }
        }

        AppLog.Info("[帧导出] 完成");
        AppLog.Shutdown();
        Application.Current.Shutdown(0);
    }

    private static void SaveRaw(System.Windows.Media.Imaging.BitmapSource source, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    /// <summary>把一帧画到目标尺寸的画布上，返回 Bgra32 结果（保留透明）。</summary>
    private static System.Windows.Media.Imaging.BitmapSource Render(
        System.Windows.Media.Imaging.BitmapSource frame,
        int? width = null,
        int? height = null,
        System.Windows.Media.Imaging.BitmapSource? backdrop = null)
    {
        int w = width ?? frame.PixelWidth;
        int h = height ?? frame.PixelHeight;

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            if (backdrop is not null)
                context.DrawImage(backdrop, new Rect(0, 0, w, h));

            context.DrawImage(frame, new Rect(0, 0, frame.PixelWidth, frame.PixelHeight));
        }

        var target = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    private static int? ReadDelay(System.Windows.Media.Imaging.BitmapMetadata? metadata)
    {
        if (metadata is null) return null;
        foreach (var query in new[] { "/grctlext/Delay", "/imgdesc/grctlext/Delay" })
        {
            try
            {
                if (metadata.ContainsQuery(query) && metadata.GetQuery(query) is ushort delay) return delay;
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
            {
                AppLog.Warn($"[帧导出] 读取延时失败: {ex.Message}");
            }
        }
        return null;
    }
}
