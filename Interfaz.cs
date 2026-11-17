using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LooperMaster
{
    // A small theme on top of the native controls: keyboard support and accessibility
    // stay in WinForms, and the application still ships as one executable.
    internal static class UiColors
    {
        public static readonly Color Background = Color.FromArgb(17, 20, 25);
        public static readonly Color Panel = Color.FromArgb(24, 28, 34);
        public static readonly Color Surface = Color.FromArgb(32, 37, 45);
        public static readonly Color Input = Color.FromArgb(20, 24, 30);
        public static readonly Color Border = Color.FromArgb(49, 57, 67);
        public static readonly Color Text = Color.FromArgb(237, 241, 245);
        public static readonly Color Muted = Color.FromArgb(157, 169, 184);
        public static readonly Color Disabled = Color.FromArgb(112, 124, 140);
        public static readonly Color Accent = Color.FromArgb(133, 228, 185);
        public static readonly Color AccentSurface = Color.FromArgb(35, 62, 53);
    }

    internal static class UiPaint
    {
        public static GraphicsPath Rounded(RectangleF bounds, float radius)
        {
            float diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            GraphicsPath path = new GraphicsPath();
            if (diameter <= 0) { path.AddRectangle(bounds); return path; }
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void Surface(Graphics graphics, Rectangle bounds, Color fill, Color border, float radius)
        {
            if (bounds.Width < 2 || bounds.Height < 2) return;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float unit = graphics.DpiX / 96F;
            using (GraphicsPath path = Rounded(new RectangleF(bounds.X + unit / 2, bounds.Y + unit / 2,
                bounds.Width - unit, bounds.Height - unit), radius * unit))
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen pen = new Pen(border, unit))
            {
                graphics.FillPath(brush, path);
                if (border != fill) graphics.DrawPath(pen, path);
            }
        }

        public static void Chevron(Graphics graphics, Color color, float x, float y, float size, bool up)
        {
            using (Pen pen = new Pen(color, Math.Max(1, graphics.DpiX / 96F)))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                float direction = up ? -1 : 1;
                graphics.DrawLines(pen, new[] { new PointF(x - size, y - direction * size / 2),
                    new PointF(x, y + direction * size / 2), new PointF(x + size, y - direction * size / 2) });
            }
        }

        public static void Loop(Graphics graphics, RectangleF bounds, Color color)
        {
            GraphicsState saved = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TranslateTransform(bounds.Left, bounds.Top);
            graphics.ScaleTransform(bounds.Width / 48F, bounds.Height / 28F);
            using (GraphicsPath path = new GraphicsPath())
            using (Pen pen = new Pen(color, 3.5F))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                path.AddBezier(24, 14, 14, -3, 1, 0, 2, 14);
                path.AddBezier(2, 14, 3, 29, 15, 30, 24, 14);
                path.AddBezier(24, 14, 34, -3, 47, 0, 46, 14);
                path.AddBezier(46, 14, 45, 29, 33, 30, 24, 14);
                graphics.DrawPath(pen, path);
            }
            graphics.Restore(saved);
        }
    }

    internal class SurfacePanel : Panel
    {
        public Color EdgeColor = UiColors.Border;
        public int CornerRadius = 10;
        public SurfacePanel() { DoubleBuffered = true; BackColor = UiColors.Panel; }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? UiColors.Background : Parent.BackColor);
            UiPaint.Surface(e.Graphics, ClientRectangle, BackColor, EdgeColor, CornerRadius);
        }
    }

    internal sealed class FieldFrame : SurfacePanel
    {
        public FieldFrame() { BackColor = UiColors.Input; CornerRadius = 6; }
        protected override void OnEnter(EventArgs e) { base.OnEnter(e); Invalidate(); }
        protected override void OnLeave(EventArgs e) { base.OnLeave(e); Invalidate(); }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            EdgeColor = ContainsFocus ? UiColors.Accent : UiColors.Border;
            base.OnPaintBackground(e);
        }
    }

    internal sealed class DropZone : SurfacePanel
    {
        public bool Highlighted;
        public bool HasFile;
        public DropZone() { BackColor = UiColors.Panel; }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            EdgeColor = Highlighted ? UiColors.Accent : UiColors.Border;
            base.OnPaintBackground(e);
        }
    }

    internal sealed class LoopMark : Control
    {
        public LoopMark() { DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent.BackColor);
            UiPaint.Surface(e.Graphics, ClientRectangle, UiColors.Accent, UiColors.Accent, 10);
            UiPaint.Loop(e.Graphics, new RectangleF(Width * .17F, Height * .29F, Width * .66F, Height * .4F), UiColors.Background);
        }
    }

    internal sealed class VideoMark : Control
    {
        public VideoMark() { DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent.BackColor);
            UiPaint.Surface(e.Graphics, ClientRectangle, UiColors.AccentSurface, UiColors.AccentSurface, 9);
            using (Pen pen = new Pen(UiColors.Accent, 1.8F * e.Graphics.DpiX / 96F))
            {
                pen.LineJoin = LineJoin.Round;
                e.Graphics.DrawPolygon(pen, new[] { new PointF(Width * .4F, Height * .3F),
                    new PointF(Width * .4F, Height * .7F), new PointF(Width * .7F, Height * .5F) });
            }
        }
    }

    internal sealed class ActionButton : Button
    {
        public bool Primary;
        private bool hovered;
        private bool pressed;
        public ActionButton() { DoubleBuffered = true; Cursor = Cursors.Hand; }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Color fill = !Enabled ? UiColors.Surface : Primary ?
                (pressed ? Color.FromArgb(109, 204, 160) : hovered ? Color.FromArgb(161, 239, 204) : UiColors.Accent) :
                (pressed ? UiColors.Input : hovered ? Color.FromArgb(44, 51, 61) : UiColors.Surface);
            Color text = !Enabled ? UiColors.Disabled : Primary ? UiColors.Background : UiColors.Text;
            e.Graphics.Clear(Parent.BackColor);
            UiPaint.Surface(e.Graphics, ClientRectangle, fill, Focused && ShowFocusCues ? UiColors.Accent :
                Primary && Enabled ? fill : UiColors.Border, 7);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -6, -6), text, fill);
        }
    }

    internal sealed class ChoiceButton : RadioButton
    {
        private bool hovered;
        public ChoiceButton()
        {
            Appearance = Appearance.Button; FlatStyle = FlatStyle.Flat;
            DoubleBuffered = true; Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Color fill = Checked && Enabled ? UiColors.AccentSurface : hovered && Enabled ? UiColors.Surface : UiColors.Input;
            Color text = !Enabled ? UiColors.Disabled : Checked ? UiColors.Accent : UiColors.Muted;
            e.Graphics.Clear(Parent.BackColor);
            UiPaint.Surface(e.Graphics, ClientRectangle, fill, Checked && Enabled ? Color.FromArgb(77, 133, 111) : fill, 6);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5), text, fill);
        }
    }

    internal sealed class OptionCheckBox : CheckBox
    {
        public OptionCheckBox() { DoubleBuffered = true; Cursor = Cursors.Hand; }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent.BackColor);
            float unit = e.Graphics.DpiX / 96F;
            int size = (int)(16 * unit);
            Rectangle box = new Rectangle(0, (Height - size) / 2, size, size);
            UiPaint.Surface(e.Graphics, box, Checked && Enabled ? UiColors.Accent : UiColors.Input,
                Checked && Enabled ? UiColors.Accent : UiColors.Border, 4);
            if (Checked)
                using (Pen pen = new Pen(Enabled ? UiColors.Background : UiColors.Disabled, 1.8F * unit))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    e.Graphics.DrawLines(pen, new[] { new PointF(size * .25F, box.Top + size * .5F),
                        new PointF(size * .44F, box.Top + size * .69F), new PointF(size * .76F, box.Top + size * .32F) });
                }
            Rectangle text = new Rectangle(size + (int)(9 * unit), 0, Width - size - (int)(9 * unit), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, text, Enabled ? UiColors.Text : UiColors.Disabled,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, text);
        }
    }

    internal sealed class OptionCombo : ComboBox
    {
        public OptionCombo()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            using (Graphics graphics = CreateGraphics()) ItemHeight = Font.Height + (int)(12 * graphics.DpiX / 96F);
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? UiColors.Panel : Parent.BackColor);
            UiPaint.Surface(e.Graphics, ClientRectangle, UiColors.Input, Focused ? UiColors.Accent : UiColors.Border, 6);
            float unit = e.Graphics.DpiX / 96F;
            Color text = Enabled ? UiColors.Text : UiColors.Disabled;
            Rectangle bounds = new Rectangle((int)(9 * unit), 0, Math.Max(0, Width - (int)(34 * unit)), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, bounds, text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            UiPaint.Chevron(e.Graphics, Enabled ? UiColors.Muted : UiColors.Disabled, Width - 15 * unit, Height / 2F, 3 * unit, false);
        }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush brush = new SolidBrush(selected ? UiColors.AccentSurface : UiColors.Surface))
                e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, Items[e.Index].ToString(), Font, Rectangle.Inflate(e.Bounds, -6, 0),
                selected ? UiColors.Accent : UiColors.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
        }
    }

    internal sealed class OptionNumber : NumericUpDown
    {
        public OptionNumber()
        {
            BorderStyle = BorderStyle.None;
            foreach (Control child in Controls)
                if (!(child is TextBox)) child.Paint += PaintButtons;
        }
        private void PaintButtons(object sender, PaintEventArgs e)
        {
            Control buttons = (Control)sender;
            e.Graphics.Clear(UiColors.Input);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color color = Enabled ? UiColors.Muted : UiColors.Disabled;
            float size = 2.5F * e.Graphics.DpiX / 96F;
            UiPaint.Chevron(e.Graphics, color, buttons.Width / 2F, buttons.Height * .26F, size, true);
            UiPaint.Chevron(e.Graphics, color, buttons.Width / 2F, buttons.Height * .76F, size, false);
        }
    }

    internal sealed class ProgressTrack : Control
    {
        private int value;
        private ProgressBarStyle style;
        private int phase;
        private readonly Timer timer;
        public int Value { get { return value; } set { this.value = Math.Max(0, Math.Min(100, value)); Invalidate(); } }
        public ProgressBarStyle Style
        {
            get { return style; }
            set { style = value; timer.Enabled = value == ProgressBarStyle.Marquee; Invalidate(); }
        }
        public ProgressTrack()
        {
            DoubleBuffered = true;
            AccessibleRole = AccessibleRole.ProgressBar;
            AccessibleName = "Progreso de exportación";
            timer = new Timer { Interval = 60 };
            timer.Tick += delegate { phase = (phase + 2) % 130; Invalidate(); };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent.BackColor);
            UiPaint.Surface(e.Graphics, ClientRectangle, UiColors.Surface, UiColors.Surface, 3);
            using (GraphicsPath clip = UiPaint.Rounded(ClientRectangle, Height / 2F))
            using (SolidBrush brush = new SolidBrush(UiColors.Accent))
            {
                e.Graphics.SetClip(clip);
                if (style == ProgressBarStyle.Marquee) e.Graphics.FillRectangle(brush, Width * (phase - 30) / 100, 0, Width * 30 / 100, Height);
                else e.Graphics.FillRectangle(brush, 0, 0, Width * value / 100, Height);
                e.Graphics.ResetClip();
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }
}
