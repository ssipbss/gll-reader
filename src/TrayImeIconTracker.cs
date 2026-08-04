using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace GenDaLangDu {
  /// <summary>多多五笔托盘指示器图标识别。
  /// 多多五笔的中/英状态只通过图标表达（按钮名称固定为"中文/英文"，不随状态变），
  /// 因此截取托盘按钮区域并与 duoIME.ime 提取的图标模板做形状匹配：
  /// 中文 = icon_5（"中"字形）或 icon_23（橙红禁止圈）；英文 = icon_2 / icon_22（键盘形）。
  /// 与旧"像素变化就翻转"不同：这里认图，悬停/时钟刷新不会误判。</summary>
  public sealed class TrayImeIconTracker : IDisposable {
    public event Action<bool> StateConfirmed;

    private readonly Bitmap _zhA;
    private readonly Bitmap _zhB;
    private readonly Bitmap _enA;
    private readonly Bitmap _enB;
    private Rectangle _rect = Rectangle.Empty;
    private DateTime _lastFindAt = DateTime.MinValue;
    private DateTime _lastCaptureAt = DateTime.MinValue;
    private int _lastApplied;   // 0 未知, 1 中文, 2 英文
    private int _pending;       // 待确认状态
    private int _confirmCount;  // 连续相同判定次数
    private bool _disposed;

    public TrayImeIconTracker() {
      _zhA = TrayImeTemplates.Load(TrayImeTemplates.Icon5Png);
      _zhB = TrayImeTemplates.Load(TrayImeTemplates.Icon23Png);
      _enA = TrayImeTemplates.Load(TrayImeTemplates.Icon2Png);
      _enB = TrayImeTemplates.Load(TrayImeTemplates.Icon22Png);
    }

    public void RefreshNow() {
      _lastCaptureAt = DateTime.MinValue;
      _lastFindAt = DateTime.MinValue;
    }

    public void Tick() {
      if (_disposed) return;
      try {
        if ((DateTime.Now - _lastCaptureAt).TotalMilliseconds < 1000) return;
        if (_rect.Width <= 0 || (DateTime.Now - _lastFindAt).TotalMilliseconds > 30000) FindRect();
        if (_rect.Width <= 0) return;

        /* 鼠标悬停在按钮上会出现高亮背景，跳过，避免干扰 */
        Native.POINT pt;
        Native.GetCursorPos(out pt);
        if (pt.X >= _rect.X - 4 && pt.X <= _rect.X + _rect.Width + 4 &&
            pt.Y >= _rect.Y - 4 && pt.Y <= _rect.Y + _rect.Height + 4) {
          _lastCaptureAt = DateTime.Now;
          return;
        }

        _lastCaptureAt = DateTime.Now;
        using (Bitmap bmp = new Bitmap(_rect.Width, _rect.Height)) {
          using (Graphics g = Graphics.FromImage(bmp)) {
            g.CopyFromScreen(_rect.X, _rect.Y, 0, 0, _rect.Size);
          }
          int v = Classify(bmp);
          if (v == 0) {
            _confirmCount = 0;
            return;
          }
          if (v == _pending) {
            _confirmCount++;
          } else {
            _pending = v;
            _confirmCount = 1;
          }
          if (_confirmCount >= 2 && v != _lastApplied) {
            _lastApplied = v;
            Action<bool> h = StateConfirmed;
            if (h != null) h(v == 1);
          }
        }
      } catch {
      }
    }

    /// <summary>对一张托盘按钮截图分类：0 未知，1 中文，2 英文。</summary>
    public int Classify(Bitmap bmp) {
      int w = bmp.Width, h = bmp.Height;
      Color[] px = new Color[w * h];
      for (int y = 0; y < h; y++) {
        for (int x = 0; x < w; x++) px[y * w + x] = bmp.GetPixel(x, y);
      }

      /* 主背景色（量化直方图） */
      Dictionary<long, int> hist = new Dictionary<long, int>();
      foreach (Color c in px) {
        long k = (long)(c.R / 16) << 16 | (long)(c.G / 16) << 8 | (c.B / 16);
        hist[k] = hist.ContainsKey(k) ? hist[k] + 1 : 1;
      }
      long bestK = -1;
      int bestN = -1;
      foreach (KeyValuePair<long, int> kv in hist) {
        if (kv.Value > bestN) {
          bestN = kv.Value;
          bestK = kv.Key;
        }
      }
      int br = (int)((bestK >> 16) & 0xF) * 16 + 8;
      int bg = (int)((bestK >> 8) & 0xF) * 16 + 8;
      int bb = (int)(bestK & 0xF) * 16 + 8;

      /* 字形掩码：与背景明显不同的像素；再膨胀一圈容忍对齐误差 */
      bool[,] mask = new bool[h, w];
      int orange = 0;
      for (int y = 0; y < h; y++) {
        for (int x = 0; x < w; x++) {
          Color c = px[y * w + x];
          int d = Math.Abs(c.R - br) + Math.Abs(c.G - bg) + Math.Abs(c.B - bb);
          if (d > 75) mask[y, x] = true;
          if (c.R > 170 && c.G > 50 && c.G < 190 && c.B < 120) orange++;
        }
      }
      bool[,] dil = Dilate(mask);

      /* 橙红禁圈（icon_23）是中文状态的特征：大量橙色像素。
         形状上它是个实心圆，和英文键盘图标太像，不能参与形状比对。 */
      if (orange >= 20) return 1;

      double zhBest = Match(_zhA, dil, w, h);
      double enBest = Math.Max(Match(_enA, dil, w, h), Match(_enB, dil, w, h));
      if (enBest >= 0.80 && enBest > zhBest + 0.06) return 2;
      if (zhBest >= 0.80 && zhBest > enBest + 0.06) return 1;
      return 0;
    }

    /// <summary>模板（透明背景）与膨胀后的截图字形掩码做 IoU。</summary>
    private static double Match(Bitmap tpl, bool[,] cap, int cw, int ch) {
      int tminX = 32, tminY = 32, tmaxX = -1, tmaxY = -1;
      for (int y = 0; y < 32; y++) {
        for (int x = 0; x < 32; x++) {
          if (tpl.GetPixel(x, y).A >= 40) {
            if (x < tminX) tminX = x;
            if (x > tmaxX) tmaxX = x;
            if (y < tminY) tminY = y;
            if (y > tmaxY) tmaxY = y;
          }
        }
      }
      if (tmaxX < 0) return 0;
      int tw = tmaxX - tminX + 1, th = tmaxY - tminY + 1;
      double best = 0;
      for (int s = 16; s <= 24; s += 2) {
        double sc = (double)s / Math.Max(tw, th);
        int sw = Math.Max(3, (int)(tw * sc));
        int sh = Math.Max(3, (int)(th * sc));
        if (sw > cw || sh > ch) continue;
        bool[,] t = new bool[sh, sw];
        for (int y = 0; y < sh; y++) {
          for (int x = 0; x < sw; x++) {
            int sx = Math.Min(31, tminX + (int)(x / sc));
            int sy = Math.Min(31, tminY + (int)(y / sc));
            t[y, x] = tpl.GetPixel(sx, sy).A >= 40;
          }
        }
        for (int ox = 0; ox <= cw - sw; ox += 2) {
          for (int oy = 0; oy <= ch - sh; oy += 2) {
            int inter = 0, uni = 0;
            for (int y = 0; y < sh; y += 2) {
              for (int x = 0; x < sw; x += 2) {
                if (t[y, x] || cap[oy + y, ox + x]) {
                  uni++;
                  if (t[y, x] && cap[oy + y, ox + x]) inter++;
                }
              }
            }
            if (uni > 15) {
              double v = (double)inter / uni;
              if (v > best) best = v;
            }
          }
        }
      }
      return best;
    }

    private static bool[,] Dilate(bool[,] m) {
      int h = m.GetLength(0), w = m.GetLength(1);
      bool[,] d = new bool[h, w];
      for (int y = 0; y < h; y++) {
        for (int x = 0; x < w; x++) {
          if (!m[y, x]) continue;
          for (int dy = -1; dy <= 1; dy++) {
            for (int dx = -1; dx <= 1; dx++) {
              int yy = y + dy, xx = x + dx;
              if (yy >= 0 && yy < h && xx >= 0 && xx < w) d[yy, xx] = true;
            }
          }
        }
      }
      return d;
    }

    private void FindRect() {
      _rect = Rectangle.Empty;
      _lastFindAt = DateTime.Now;
      try {
        foreach (IntPtr tray in Native.EnumerateTaskbars()) {
          try {
            AutomationElement rootEl = AutomationElement.FromHandle(tray);
            if (rootEl == null) continue;
            AutomationElementCollection btns = rootEl.FindAll(TreeScope.Descendants,
              new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            foreach (AutomationElement el in btns) {
              string n = el.Current.Name ?? "";
              if (n.IndexOf("托盘输入指示器", StringComparison.Ordinal) < 0) continue;
              if (n.IndexOf("要切换输入法", StringComparison.Ordinal) >= 0) continue;
              /* 微软输入法的名称是"中文模式/英语模式"（文字通道已覆盖）；
                 多多五笔等 IMM 输入法是"中文/英文"，名称固定，只能靠图标。 */
              if (n.IndexOf("模式", StringComparison.Ordinal) >= 0) continue;
              System.Windows.Rect r = el.Current.BoundingRectangle;
              if (r.Width > 1 && r.Height > 1 && r.Width <= 55) {
                _rect = new Rectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
                return;
              }
            }
          } catch { }
        }
      } catch {
      }
    }

    public void Dispose() {
      if (_disposed) return;
      _disposed = true;
      try { _zhA.Dispose(); } catch { }
      try { _zhB.Dispose(); } catch { }
      try { _enA.Dispose(); } catch { }
      try { _enB.Dispose(); } catch { }
    }
  }
}
