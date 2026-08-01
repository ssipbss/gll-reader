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

    public static string GetFocusedText(out string elementId) {
      elementId = null;
      try {
        AutomationElement el = AutomationElement.FocusedElement;
        if (el == null) return null;
        try {
          int[] rid = el.GetRuntimeId();
          if (rid != null && rid.Length > 0) elementId = string.Join("-", rid);
        } catch { }
        if (elementId == null) {
          elementId = (el.Current.ClassName ?? "") + "|" + (el.Current.Name ?? "");
        }
        string t = TryReadText(el);
        if (t != null) return t;
        AutomationElement cur = el;
        for (int i = 0; i < 12; i++) {
          try { cur = TreeWalker.ControlViewWalker.GetParent(cur); } catch { break; }
          if (cur == null) break;
          t = TryReadText(cur);
          if (t != null) return t;
        }
        string longText = null;
        try {
          System.Collections.Generic.Queue<AutomationElement> queue =
            new System.Collections.Generic.Queue<AutomationElement>();
          AutomationElement firstChild = TreeWalker.ControlViewWalker.GetFirstChild(el);
          if (firstChild != null) queue.Enqueue(firstChild);
          int scanned = 0;
          while (queue.Count > 0 && scanned < 300) {
            AutomationElement child = queue.Dequeue();
            scanned++;
            t = TryReadText(child);
            if (t != null) {
              if (t.Length <= 1000) return t;
              if (longText == null || t.Length < longText.Length) longText = t;
            }
            AutomationElement next = TreeWalker.ControlViewWalker.GetFirstChild(child);
            while (next != null) {
              queue.Enqueue(next);
              next = TreeWalker.ControlViewWalker.GetNextSibling(next);
            }
          }
        } catch { }
        if (longText != null) return longText;
        try {
          IntPtr hwndFocus = GetFocusedHwnd();
          if (hwndFocus != IntPtr.Zero) {
            AutomationElement byHwnd = AutomationElement.FromHandle(hwndFocus);
            if (byHwnd != null) {
              t = TryReadText(byHwnd);
              if (t != null) return t;
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

    private static string TryReadText(AutomationElement el) {
      if (el == null) return null;
      try {
        object pattern;
        if (el.TryGetCurrentPattern(ValuePattern.Pattern, out pattern)) {
          string v = ((ValuePattern)pattern).Current.Value;
          if (!string.IsNullOrEmpty(v)) return v;
        }
        if (el.TryGetCurrentPattern(TextPattern.Pattern, out pattern)) {
          TextPattern tp = (TextPattern)pattern;
          TextPatternRange range = tp.DocumentRange;
          try {
            TextPatternRange tail = range.Clone();
            tail.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -5000);
            return tail.GetText(-1);
          } catch {
            try {
              TextPatternRange[] visible = tp.GetVisibleRanges();
              if (visible != null && visible.Length > 0) {
                string v = visible[0].GetText(20000);
                if (!string.IsNullOrEmpty(v)) return v;
              }
            } catch { }
            return range.GetText(20000);
          }
        }
      } catch { }
      return null;
    }
  }
}
