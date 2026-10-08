using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ElysiaPet.Models;
using ElysiaPet.Services;

namespace ElysiaPet.Views;

/// <summary>
/// 桌宠主窗口：无边框透明窗口 + GIF 表情 + 打字机气泡 + 快捷输入框 + 秒级时间调度。
/// 旧版把这些全塞在一个 1600 行的类里，这里按职责切分成若干区域方法：
/// 生命周期 / 位置尺寸 / 气泡动画 / 输入处理 / 时间调度 / 鼠标交互。
/// </summary>
public partial class PetWindow : Window, IPetHost, ITrayHost
{
    private const double BaseWidth = 220;
    private const double MinPetWidth = 80;
    private const double MaxPetWidth = 800;
    private const int TypewriterDelayMs = 500;
    private const int TypewriterIntervalMs = 45;
    private const int InputIdleHideMs = 10000;

    /// <summary>气泡打完后停留多久再淡出（毫秒）。</summary>
    private int _bubbleHoldMs = 4500;

    private readonly ConfigService _configService;
    private readonly AiClient _aiClient;
    private readonly Dictionary<string, GifAnimator> _animators = new(StringComparer.OrdinalIgnoreCase);
    private string? _currentState;
    private int _framePresentCount;
    private readonly Random _random = new();

    private TrayService? _tray;
    private DashboardWindow? _dashboard;

    // --- 气泡状态 ---
    private readonly DispatcherTimer _typewriterTimer = new() { Interval = TimeSpan.FromMilliseconds(TypewriterIntervalMs) };
    private readonly DispatcherTimer _bubbleHideTimer = new() { Interval = TimeSpan.FromMilliseconds(4500) };
    private DispatcherTimer? _inputHideTimer;
    private string _bubbleFullText = string.Empty;
    private int _bubbleCursor;
    private bool _isTyping;
    private bool _bubbleVisible;

    // --- 时间调度状态 ---
    private readonly DispatcherTimer _tickTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _idleSeconds;
    private int _inactiveSeconds;
    private int _lastChimedHour = -1;
    private DateTime _lastReminderKey = DateTime.MinValue;

    // --- 交互状态 ---
    private bool _isDragging;
    private bool _isResizing;
    private bool _dragMoved;

    /// <summary>右键拖拽是否真的改变过尺寸；用它区分「右键单击」和「右键缩放」。</summary>
    private bool _resizeMoved;

    /// <summary>按下时光标相对窗口左上角的偏移（DIP）。拖动时用它做绝对定位，避免误差累积。</summary>
    private Vector _grabOffsetInWindow;

    /// <summary>缩放基准：按下时的鼠标横坐标与当时的桌宠宽度。</summary>
    private double _resizeAnchorPointerX;
    private double _resizeAnchorWidth;
    private double _currentPetWidth;
    private bool _suppressInputAutoHide;
    private CancellationTokenSource? _aiCts;
    private bool _disposed;

    /// <summary>首次落地是否已完成。用于区分「启动时的可见性兜底」和「平时的重排」。</summary>
    private bool _placementInitialized;

    /// <summary>输入框当前是否被放到了桌宠上方（下方没空间时才这样）。</summary>
    private bool _inputBoxAbove;

    /// <summary>
    /// 当前气泡槽位高度。气泡在槽内生长时窗口完全不动；
    /// 只有气泡超过槽位才会撑高窗口，并同步补偿 Top 以保持桌宠屏幕位置不变。
    /// </summary>
    private double _bubbleSlotHeight = 132;

    public PetWindow(ConfigService configService, AiClient aiClient)
    {
        _configService = configService;
        _aiClient = aiClient;

        InitializeComponent();

        Config.Normalize();
        _currentPetWidth = Config.PetWidth;

        _typewriterTimer.Tick += OnTypewriterTick;
        _bubbleHideTimer.Tick += (_, _) => FadeOutBubble();
        _tickTimer.Tick += OnTick;

        InputBox.KeyDown += OnInputKeyDown;
        InputBox.TextChanged += OnInputTextChanged;
        InputBox.LostKeyboardFocus += OnInputLostFocus;
        MouseWheel += OnMouseWheelZoom;
        MouseMove += OnPetMouseMove;
        MouseEnter += OnPetMouseEnter;

        Loaded += OnLoaded;
        Closing += OnClosing;

        AppLog.Info("桌宠窗口初始化完成");
    }

    public AppConfig Config => _configService.Current;

    // ==================================================================
    //  生命周期
    // ==================================================================

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowLevelService.ApplyToolWindowStyle(this);

        ApplyWindowIcon();
        ChangeState("waiting");          // 载入并开始播放待机动画
        ApplyPetScale();                 // 会顺带按桌宠尺寸把窗口排好
        RestoreWindowPlacement();
        ClampPetIntoWorkArea();          // 首次落地时确保桌宠在屏内
        _placementInitialized = true;
        WindowLevelService.Apply(this, Config.Level);

        RefreshTray();
        StartBreathing();

        _tickTimer.Start();
        AppLog.Info($"窗口层级: {Config.Level}, 桌宠宽度: {_currentPetWidth:0}, " +
                    $"窗口 {Width:0}x{Height:0}");
    }

    /// <summary>
    /// 给桌宠加一个极轻微的呼吸缩放（只缩放，不移动窗口）。
    /// 用 ScaleTransform 而不是改尺寸，避免和布局互抢导致抖动。
    /// </summary>
    private void StartBreathing()
    {
        try
        {
            var breath = new DoubleAnimation
            {
                From = 1.0,
                To = 1.022,
                Duration = TimeSpan.FromSeconds(2.6),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            PetBreath.BeginAnimation(ScaleTransform.ScaleYProperty, breath);

            var breathX = new DoubleAnimation
            {
                From = 1.0,
                To = 1.012,
                Duration = TimeSpan.FromSeconds(2.6),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            PetBreath.BeginAnimation(ScaleTransform.ScaleXProperty, breathX);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"桌宠呼吸动画启动失败（不影响使用）: {ex.Message}");
        }
    }

    /// <summary>设置窗口图标：外部文件优先，没有就用手写进程序集的内嵌图标。</summary>
    private void ApplyWindowIcon()
    {
        try
        {
            Icon = File.Exists(AppPaths.Icon)
                ? new BitmapImage(new Uri(AppPaths.Icon, UriKind.Absolute))
                : new BitmapImage(AppPaths.EmbeddedIconUri);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
        {
            AppLog.Warn($"设置窗口图标失败（不影响使用）: {ex.Message}");
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_disposed) return;

        // 关闭按钮（Alt+F4 等）只隐藏，保留托盘与后台驻留能力，由托盘「退出应用」真正结束进程。
        // 先确保不是最小化状态，否则 Left/Top 会变成窗口恢复前的异常值并被存进配置。
        if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;

        e.Cancel = true;
        SavePlacement();
        Hide();
    }

    private void RestoreWindowPlacement()
    {
        // 窗口顶部到桌宠顶部的固定距离（气泡槽位），用它把「配置里存的桌宠位置」
        // 换算回窗口位置，保证保存/恢复前后桌宠落在同一个屏幕点上。
        const double petOffsetInWindow = BubbleSlotHeight;

        if (Config.WindowX is { } savedX && Config.WindowY is { } savedY && IsOnAnyScreen(savedX, savedY))
        {
            // 旧坐标只有在确实落在某块真实显示器上时才恢复，否则宁可回默认位置
            Left = savedX;
            Top = Math.Max(0, savedY - petOffsetInWindow);
        }
        else
        {
            var (left, top) = DefaultPlacement();
            Left = left;
            Top = top;
        }
    }

    private void SavePlacement()
    {
        Config.PetWidth = _currentPetWidth;

        // 存「桌宠本体」的坐标而不是窗口坐标：窗口还带着气泡预留区，
        // 存窗口坐标会让下次启动时桌宠整体上移一个槽位的高度。
        var petLeft = Left;
        var petTop = Top + BubbleSlotHeight;

        // 位置明显不合理时不要污染配置，避免下次启动继续跑到屏幕外
        if (IsOnAnyScreen(petLeft, petTop))
        {
            Config.WindowX = petLeft;
            Config.WindowY = petTop;
        }

        _ = SaveConfigAsync();
    }

    /// <summary>
    /// 判断坐标是否落在任一真实显示器的可见范围内。
    /// 不用 SystemParameters.WorkArea：在某些会话下它会返回异常值，
    /// 导致桌宠被存到屏幕外（这正是旧版偶尔「桌宠不见了」的原因之一）。
    /// 注意：Window.Left/Top 是 DIP，而显示器边界是物理像素，
    /// 所以这里必须先把屏幕边界换算成 DIP 再比较，否则在 125%/150% 缩放下会误判。
    /// </summary>
    private bool IsOnAnyScreen(double x, double y)
    {
        if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y)) return false;

        try
        {
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                var scale = DpiScaleFor(screen);
                var left = screen.Bounds.Left / scale;
                var top = screen.Bounds.Top / scale;
                var right = screen.Bounds.Right / scale;
                var bottom = screen.Bounds.Bottom / scale;

                // 至少要有一部分落在屏幕里，且不能是 Windows 用来隐藏窗口的坐标区
                if (x > left - 60 && x < right - 40 && y > top - 60 && y < bottom - 40)
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppLog.Warn($"枚举显示器失败: {ex.Message}");
            return false;
        }

        return false;
    }

    /// <summary>
    /// 取某块显示器的 DPI 缩放系数（1.0 表示 100%）。
    /// 用窗口自身的物理宽度 / DIP 宽度推算，比调用 GetDpiForWindow 更可靠，
    /// 也不依赖窗口句柄是否已经创建。
    /// </summary>
    private double DpiScaleFor(System.Windows.Forms.Screen screen)
    {
        var scale = WindowLevelService.GetWindowDpiScale(this);
        return scale > 0 ? scale : 1.0;
    }

    /// <summary>
    /// 默认落点：主屏右下角，且让「桌宠本体」贴着右下（而不是让整扇窗口贴边），
    /// 这样气泡预留区自然落在屏幕内的上方。
    /// </summary>
    private (double Left, double Top) DefaultPlacement()
    {
        var area = PrimaryWorkAreaDip();

        // 底部预留输入框槽位，让默认位置下输入框也能完整显示在屏幕内
        var petHeight = MeasurePetHeight(_currentPetWidth);
        var petLeft = area.Right - _currentPetWidth - 60;
        var petTop = area.Bottom - petHeight - InputSlotHeight - 8;

        // 窗口顶部 = 桌宠顶部 - 气泡预留区
        return (petLeft, Math.Max(area.Top, petTop - BubbleSlotHeight));
    }

    /// <summary>主屏工作区，单位已换算成 DIP，可直接与 Window.Left/Top 运算。</summary>
    private static Rect PrimaryWorkAreaDip()
    {
        try
        {
            if (System.Windows.Forms.Screen.PrimaryScreen is { } primary)
            {
                var area = primary.WorkingArea;
                var dpi = WindowLevelService.GetSystemDpi();
                var scale = dpi > 0 ? dpi / 96.0 : 1.0;
                return new Rect(area.Left / scale, area.Top / scale, area.Width / scale, area.Height / scale);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppLog.Warn($"读取主屏工作区失败: {ex.Message}");
        }

        return new Rect(0, 0, 1920, 1080);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _tickTimer.Stop();
        _typewriterTimer.Stop();
        _bubbleHideTimer.Stop();
        _inputHideTimer?.Stop();
        _aiCts?.Cancel();
        _aiCts?.Dispose();
        _tray?.Dispose();
        _dashboard?.ForceClose();

        // 停掉所有表情动画的定时器，避免退出后回调还在刷帧
        foreach (var animator in _animators.Values) animator.Dispose();
        _animators.Clear();

        SavePlacement();
    }

    // ==================================================================
    //  IPetHost / ITrayHost：对外能力
    // ==================================================================

    public async Task SaveConfigAsync()
    {
        Config.Normalize();
        await _configService.SaveAsync();
    }

    public void ShowMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        _bubbleHideTimer.Stop();
        _bubbleFullText = text.Trim();
        _bubbleCursor = 1;
        _isTyping = true;
        _bubbleVisible = true;

        BubbleText.Text = _bubbleFullText[.._bubbleCursor];
        BubbleBorder.Visibility = Visibility.Visible;
        UpdateBubbleLayout(animateGrowth: true);
        FadeBubble(toVisible: true, durationMs: 260);

        _typewriterTimer.Stop();
        _typewriterTimer.Start();
    }

    public void ChangeState(string state)
    {
        var asset = TextParsers.StateToAsset(state);
        if (string.Equals(asset, _currentState, StringComparison.OrdinalIgnoreCase)) return;

        var animator = GetAnimator(asset);
        if (animator is null)
        {
            // 素材缺失时退回 waiting，保证桌宠不会变成空白
            if (!string.Equals(asset, "waiting", StringComparison.OrdinalIgnoreCase)) ChangeState("waiting");
            return;
        }

        // 先把旧表情停掉，避免多个动画器同时往 Image 上刷帧
        if (_currentState is not null && _animators.TryGetValue(_currentState, out var previous))
            previous.Stop();

        _currentState = asset;
        animator.Start();
        AppLog.Info($"表情切换: {asset}（{animator.FrameCount} 帧）");
    }

    public void SubmitInput(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _ = HandleUserInputAsync(text.Trim());
    }

    /// <summary>管理台的实时日志区。旧版直接 append 到 QTextEdit，这里同样保持追加语义。</summary>
    public void AppendLiveLog(string text)
    {
        _dashboard?.AppendLiveLog(text);
    }

    public void RefreshPanel(DashboardPage page)
    {
        _dashboard?.RefreshPanel(page);
    }

    public void ReloadSettingsIntoDashboard()
    {
        _dashboard?.ReloadFromConfig();
    }

    public void OpenPanel(DashboardPage page)
    {
        _dashboard ??= CreateDashboard();
        _dashboard.Show();
        _dashboard.OpenPage(page);
        _dashboard.Activate();
    }

    public void ResetPosition()
    {
        var (left, top) = DefaultPlacement();
        Left = left;
        Top = top;

        WindowLevelService.Apply(this, Config.Level);
        ResetOpacity();
        Show();
        Activate();
        SavePlacement();

        ShowMessage("妖精小姐闪现回来啦！没有让你等太久吧~ ♪");
        ChangeState("wink");
    }

    public void Shutdown()
    {
        SavePlacement();
        (Application.Current as App)?.ShutdownApplication();
    }

    private DashboardWindow CreateDashboard() => new(this);

    // ==================================================================
    //  表情与尺寸
    // ==================================================================

    /// <summary>
    /// 取某个表情的逐帧播放器，没有就现场加载。
    /// 用自研的 <see cref="GifAnimator"/> 而不是把 GIF 直接丢给 WPF 的 BitmapImage：
    /// 后者在本项目实测只会显示一帧、不推进动画（桌宠看起来「一动不动」）。
    /// </summary>
    private GifAnimator? GetAnimator(string state)
    {
        if (_animators.TryGetValue(state, out var cached)) return cached;

        // 优先外部文件（方便换角色），没有再用内嵌资源（保证单文件 exe 可用）
        var animator = new GifAnimator(PresentFrame, state);
        var loaded = animator.TryLoadFile(AppPaths.Gif(state)) || animator.TryLoadResource(AppPaths.EmbeddedGifUri(state));

        if (!loaded)
        {
            animator.Dispose();
            AppLog.Warn($"表情素材不可用（外部文件与内嵌资源都失败）: {state}");
            return null;
        }

        _animators[state] = animator;
        return animator;
    }

    /// <summary>把某一帧送到 Image 上。由 GifAnimator 的定时器回调，始终在 UI 线程。</summary>
    private void PresentFrame(ImageSource frame)
    {
        PetImage.Source = frame;

        // 记录当前帧指纹，供自检确认动画真的在推进（而不是一直显示同一帧）
        CurrentFrameHash = HashFrame(frame);
        _framePresentCount++;
    }

    /// <summary>当前显示帧的像素指纹；自检用来判断画面有没有换过。</summary>
    public string CurrentFrameHash { get; private set; } = string.Empty;

    /// <summary>已经送出的帧次数，用于确认定时器在工作。</summary>
    public int FramePresentCount => _framePresentCount;

    private static string HashFrame(ImageSource frame)
    {
        if (frame is not BitmapSource source) return "?";

        try
        {
            var stride = source.PixelWidth * 4;
            var pixels = new byte[stride * source.PixelHeight];
            source.CopyPixels(pixels, stride, 0);

            using var md5 = System.Security.Cryptography.MD5.Create();
            return Convert.ToHexString(md5.ComputeHash(pixels))[..12];
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return "?";
        }
    }

    /// <summary>载入某个表情并开始播放。</summary>
    private void LoadAndPlay(string state)
    {
        var animator = GetAnimator(state);

        if (animator is null)
        {
            if (!string.Equals(state, "waiting", StringComparison.OrdinalIgnoreCase))
            {
                LoadAndPlay("waiting");
                return;
            }

            return;
        }

        animator.Start();
    }

    private void ApplyPetScale()
    {
        var width = _currentPetWidth;
        var height = MeasurePetHeight(width);

        // Image 与它的容器都设显式尺寸。
        // 容器必须显式定高：它的行是 Auto，若量到 0 高，
        // WPF 会把超出布局槽的子元素整块裁掉，桌宠就完全不可见（实测正是这个现象）。
        PetImage.Width = width;
        PetImage.Height = height;
        PetHost.Width = width;
        PetHost.Height = height;

        UpdateInputBoxSize();
        UpdateLayoutForBubble(animate: false);
    }

    private void UpdateInputBoxSize()
    {
        // 输入框宽度跟随桌宠缩放，但保底不至于太窄输不下字
        var scale = _currentPetWidth / BaseWidth;
        InputBox.Width = Math.Clamp(210 * scale, 150, 520);
        InputBorder.MinWidth = Math.Clamp(230 * scale, 170, 560);
    }

    /// <summary>
    /// 【布局总擎】窗口尺寸只由桌宠本体决定，气泡与输入框各占一个固定槽位。
    ///
    /// 关键点：
    ///   1. 气泡槽位（<see cref="BubbleSlotHeight"/>）与输入框槽位（<see cref="InputSlotHeight"/>）
    ///      都是【恒定预留】的，不随内容显隐变化 —— 所以窗口总高度永远不变；
    ///   2. 气泡与输入框只切换 Visibility，不改变窗口尺寸；
    ///   3. 窗口位置（Left/Top）只由桌宠的屏幕坐标反推一次，之后不再被布局改写。
    ///
    /// 这三点合起来保证了「点击桌宠弹出输入框时桌宠绝不位移」——
    /// 旧实现靠「记住窗口底边、改完尺寸再把 Top 挪回去」补偿，气泡和输入框同时出现时会互相打架，
    /// 于是就有了用户看到的瞬移。
    /// </summary>
    private void UpdateLayoutForBubble(bool animate)
    {
        var petWidth = Math.Max(MinPetWidth, _currentPetWidth);
        var desiredWidth = Math.Max(petWidth, 240);
        var petHeight = MeasurePetHeight(petWidth);

        // 记住上次的槽高，用于「气泡超过上限」时做 Top 补偿
        var previousSlot = _bubbleSlotHeight;

        // 第 0 行固定为气泡槽高，气泡自身贴底摆放 ——
        // 它是在这块空间里向上生长的，绝不会把桌宠往下推。
        // （之前把「固定行高」和「PetHost 顶部留白」叠加，等于把桌宠推下去两次，桌宠就被挤出了窗口。）
        _bubbleSlotHeight = BubbleSlotHeight;
        BubbleRow.Height = new GridLength(_bubbleSlotHeight);

        BubbleBorder.Width = double.NaN;
        BubbleBorder.MaxWidth = Math.Max(200, desiredWidth - 8);

        // 桌宠不再需要额外留白：纵向位置完全由行高决定
        PetHost.Margin = new Thickness(0);
        InputBorder.Height = InputSlotHeight;

        Width = desiredWidth;
        Height = _bubbleSlotHeight + petHeight + InputSlotHeight;

        // 只有气泡高度超过槽位时才把窗口撑高，并做一次 Top 反向补偿
        UpdateLayout();
        var bubbleHeight = BubbleBorder.Visibility == Visibility.Visible
            ? BubbleBorder.ActualHeight
            : 0;
        var newSlot = Math.Max(BubbleSlotHeight, Math.Min(bubbleHeight, BubbleSlotHeight * 3));
        var delta = newSlot - previousSlot;

        if (Math.Abs(delta) > 0.5)
        {
            // 槽位变高会把桌宠往下推 delta，于是把窗口整体上移同样的量，桌宠屏幕位置不变
            _bubbleSlotHeight = newSlot;
            BubbleRow.Height = new GridLength(newSlot);
            Top -= delta;
            Height = newSlot + petHeight + InputSlotHeight;
        }

        _ = animate;   // 尺寸恒定后不再需要「生长动画」，保留参数是为了兼容既有调用点

        // 只在首次落地时做一次可见性兜底；平时不做任何位移，
        // 用户明确要求「无空间时允许气泡显示在屏幕外」，不要为此挪动桌宠。
        if (!_placementInitialized)
        {
            ClampPetIntoWorkArea();
            _placementInitialized = true;
        }
    }

    /// <summary>按当前素材的宽高比计算桌宠在指定宽度下应有的高度。</summary>
    private double MeasurePetHeight(double width)
    {
        if (PetImage.Source is System.Windows.Media.Imaging.BitmapSource { PixelWidth: > 0, PixelHeight: > 0 } source)
            return width * source.PixelHeight / (double)source.PixelWidth;

        // 拿不到素材信息时退化为正方形（GIF 素材本来就是 320x320）
        return width;
    }
    /// <summary>气泡槽位的固定高度。超出部分会被裁剪，以保证桌宠位置稳定。</summary>
    private const double BubbleSlotHeight = 132;

    /// <summary>输入框槽位的固定高度（含与桌宠之间的间距）。恒定预留，不随显隐变化。</summary>
    private const double InputSlotHeight = 62;

    private void UpdateBubbleLayout(bool animateGrowth)
    {
        _bubbleVisible = true;
        UpdateLayoutForBubble(animateGrowth);
    }

    /// <summary>
    /// 只在初始化或用户主动重置位置时调用：把「桌宠本体」拉回工作区内。
    /// 刻意以桌宠（而不是整扇窗口）为基准，这样气泡即便超出屏幕也不会把桌宠顶走。
    /// </summary>
    private void ClampPetIntoWorkArea()
    {
        var area = CurrentWorkAreaDip();

        var petTop = Top + BubbleSlotHeight;
        var petBottom = petTop + (PetImage.ActualHeight > 0 ? PetImage.ActualHeight : _currentPetWidth);
        var petLeft = Left;
        var petRight = Left + Width;

        var dy = 0.0;
        if (petBottom > area.Bottom) dy = area.Bottom - petBottom;
        else if (petTop < area.Top) dy = area.Top - petTop;

        var dx = 0.0;
        if (petRight > area.Right) dx = area.Right - petRight;
        else if (petLeft < area.Left) dx = area.Left - petLeft;

        if (dy != 0) Top += dy;
        if (dx != 0) Left += dx;
    }

    /// <summary>
    /// 取窗口中心所在显示器的工作区，单位换算成 DIP（与 Window.Left/Top 一致）。
    /// 显示器边界本身是物理像素，直接在 125%/150% 缩放下比较会整体偏移。
    /// </summary>
    private Rect CurrentWorkAreaDip()
    {
        try
        {
            var scale = DpiScaleFor(System.Windows.Forms.Screen.PrimaryScreen ?? System.Windows.Forms.Screen.AllScreens[0]);

            // 用窗口中心的物理坐标判断落在哪块屏
            var centerX = (Left + Width / 2) * scale;
            var centerY = (Top + Height / 2) * scale;

            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                if (!screen.Bounds.Contains((int)centerX, (int)centerY)) continue;

                var area = screen.WorkingArea;
                return new Rect(area.Left / scale, area.Top / scale, area.Width / scale, area.Height / scale);
            }

            if (System.Windows.Forms.Screen.PrimaryScreen is { } primary)
            {
                var area = primary.WorkingArea;
                return new Rect(area.Left / scale, area.Top / scale, area.Width / scale, area.Height / scale);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppLog.Warn($"读取显示器工作区失败: {ex.Message}");
        }

        return new Rect(0, 0, 1920, 1080);
    }

    // ==================================================================
    //  气泡与打字机
    // ==================================================================

    private void FadeBubble(bool toVisible, int durationMs)
    {
        var animation = new DoubleAnimation
        {
            To = toVisible ? 1.0 : 0.0,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
        };

        if (!toVisible)
        {
            animation.Completed += (_, _) => OnBubbleFadedOut();
        }

        BubbleBorder.BeginAnimation(OpacityProperty, animation);
    }

    private void OnBubbleFadedOut()
    {
        if (_isTyping) return;

        _bubbleVisible = false;
        BubbleBorder.Visibility = Visibility.Collapsed;
        BubbleText.Text = string.Empty;
        _bubbleFullText = string.Empty;
        UpdateLayoutForBubble(animate: false);

        // 说完话要回到待机表情，否则对话时设的开心/疑问等表情会一直挂着，桌宠看起来就「僵住」了。
        // （旧版对应的是气泡淡出后调用 change_state("idle")，重构时被漏掉了。）
        ChangeState(IdleState);
    }

    /// <summary>已经回到待机表情、可以接受下一次互动。</summary>
    public bool IsIdleAnimation => string.Equals(_currentState, IdleState, StringComparison.OrdinalIgnoreCase);

    /// <summary>当前正在播放的表情名（自检使用）。</summary>
    public string? CurrentState => _currentState;

    /// <summary>缩短气泡停留时长，供自检快速走完「气泡淡出后回待机」的链路。</summary>
    public void SetBubbleHoldForTest(int milliseconds)
    {
        _bubbleHoldMs = Math.Clamp(milliseconds, 300, 60000);
        _bubbleHideTimer.Interval = TimeSpan.FromMilliseconds(_bubbleHoldMs);
    }

    /// <summary>
    /// 当前的「待机」表情：深夜是睡觉，其余时间用 waiting。
    /// 与 <see cref="CheckTimeState"/> 的判定保持一致。
    /// </summary>
    private static string IdleState => IsNightTime(DateTime.Now) ? "sleep" : "waiting";

    /// <summary>深夜判定：22:00 到次日 08:00 之间算夜间。</summary>
    private static bool IsNightTime(DateTime now) => now.Hour >= 22 || now.Hour < 8;

    private void OnTypewriterTick(object? sender, EventArgs e)
    {
        if (_bubbleCursor <= _bubbleFullText.Length)
        {
            BubbleText.Text = _bubbleFullText[.._bubbleCursor];
            _bubbleCursor++;
            UpdateLayoutForBubble(animate: true);
        }
        else
        {
            _typewriterTimer.Stop();
            _isTyping = false;
            _bubbleHideTimer.Start();
        }
    }

    private void FadeOutBubble()
    {
        if (_isTyping) return;
        _bubbleHideTimer.Stop();
        FadeBubble(toVisible: false, durationMs: 420);
    }

    /// <summary>立刻显示气泡全文并停掉打字机（点击加速、以及截图/自检都走这里）。</summary>
    public void CompleteTypewriter()
    {
        if (!_isTyping) return;

        _typewriterTimer.Stop();
        BubbleText.Text = _bubbleFullText;
        _bubbleCursor = _bubbleFullText.Length + 1;
        _isTyping = false;
        UpdateLayoutForBubble(animate: true);
        _bubbleHideTimer.Start();
    }

    /// <summary>
    /// 跳过淡入动画，直接把气泡拉到完全不透明（截图与自检用）。
    /// 淡入由属性动画驱动，而截图模式不跑渲染时钟，动画值会一直停在起始的 0 ——
    /// 于是气泡位置尺寸都对，却整块看不见。
    /// </summary>
    public void SetBubbleOpacityImmediately(double opacity)
    {
        BubbleBorder.BeginAnimation(OpacityProperty, null);   // 清掉动画，改为直接赋值
        BubbleBorder.Opacity = opacity;
    }

    /// <summary>气泡还在打字时点击桌宠：立刻显示全文，方便快速阅读。</summary>
    private bool TrySkipTypewriter()
    {
        if (!_bubbleVisible) return false;

        if (_isTyping)
        {
            CompleteTypewriter();
            return true;
        }

        return false;
    }

    // ==================================================================
    //  用户输入处理
    // ==================================================================

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return)) return;

        e.Handled = true;
        var text = InputBox.Text.Trim();
        InputBox.Clear();
        HideInputBox();

        if (text.Length > 0) _ = HandleUserInputAsync(text);
    }

    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
    {
        // 只要还在输入，就一直给隐藏倒计时续命
        if (_suppressInputAutoHide) return;
        ResetInputHideTimer();
    }

    private void OnInputLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_suppressInputAutoHide) return;
        ResetInputHideTimer();
    }

    private void ResetInputHideTimer()
    {
        // 输入框已经收起来了就不必再计时
        if (InputBorder.Visibility != Visibility.Visible) return;

        _inputHideTimer ??= CreateInputHideTimer();
        _inputHideTimer.Stop();
        _inputHideTimer.Start();
    }

    private DispatcherTimer CreateInputHideTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(InputIdleHideMs) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            HideInputBox();
        };
        return timer;
    }

    /// <summary>
    /// 弹出输入框并开始「10 秒无操作自动收起」倒计时。
    /// 计时器必须在显示的同一条路径上启动：早先只在「敲了字」或「失去焦点」时才启动，
    /// 于是点开输入框后什么都不做的话，它会一直挂在屏幕上永不消失。
    /// </summary>
    public void ShowInputBox()
    {
        _suppressInputAutoHide = false;
        InputBorder.Visibility = Visibility.Visible;

        // 先按「预留了输入框槽位」的规则把窗口排好，再决定输入框贴上还是贴下
        UpdateLayoutForBubble(animate: true);
        PlaceInputBox();

        InputBox.Focus();
        ResetInputHideTimer();
    }

    /// <summary>
    /// 决定输入框显示在桌宠下方还是上方。
    ///
    /// 判断方式直接算屏幕坐标：把输入框放在桌宠下方，看它的底边会不会超出工作区。
    /// 不用「窗口内部富余空间」来判断 —— 那个值依赖布局细节，很容易被别的改动带偏；
    /// 用屏幕坐标则与布局实现无关，也和用户看到的一致。
    ///
    /// 上下都放不下时保持下方，允许它露出一部分在屏幕外 ——
    /// 这是刻意的：绝不为了让输入框可见而挪动桌宠。
    /// </summary>
    private void PlaceInputBox()
    {
        var petHeight = PetImage.ActualHeight > 0 ? PetImage.ActualHeight : _currentPetWidth;
        var petTop = Top + BubbleSlotHeight;

        var inputHeight = Math.Max(InputBorder.ActualHeight, 40);
        var gap = 8.0;
        var area = CurrentWorkAreaDip();

        // 放在下方时的底边：桌宠底边 + 间距 + 输入框高度
        var bottomIfBelow = petTop + petHeight + gap + inputHeight;
        var placeAbove = bottomIfBelow > area.Bottom - 2;

        if (placeAbove)
        {
            // 放到桌宠上方：占据气泡那一行的底部，紧贴桌宠头顶
            Grid.SetRow(InputBorder, 0);
            Grid.SetRowSpan(InputBorder, 1);
            InputBorder.VerticalAlignment = VerticalAlignment.Bottom;
            InputBorder.Margin = new Thickness(0, 0, 0, 6);
        }
        else
        {
            // 默认：桌宠下方
            Grid.SetRow(InputBorder, 2);
            Grid.SetRowSpan(InputBorder, 1);
            InputBorder.VerticalAlignment = VerticalAlignment.Top;
            InputBorder.Margin = new Thickness(0, 0, 0, 0);
        }

        _inputBoxAbove = placeAbove;
    }

    /// <summary>输入框当前是否显示在桌宠上方（自检使用）。</summary>
    public bool IsInputBoxAbove => _inputBoxAbove;

    /// <summary>桌宠本体当前在屏幕上的矩形：Left, Top, Width, Height（自检使用）。</summary>
    public (double Left, double Top, double Width, double Height) GetPetScreenRect()
    {
        var height = PetImage.ActualHeight > 0 ? PetImage.ActualHeight : _currentPetWidth;
        return (Left, Top + BubbleSlotHeight, Width, height);
    }

    /// <summary>窗口当前的高度（自检使用，用于确认布局尺寸稳定）。</summary>
    public double WindowHeight => Height;

    /// <summary>把桌宠本体移动到指定屏幕坐标（自检使用）。</summary>
    public void MovePetTo(double petLeft, double petTop)
    {
        Left = petLeft;
        Top = petTop - BubbleSlotHeight;
    }

    /// <summary>布局细节快照，供诊断输出（自检/截图模式使用）。</summary>
    public string DescribeLayout()
    {
        var petBandTop = Top + BubbleSlotHeight;
        var hostPt = PetHost.TranslatePoint(new System.Windows.Point(0, 0), this);
        var hostY = hostPt.Y;
        var imgPt = PetImage.TranslatePoint(new System.Windows.Point(0, 0), this);
        return $"窗口 {Width:0}x{Height:0} @({Left:0},{Top:0}) | " +
               $"桌宠带顶 y={petBandTop:0} | 容器 y={hostY:0} 高={PetHost.ActualHeight:0} 宽={PetHost.ActualWidth:0} | " +
               $"图 高={PetImage.ActualHeight:0} 宽={PetImage.ActualWidth:0} | " +
               $"气泡={BubbleBorder.Visibility}/高{BubbleBorder.ActualHeight:0} | " +
               $"图在窗口内=({imgPt.X:0},{imgPt.Y:0}) | " +
               $"输入框={InputBorder.Visibility}/上方{_inputBoxAbove}/高{InputBorder.ActualHeight:0} | " +
               $"PetHost.Margin={PetHost.Margin} | 行高[{BubbleRow.ActualHeight:0},{PetHost.ActualHeight:0}] | " +
               $"行0={LayoutRoot.RowDefinitions[0].Height} 行1={LayoutRoot.RowDefinitions[1].Height} 行2={LayoutRoot.RowDefinitions[2].Height}";
    }
    /// <summary>当前显示器的工作区（自检使用）。</summary>
    public Rect CurrentWorkAreaForTest() => CurrentWorkAreaDip();

    /// <summary>收起输入框（自检使用，等价于用户再点一次桌宠）。</summary>
    public void HideInputBoxForTest() => HideInputBox();

    private void HideInputBox()
    {
        if (InputBorder.Visibility != Visibility.Visible) return;

        // 先停掉计时器，再清空文本，避免 Clear() 触发的 TextChanged 又去重置计时
        _inputHideTimer?.Stop();

        _suppressInputAutoHide = true;
        InputBorder.Visibility = Visibility.Collapsed;
        InputBox.Clear();
        _suppressInputAutoHide = false;

        UpdateLayoutForBubble(animate: false);
    }

    /// <summary>输入框当前是否展开（自检使用）。</summary>
    public bool IsInputBoxVisible => InputBorder.Visibility == Visibility.Visible;

    /// <summary>右键菜单被弹出过几次（自检使用）。</summary>
    public int ContextMenuShowCount { get; private set; }

    /// <summary>桌宠当前宽度（自检使用）。</summary>
    public double CurrentPetWidth => _currentPetWidth;

    /// <summary>设置桌宠宽度并立即重排（自检使用）。</summary>
    public void SetPetWidth(double width)
    {
        _currentPetWidth = Math.Clamp(width, MinPetWidth, MaxPetWidth);
        ApplyPetScale();
        UpdateLayoutForBubble(_bubbleVisible);
    }

    /// <summary>缩放状态快照，便于自检输出诊断信息。</summary>
    public string DescribeResizeState()
    {
        var pointer = GetPointerScreenPosition();
        return $"光标X={pointer.X:0} 基准X={_resizeAnchorPointerX:0} " +
               $"基准宽={_resizeAnchorWidth:0} 当前宽={_currentPetWidth:0} " +
               $"缩放中={_isResizing} 已拖动={_resizeMoved}";
    }

    private void ToggleInputBox()
    {
        if (InputBorder.Visibility != Visibility.Visible)
        {
            ShowInputBox();
        }
        else if (string.IsNullOrEmpty(InputBox.Text))
        {
            HideInputBox();
        }
    }

    /// <summary>完整输入链路：声控日程 -> 斜杠快捷指令 -> AI 对话。</summary>
    private async Task HandleUserInputAsync(string text)
    {
        ResetOpacity();
        ResetIdleCounters();

        // 1) 「12:00 提醒我吃饭」
        if (TextParsers.TryParseReminder(text, out var time, out var content))
        {
            Config.Reminders.Add(new Reminder { Time = time, Content = content });
            await SaveConfigAsync();
            RefreshPanel(DashboardPage.Reminders);

            ShowMessage($"已经记在心里啦，{time} 准时叫你哦~ ♪");
            ChangeState("wink");
            AppendLiveLog($"⏰ [新建日程] {time} {content}");
            return;
        }

        // 2) 斜杠系统指令
        if (text.StartsWith('/'))
        {
            RunSlashCommand(text[1..].Trim());
            return;
        }

        // 3) 交给大模型
        await AskAiAsync(text);
    }

    private void RunSlashCommand(string key)
    {
        var normalized = key.ToLowerInvariant();
        if (!PersonaDefaults.SlashCommands.TryGetValue(normalized, out var target))
        {
            ShowMessage($"爱莉希雅没听过『/{key}』这个指令呢，要检查一下吗？");
            ChangeState("question");
            return;
        }

        if (ProcessLauncher.TryLaunchSystemTool(target, out var error))
        {
            ShowMessage($"已经为你唤醒『{key}』啦，指挥官~ ♪");
            ChangeState("wink");
            AppendLiveLog($"➤ [快捷指令] {key} -> {target}");
        }
        else
        {
            AppLog.Warn($"斜杠指令 {key} 启动失败: {error}");
            ShowMessage("唔……唤醒失败了，可能系统不兼容呢。");
            ChangeState("cry");
        }
    }

    private async Task AskAiAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(Config.ApiKey))
        {
            ShowMessage("还没有绑定 API Key 哦，去后台「系统设置」里填一个吧~");
            ChangeState("question");
            return;
        }

        ChangeState("question");
        AppendLiveLog($"你: {prompt}");

        _aiCts?.Cancel();
        _aiCts?.Dispose();
        _aiCts = new CancellationTokenSource();

        var history = Config.History.Count > Config.ContextLinkCount
            ? Config.History.GetRange(Config.History.Count - Config.ContextLinkCount, Config.ContextLinkCount)
            : new List<ChatTurn>(Config.History);

        try
        {
            var reply = await _aiClient.ChatAsync(
                Config, prompt, history, TimeSpan.FromSeconds(20), _aiCts.Token);
            await OnAiReplyAsync(prompt, reply);
        }
        catch (OperationCanceledException)
        {
            // 用户又发了一条新消息，这条被主动取消，静默丢弃
        }
        catch (AiException ex)
        {
            ShowMessage(ex.Message);
            ChangeState("cry");
            AppendLiveLog($"⚠ [AI 异常] {ex.Message}");
        }
        catch (Exception ex)
        {
            AppLog.Error("AI 对话出现未预期异常", ex);
            ShowMessage("唔……妖精小姐的信号出了点问题，看看 elysia.log 好吗？");
            ChangeState("cry");
        }
    }

    private async Task OnAiReplyAsync(string prompt, string rawReply)
    {
        var (text, state) = TextParsers.ParseReply(TextParsers.StripThinking(rawReply));
        if (text.Length == 0) text = "……嗯，我在听哦。";

        ChangeState(state);
        ShowMessage(text);

        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        Config.History.Add(new ChatTurn { User = prompt, Pet = text, Time = stamp });
        TrimHistory(Config.History, Config.MaxChatHistory);

        await SaveConfigAsync();
        RefreshPanel(DashboardPage.ChatHistory);
        AppendLiveLog($"爱莉希雅: {text}");
    }

    private static void TrimHistory(List<ChatTurn> history, int max)
    {
        if (history.Count <= max) return;
        history.RemoveRange(0, history.Count - max);
    }

    // ==================================================================
    //  秒级时间调度：入夜判定 / 报时 / 日程 / 待机冒泡 / 自动透明
    // ==================================================================

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;

        CheckNightState(now);
        CheckHourlyChime(now);
        CheckReminders(now);
        CheckIdle(now);
        CheckOpacity();
    }

    /// <summary>
    /// 入夜判定：深夜 22:00 到次日 08:00 之间让桌宠趴着睡觉。
    /// 重构时漏掉了旧版的这条「时钟A」，补回来 —— 白天醒着、夜里睡觉的观感才算完整。
    /// </summary>
    private void CheckNightState(DateTime now)
    {
        if (!IsNightTime(now)) return;

        // 只在待机（没有气泡、不在打字）时才切睡眠，避免打断正在进行的对话
        if (_bubbleVisible || _isTyping) return;
        if (string.Equals(_currentState, "sleep", StringComparison.OrdinalIgnoreCase)) return;

        ChangeState("sleep");
    }

    private void CheckHourlyChime(DateTime now)
    {
        if (!Config.HourlyChime) return;

        // 旧版要求 minute==0 && second==0，定时器抖动一次就会整天不报时；
        // 这里放宽到「整点后 30 秒内只报一次」，用 lastChimedHour 保证唯一性。
        if (now.Minute != 0 || now.Second > 30) return;
        if (_lastChimedHour == now.Hour) return;

        _lastChimedHour = now.Hour;
        RefreshPanel(DashboardPage.Chime);

        var key = now.Hour.ToString("00");
        var message = Config.HourlyMessages.TryGetValue(key, out var list) && list.Count > 0
            ? list[_random.Next(list.Count)]
            : PersonaDefaults.FallbackChime(now.Hour);

        ShowMessage(message);
        ChangeState("wink");
        AppendLiveLog($"♪ [整点报时] {message}");
    }

    private void CheckReminders(DateTime now)
    {
        if (Config.Reminders.Count == 0) return;
        if (now.Second != 0) return;

        var key = now.Date.AddHours(now.Hour).AddMinutes(now.Minute);
        if (key == _lastReminderKey) return;

        foreach (var reminder in Config.Reminders)
        {
            if (!TextParsers.IsValidTime(reminder.Time)) continue;
            if (!string.Equals(reminder.Time, now.ToString("HH:mm"), StringComparison.Ordinal)) continue;

            _lastReminderKey = key;
            ShowMessage($"⏰ 亲爱的！妖精小姐来提醒你：『 {reminder.Content} 』");
            ChangeState("hurry");
            AppendLiveLog($"⏰ [日程提醒] {reminder.Content}");

            // 除了气泡，再弹一个系统级对话框，重要的事情不漏掉
            MessageBox.Show(
                $"爱莉希雅提醒你：\n\n{reminder.Content}",
                $"⏰ 日程提醒  {reminder.Time}",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            // 旧版保留已触发的日程；这里保持一致，但标记为已触发避免同一分钟内重复弹窗
            reminder.Fired = true;
            _ = SaveConfigAsync();
            break;
        }
    }

    private void CheckIdle(DateTime now)
    {
        _idleSeconds++;
        if (_idleSeconds < Config.IdleTimeout) return;

        _idleSeconds = 0;

        if (Config.AiIdleEnabled && !string.IsNullOrWhiteSpace(Config.ApiKey))
        {
            _ = GenerateAiIdleLineAsync();
            return;
        }

        if (Config.IdleMessages.Count == 0) return;
        var message = Config.IdleMessages[_random.Next(Config.IdleMessages.Count)];
        ShowMessage(message);
        ChangeState("hurry");
        RecordBubbleLog("❀ [本地系统冒泡]", message);
    }

    private async Task GenerateAiIdleLineAsync()
    {
        try
        {
            var reply = await _aiClient.IdleLineAsync(Config, TimeSpan.FromSeconds(12));
            var (text, state) = TextParsers.ParseReply(TextParsers.StripThinking(reply), fallback: "hurry");
            if (text.Length == 0) return;

            ShowMessage(text);
            ChangeState(state);
            RecordBubbleLog("✦ [AI待机自动冒泡]", text);
        }
        catch (AiException ex)
        {
            AppLog.Warn($"AI 待机冒泡失败: {ex.Message}");
        }
        catch (Exception ex)
        {
            AppLog.Error("AI 待机冒泡出现未预期异常", ex);
        }
    }

    private void RecordBubbleLog(string tag, string text)
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        Config.BubbleHistory.Add(new ChatTurn { User = tag, Pet = text, Time = stamp });
        TrimHistory(Config.BubbleHistory, Config.MaxBubbleHistory);

        RefreshPanel(DashboardPage.BubbleHistory);
        AppendLiveLog($"{tag} {text}");
        _ = SaveConfigAsync();
    }

    private void CheckOpacity()
    {
        if (!Config.OpacityEnabled)
        {
            if (Opacity < 1.0) ResetOpacity();
            return;
        }

        if (!IsVisible) return;

        // 鼠标贴在桌宠身上、或正在输入框里打字时，保持全亮
        if (IsMouseOver || (InputBorder.Visibility == Visibility.Visible && InputBox.IsKeyboardFocused))
        {
            ResetOpacity();
            return;
        }

        _inactiveSeconds++;
        if (_inactiveSeconds < Config.OpacityTimeout) return;
        if (Opacity <= Config.OpacityValue + 0.001) return;

        AnimateOpacity(Config.OpacityValue, 700);
    }

    private void ResetOpacity()
    {
        _inactiveSeconds = 0;
        if (Opacity >= 0.999) return;
        AnimateOpacity(1.0, 220);
    }

    private void AnimateOpacity(double target, int durationMs)
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            To = target,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });
    }

    private void ResetIdleCounters()
    {
        _idleSeconds = 0;
        _inactiveSeconds = 0;
    }

    /// <summary>
    /// 鼠标在桌宠身上移动时持续保持全亮。
    /// 只靠 enterEvent 会导致「鼠标停在旁边看时桌宠半透明、看不清动画」，
    /// 这也是旧版给人的「桌宠像死了一样」的主要观感来源。
    /// </summary>
    private void OnPetMouseMove(object sender, MouseEventArgs e)
    {
        ResetOpacity();
        ResetIdleCounters();
    }

    /// <summary>鼠标滚轮在桌宠身上时也保持全亮（实际缩放逻辑在 OnMouseWheelZoom 中）。</summary>
    private void OnPetMouseEnter(object sender, MouseEventArgs e) => ResetOpacity();

    // ==================================================================
    //  鼠标交互：拖动 / 等比缩放 / 右键菜单
    //
    //  拖动采用「绝对定位」：窗口位置 = 当前光标屏幕坐标 - 按下时记录的光标相对窗口偏移。
    //  旧写法是 Left += (当前 - 起点)，但起点从不更新，每次移动事件都会把同一段位移再叠加
    //  一次，形成正反馈，实测拖 8 步窗口就跑到 (9140, 5364) 彻底飞出屏幕。
    //  这种写法还有一个好处：屏幕坐标与窗口坐标都是物理像素，不受 DPI 缩放影响
    //  （先前 PointToScreen 得到物理像素、而 Left/Top 是 DIP，在 125%/150% 缩放下会放大误差）。
    // ==================================================================

    /// <summary>
    /// 光标在屏幕上的坐标，单位为 DIP（与 Window.Left/Top 同一坐标系）。
    /// 刻意不用 <c>Mouse.GetPosition()</c>：那个值来自 WPF 缓存的上一次鼠标消息，
    /// 缓存一旦滞后，拖动就会一顿一顿的。这里直接向 Windows 查询真实光标位置。
    /// </summary>
    private Point GetPointerScreenPosition()
    {
        if (!WindowLevelService.TryGetCursorPosition(out var screenX, out var screenY))
        {
            // 查不到时退回 WPF 的缓存值，至少不会把拖动彻底弄坏
            return PointToScreen(Mouse.GetPosition(this));
        }

        var scale = WindowLevelService.GetWindowDpiScale(this);
        if (scale <= 0) scale = 1.0;

        return new Point(screenX / scale, screenY / scale);
    }

    /// <summary>
    /// 按「窗口左上角 = 光标 - 抓取偏移」重新定位窗口，并保证桌宠至少有一部分留在显示器内，
    /// 避免像旧版那样把窗口丢到 (9140, 5364) 这种位置再也找不回来。
    /// </summary>
    private void SetWindowPositionFromPointer(Point pointer)
    {
        var targetLeft = pointer.X - _grabOffsetInWindow.X;
        var targetTop = pointer.Y - _grabOffsetInWindow.Y;

        var area = CurrentWorkAreaDip();

        // 允许桌宠贴边或露出大部分在屏幕外，但必须留出至少 40 的可抓取区域
        const double keepVisible = 40;
        var minLeft = area.Left - Math.Max(0, Width - keepVisible);
        var maxLeft = area.Right - keepVisible;
        var minTop = area.Top - Math.Max(0, Height - keepVisible);
        var maxTop = area.Bottom - keepVisible;

        Left = Math.Clamp(targetLeft, minLeft, maxLeft);
        Top = Math.Clamp(targetTop, minTop, maxTop);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);


        // 点在输入框或气泡上的点击不应该触发拖动
        if (IsInsideInteractiveControl(e.OriginalSource as DependencyObject))
        {
            ResetOpacity();
            return;
        }

        ResetOpacity();
        ResetIdleCounters();

        if (TrySkipTypewriter())
        {
            e.Handled = true;
            return;
        }

        // 气泡正在显示时的下一次点击：让它提前消失
        if (_bubbleVisible)
        {
            _bubbleHideTimer.Stop();
            FadeOutBubble();
            e.Handled = true;
            return;
        }

        var pointer = GetPointerScreenPosition();
        _isDragging = true;
        _dragMoved = false;
        _grabOffsetInWindow = new Vector(pointer.X - Left, pointer.Y - Top);

        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_isDragging)
        {
            var pointer = GetPointerScreenPosition();
            var targetLeft = pointer.X - _grabOffsetInWindow.X;
            var targetTop = pointer.Y - _grabOffsetInWindow.Y;

            if (Math.Abs(targetLeft - Left) > 1.5 || Math.Abs(targetTop - Top) > 1.5)
            {
                _dragMoved = true;
                SetWindowPositionFromPointer(pointer);
            }

            e.Handled = true;
            return;
        }

        if (_isResizing)
        {
            ApplyResizeStep();
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (!_isDragging) return;

        _isDragging = false;
        ReleaseMouseCapture();
        e.Handled = true;

        if (_dragMoved)
        {
            SavePlacement();
            return;
        }

        // 几乎没移动 -> 视为单击，切换快捷输入框
        ToggleInputBox();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);


        if (IsInsideInteractiveControl(e.OriginalSource as DependencyObject)) return;

        BeginResize();
        e.Handled = true;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);


        if (EndResize())
        {
            e.Handled = true;
            return;
        }

        if (!IsInsideInteractiveControl(e.OriginalSource as DependencyObject) && !_bubbleVisible)
            ShowContextMenu();
    }

    /// <summary>
    /// 开始右键缩放。抽出坐标无关的核心逻辑，便于自检直接驱动。
    /// <paramref name="captureMouse"/> 为 false 时不抢占鼠标捕获，
    /// 这样自检驱动时不会把后续的鼠标交互搅乱。
    /// </summary>
    private void BeginResize(bool captureMouse = true)
    {
        _isResizing = true;
        _resizeMoved = false;
        _resizeAnchorPointerX = GetPointerScreenPosition().X;
        _resizeAnchorWidth = _currentPetWidth;
        if (captureMouse) CaptureMouse();

        AppLog.Info($"[缩放] 开始 基准X={_resizeAnchorPointerX:0} 基准宽={_resizeAnchorWidth:0} 捕获={captureMouse}");
    }

    /// <summary>
    /// 结束右键缩放。返回 true 表示这次右键属于「缩放」而非「单击」。
    /// 只有确实拖动过尺寸才保存设置，绝不顺手弹出右键菜单。
    /// </summary>
    private bool EndResize()
    {
        if (!_isResizing) return false;

        _isResizing = false;
        ReleaseMouseCapture();

        if (_resizeMoved) SavePlacement();
        else ShowContextMenu();

        AppLog.Info($"[缩放] 结束 是否拖动过={_resizeMoved} 当前宽={_currentPetWidth:0}");

        return true;
    }

    /// <summary>
    /// 按给定光标位置推进一次缩放。
    /// 与 <see cref="OnMouseMove"/> 里的缩放分支共用同一套计算；
    /// <paramref name="pointerOverride"/> 仅供自检注入坐标，正常使用时传 null 表示读真实光标。
    /// </summary>
    private void ApplyResizeStep(Point? pointerOverride = null)
    {
        if (!_isResizing) return;

        var pointer = pointerOverride ?? GetPointerScreenPosition();
        var deltaX = pointer.X - _resizeAnchorPointerX;

        // 1:1 跟随光标：光标右移多少像素，桌宠就加宽多少
        var targetWidth = Math.Clamp(_resizeAnchorWidth + deltaX, MinPetWidth, MaxPetWidth);
        if (Math.Abs(targetWidth - _currentPetWidth) < 0.5) return;

        // 缩放时钉住桌宠本体的底边：因为它不是窗口底边（下方还可能有输入框），
        // 所以必须用「桌宠底边」而不是「窗口底边」来补偿。
        var petBottom = Top + BubbleSlotHeight + PetImage.ActualHeight;
        _currentPetWidth = targetWidth;
        ApplyPetScale();
        Top = petBottom - BubbleSlotHeight - PetImage.ActualHeight;

        // 关键：标记「确实拖动过」，松手时才不会把这次拖拽当成右键单击而弹出菜单
        _resizeMoved = true;
    }

    // ---- 供自检驱动的公开入口（正常使用时不会被调用） ----

    /// <summary>模拟一次右键按下并记录缩放基准（自检用，不抢占鼠标捕获）。</summary>
    public void SimulateResizeBegin() => BeginResize(captureMouse: false);

    /// <summary>模拟一次鼠标移动导致的缩放推进（自检用）。</summary>
    public void SimulateResizeStep() => ApplyResizeStep();

    /// <summary>
    /// 用显式坐标模拟一次缩放推进（自检用）。
    /// 直接注入坐标而不是去挪真实光标：真实光标可能被系统或其它进程抢走，
    /// 导致测试结果不稳定。
    /// </summary>
    public void SimulateResizeStepAt(double screenX, double screenY) =>
        ApplyResizeStep(new Point(screenX, screenY));

    /// <summary>用显式坐标设置缩放基准（自检用）。</summary>
    public void SimulateResizeBeginAt(double screenX)
    {
        BeginResize(captureMouse: false);
        _resizeAnchorPointerX = screenX;
    }

    /// <summary>模拟一次右键抬起，返回是否属于缩放操作（自检用）。</summary>
    public bool SimulateResizeEnd() => EndResize();

    private static bool IsInsideInteractiveControl(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is TextBox or ScrollViewer or Border { Name: "BubbleBorder" }) return true;
            if (source is PetWindow) return false;
            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void OnMouseWheelZoom(object sender, MouseWheelEventArgs e)
    {
        // 滚轮微调大小：比右键拖拽更精细，属于新增的便利操作
        if (Keyboard.Modifiers != ModifierKeys.None) return;

        var petBottom = Top + BubbleSlotHeight + PetImage.ActualHeight;
        _currentPetWidth = Math.Clamp(_currentPetWidth + Math.Sign(e.Delta) * 10, MinPetWidth, MaxPetWidth);
        ApplyPetScale();
        Top = petBottom - BubbleSlotHeight - PetImage.ActualHeight;
        e.Handled = true;
    }
    private void ShowContextMenu()
    {
        // 记录一次调用，供自检确认「右键缩放后没有误弹菜单」
        ContextMenuShowCount++;

        var menu = new ContextMenu
        {
            PlacementTarget = PetImage,
            FontFamily = (FontFamily)FindResource("UiFont"),
        };
        // 1) 用户自定义的快捷启动项排在最上面
        if (Config.QuickApps.Count > 0)
        {
            foreach (var app in Config.QuickApps)
            {
                var item = new MenuItem { Header = $"➤ {app.Name}" };
                var captured = app;
                item.Click += (_, _) => LaunchQuickApp(captured);
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
        }

        // 2) 管理台入口
        AddMenu(menu, "❝ 历史对话面板", () => OpenPanel(DashboardPage.ChatHistory));
        AddMenu(menu, "❀ 自动冒泡记录", () => OpenPanel(DashboardPage.BubbleHistory));
        AddMenu(menu, "⚙ 设定修改", () => OpenPanel(DashboardPage.Settings));
        AddMenu(menu, "⏰ 提醒事项", () => OpenPanel(DashboardPage.Reminders));
        AddMenu(menu, "♪ 报时设置", () => OpenPanel(DashboardPage.Chime));
        AddMenu(menu, "➤ 快捷启动设置", () => OpenPanel(DashboardPage.QuickApps));

        menu.Items.Add(new Separator());
        AddMenu(menu, "↻ 重置位置与置顶", ResetPosition);
        AddMenu(menu, "❖ 使用说明", () => OpenPanel(DashboardPage.Help));

        menu.Items.Add(new Separator());
        AddMenu(menu, "✖ 退出应用", Shutdown);

        menu.IsOpen = true;
    }

    private static void AddMenu(ContextMenu menu, string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private void LaunchQuickApp(QuickApp app)
    {
        if (ProcessLauncher.TryLaunchApp(app.Path, out var error))
        {
            ShowMessage("已经为你开启程序啦，指挥官~ ♪");
            ChangeState("wink");
            return;
        }

        AppLog.Warn($"快捷启动失败 {app.Name}: {error}");
        if (!File.Exists(app.Path))
        {
            ShowMessage("妖精小姐找不到这个文件了，路径是不是被移动了呀？");
            ChangeState("question");
        }
        else
        {
            ShowMessage($"唔……启动失败了呢：{error}");
            ChangeState("cry");
        }
    }

    // ==================================================================
    //  设置应用（管理台保存后回调）
    // ==================================================================

    /// <summary>把最新配置应用到运行中的桌宠上（层级、透明度、托盘、尺寸）。</summary>
    public void ApplyRuntimeSettings()
    {
        WindowLevelService.Apply(this, Config.Level);
        RefreshTray();

        _currentPetWidth = Config.PetWidth;
        ApplyPetScale();

        if (!Config.OpacityEnabled) ResetOpacity();
        UpdateLayoutForBubble(_bubbleVisible);

        ResetIdleCounters();
    }

    private void RefreshTray()
    {
        _tray ??= new TrayService(this);
        _tray.SetEnabled(Config.TrayIconEnabled);
    }
}
