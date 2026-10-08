using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ElysiaPet.Controls;

/// <summary>
/// 粉色浪漫氛围层：飘落的花瓣 + 缓缓流动的柔光丝带。
///
/// 作为覆盖层使用（放在内容之上的 Grid 层里，IsHitTestVisible 默认关闭，
/// 不会挡住按钮点击）。花瓣与丝带全部用形状 + 属性动画实现，
/// 不依赖任何图片素材，因此单文件 exe 也不会额外变大。
/// </summary>
public sealed class PetalsOverlay : Canvas
{
    private static readonly Random Rng = new();

    private static readonly Color[] PetalColors =
    {
        Color.FromRgb(0xFF, 0xB3, 0xC9),   // 浅粉
        Color.FromRgb(0xF7, 0x8F, 0xB4),   // 玫瑰粉
        Color.FromRgb(0xFF, 0xD6, 0xE3),   // 樱粉
        Color.FromRgb(0xE9, 0x8A, 0xC0),   // 品红
        Color.FromRgb(0xFF, 0xC8, 0xD8),   // 蜜桃粉
    };

    private readonly List<Shape> _petals = new();
    private PetalMotion[] _motions = Array.Empty<PetalMotion>();
    private readonly DispatcherTimer _respawnTimer;
    private readonly int _petalCount;
    private bool _ribbonsBuilt;

    /// <param name="petalCount">花瓣数量。后台面板用 26 左右，桌宠窗口用 8 左右即可。</param>
    /// <param name="showRibbons">是否绘制流动的柔光丝带（昔涟那种光带感）。</param>
    public PetalsOverlay(int petalCount = 24, bool showRibbons = true)
    {
        _petalCount = Math.Max(0, petalCount);
        IsHitTestVisible = false;
        ClipToBounds = true;
        Background = Brushes.Transparent;

        if (showRibbons) BuildRibbons();

        for (var i = 0; i < _petalCount; i++) _petals.Add(CreatePetal());

        // 尺寸就绪后再布局花瓣：此时才知道 ActualWidth/Height
        Loaded += (_, _) =>
        {
            _motions = new PetalMotion[_petals.Count];
            for (var i = 0; i < _petals.Count; i++)
            {
                _motions[i] = new PetalMotion();
                PlacePetal(i, initial: true);
            }
        };

        // 兜底：万一动画被系统暂停，定时器负责把跑出画面的花瓣重新放回顶部
        _respawnTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _respawnTimer.Tick += (_, _) => RespawnEscapedPetals();
        _respawnTimer.Start();
        Unloaded += (_, _) => _respawnTimer.Stop();
    }

    // ------------------------------------------------------------------
    //  花瓣
    // ------------------------------------------------------------------

    private static Shape CreatePetal()
    {
        var color = PetalColors[Rng.Next(PetalColors.Length)];

        // 用「上尖下圆」的水滴形状近似花瓣
        var petal = new Path
        {
            Data = Geometry.Parse("M 0,-8 C 6,-4 6,4 0,8 C -6,4 -6,-4 0,-8 Z"),
            Fill = new SolidColorBrush(color) { Opacity = 0.95 },
            Stroke = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 0.7,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };

        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(1, 1));
        group.Children.Add(new RotateTransform(0));
        petal.RenderTransform = group;

        return petal;
    }

    private void PlacePetal(int index, bool initial)
    {
        if (_petals.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0) return;

        var petal = _petals[index];
        var motion = _motions[index];

        motion.Size = 8 + Rng.NextDouble() * 14;                // 花瓣尺寸
        motion.SwayWidth = 40 + Rng.NextDouble() * 110;         // 左右摆动幅度
        motion.FallSeconds = 10 + Rng.NextDouble() * 11;        // 下落耗时
        motion.SpinSeconds = 4 + Rng.NextDouble() * 7;
        motion.DriftSeconds = 3 + Rng.NextDouble() * 4;

        var x = Rng.NextDouble() * ActualWidth;
        var startY = initial ? Rng.NextDouble() * ActualHeight : -30;
        motion.StartX = x;
        motion.EndX = Math.Max(-20, Math.Min(ActualWidth + 20, x + motion.SwayWidth * (Rng.NextDouble() < 0.5 ? -1 : 1)));

        if (!Children.Contains(petal)) Children.Add(petal);

        Canvas.SetLeft(petal, x);
        Canvas.SetTop(petal, startY);

        var scale = (ScaleTransform)((TransformGroup)petal.RenderTransform).Children[0];
        scale.ScaleX = motion.Size / 12.0;
        scale.ScaleY = motion.Size / 12.0;

        Animate(petal, motion, startY);
    }

    private void Animate(Shape petal, PetalMotion motion, double startY)
    {
        var travel = Math.Max(1, ActualHeight + 80 - startY);

        // 垂直下落
        var fall = new DoubleAnimation
        {
            From = startY,
            To = ActualHeight + 60,
            Duration = TimeSpan.FromSeconds(motion.FallSeconds),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        petal.BeginAnimation(TopProperty, fall);

        // 左右摆动（让它像被风吹着飘）
        var drift = new DoubleAnimation
        {
            From = motion.StartX,
            To = motion.EndX,
            Duration = TimeSpan.FromSeconds(motion.DriftSeconds),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        petal.BeginAnimation(LeftProperty, drift);

        // 自旋
        var spin = new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = TimeSpan.FromSeconds(motion.SpinSeconds),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        ((TransformGroup)petal.RenderTransform).Children[1].BeginAnimation(RotateTransform.AngleProperty, spin);

        // 明暗呼吸，避免整体太呆板
        var glow = new DoubleAnimation
        {
            From = 0.7,
            To = 1.0,
            Duration = TimeSpan.FromSeconds(1.6 + Rng.NextDouble() * 2),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        petal.BeginAnimation(OpacityProperty, glow);

        _ = travel;
    }

    /// <summary>把因为窗口尺寸变化而跑偏的花瓣放回顶部。</summary>
    private void RespawnEscapedPetals()
    {
        if (ActualHeight <= 0 || ActualWidth <= 0) return;

        for (var i = 0; i < _petals.Count; i++)
        {
            var petal = _petals[i];
            var top = GetTop(petal);
            var left = GetLeft(petal);

            var escaped = double.IsNaN(top) || double.IsNaN(left) ||
                          top > ActualHeight + 30 || left < -60 || left > ActualWidth + 60;

            if (escaped) PlacePetal(i, initial: false);
        }
    }

    /// <summary>窗口尺寸变化后重新分布花瓣，避免挤在一角。</summary>
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);

        if (sizeInfo.NewSize.Width <= 0 || sizeInfo.NewSize.Height <= 0) return;

        for (var i = 0; i < _petals.Count && i < _motions.Length; i++)
        {
            var petal = _petals[i];
            if (!Children.Contains(petal)) continue;

            // 只把横向坐标重新随机到新宽度内，纵向动画继续
            var x = Rng.NextDouble() * sizeInfo.NewSize.Width;
            _motions[i].StartX = x;
            _motions[i].EndX = Math.Max(-20, Math.Min(sizeInfo.NewSize.Width + 20, x + _motions[i].SwayWidth * (Rng.NextDouble() < 0.5 ? -1 : 1)));
            Canvas.SetLeft(petal, x);
        }
    }

    // ------------------------------------------------------------------
    //  柔光丝带
    // ------------------------------------------------------------------

    private void BuildRibbons()
    {
        if (_ribbonsBuilt) return;
        _ribbonsBuilt = true;

        Loaded += (_, _) =>
        {
            for (var i = 0; i < 2; i++) Children.Insert(0, CreateRibbon(i));
        };
    }

    private UIElement CreateRibbon(int index)
    {
        var duration = 9 + index * 3;
        var top = index == 0 ? 0.22 : 0.68;

        var ribbon = new Path
        {
            Data = Geometry.Parse(
                "M -0.15,0.5 C 0.15,0.05 0.35,0.95 0.6,0.5 C 0.78,0.2 0.9,0.7 1.15,0.45"),
            Stretch = Stretch.Fill,
            StrokeThickness = index == 0 ? 46 : 30,
            Stroke = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0x00, 0xFF, 0xC2, 0xD6), 0.0),
                    new GradientStop(Color.FromArgb(0x55, 0xFF, 0xB3, 0xC9), 0.35),
                    new GradientStop(Color.FromArgb(0x66, 0xE9, 0x8A, 0xC0), 0.6),
                    new GradientStop(Color.FromArgb(0x00, 0xFF, 0xC2, 0xD6), 1.0),
                },
            },
            Opacity = 0.5,
            IsHitTestVisible = false,
            Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 26 },
            RenderTransform = new TranslateTransform(),
        };

        // 让形状铺满整个面板高度，再横向缓慢平移，形成「光带流动」的观感
        ribbon.Width = double.NaN;
        ribbon.Height = double.NaN;
        Canvas.SetLeft(ribbon, -300);
        Canvas.SetTop(ribbon, 0);

        var translate = (TranslateTransform)ribbon.RenderTransform;
        translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
        {
            From = -260,
            To = 320,
            Duration = TimeSpan.FromSeconds(duration),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });

        ribbon.BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            From = 0.25,
            To = 0.6,
            Duration = TimeSpan.FromSeconds(4 + index),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });

        // 纵向随面板高度铺开
        Loaded += (_, _) =>
        {
            ribbon.Height = Math.Max(60, ActualHeight * 0.5);
            ribbon.Width = Math.Max(200, ActualWidth * 1.4);
            Canvas.SetTop(ribbon, ActualHeight * top);
        };

        return ribbon;
    }

    private sealed class PetalMotion
    {
        public double Size;
        public double SwayWidth;
        public double FallSeconds;
        public double SpinSeconds;
        public double DriftSeconds;
        public double StartX;
        public double EndX;
    }
}
