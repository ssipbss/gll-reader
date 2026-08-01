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
    }

    public static string GetFocusedText(out string elementId, out string diag, out int caret) {
      elementId = null;
      diag = null;
      caret = -1;
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
          return rr.Text;
        }
        AutomationElement cur = el;
        for (int i = 0; i < 12; i++) {
          try { cur = TreeWalker.ControlViewWalker.GetParent(cur); } catch { break; }
          if (cur == null) break;
          rr = TryRead(cur);
          if (rr != null) {
            caret = rr.Caret;
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
            tail.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -5000);
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
            return new ReadResult { Text = text, Caret = caret };
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
  }
}
