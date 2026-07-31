using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

class UiImeDiag3 {
  [StructLayout(LayoutKind.Sequential)]
  public struct INPUT {
    public int type;
    public InputUnion U;
  }

  [StructLayout(LayoutKind.Explicit)]
  public struct InputUnion {
    [FieldOffset(0)]
    public KEYBDINPUT ki;
    [FieldOffset(0)]
    public MOUSEINPUT mi;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct KEYBDINPUT {
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct MOUSEINPUT {
    public int dx;
    public int dy;
    public uint mouseData;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
  }

  [DllImport("user32.dll")]
  static extern IntPtr GetForegroundWindow();

  [DllImport("user32.dll")]
  static extern bool SetForegroundWindow(IntPtr hWnd);

  [DllImport("user32.dll")]
  static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

  [DllImport("user32.dll")]
  static extern bool IsWindowVisible(IntPtr hWnd);

  [DllImport("user32.dll")]
  static extern bool IsIconic(IntPtr hWnd);

  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

  [DllImport("user32.dll", SetLastError = true)]
  static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

  [DllImport("user32.dll")]
  static extern short VkKeyScan(char ch);

  [DllImport("user32.dll")]
  static extern uint MapVirtualKey(uint uCode, uint uMapType);

  [DllImport("user32.dll")]
  static extern bool SetCursorPos(int x, int y);

  delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

  [DllImport("user32.dll")]
  static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

  [DllImport("user32.dll")]
  static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

  const int INPUT_KEYBOARD = 1;
  const int INPUT_MOUSE = 0;
  const uint KEYEVENTF_KEYUP = 0x0002;
  const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
  const uint MOUSEEVENTF_LEFTUP = 0x0004;
  const uint MOUSEEVENTF_MOVE = 0x0001;
  const int SW_RESTORE = 9;

  static void SendKey(ushort vk, ushort scan, bool up) {
    INPUT inp = new INPUT();
    inp.type = INPUT_KEYBOARD;
    inp.U.ki.wVk = vk;
    inp.U.ki.wScan = scan;
    inp.U.ki.dwFlags = up ? KEYEVENTF_KEYUP : 0;
    inp.U.ki.time = 0;
    inp.U.ki.dwExtraInfo = IntPtr.Zero;
    SendInput(1, new INPUT[] { inp }, Marshal.SizeOf(typeof(INPUT)));
  }

  static void PressVk(ushort vk) {
    ushort scan = (ushort)MapVirtualKey(vk, 0);
    SendKey(vk, scan, false);
    SendKey(vk, scan, true);
    Thread.Sleep(80);
  }

  static void TypeText(string s) {
    foreach (char c in s) {
      short sc = VkKeyScan(c);
      if (sc == -1) continue;
      ushort vk = (ushort)(sc & 0xFF);
      bool shift = (sc & 0x100) != 0;
      if (shift) SendKey(0x10, 0, false);
      ushort scan = (ushort)MapVirtualKey(vk, 0);
      SendKey(vk, scan, false);
      SendKey(vk, scan, true);
      if (shift) SendKey(0x10, 0, true);
      Thread.Sleep(100);
    }
  }

  static void ClickAt(int x, int y) {
    SetCursorPos(x, y);
    Thread.Sleep(200);
    INPUT m = new INPUT();
    m.type = INPUT_MOUSE;
    m.U.mi.dx = 0;
    m.U.mi.dy = 0;
    m.U.mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
    SendInput(1, new INPUT[] { m }, Marshal.SizeOf(typeof(INPUT)));
    Thread.Sleep(80);
    m.U.mi.dwFlags = MOUSEEVENTF_LEFTUP;
    SendInput(1, new INPUT[] { m }, Marshal.SizeOf(typeof(INPUT)));
    Thread.Sleep(250);
  }

  static string FocusedInfo() {
    try {
      AutomationElement el = AutomationElement.FocusedElement;
      if (el == null) return "<null>";
      object pattern;
      string v = null;
      if (el.TryGetCurrentPattern(ValuePattern.Pattern, out pattern)) {
        v = ((ValuePattern)pattern).Current.Value;
      }
      string t = null;
      if (el.TryGetCurrentPattern(TextPattern.Pattern, out pattern)) {
        t = ((TextPattern)pattern).DocumentRange.GetText(2000);
      }
      System.Windows.Rect r = el.Current.BoundingRectangle;
      return "class=" + el.Current.ClassName + " rect=" + r.X + "," + r.Y + "," + r.Width + "," + r.Height +
             " V=[" + v + "] T=[" + t + "]";
    } catch (Exception ex) {
      return "<err " + ex.Message + ">";
    }
  }

  static string ClipboardNow() {
    try {
      SendKey(0x11, 0, false);
      PressVk(0x41);
      PressVk(0x43);
      SendKey(0x11, 0, true);
      Thread.Sleep(400);
      return Clipboard.GetText();
    } catch (Exception ex) {
      return "<err " + ex.Message + ">";
    }
  }

  static List<IntPtr> _windows = new List<IntPtr>();

  static bool EnumProc(IntPtr hWnd, IntPtr lParam) {
    uint pid;
    GetWindowThreadProcessId(hWnd, out pid);
    if (pid == 0) return true;
    System.Diagnostics.Process proc = null;
    try { proc = System.Diagnostics.Process.GetProcessById((int)pid); } catch { return true; }
    if (proc != null && proc.ProcessName.ToLowerInvariant() == "notepad" && IsWindowVisible(hWnd)) {
      _windows.Add(hWnd);
    }
    return true;
  }

  static void Main() {
    Console.OutputEncoding = Encoding.UTF8;
    try {
      System.Diagnostics.Process.Start("notepad.exe");
      Thread.Sleep(2500);

      _windows.Clear();
      EnumWindows(EnumProc, IntPtr.Zero);
      Console.WriteLine("notepad windows: " + _windows.Count);
      IntPtr target = IntPtr.Zero;
      foreach (IntPtr w in _windows) {
        StringBuilder sb = new StringBuilder(256);
        GetWindowText(w, sb, 256);
        Console.WriteLine("win=0x" + w.ToInt64().ToString("X") + " iconic=" + IsIconic(w) + " title=[" + sb + "]");
        if (target == IntPtr.Zero && sb.ToString().StartsWith("无标题")) target = w;
        if (target == IntPtr.Zero) target = w;
      }
      if (target == IntPtr.Zero) {
        Console.WriteLine("no notepad window found");
        return;
      }

      ShowWindow(target, SW_RESTORE);
      Thread.Sleep(300);
      SetForegroundWindow(target);
      Thread.Sleep(500);
      Console.WriteLine("fg=0x" + GetForegroundWindow().ToInt64().ToString("X") + " target=0x" + target.ToInt64().ToString("X"));
      Console.WriteLine("focused=[" + FocusedInfo() + "]");

      // click the focused element's center only if it is not already a text editor
      try {
        AutomationElement el = AutomationElement.FocusedElement;
        string cls = el.Current.ClassName;
        if (cls == null || (cls.IndexOf("Edit", StringComparison.OrdinalIgnoreCase) < 0 && cls.IndexOf("RichEdit", StringComparison.OrdinalIgnoreCase) < 0)) {
          System.Windows.Rect r = el.Current.BoundingRectangle;
          if (r.Width > 50 && r.Height > 20) {
            ClickAt((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2));
          }
        }
      } catch { }
      Console.WriteLine("after click fg=0x" + GetForegroundWindow().ToInt64().ToString("X"));
      Console.WriteLine("focused=[" + FocusedInfo() + "]");

      PressVk(0x10);
      Thread.Sleep(400);
      TypeText("hello");
      Thread.Sleep(500);
      Console.WriteLine("EN focused=[" + FocusedInfo() + "]");
      Console.WriteLine("EN clip=[" + ClipboardNow() + "]");

      PressVk(0x10);
      Thread.Sleep(400);
      TypeText("nihao");
      Thread.Sleep(600);
      Console.WriteLine("CN composing focused=[" + FocusedInfo() + "]");
      PressVk(0x20);
      Thread.Sleep(700);
      Console.WriteLine("CN after space focused=[" + FocusedInfo() + "]");
      Console.WriteLine("CN after space clip=[" + ClipboardNow() + "]");
    } catch (Exception ex) {
      Console.WriteLine("ERR: " + ex);
    }
    Console.WriteLine("DONE");
  }
}
