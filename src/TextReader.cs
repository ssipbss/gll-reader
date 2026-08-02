using System;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace GenDaLangDu {
  public static class TextReader {
    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO {
      public int cbSize;
      public uint flags;
      public IntPtr hwndActive;
      public IntPtr hwndFocus;
      public IntPtr hwndCapture;
      public IntPtr hwndMenuOwner;
      public IntPtr hwndMoveSize;
      public IntPtr hwndCaret;
      public System.Drawing.Rectangle rcCaret;
    }

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint idThread, out GUITHREADINFO lpgui);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private sealed class ReadResult {
      public string Text;
      public int Caret;
      public bool CaretAbsolute;
    }

    public static string GetFocusedText(out string elementId, out string diag, out int caret, out bool caretAbs) {
      elementId = null;
      diag = null;
      caret = -1;
      caretAbs = false;
      try {
        // TSF 钩子激活时，被 TSF 覆盖的应用（WPS/浏览器/记事本等）的中文
        // 提交由钩子直接上报；UI 线程绝不向这些应用发起 UIA/COM 调用，
        // 否则应用忙（如 WPS 新建文档）时调用会卡死整个程序
        // TSF 钩子激活时 WPS 提交由钩子上报；不再后台轮询 WPS COM，
        // 避免给 WPS 打字增加负载（COM 兜底仅在钩子未安装时使用）
        if (!TsfHook.IsActive) {
          string wpsText = WpsComReader.GetFocusedText(out elementId, out diag, out caret);
          if (wpsText != null) return wpsText;
        }
        if (!TsfHook.IsActive || !IsTsfCoveredForeground()) {
          AutomationElement el = AutomationElement.FocusedElement;
          string r = ReadFocused(el, out elementId, out diag, out caret);
          if (r != null) caretAbs = _lastCaretAbsolute;
          return r;
        }
        return null;
      } catch {
        return null;
      }
    }

    /// <summary>判断焦点元素是否可输入（有文本/值控件模式）。仅在轻按 Shift 时调用一次，
    /// 用于防止在游戏、桌面等没有输入光标的地方误翻转中英状态。</summary>
    public static bool IsFocusEditable() {
      try {
        AutomationElement el = AutomationElement.FocusedElement;
        if (el == null) return false;
        AutomationElement cur = el;
        for (int i = 0; i < 12; i++) {
          if (cur == null) break;
          try {
            object p;
            if (cur.TryGetCurrentPattern(TextPattern.Pattern, out p)) return true;
            if (cur.TryGetCurrentPattern(ValuePattern.Pattern, out p)) return true;
          } catch { }
          try {
            cur = TreeWalker.ControlViewWalker.GetParent(cur);
          } catch {
            break;
          }
        }
        return false;
      } catch {
        return false;
      }
    }

    /// <summary>判断焦点控件当前是否有选区（只取选区数量，不读文本）。
    /// 注意：对选区调用 GetText 在某些应用上会触发 UIA 原生崩溃，取文本改用剪贴板。</summary>
    public static bool HasSelection() {
      try {
        AutomationElement el = AutomationElement.FocusedElement;
        if (el == null) return false;
        AutomationElement cur = el;
        for (int i = 0; i < 12; i++) {
          if (cur == null) break;
          try {
            object pattern;
            if (cur.TryGetCurrentPattern(TextPattern.Pattern, out pattern)) {
              TextPattern tp = (TextPattern)pattern;
              TextPatternRange[] sel = tp.GetSelection();
              if (sel != null && sel.Length > 0) {
                /* 光标 vs 选区：用边界矩形总宽度区分（光标约1px，选中文字数十px以上）。
                   不用 CompareEndpoints（Chromium 下不可靠），更不读文本（会触发 UIA 原生崩溃） */
                try {
                  System.Windows.Rect[] rects = sel[0].GetBoundingRectangles();
                  if (rects != null && rects.Length > 0) {
                    double totalWidth = 0;
                    foreach (System.Windows.Rect rc in rects) totalWidth += rc.Width;
                    if (totalWidth > 4) return true;
                  }
                } catch {
                  return false;
                }
              }
              return false;
            }
          } catch { }
          try {
            cur = TreeWalker.ControlViewWalker.GetParent(cur);
          } catch {
            break;
          }
        }
      } catch { }
      return false;
    }

    internal static bool IsTsfCoveredForeground() {
      try {
        IntPtr h = Native.GetForegroundWindow();
        if (h == IntPtr.Zero) return false;
        uint pid;
        Native.GetWindowThreadProcessId(h, out pid);
        if (pid == 0) return false;
        using (System.Diagnostics.Process p = System.Diagnostics.Process.GetProcessById((int)pid)) {
          string name = p.ProcessName.ToLowerInvariant();
          return name == "wps" || name == "et" || name == "wpp" ||
                 name == "bilibili" || name == "哔哩哔哩" ||
                 name == "notepad" || name == "everything" ||
                 name == "winword" || name == "excel" || name == "powerpnt" ||
                 name == "notepad++";
        }
      } catch {
        return false;
      }
    }

    private static string ReadFocused(AutomationElement el, out string elementId, out string diag, out int caret) {
      elementId = null;
      diag = null;
      caret = -1;
      try {
        if (el == null) return null;
        try {
          int[] rid = el.GetRuntimeId();
          if (rid != null && rid.Length > 0) elementId = string.Join("-", rid);
        } catch { }
        if (elementId == null) {
          elementId = (el.Current.ClassName ?? "") + "|" + (el.Current.Name ?? "");
        }
        try {
          object pv;
          object pt;
          diag = "pid=" + el.Current.ProcessId +
                 " type=" + el.Current.ControlType.ProgrammaticName +
                 " class=" + (el.Current.ClassName ?? "") +
                 " name=" + (el.Current.Name ?? "") +
                 " val=" + el.TryGetCurrentPattern(ValuePattern.Pattern, out pv) +
                 " txt=" + el.TryGetCurrentPattern(TextPattern.Pattern, out pt);
        } catch { }
        ReadResult rr = TryRead(el);
        if (rr != null) {
          caret = rr.Caret;
          _lastCaretAbsolute = rr.CaretAbsolute;
          return rr.Text;
        }
        AutomationElement cur = el;
        for (int i = 0; i < 12; i++) {
          try { cur = TreeWalker.ControlViewWalker.GetParent(cur); } catch { break; }
          if (cur == null) break;
          rr = TryRead(cur);
          if (rr != null) {
            caret = rr.Caret;
            _lastCaretAbsolute = rr.CaretAbsolute;
            return rr.Text;
          }
        }
        ReadResult longText = null;
        try {
          System.Collections.Generic.Queue<AutomationElement> queue =
            new System.Collections.Generic.Queue<AutomationElement>();
          AutomationElement firstChild = TreeWalker.ControlViewWalker.GetFirstChild(el);
          if (firstChild != null) queue.Enqueue(firstChild);
          int scanned = 0;
          while (queue.Count > 0 && scanned < 300) {
            AutomationElement child = queue.Dequeue();
            scanned++;
            rr = TryRead(child);
            if (rr != null) {
              if (rr.Text.Length <= 1000) {
                caret = rr.Caret;
                _lastCaretAbsolute = rr.CaretAbsolute;
                return rr.Text;
              }
              if (longText == null || rr.Text.Length < longText.Text.Length) longText = rr;
            }
            AutomationElement next = TreeWalker.ControlViewWalker.GetFirstChild(child);
            while (next != null) {
              queue.Enqueue(next);
              next = TreeWalker.ControlViewWalker.GetNextSibling(next);
            }
          }
        } catch { }
        if (longText != null) {
          caret = longText.Caret;
          _lastCaretAbsolute = longText.CaretAbsolute;
          return longText.Text;
        }
        try {
          IntPtr hwndFocus = GetFocusedHwnd();
          if (hwndFocus != IntPtr.Zero) {
            AutomationElement byHwnd = AutomationElement.FromHandle(hwndFocus);
            if (byHwnd != null) {
              rr = TryRead(byHwnd);
              if (rr != null) {
                caret = rr.Caret;
                _lastCaretAbsolute = rr.CaretAbsolute;
                return rr.Text;
              }
            }
          }
        } catch { }
        return null;
      } catch {
        return null;
      }
    }

    private static IntPtr GetFocusedHwnd() {
      try {
        IntPtr fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero) return IntPtr.Zero;
        uint pid;
        uint tid = GetWindowThreadProcessId(fg, out pid);
        GUITHREADINFO info = new GUITHREADINFO();
        info.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
        if (GetGUIThreadInfo(tid, out info)) return info.hwndFocus;
      } catch { }
      return IntPtr.Zero;
    }

    private static ReadResult TryRead(AutomationElement el) {
      if (el == null) return null;
      try {
        object pattern;
        if (el.TryGetCurrentPattern(ValuePattern.Pattern, out pattern)) {
          string v = ((ValuePattern)pattern).Current.Value;
          if (!string.IsNullOrEmpty(v)) return new ReadResult { Text = v, Caret = -1 };
        }
        if (el.TryGetCurrentPattern(TextPattern.Pattern, out pattern)) {
          TextPattern tp = (TextPattern)pattern;
          TextPatternRange range = tp.DocumentRange;
          try {
            TextPatternRange tail = range.Clone();
            int moved = tail.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -5000);
            string text = tail.GetText(-1);
            int caret = -1;
            try {
              TextPatternRange[] sel = tp.GetSelection();
              if (sel != null && sel.Length > 0) {
                TextPatternRange r = tail.Clone();
                r.MoveEndpointByRange(TextPatternRangeEndpoint.End, sel[0], TextPatternRangeEndpoint.Start);
                caret = r.GetText(-1).Length;
                if (caret < 0) caret = 0;
                if (caret > text.Length) caret = text.Length;
              }
            } catch { }
            /* moved > -5000 表示窗口起点被钳制在文档开头（文档不足5000字），
               此时 caret 即绝对光标位置，可直接用新旧光标区间提取刚输入的内容 */
            return new ReadResult { Text = text, Caret = caret, CaretAbsolute = moved > -5000 };
          } catch {
            try {
              TextPatternRange[] visible = tp.GetVisibleRanges();
              if (visible != null && visible.Length > 0) {
                string v = visible[0].GetText(20000);
                if (!string.IsNullOrEmpty(v)) return new ReadResult { Text = v, Caret = -1 };
              }
            } catch { }
            return new ReadResult { Text = range.GetText(20000), Caret = -1 };
          }
        }
      } catch { }
      return null;
    }

    private static bool _lastCaretAbsolute;
  }
}
