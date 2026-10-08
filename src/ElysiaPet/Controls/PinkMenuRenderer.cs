using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ElysiaPet.Controls;

/// <summary>
/// 托盘的粉色菜单渲染器。
///
/// 为什么不用 WPF 的 ContextMenu：托盘图标用的是 WinForms 的 NotifyIcon，
/// 它的右键菜单必须是 ToolStripDropDown 体系；弹窗的窗口形状由系统绘制，
/// 第三方无法改成圆角，所以这里退一步把【条目配色与高亮】全部自绘成粉色圆角，
/// 视觉上和后台的粉色菜单保持一致。
/// </summary>
internal sealed class PinkMenuRenderer : ToolStripProfessionalRenderer
{
    private static readonly Color Accent = Color.FromArgb(0xE8, 0x60, 0x8C);
    private static readonly Color AccentSoft = Color.FromArgb(0xFF, 0xE7, 0xF0);
    private static readonly Color PanelBack = Color.FromArgb(0xFF, 0xFB, 0xFD);
    private static readonly Color BorderColor = Color.FromArgb(0xE7, 0xA9, 0xC4);
    private static readonly Color TextColor = Color.FromArgb(0x4A, 0x2C, 0x3C);
    private static readonly Color MutedText = Color.FromArgb(0xA8, 0x79, 0x92);
    private static readonly Color SeparatorColor = Color.FromArgb(0xF6, 0xD5, 0xE2);

    public PinkMenuRenderer() : base(new PinkColorTable())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(PanelBack);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        // 画一圈玫瑰金描边，代替系统默认的灰边
        using var pen = new Pen(BorderColor, 1.4f);
        var rect = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        e.Graphics.DrawRectangle(pen, rect);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var item = e.Item;
        if (!item.Selected || !item.Enabled) return;

        // 悬停/选中项：粉色圆角高亮
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(3, 1, e.Item.Width - 6, e.Item.Height - 2);

        using var path = RoundedRect(rect, 8);
        using var brush = new SolidBrush(AccentSoft);
        e.Graphics.FillPath(brush, path);
        using var pen = new Pen(Color.FromArgb(0x90, Accent), 1f);
        e.Graphics.DrawPath(pen, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? TextColor : MutedText;
        e.TextFont = new Font("幼圆", 10.5f, FontStyle.Regular, GraphicsUnit.Point);
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(SeparatorColor, 1f);
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 12, y, e.Item.Width - 12, y);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        // 不画左侧图标栏的背景，保持整块干净
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();

        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }

    /// <summary>粉色配色表：让系统绘制的边角余量也用浅粉，避免出现灰色块。</summary>
    private sealed class PinkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => PanelBack;
        public override Color MenuBorder => BorderColor;
        public override Color MenuItemBorder => Accent;
        public override Color MenuItemSelected => AccentSoft;
        public override Color MenuItemSelectedGradientBegin => AccentSoft;
        public override Color MenuItemSelectedGradientEnd => AccentSoft;
        public override Color MenuItemPressedGradientBegin => AccentSoft;
        public override Color MenuItemPressedGradientEnd => AccentSoft;
        public override Color ImageMarginGradientBegin => PanelBack;
        public override Color ImageMarginGradientMiddle => PanelBack;
        public override Color ImageMarginGradientEnd => PanelBack;
        public override Color SeparatorDark => SeparatorColor;
        public override Color SeparatorLight => Color.White;
    }
}
