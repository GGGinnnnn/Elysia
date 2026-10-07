using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ElysiaPet.Services;

/// <summary>
/// GIF 逐帧播放器。
///
/// 为什么不直接把 GIF 交给 WPF 的 <c>BitmapImage</c>：
/// 把 BitmapImage 绑到 Image.Source 时，它是否真的推进动画并不可靠
/// （本项目实测就是只显示其中一帧，桌宠一直"不动"）。
/// 这里改为自己解码所有帧 + 用定时器按每帧延时逐帧切换，
/// 行为完全可控，也能在自检里验证「帧确实换过」。
/// </summary>
public sealed class GifAnimator : IDisposable
{
    /// <summary>轮询间隔。取得比最小帧延时（80ms）小，保证不会漏掉帧。</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(30);

    private readonly DispatcherTimer _timer;
    private readonly Dispatcher _dispatcher;
    private readonly Action<ImageSource> _present;
    private readonly string _name;

    private IReadOnlyList<BitmapSource> _frames = Array.Empty<BitmapSource>();
    private int[] _frameDurationsMs = Array.Empty<int>();
    private int _cursor;
    private int _elapsedMs;

    public GifAnimator(Action<ImageSource> present, string name)
    {
        _present = present;
        _name = name;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _timer = new DispatcherTimer(DispatcherPriority.Render, _dispatcher) { Interval = PollInterval };
        _timer.Tick += OnTick;
    }

    public bool IsLoaded => _frames.Count > 0;

    public int FrameCount => _frames.Count;

    /// <summary>从文件加载；文件不存在或解码失败返回 false（调用方可以再试内嵌资源）。</summary>
    public bool TryLoadFile(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            using var stream = File.OpenRead(path);
            return Load(stream);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
        {
            AppLog.Error($"[动画] 读取素材失败: {path}", ex);
            return false;
        }
    }

    /// <summary>从程序集内嵌资源加载。</summary>
    public bool TryLoadResource(Uri uri)
    {
        try
        {
            var stream = System.Windows.Application.GetResourceStream(uri)?.Stream;
            if (stream is null) return false;

            using (stream)
            {
                return Load(stream);
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UriFormatException)
        {
            AppLog.Error($"[动画] 读取内嵌素材失败: {uri}", ex);
            return false;
        }
    }

    private bool Load(Stream stream)
    {
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        if (decoder.Frames.Count == 0) return false;

        var canvasWidth = decoder.Frames[0].PixelWidth;
        var canvasHeight = decoder.Frames[0].PixelHeight;
        if (canvasWidth <= 0 || canvasHeight <= 0) return false;

        // 判断是否需要自己合成：只要有任意一帧不是整张画布大小，
        // 就说明这组帧是"局部更新 + 依赖前一帧"的形式，必须逐帧叠加出完整画面。
        var needsComposition = false;
        for (var i = 0; i < decoder.Frames.Count; i++)
        {
            if (decoder.Frames[i].PixelWidth != canvasWidth || decoder.Frames[i].PixelHeight != canvasHeight)
            {
                needsComposition = true;
                break;
            }
        }

        var frames = new List<BitmapSource>(decoder.Frames.Count);
        var durations = new int[decoder.Frames.Count];

        BitmapSource? previous = null;
        for (var i = 0; i < decoder.Frames.Count; i++)
        {
            var raw = decoder.Frames[i];
            BitmapSource composed;

            if (!needsComposition)
            {
                // 每帧都是完整画面，直接转成带透明通道的格式即可
                composed = ToBgra32(raw);
            }
            else
            {
                // 把当前帧叠到上一帧画面上，得到完整的一格
                composed = Compose(previous, ToBgra32(raw), canvasWidth, canvasHeight);
            }

            composed.Freeze();
            frames.Add(composed);
            previous = composed;

            durations[i] = ReadDurationMs(raw.Metadata as BitmapMetadata);
        }

        _frames = frames;
        _frameDurationsMs = durations;
        _cursor = 0;
        _elapsedMs = 0;

        AppLog.Info($"[动画] {_name} 已载入 {frames.Count} 帧, 画布 {canvasWidth}x{canvasHeight}, " +
                    $"需合成={needsComposition}, 总时长={TotalDurationMs()}ms");

        return true;
    }

    private int TotalDurationMs()
    {
        var total = 0;
        foreach (var d in _frameDurationsMs) total += d;
        return total;
    }

    /// <summary>开始播放（立即显示第一帧）。</summary>
    public void Start()
    {
        if (_frames.Count == 0) return;

        _cursor = 0;
        _elapsedMs = 0;
        ShowCurrent();

        // 单帧素材不需要定时器
        if (_frames.Count > 1 && !_timer.IsEnabled) _timer.Start();
    }

    public void Stop()
    {
        if (_timer.IsEnabled) _timer.Stop();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_frames.Count <= 1) return;

        _elapsedMs += (int)PollInterval.TotalMilliseconds;

        var guard = 0;
        while (_elapsedMs >= _frameDurationsMs[_cursor] && guard++ < 1000)
        {
            _elapsedMs -= _frameDurationsMs[_cursor];
            _cursor = (_cursor + 1) % _frames.Count;
        }

        ShowCurrent();
    }

    private void ShowCurrent() => _present(_frames[_cursor]);

    /// <summary>读取 GIF 帧延时（元数据单位是 1/100 秒）。</summary>
    private static int ReadDurationMs(BitmapMetadata? metadata)
    {
        const int fallback = 100;

        if (metadata is null) return fallback;

        try
        {
            foreach (var query in new[] { "/grctlext/Delay", "/imgdesc/grctlext/Delay" })
            {
                if (!metadata.ContainsQuery(query)) continue;
                if (metadata.GetQuery(query) is not ushort delay) continue;

                // 0 或异常值会让动画"卡住不动"，统一按 100ms 处理
                if (delay == 0) return fallback;
                return delay * 10;
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
        {
            AppLog.Warn($"读取帧延时失败: {ex.Message}");
        }

        return fallback;
    }

    /// <summary>统一转成 Pbgra32：索引调色板 + 单一透明色的 GIF 帧必须转换后才能正确参与合成。</summary>
    private static BitmapSource ToBgra32(BitmapSource source)
    {
        if (source.Format == PixelFormats.Pbgra32 || source.Format == PixelFormats.Bgra32) return source;

        var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    /// <summary>把当前帧画到上一帧之上，得到「完整的一格」画面。</summary>
    private static BitmapSource Compose(BitmapSource? backdrop, BitmapSource frame, int width, int height)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            if (backdrop is not null) context.DrawImage(backdrop, new System.Windows.Rect(0, 0, width, height));
            context.DrawImage(frame, new System.Windows.Rect(0, 0, frame.PixelWidth, frame.PixelHeight));
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        return target;
    }

    public void Dispose()
    {
        if (_timer.IsEnabled) _timer.Stop();
        _timer.Tick -= OnTick;
        _frames = Array.Empty<BitmapSource>();
    }
}
