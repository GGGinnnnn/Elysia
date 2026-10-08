using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ElysiaPet.Services;

namespace ElysiaPet;

/// <summary>
/// 图标字形诊断（命令行加 <c>--icontest</c>）。
/// 把候选图标逐字排版并离屏渲染，统计每个字形实际绘制出的颜色种类：
/// 只用一种颜色 = 单色字形（适合粉色主题）；出现多种颜色 = 彩色 emoji（会破坏主题）。
/// 结果写进 elysia.log，用于挑选真正可控的图标。
/// </summary>
internal static class IconTest
{
    /// <summary>候选图标：(显示名, 字符, 编码点描述)。</summary>
    private static readonly (string Name, string Glyph)[] Candidates =
    {
        ("气泡-会话", "\uE9D9"),          // 私用区？先试
        ("聊天", "\uD83D\uDCAC\uFE0E"),   // 💬 + 文本呈现
        ("聊天-裸", "\uD83D\uDCAC"),
        ("叶", "\uD83C\uDF43\uFE0E"),
        ("齿轮", "\u2699\uFE0E"),
        ("星", "\u2726"),
        ("四角星", "\u2727"),
        ("闪光", "\u2728\uFE0E"),
        ("花环", "\u2740"),
        ("花朵", "\u2741"),
        ("实心花", "\u274B"),
        ("樱瓣", "\u273F"),
        ("玫瑰", "\uD83C\uDF39\uFE0E"),
        ("铃", "\uD83D\uDD14\uFE0E"),
        ("钟", "\u23F0\uFE0E"),
        ("时钟", "\uD83D\uDD52\uFE0E"),
        ("沙漏", "\u231B\uFE0E"),
        ("心", "\u2665"),
        ("空心心", "\u2661"),
        ("音符", "\u266A"),
        ("双音符", "\u266B"),
        ("问号", "\uFF1F"),
        ("感叹", "\uFF01"),
        ("书", "\uD83D\uDCD6\uFE0E"),
        ("火箭", "\uD83D\uDE80\uFE0E"),
        ("文件夹", "\uD83D\uDCC2\uFE0E"),
        ("钢笔", "\u270F\uFE0E"),
        ("扫帚", "\uD83E\uDDF9\uFE0E"),
        ("回收", "\u267B\uFE0E"),
        ("勾", "\u2714\uFE0E"),
        ("叉", "\u2716\uFE0E"),
        ("加号", "\uFF0B"),
        ("减号", "\uFF0D"),
        ("上箭头", "\u2191"),
        ("下箭头", "\u2193"),
        ("刷新", "\u21BB"),
        ("骰子-星", "\u2735"),
        ("雪花", "\u2744\uFE0E"),
        ("圆点", "\u25CF"),
        ("小圆点", "\u2022"),
        ("缎带", "\uD83C\uDF80\uFE0E"),
        ("蝴蝶结-裸", "\uD83C\uDF80"),
        ("选项-方块", "\u25A0"),
        ("信息", "\u2139\uFE0E"),
        ("警告", "\u26A0\uFE0E"),
        ("目录", "\u2630"),
        ("铅笔-简", "\u270E"),
        ("剪刀", "\u2702\uFE0E"),
        ("钥匙", "\uD83D\uDD11\uFE0E"),
        ("音乐", "\uD83C\uDFB5\uFE0E"),
        ("相机", "\uD83D\uDCF7\uFE0E"),
        ("图标-项目", "\uD83D\uDD17\uFE0E"),
        ("地球", "\uD83C\uDF10\uFE0E"),
    };

    public static void Run()
    {
        AppLog.Info("======== 图标字形检测开始 ========");

        var mono = new List<string>();
        var color = new List<string>();

        foreach (var (name, glyph) in Candidates)
        {
            var (isMono, colors, rendered) = Analyze(glyph);
            var codepoints = Describe(glyph);
            var line = $"[图标] {name,-12} {codepoints,-22} 渲染={(rendered ? "有" : "无")} " +
                       $"颜色数={colors} => {(isMono ? "单色✓" : "彩色✗")}";

            AppLog.Info(line);

            if (!rendered) continue;
            if (isMono) mono.Add($"{name}({codepoints})");
            else color.Add(name);
        }

        AppLog.Info("======== 汇总 ========");
        AppLog.Info($"[图标] 单色可用 {mono.Count} 个: {string.Join("、", mono)}");
        AppLog.Info($"[图标] 彩色/不适用 {color.Count} 个: {string.Join("、", color)}");

        AppLog.Info("======== 图标字形检测结束 ========");
        AppLog.Shutdown();
        Application.Current.Shutdown(0);
    }

    /// <summary>离屏渲染单个字形，统计非透明像素里出现了多少种不同的 RGB 颜色。</summary>
    private static (bool IsMono, int ColorCount, bool Rendered) Analyze(string glyph)
    {
        const int size = 64;
        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, size, size));
            var text = new FormattedText(
                glyph,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI Emoji, Segoe UI Symbol, Segoe UI"),
                    FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                40,
                Brushes.Black,   // 故意用纯黑，彩色 emoji 会无视它
                96);
            dc.DrawText(text, new Point(4, 8));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var stride = size * 4;
        var pixels = new byte[stride * size];
        bitmap.CopyPixels(pixels, stride, 0);

        var colors = new HashSet<int>();
        var opaque = 0;

        for (var i = 0; i < pixels.Length; i += 4)
        {
            var a = pixels[i + 3];
            if (a < 24) continue;

            opaque++;
            var key = (pixels[i + 2] << 16) | (pixels[i + 1] << 8) | pixels[i];  // BGR -> RGB
            colors.Add(key);
            if (colors.Count > 12) break;   // 够多了，判定为彩色
        }

        var rendered = opaque > 6;
        return (!rendered || colors.Count <= 2, colors.Count, rendered);
    }

    private static string Describe(string glyph)
    {
        var sb = new StringBuilder();
        foreach (var ch in glyph)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append("U+").Append(((int)ch).ToString("X4"));
        }
        return sb.ToString();
    }
}
