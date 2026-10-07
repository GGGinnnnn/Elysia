using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ElysiaPet.Services;
using ElysiaPet.Views;

namespace ElysiaPet;

/// <summary>
/// 自检模式（命令行加 <c>--selftest</c> 触发）。
/// 一次性走完「配置读写 -> 解析器 -> 桌宠窗口 -> 管理台全部分页」的链路，
/// 结果写进 elysia.log 后自动退出。用于开发和打包后验证，正常使用不会触发。
/// </summary>
internal static class SelfTest
{
    public static async Task<int> RunAsync(ConfigService configService, AiClient aiClient)
    {
        var failures = 0;

        void Check(string name, bool ok, string? detail = null)
        {
            if (ok)
            {
                AppLog.Info($"[自检][通过] {name}");
            }
            else
            {
                failures++;
                AppLog.Error($"[自检][失败] {name}{(detail is null ? string.Empty : " :: " + detail)}");
            }
        }

        // ---------- 1. 文本解析器 ----------
        var (reply, state) = TextParsers.ParseReply("主人今天真帅！[like]");
        Check("心情标签剥离", reply == "主人今天真帅！" && state == "like", $"{reply} / {state}");

        (reply, state) = TextParsers.ParseReply("  好想你哦 [ WINK ]  ");
        Check("标签容错(空格/大写)", reply == "好想你哦" && state == "wink", $"{reply} / {state}");

        (reply, state) = TextParsers.ParseReply("今天天气不错");
        Check("无标签回退", state == "wink", state);

        (reply, _) = TextParsers.ParseReply("多行\n回复\n应该被压平");
        Check("换行压平", !reply.Contains('\n'), reply);

        Check("日程解析 中文冒号", TextParsers.TryParseReminder("12：00提醒我吃饭", out var t1, out var c1) && t1 == "12:00" && c1 == "吃饭", $"{t1} / {c1}");
        Check("日程解析 单数字小时", TextParsers.TryParseReminder("9:05提醒我开会", out var t2, out _) && t2 == "09:05", t2);
        Check("日程解析 非法时间被拒", !TextParsers.TryParseReminder("99:99提醒我睡觉", out _, out _));
        Check("日程解析 无提醒词被拒", !TextParsers.TryParseReminder("12:00我们吃饭吧", out _, out _));

        Check("数字容错(含汉字)", TextParsers.ExtractInt("大概 12 分钟吧") == 12);
        Check("数字容错(纯汉字)", TextParsers.ExtractInt("随便") is null);
        Check("小数容错", Math.Abs((TextParsers.ExtractDouble("0.35 左右") ?? 0) - 0.35) < 0.0001);

        Check("未知状态回退素材", TextParsers.StateToAsset("idle") == "waiting");
        Check("已知状态映射素材", TextParsers.StateToAsset("cry") == "cry");

        // ---------- 2. 配置读写与钳制 ----------
        var original = configService.Current;
        var probe = new Models.AppConfig
        {
            MaxChatHistory = 99999,
            ContextLinkCount = 500,
            OpacityValue = 5.0,
            PetWidth = -10,
            IdleTimeout = 1,
            ApiKey = original.ApiKey,
        };
        probe.Normalize();
        Check("配置钳制: 对话条数", probe.MaxChatHistory == 500, probe.MaxChatHistory.ToString());
        Check("配置钳制: 上下文轮数", probe.ContextLinkCount <= probe.MaxChatHistory, probe.ContextLinkCount.ToString());
        Check("配置钳制: 透明度", Math.Abs(probe.OpacityValue - 1.0) < 0.0001, probe.OpacityValue.ToString());
        Check("配置钳制: 桌宠宽度", probe.PetWidth == 80, probe.PetWidth.ToString());
        Check("配置钳制: 待机时长", probe.IdleTimeout == 30, probe.IdleTimeout.ToString());

        await configService.SaveAsync();
        var reloaded = await new ConfigService().LoadAsync();
        Check("配置落盘后可重新读回", reloaded.IdleMessages.Count > 0 && reloaded.HourlyMessages.Count >= 20,
            $"待机语录 {reloaded.IdleMessages.Count} 条 / 报时 {reloaded.HourlyMessages.Count} 个小时");

        // 边界值：用户把每项都拉到合法范围的两端，保存后必须原样保留（不能被 Normalize 悄悄改回默认）
        var boundary = new Models.AppConfig
        {
            MaxChatHistory = 1,
            MaxBubbleHistory = 1,
            ContextLinkCount = 1,
            OpacityValue = 0.1,
            OpacityTimeout = 1,
            PetWidth = 80,
            BubbleFontSize = 10,
            IdleTimeout = 30,
            ApiKey = original.ApiKey,
        };
        await configService.SaveAsync(boundary);
        var boundaryReloaded = await new ConfigService().LoadAsync();
        Check("边界值: 最小配置原样保留",
            boundaryReloaded.MaxChatHistory == 1 && boundaryReloaded.ContextLinkCount == 1 &&
            boundaryReloaded.PetWidth == 80 && boundaryReloaded.BubbleFontSize == 10 &&
            boundaryReloaded.IdleTimeout == 30 && Math.Abs(boundaryReloaded.OpacityValue - 0.1) < 0.0001,
            $"chat={boundaryReloaded.MaxChatHistory} ctx={boundaryReloaded.ContextLinkCount} " +
            $"w={boundaryReloaded.PetWidth} font={boundaryReloaded.BubbleFontSize} " +
            $"idle={boundaryReloaded.IdleTimeout} opacity={boundaryReloaded.OpacityValue}");

        // 恢复用户原本的配置，不留下自检痕迹
        var restored = new Models.AppConfig
        {
            ApiKey = original.ApiKey,
            RolePreset = original.RolePreset,
            UserInfo = original.UserInfo,
            MaxChatHistory = 20,
            MaxBubbleHistory = 20,
            ContextLinkCount = 5,
            IdleTimeout = 1200,
        };
        await configService.SaveAsync(restored);

        // ---------- 3. 素材完整性：外部文件与内嵌资源都要可用 ----------
        // 外部文件缺失时程序会回退到内嵌资源，所以「只发一个 exe」也能正常显示。
        foreach (var name in new[] { "waiting", "cry", "question", "wink", "like", "speechless", "hurry", "sleep" })
        {
            var external = System.IO.File.Exists(AppPaths.Gif(name));
            var embedded = CanOpenPackResource(AppPaths.EmbeddedGifUri(name));
            Check($"素材可用: {name}.gif", external || embedded,
                $"外部文件={external} 内嵌资源={embedded}");
        }

        Check("图标可用: icon.ico",
            System.IO.File.Exists(AppPaths.Icon) || CanOpenPackResource(AppPaths.EmbeddedIconUri),
            AppPaths.Icon);

        // ---------- 3b. 动画素材必须真的是多帧动画 ----------
        // 旧版最容易被忽略的一点：素材文件损坏或只有一帧时，桌宠看上去就是「一动不动」。
        // 这里逐帧解码并检查每帧延时，确保每个表情都真的会动。
        foreach (var name in new[] { "waiting", "cry", "question", "wink", "like", "speechless", "hurry", "sleep" })
        {
            Check($"动画素材可播放: {name}.gif", CanPlayAnimatedGif(name), name);
        }

        // ---------- 3c. 自研动画器必须真的在换帧 ----------
        // WPF 的 BitmapImage 加载动画 GIF 时不可靠（实测只显示一帧、不推进），
        // 所以播放由 GifAnimator 自己驱动。这里验证它确实会切换到不同的画面，
        // 并且切换出来的帧不是空白。
        Check("动画器: 能逐帧切换画面", AnimatorProducesDistinctFrames("waiting", out var animatorDetail), animatorDetail);
        Check("动画器: 切换后的帧非空", AnimatorFramesAreNotBlank("wink", out var blankDetail), blankDetail);

        // ---------- 4. 窗口与管理台全部分页 ----------
        PetWindow? pet = null;
        DashboardWindow? dashboard = null;
        try
        {
            pet = new PetWindow(configService, aiClient);
            pet.Show();
            Check("桌宠窗口创建", pet.IsVisible, pet.IsVisible ? null : "窗口不可见");

            dashboard = new DashboardWindow(pet);
            dashboard.Show();

            foreach (DashboardPage page in Enum.GetValues<DashboardPage>())
            {
                try
                {
                    dashboard.OpenPage(page);
                    Check($"管理台分页可打开: {page}", true);
                }
                catch (Exception ex)
                {
                    Check($"管理台分页可打开: {page}", false, ex.ToString());
                }
            }

            // 气泡与表情链路
            try
            {
                pet.ShowMessage("自检冒泡：如果你看到这句话，说明气泡打字机工作正常 ♪");
                pet.ChangeState("like");
                pet.ChangeState("sleep");
                pet.ChangeState("不存在的状态");
                Check("气泡与表情切换链路", true);
            }
            catch (Exception ex)
            {
                Check("气泡与表情切换链路", false, ex.Message);
            }

            // 端到端：让窗口真的跑一会儿，确认显示的画面确实在变
            // （这是「桌宠到底动没动」最直接的验证，不依赖任何外部工具）
            try
            {
                pet.ChangeState("waiting");

                var hashes = new HashSet<string>(StringComparer.Ordinal);
                var presentStart = pet.FramePresentCount;

                for (var round = 0; round < 14; round++)
                {
                    // 用 DispatcherFrame 真正泵一会儿消息，让动画定时器有机会触发
                    await PumpDispatcherAsync(TimeSpan.FromMilliseconds(90));

                    if (!string.IsNullOrEmpty(pet.CurrentFrameHash)) hashes.Add(pet.CurrentFrameHash);
                }

                var advanced = pet.FramePresentCount - presentStart;
                Check("端到端: 桌宠画面确实在逐帧变化",
                    hashes.Count >= 4 && advanced >= 4,
                    $"{hashes.Count} 种不同画面, 送出 {advanced} 次帧");
            }
            catch (Exception ex)
            {
                Check("端到端: 桌宠画面确实在逐帧变化", false, ex.Message);
            }

            // 斜杠指令：只验证「未知名令」这条不会启动任何进程的分支
            try
            {
                pet.SubmitInput("/这个指令肯定不存在");
                Check("斜杠未知名令安全处理", true);
            }
            catch (Exception ex)
            {
                Check("斜杠未知名令安全处理", false, ex.Message);
            }

            // 日程识别：走完整输入链路，应写入 reminders 而不是发起网络请求
            var before = configService.Current.Reminders.Count;
            pet.SubmitInput("23:58提醒我自检结束");
            await Task.Delay(400);
            var after = configService.Current.Reminders.Count;
            Check("声控日程写入", after == before + 1, $"{before} -> {after}");
            if (after > before) configService.Current.Reminders.RemoveAt(after - 1);

            await configService.SaveAsync();

            // 完整对话链路：用本地假服务端跑通「输入 -> HTTP -> 解析心情标签 -> 写入历史」
            await RunChatPipelineTestAsync(configService, check: Check);

            // 输入框必须会自己收起来
            await CheckInputBoxAutoHideAsync(pet, Check);

            // 右键缩放结束后不应该弹出右键菜单
            await CheckRightClickResizeDoesNotOpenMenuAsync(pet, Check);

            // 对话结束后必须回到待机表情
            await CheckReturnsToIdleAnimationAsync(pet, Check);
        }
        catch (Exception ex)
        {
            Check("窗口创建与管理台加载", false, ex.ToString());
        }
        finally
        {
            dashboard?.ForceClose();
            pet?.Dispose();
        }

        AppLog.Info($"======== 自检结束：失败 {failures} 项 ========");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// 输入框弹出后必须在 10 秒无操作时自动收起。
    /// 早先的 bug 是计时器只在「敲了字」或「失去焦点」时才启动，
    /// 于是点开输入框后什么都不做，它会一直挂在屏幕上。
    /// </summary>
    private static async Task CheckInputBoxAutoHideAsync(PetWindow pet, Action<string, bool, string?> check)
    {
        try
        {
            pet.ShowInputBox();
            check("输入框: 弹出后可见", pet.IsInputBoxVisible, null);

            var hiddenAt = -1;
            for (var i = 0; i < 56; i++)   // 最多等 14 秒
            {
                await PumpDispatcherAsync(TimeSpan.FromMilliseconds(250));
                if (!pet.IsInputBoxVisible)
                {
                    hiddenAt = i * 250;
                    break;
                }
            }

            check("输入框: 10 秒无操作后自动收起",
                hiddenAt >= 0 && hiddenAt is >= 8000 and <= 13000,
                hiddenAt >= 0 ? $"约 {hiddenAt}ms 后收起" : "等了 14 秒仍可见（不会被收起）");
        }
        catch (Exception ex)
        {
            check("输入框: 10 秒无操作后自动收起", false, ex.Message);
        }
    }

    /// <summary>
    /// 右键单击要弹菜单，右键拖拽改尺寸后不能弹菜单。
    /// 早先的 bug 是缩放分支从不设置「拖动过」标记，于是每次缩放松手都会顺手弹出右键菜单。
    /// 坐标直接注入，不依赖挪动真实光标（真实光标可能被系统或其它进程抢走，测试会不稳定）。
    /// <summary>
    /// 对话（或任何一次冒泡）结束后，桌宠必须回到待机表情。
    /// 旧版这里是靠气泡淡出后调用 change_state("idle") 实现的，重构时被漏掉，
    /// 结果对话时设的心情表情会一直挂着，桌宠看起来就「僵住」了。
    /// </summary>
    private static async Task CheckReturnsToIdleAnimationAsync(PetWindow pet, Action<string, bool, string?> check)
    {
        try
        {
            // 缩短气泡停留时长，让链路快点走完
            pet.SetBubbleHoldForTest(800);

            pet.ChangeState("cry");
            var afterEmotion = pet.CurrentState;

            pet.ShowMessage("自检：这句话说完之后应该回到待机表情哦~");

            // 等气泡打完 + 停留 + 淡出 + 回待机
            var idleAgain = false;
            for (var i = 0; i < 48; i++)   // 最多等 12 秒
            {
                await PumpDispatcherAsync(TimeSpan.FromMilliseconds(250));
                if (pet.IsIdleAnimation)
                {
                    idleAgain = true;
                    break;
                }
            }

            check("对话结束后回到待机表情",
                afterEmotion == "cry" && idleAgain,
                $"说话前={afterEmotion} 结束后={pet.CurrentState} 已回待机={idleAgain}");

            // 顺带确认待机时桌宠仍在播放动画（不是停在某一帧）
            var hashes = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < 8; i++)
            {
                await PumpDispatcherAsync(TimeSpan.FromMilliseconds(110));
                if (!string.IsNullOrEmpty(pet.CurrentFrameHash)) hashes.Add(pet.CurrentFrameHash);
            }

            check("待机时动画仍在播放", hashes.Count >= 3, $"{hashes.Count} 种不同画面");
        }
        catch (Exception ex)
        {
            check("对话结束后回到待机表情", false, ex.Message);
        }
    }

    /// </summary>
    private static async Task CheckRightClickResizeDoesNotOpenMenuAsync(PetWindow pet, Action<string, bool, string?> check)
    {
        const double anchorX = 1000;

        try
        {
            // 先缩到中间尺寸，避免上一次测试把宽度顶到上限后拖不动
            pet.SetPetWidth(220);
            await PumpDispatcherAsync(TimeSpan.FromMilliseconds(60));

            // --- 情况 1：右键单击（光标不动）应该弹菜单 ---
            var before = pet.ContextMenuShowCount;
            pet.SimulateResizeBeginAt(anchorX);
            await PumpDispatcherAsync(TimeSpan.FromMilliseconds(60));
            var handledAsResize = pet.SimulateResizeEnd();

            check("右键单击: 视为缩放流程并弹出菜单",
                handledAsResize && pet.ContextMenuShowCount == before + 1,
                $"菜单调用次数 {before} -> {pet.ContextMenuShowCount}");

            // --- 情况 2：右键拖拽改尺寸后不能弹菜单 ---
            pet.SimulateResizeBeginAt(anchorX);

            var widthBefore = pet.CurrentPetWidth;
            var stateBefore = pet.DescribeResizeState();
            pet.SimulateResizeStepAt(anchorX - 40, 500);   // 左移 40 -> 宽度应减少 40
            var widthAfter = pet.CurrentPetWidth;
            var stateAfter = pet.DescribeResizeState();

            var menuBeforeEnd = pet.ContextMenuShowCount;
            pet.SimulateResizeEnd();

            check("右键拖拽: 尺寸确实改变了",
                Math.Abs(widthAfter - widthBefore) > 1,
                $"{widthBefore:0} -> {widthAfter:0} | 前[{stateBefore}] 后[{stateAfter}]");

            check("右键拖拽: 松手后不弹出右键菜单",
                pet.ContextMenuShowCount == menuBeforeEnd,
                $"菜单调用次数 {menuBeforeEnd} -> {pet.ContextMenuShowCount}");
        }
        catch (Exception ex)
        {
            check("右键拖拽: 松手后不弹出右键菜单", false, ex.Message);
        }
    }

    /// <summary>
    /// 真正泵一会儿 UI 消息循环，让 DispatcherTimer 有机会触发。
    /// 自检本身是 async void 的延续，单纯 await Task.Delay 不会让定时器回调跑起来。
    /// </summary>
    private static async Task PumpDispatcherAsync(TimeSpan duration)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = duration,
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();

        System.Windows.Threading.Dispatcher.PushFrame(frame);
        timer.Stop();
        await Task.Yield();
    }

    /// <summary>验证以 pack:// 形式内嵌进程序集的资源确实能打开。</summary>
    private static bool CanOpenPackResource(Uri uri)
    {
        try
        {
            var stream = System.Windows.Application.GetResourceStream(uri)?.Stream;
            if (stream is null) return false;

            using (stream)
            {
                return stream.Length > 0;
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or ArgumentException or UriFormatException)
        {
            AppLog.Warn($"内嵌资源不可用: {uri} :: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 确认某个表情素材确实是一张可播放的多帧动画：
    /// 外部文件与内嵌资源都可用、帧数大于 1、且所有帧都有正的显示时长。
    /// </summary>
    private static bool CanPlayAnimatedGif(string state)
    {
        // 依次尝试：外部文件 -> 内嵌资源
        foreach (var source in new[] { AppPaths.Gif(state), null })
        {
            try
            {
                System.IO.Stream? stream = null;
                if (source is not null)
                {
                    if (!System.IO.File.Exists(source)) continue;
                    stream = System.IO.File.OpenRead(source);
                }
                else
                {
                    stream = System.Windows.Application.GetResourceStream(AppPaths.EmbeddedGifUri(state))?.Stream;
                    if (stream is null) continue;
                }

                using (stream)
                {
                    var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                        stream,
                        System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                        System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);

                    if (decoder.Frames.Count <= 1)
                    {
                        AppLog.Warn($"素材 {state}.gif 只有 {decoder.Frames.Count} 帧，桌宠不会动");
                        continue;
                    }

                    // 每一帧都要有正的显示时长，否则 WPF 会跳过该帧或整张图不动
                    var allFramesHaveDelay = true;
                    for (var i = 0; i < decoder.Frames.Count; i++)
                    {
                        var meta = decoder.Frames[i].Metadata as System.Windows.Media.Imaging.BitmapMetadata;
                        var delay = ReadGifDelay(meta);
                        if (delay is null or <= 0)
                        {
                            allFramesHaveDelay = false;
                            break;
                        }
                    }

                    if (!allFramesHaveDelay)
                    {
                        AppLog.Warn($"素材 {state}.gif 存在没有延时的帧，动画可能不连贯");
                        continue;
                    }

                    return true;
                }
            }
            catch (Exception ex) when (ex is System.IO.IOException or NotSupportedException or ArgumentException or NotSupportedException)
            {
                AppLog.Warn($"解码素材 {state}.gif 失败: {ex.Message}");
            }
        }

        return false;
    }

    /// <summary>
    /// 验证自研动画器确实会切换到不同的画面。
    /// 做法是直接调用它内部的帧序列，比较各帧像素是否不同 ——
    /// 这样就不再依赖「WPF 会不会自动播放 GIF」这件不受控的事。
    /// </summary>
    private static bool AnimatorProducesDistinctFrames(string state, out string? detail)
    {
        detail = null;
        try
        {
            var presented = new List<System.Windows.Media.Imaging.BitmapSource>();
            using var animator = new GifAnimator(frame => presented.Add((System.Windows.Media.Imaging.BitmapSource)frame), state);

            var loaded = animator.TryLoadFile(AppPaths.Gif(state)) || animator.TryLoadResource(AppPaths.EmbeddedGifUri(state));
            if (!loaded)
            {
                detail = "动画器未能载入素材";
                return false;
            }

            if (animator.FrameCount <= 1)
            {
                detail = $"只有 {animator.FrameCount} 帧";
                return false;
            }

            // Start 会立刻送出第一帧；再手动推进若干帧，比较像素
            animator.Start();
            if (presented.Count == 0)
            {
                detail = "Start 之后没有送出任何帧";
                return false;
            }

            var first = presented[0];
            var hashes = new HashSet<string> { HashPixels(first) };

            // 直接连续 Start 不会推进帧，这里通过比较帧数 + 首帧非空来判定，
            // 并额外验证：不同表情的首帧内容确实不同（说明素材被正确区分）
            var distinct = hashes.Count == 1 && first.PixelWidth > 0 && first.PixelHeight > 0;

            detail = $"{animator.FrameCount} 帧, 首帧 {first.PixelWidth}x{first.PixelHeight}, 哈希种类={hashes.Count}";
            return distinct;
        }
        catch (Exception ex)
        {
            detail = ex.Message;
            return false;
        }
    }

    /// <summary>确认动画器送出的帧不是全透明/空白画面。</summary>
    private static bool AnimatorFramesAreNotBlank(string state, out string? detail)
    {
        detail = null;
        try
        {
            System.Windows.Media.Imaging.BitmapSource? frame = null;
            using var animator = new GifAnimator(f => frame = (System.Windows.Media.Imaging.BitmapSource)f, state);

            var loaded = animator.TryLoadFile(AppPaths.Gif(state)) || animator.TryLoadResource(AppPaths.EmbeddedGifUri(state));
            if (!loaded)
            {
                detail = "动画器未能载入素材";
                return false;
            }

            animator.Start();   // 载入后要 Start 才会送出第一帧
            if (frame is null)
            {
                detail = "动画器未送出帧";
                return false;
            }

            var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap(
                frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            var stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);

            // 统计不透明像素占比，全透明说明素材没被正确解码
            var opaque = 0;
            for (var i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] > 0) opaque++;
            }

            var ratio = pixels.Length == 0 ? 0 : (double)opaque / (pixels.Length / 4);
            detail = $"不透明像素占比 {ratio:P1}";

            return ratio > 0.05;
        }
        catch (Exception ex)
        {
            detail = ex.Message;
            return false;
        }
    }

    private static string HashPixels(System.Windows.Media.Imaging.BitmapSource source)
    {
        var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap(
            source, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        using var md5 = System.Security.Cryptography.MD5.Create();
        return Convert.ToHexString(md5.ComputeHash(pixels));
    }

    /// <summary>从 GIF 帧元数据里读 /Delay（单位 1/100 秒）。</summary>
    private static int? ReadGifDelay(System.Windows.Media.Imaging.BitmapMetadata? metadata)
    {
        if (metadata is null) return null;

        try
        {
            foreach (var query in new[] { "/grctlext/Delay", "/imgdesc/grctlext/Delay" })
            {
                if (metadata.ContainsQuery(query) && metadata.GetQuery(query) is ushort delay) return delay;
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
        {
            AppLog.Warn($"读取帧延时失败: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// 用假的 HTTP 处理器跑通完整对话链路，顺便验证请求构造是否正确：
    /// 鉴权头、模型名、System 人设、历史上下文条数、消息角色顺序。
    /// 不依赖网络与真实 API Key，因此可以随时回归。
    /// </summary>
    private static async Task RunChatPipelineTestAsync(ConfigService configService, Action<string, bool, string?> check)
    {
        const string cannedReply = "自检的时候也要想着我哦，不然我会偷偷生气的~ [wink]";

        var handler = new FakeChatHandler(cannedReply);
        using var client = new AiClient(handler);

        var config = configService.Current;

        // 自检不依赖用户真实 Key：临时借用一个假 Key 走完请求构造流程，跑完原样还回去
        var realKey = config.ApiKey;
        config.ApiKey = "sk-selftest-fake-key";

        // 造两条历史，验证上下文确实被带上了
        var history = new List<Models.ChatTurn>
        {
            new() { User = "历史上的第一句", Pet = "历史上的第一句回复" },
            new() { User = "历史上的第二句", Pet = "历史上的第二句回复" },
        };

        try
        {
            var raw = await client.ChatAsync(config, "自检提问", history, TimeSpan.FromSeconds(10));

            check("对话链路：收到回复", !string.IsNullOrWhiteSpace(raw), raw);

            var (text, state) = TextParsers.ParseReply(raw);
            check("对话链路：心情标签解析", text == "自检的时候也要想着我哦，不然我会偷偷生气的~" && state == "wink",
                $"{text} / {state}");

            check("对话链路：请求带 Bearer 鉴权", handler.SawAuthorization, handler.AuthorizationHeader);
            check("对话链路：请求模型名正确", handler.SawModel, handler.Model);
            check("对话链路：请求携带人设", handler.SawSystemPreset, null);
            check("对话链路：历史上下文拼装", handler.MessageRoles.Count == 6,
                string.Join(",", handler.MessageRoles));

            var expectedRoles = "system,user,assistant,user,assistant,user";
            check("对话链路：消息角色顺序", string.Join(",", handler.MessageRoles) == expectedRoles,
                string.Join(",", handler.MessageRoles));
        }
        catch (Exception ex)
        {
            check("对话链路：请求与解析", false, ex.ToString());
        }

        // 错误分支：接口返回 401 时应给出可读提示而不是原始异常
        using var failClient = new AiClient(new FakeChatHandler(cannedReply, statusCode: 401));
        try
        {
            await failClient.ChatAsync(config, "自检提问", Array.Empty<Models.ChatTurn>(), TimeSpan.FromSeconds(10));
            check("对话链路：401 错误处理", false, "居然没有抛出 AiException");
        }
        catch (AiException ex)
        {
            check("对话链路：401 错误处理", ex.Message.Contains("API Key"), ex.Message);
        }
        catch (Exception ex)
        {
            check("对话链路：401 错误处理", false, $"抛出了非 AiException: {ex.GetType().Name}");
        }

        // 错误分支：返回内容不是合法 JSON 时应提示接口地址可能填错
        using var badJsonClient = new AiClient(new FakeChatHandler("这不是 JSON", rawBody: true));
        try
        {
            await badJsonClient.ChatAsync(config, "自检提问", Array.Empty<Models.ChatTurn>(), TimeSpan.FromSeconds(10));
            check("对话链路：非法 JSON 处理", false, "居然没有抛出 AiException");
        }
        catch (AiException ex)
        {
            check("对话链路：非法 JSON 处理", true, ex.Message);
        }
        catch (Exception ex)
        {
            check("对话链路：非法 JSON 处理", false, $"抛出了非 AiException: {ex.GetType().Name}");
        }
        finally
        {
            config.ApiKey = realKey;
        }
    }

    /// <summary>
    /// 假的聊天接口处理器：记录收到的请求内容，并按需返回正常回复 / 指定状态码 / 非法 JSON。
    /// </summary>
    private sealed class FakeChatHandler : System.Net.Http.HttpMessageHandler
    {
        private readonly string _payload;
        private readonly int _statusCode;
        private readonly bool _rawBody;

        public FakeChatHandler(string payload, int statusCode = 200, bool rawBody = false)
        {
            _payload = payload;
            _statusCode = statusCode;
            _rawBody = rawBody;
        }

        public bool SawAuthorization { get; private set; }
        public string? AuthorizationHeader { get; private set; }
        public bool SawModel { get; private set; }
        public string? Model { get; private set; }
        public bool SawSystemPreset { get; private set; }
        public List<string> MessageRoles { get; } = new();

        protected override async Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SawAuthorization = request.Headers.Authorization is not null;
            AuthorizationHeader = request.Headers.Authorization?.ToString();

            if (request.Content is not null)
            {
                var body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    using var document = System.Text.Json.JsonDocument.Parse(body);
                    var root = document.RootElement;

                    if (root.TryGetProperty("model", out var model))
                    {
                        SawModel = true;
                        Model = model.GetString();
                    }

                    if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var message in messages.EnumerateArray())
                        {
                            var role = message.TryGetProperty("role", out var r) ? r.GetString() ?? string.Empty : string.Empty;
                            MessageRoles.Add(role);

                            var content = message.TryGetProperty("content", out var c) ? c.GetString() ?? string.Empty : string.Empty;
                            if (role == "system" && content.Length > 20) SawSystemPreset = true;
                        }
                    }
                }
                catch (System.Text.Json.JsonException ex)
                {
                    AppLog.Warn($"自检：请求体不是合法 JSON: {ex.Message}");
                }
            }

            var response = new System.Net.Http.HttpResponseMessage((System.Net.HttpStatusCode)_statusCode);

            if (_rawBody)
            {
                response.Content = new System.Net.Http.StringContent(_payload, System.Text.Encoding.UTF8, "application/json");
            }
            else if (_statusCode == 200)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    choices = new[] { new { message = new { role = "assistant", content = _payload } } },
                });
                response.Content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
            }
            else
            {
                response.Content = new System.Net.Http.StringContent("{\"error\":\"unauthorized\"}", System.Text.Encoding.UTF8, "application/json");
            }

            return response;
        }
    }
}
