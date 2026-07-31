using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GenDaLangDu {
  public static class UiColors {
    public static readonly Color HeaderTop = Color.White;
    public static readonly Color HeaderBottom = Color.White;
    public static readonly Color Accent = Color.FromArgb(17, 24, 39);
    public static readonly Color AccentHover = Color.FromArgb(55, 65, 81);
    public static readonly Color AccentDown = Color.FromArgb(3, 7, 18);
    public static readonly Color PageBg = Color.FromArgb(247, 248, 250);
    public static readonly Color CardBg = Color.White;
    public static readonly Color CardBorder = Color.FromArgb(229, 231, 235);
    public static readonly Color TextDark = Color.FromArgb(17, 24, 39);
    public static readonly Color TextGray = Color.FromArgb(107, 114, 128);
    public static readonly Color Track = Color.FromArgb(229, 231, 235);
    public static readonly Color Ok = Color.FromArgb(5, 122, 85);
    public static readonly Color Paused = Color.FromArgb(107, 114, 128);
  }

  public static class UiDraw {
    public static GraphicsPath RoundRect(Rectangle r, int radius) {
      GraphicsPath p = new GraphicsPath();
      int d = radius * 2;
      p.AddArc(r.Left, r.Top, d, d, 180, 90);
      p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
      p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
      p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
      p.CloseFigure();
      return p;
    }
  }

  public class RoundedButton : Button {
    private bool _hover;
    private bool _down;

    public int CornerRadius = 8;
    public Color BgFill = Color.White;
    public Color FillColor = UiColors.Accent;
    public Color HoverColor = UiColors.AccentHover;
    public Color DownColor = UiColors.AccentDown;
    public Color BorderColor = Color.Transparent;

    public RoundedButton() {
      SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
               ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
      BackColor = Color.Transparent;
      Cursor = Cursors.Hand;
      ForeColor = Color.White;
    }

    protected override void OnPaint(PaintEventArgs e) {
      e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
      e.Graphics.Clear(BgFill);
      Color c = _down ? DownColor : _hover ? HoverColor : FillColor;
      Rectangle r = ClientRectangle;
      r.Width--;
      r.Height--;
      using (GraphicsPath path = UiDraw.RoundRect(r, CornerRadius)) {
        using (SolidBrush b = new SolidBrush(c)) e.Graphics.FillPath(b, path);
        if (BorderColor.A > 0) {
          using (Pen pen = new Pen(BorderColor, 1.5f)) e.Graphics.DrawPath(pen, path);
        }
      }
      TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) {
      if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); }
      base.OnMouseDown(e);
    }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
  }

  public class ModernCheckBox : CheckBox {
    public Color CheckColor = UiColors.Accent;

    public ModernCheckBox() {
      SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
               ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
      Cursor = Cursors.Hand;
      AutoSize = false;
      Height = 26;
      Width = 190;
    }

    protected override void OnPaint(PaintEventArgs e) {
      e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
      e.Graphics.Clear(Color.White);
      Rectangle box = new Rectangle(2, (Height - 18) / 2, 18, 18);
      using (GraphicsPath p = UiDraw.RoundRect(box, 5)) {
        using (SolidBrush b = new SolidBrush(Checked ? CheckColor : Color.White)) e.Graphics.FillPath(b, p);
        using (Pen pen = new Pen(Checked ? CheckColor : UiColors.Track, 1.5f)) e.Graphics.DrawPath(pen, p);
      }
      if (Checked) {
        using (Pen pen = new Pen(Color.White, 2.2f)) {
          pen.StartCap = LineCap.Round;
          pen.EndCap = LineCap.Round;
          e.Graphics.DrawLine(pen, box.Left + 5, box.Top + 10, box.Left + 8, box.Top + 13);
          e.Graphics.DrawLine(pen, box.Left + 8, box.Top + 13, box.Left + 14, box.Top + 5);
        }
      }
      TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(28, 0, Width - 30, Height),
        ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
  }

  public class ModernSlider : Control {
    private bool _drag;
    private int _value;

    public int Minimum { get; set; }
    public int Maximum { get; set; }

    public event EventHandler ValueChanged;

    public int Value {
      get { return _value; }
      set {
        int v = Math.Max(Minimum, Math.Min(Maximum, value));
        if (v != _value) {
          _value = v;
          Invalidate();
          EventHandler h = ValueChanged;
          if (h != null) h(this, EventArgs.Empty);
        }
      }
    }

    public ModernSlider() {
      SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
               ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
      Minimum = 0;
      Maximum = 100;
      Value = 50;
      Height = 24;
    }

    private int ThumbX() {
      if (Maximum <= Minimum) return 8;
      float ratio = (float)(_value - Minimum) / (Maximum - Minimum);
      return 8 + (int)(ratio * (Width - 24));
    }

    protected override void OnPaint(PaintEventArgs e) {
      e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
      e.Graphics.Clear(Color.White);
      int y = Height / 2;
      int x = ThumbX();
      using (Pen pen = new Pen(UiColors.Track, 4)) {
        pen.StartCap = LineCap.Round;
        pen.EndCap = LineCap.Round;
        e.Graphics.DrawLine(pen, 6, y, Width - 10, y);
      }
      using (Pen pen = new Pen(UiColors.Accent, 4)) {
        pen.StartCap = LineCap.Round;
        pen.EndCap = LineCap.Round;
        e.Graphics.DrawLine(pen, 6, y, x, y);
      }
      Rectangle r = new Rectangle(x - 8, y - 8, 16, 16);
      using (SolidBrush b = new SolidBrush(Color.White)) e.Graphics.FillEllipse(b, r);
      using (Pen pen = new Pen(UiColors.Accent, 2)) e.Graphics.DrawEllipse(pen, r);
    }

    protected override void OnMouseDown(MouseEventArgs e) {
      if (e.Button == MouseButtons.Left) {
        _drag = true;
        SetFromX(e.X);
        Capture = true;
      }
      base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e) {
      if (_drag) SetFromX(e.X);
      base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e) {
      _drag = false;
      Capture = false;
      base.OnMouseUp(e);
    }

    private void SetFromX(int x) {
      float ratio = (float)(x - 8) / Math.Max(1, Width - 24);
      ratio = Math.Max(0, Math.Min(1, ratio));
      Value = Minimum + (int)Math.Round(ratio * (Maximum - Minimum));
    }
  }

  public class GradientPanel : Panel {
    public Color ColorTop = UiColors.HeaderTop;
    public Color ColorBottom = UiColors.HeaderBottom;

    public GradientPanel() {
      SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
               ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e) {
      if (ClientRectangle.Width <= 0 || ClientRectangle.Height <= 0) return;
      using (LinearGradientBrush b = new LinearGradientBrush(ClientRectangle, ColorTop, ColorBottom, 90f)) {
        e.Graphics.FillRectangle(b, ClientRectangle);
      }
    }
  }

  public class CardPanel : Panel {
    public int CornerRadius = 10;

    public CardPanel() {
      SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
               ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
      BackColor = UiColors.CardBg;
    }

    protected override void OnPaint(PaintEventArgs e) {
      e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
      Rectangle r = ClientRectangle;
      r.Width--;
      r.Height--;
      using (GraphicsPath p = UiDraw.RoundRect(r, CornerRadius)) {
        using (SolidBrush b = new SolidBrush(UiColors.CardBg)) e.Graphics.FillPath(b, p);
        using (Pen pen = new Pen(UiColors.CardBorder)) e.Graphics.DrawPath(pen, p);
      }
    }
  }

  public class StatusPill : Control {
    public bool Active = true;

    public StatusPill() {
      SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
               ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
      Height = 20;
    }

    protected override void OnPaint(PaintEventArgs e) {
      e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
      e.Graphics.Clear(Color.White);
      Rectangle r = ClientRectangle;
      r.Width--;
      r.Height--;
      using (GraphicsPath p = UiDraw.RoundRect(r, r.Height / 2)) {
        using (SolidBrush b = new SolidBrush(Active ? Color.FromArgb(233, 248, 241) : Color.FromArgb(243, 244, 246))) {
          e.Graphics.FillPath(b, p);
        }
      }
      string text = Active ? "● 正在监听" : "○ 已暂停";
      Color c = Active ? Color.FromArgb(5, 122, 85) : Color.FromArgb(107, 114, 128);
      TextRenderer.DrawText(e.Graphics, text, Font, ClientRectangle, c,
        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
  }
}