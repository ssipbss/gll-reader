using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GenDaLangDu {
  /// <summary>选中文本时出现的悬浮"朗读"按钮：置顶、无边框、不抢焦点。</summary>
  public sealed class SelectionFloater : Form {
    public event Action SpeakRequested;

    private readonly RoundedButton _btn;
    private bool _reading;

    public bool IsReading {
      get { return _reading; }
    }

    public SelectionFloater() {
      FormBorderStyle = FormBorderStyle.None;
      ShowInTaskbar = false;
      TopMost = true;
      StartPosition = FormStartPosition.Manual;
      AutoScaleMode = AutoScaleMode.None;
      BackColor = Color.FromArgb(255, 255, 255);
      Padding = new Padding(0);
      ClientSize = new Size(86, 30);

      _btn = new RoundedButton();
      _btn.Text = "开始朗读";
      _btn.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
      _btn.Location = new Point(0, 0);
      _btn.Size = new Size(86, 30);
      _btn.FillColor = Color.White;
      _btn.HoverColor = Color.FromArgb(241, 245, 249);
      _btn.DownColor = Color.FromArgb(226, 232, 240);
      _btn.BorderColor = Color.FromArgb(148, 163, 184);
      _btn.ForeColor = Color.FromArgb(30, 41, 59);
      _btn.Cursor = Cursors.Hand;
      _btn.Click += delegate {
        Action h = SpeakRequested;
        if (h != null) h();
      };
      Controls.Add(_btn);
    }

    /// <summary>切换按钮状态：开始朗读 / 结束朗读。</summary>
    public void SetReading(bool reading) {
      _reading = reading;
      _btn.Text = reading ? "结束朗读" : "开始朗读";
      _btn.Width = reading ? 96 : 96;
      ClientSize = new Size(96, 30);
    }

    protected override bool ShowWithoutActivation {
      get { return true; }
    }

    protected override CreateParams CreateParams {
      get {
        CreateParams cp = base.CreateParams;
        cp.ExStyle |= 0x00000080;  /* WS_EX_TOOLWINDOW */
        cp.ExStyle |= 0x08000000;  /* WS_EX_NOACTIVATE */
        return cp;
      }
    }

    /// <summary>在鼠标附近显示朗读按钮；选中文本为空/变化为无选区时调用 HideNow 隐藏。</summary>
    public void ShowFor(Point screenPos) {
      Point pos = screenPos;
      pos.Offset(14, 14);
      Rectangle wa = Screen.GetWorkingArea(pos);
      if (pos.X + Width > wa.Right) pos.X = wa.Right - Width - 4;
      if (pos.Y + Height > wa.Bottom) pos.Y = wa.Bottom - Height - 4;
      if (pos.X < wa.Left) pos.X = wa.Left + 4;
      if (pos.Y < wa.Top) pos.Y = wa.Top + 4;
      Location = pos;
      if (!Visible) {
        Show();
      }
      try {
        BringToFront();
      } catch { }
    }

    public void HideNow() {
      if (Visible) Hide();
    }
  }
}
