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

    [ComImport, Guid("618736e0-3c3d-11cf-810c-00aa00389b71"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAccessible {
      [PreserveSig] int get_accParent([MarshalAs(UnmanagedType.Interface)] out IAccessible ppdispParent);
      [PreserveSig] int get_accChildCount(out int pcountChildren);
      [PreserveSig] int get_accChild([In, MarshalAs(UnmanagedType.Struct)] object varChild, [MarshalAs(UnmanagedType.Interface)] out IAccessible ppdispChild);
      [PreserveSig] int get_accName([In, MarshalAs(UnmanagedType.Struct)] object varChild, [MarshalAs(UnmanagedType.BStr)] out string pszName);
      [PreserveSig] int get_accValue([In, MarshalAs(UnmanagedType.Struct)] object varChild, [MarshalAs(UnmanagedType.BStr)] out string pszValue);
      [PreserveSig] int get_accDescription([In, MarshalAs(UnmanagedType.Struct)] object varChild, [MarshalAs(UnmanagedType.BStr)] out string pszDescription);
      [PreserveSig] int get_accRole([In, MarshalAs(UnmanagedType.Struct)] object varChild, out object pvarRole);
      [PreserveSig] int get_accState([In, MarshalAs(UnmanagedType.Struct)] object varChild, out object pvarState);
      [PreserveSig] int get_accHelp([In, MarshalAs(UnmanagedType.Struct)] object varChild, [MarshalAs(UnmanagedType.BStr)] out string pszHelp);
      [PreserveSig] int get_accHelpTopic([MarshalAs(UnmanagedType.BStr)] out string pszHelpFile, [In, MarshalAs(UnmanagedType.Struct)] object varChild, out int pidTopic);
      [PreserveSig] int get_accKeyboardShortcut([In, MarshalAs(UnmanagedType.Struct)] object varChild, [MarshalAs(UnmanagedType.BStr)] out string pszKeyboardShortcut);
      [PreserveSig] int get_accFocus(out object pvarChild);
      [PreserveSig] int get_accSelection(out object pvarChildren);
      [PreserveSig] int get_accDefaultAction([In, MarshalAs(UnmanagedType.Struct)] object varChild, [MarshalAs(UnmanagedType.BStr)] out string pszDefaultAction);
      [PreserveSig] int accSelect(int flagsSelect, [In, MarshalAs(UnmanagedType.Struct)] object varChild);
      [PreserveSig] int accLocation(out int pxLeft, out int pyTop, out int pcxWidth, out int pcyHeight, [In, MarshalAs(UnmanagedType.Struct)] object varChild);
      [PreserveSig] int accNavigate(int navDir, [In, MarshalAs(UnmanagedType.Struct)] object varChild, out object pvarEndUpAt);
      [PreserveSig] int accHitTest(int xLeft, int yTop, out object pvarChild);
      [PreserveSig] int accDoDefaultAction([In, MarshalAs(UnmanagedType.Struct)] object varChild);
      [PreserveSig] int put_accName([In, MarshalAs(UnmanagedType.Struct)] object varChild, [MarshalAs(UnmanagedType.BStr)] string szName);
      [PreserveSig] int put_accValue([In, MarshalAs(UnmanagedType.Struct)] object varChild, [MarshalAs(UnmanagedType.BStr)] string szValue);
    }

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint dwObjectID, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IAccessible ppvObject);

    private static Guid IID_IAccessible = new Guid("618736e0-3c3d-11cf-810c-00aa00389b71");

    public static string GetFocusedText(out string elementId, out string diag) {
      elementId = null;
      diag = null;
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
        string msaa = TryMsaaText(GetFocusedHwnd());
        if (msaa != null) return msaa;
        return null;
      } catch {
        return null;
      }
    }

    private static string TryMsaaText(IntPtr hwnd) {
      if (hwnd == IntPtr.Zero) return null;
      try {
        IAccessible acc;
        if (AccessibleObjectFromWindow(hwnd, 0xFFFFFFFC, ref IID_IAccessible, out acc) != 0 || acc == null) return null;
        string v = MsaaValue(acc, null);
        if (!string.IsNullOrEmpty(v)) return v;
        object focusChild;
        if (acc.get_accFocus(out focusChild) == 0) {
          if (focusChild is int) {
            v = MsaaValue(acc, focusChild);
            if (!string.IsNullOrEmpty(v)) return v;
          } else if (focusChild is IAccessible) {
            v = MsaaValue((IAccessible)focusChild, null);
            if (!string.IsNullOrEmpty(v)) return v;
          }
        }
      } catch { }
      return null;
    }

    private static string MsaaValue(IAccessible acc, object childId) {
      try {
        object roleObj;
        int hrRole = acc.get_accRole(childId, out roleObj);
        string v = null;
        int hrV = acc.get_accValue(childId, out v);
        if (hrV == 0 && !string.IsNullOrEmpty(v)) return v;
        string n = null;
        int hrN = acc.get_accName(childId, out n);
        if (hrRole == 0 && hrN == 0 && !string.IsNullOrEmpty(n)) {
          int role = 0;
          try { role = Convert.ToInt32(roleObj); } catch { }
          if (role == 0x2D || role == 0x2B || role == 0x0F) return n;
        }
      } catch { }
      return null;
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
