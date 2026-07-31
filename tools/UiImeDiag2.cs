using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

class UiImeDiag2 {
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

  [StructLayout(LayoutKind.Sequential)]
  public struct RECT {
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
  }

  [DllImport("user32.dll")]
  static extern IntPtr GetForegroundWindow();

  [DllImport("user32.dll")]
  static extern bool SetForegroundWindow(IntPtr hWnd);

  [DllImport("user32.dll")]
  static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

  [DllImport("user32.dll", SetLastError = true)]
  static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

  [DllImport("user32.dll")]
  static extern short VkKeyScan(char ch);

  [DllImport("user32.dll")]
  static extern uint MapVirtualKey(uint uCode, uint uMapType);

  const int INPUT_KEYBOARD = 1;
  const int INPUT_MOUSE = 0;
  const uint KEYEVENTF_KEYUP = 0x0002;
  const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
  const uint MOUSEEVENTF_LEFTUP = 0x0004;
  const uint MOUSEEVENTF_MOVE = 0x0001;

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

  static void ClickCenter(IntPtr hwnd) {
    RECT r;
    if (!GetWindowRect(hwnd, out r)) return;
    int x = (r.Left + r.Right) / 2;
    int y = (r.Top + r.Bottom) / 2;
    INPUT m = new INPUT();
    m.type = INPUT_MOUSE;
    m.U.mi.dx = x;
    m.U.mi.dy = y;
    m.U.mi.dwFlags = MOUSEEVENTF_MOVE;
    SendInput(1, new INPUT[] { m }, Marshal.SizeOf(typeof(INPUT)));
    Thread.Sleep(150);
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
      return "class=" + el.Current.ClassName + " V=[" + v + "] T=[" + t + "]";
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

  static void Main() {
    Console.OutputEncoding = Encoding.UTF8;
    Process p = null;
    try {
      Process.Start("notepad.exe");
      Thread.Sleep(2500);
      Process[] procs = Process.GetProcessesByName("notepad");
      DateTime newest = DateTime.MinValue;
      IntPtr h = IntPtr.Zero;
      foreach (Process q in procs) {
        try {
          q.Refresh();
          if (q.MainWindowHandle != IntPtr.Zero && q.StartTime > newest) {
            newest = q.StartTime;
            h = q.MainWindowHandle;
            p = q;
          }
        } catch { }
      }
      Console.WriteLine("hwnd=0x" + h.ToInt64().ToString("X"));
      if (h == IntPtr.Zero) return;
      SetForegroundWindow(h);
      Thread.Sleep(500);
      ClickCenter(h);
      Console.WriteLine("after click fg=0x" + GetForegroundWindow().ToInt64().ToString("X"));
      Console.WriteLine("focused=[" + FocusedInfo() + "]");

      // English test first (toggle with Shift to be safe)
      PressVk(0x10);
      Thread.Sleep(400);
      TypeText("hello");
      Thread.Sleep(500);
      Console.WriteLine("after hello focused=[" + FocusedInfo() + "]");
      Console.WriteLine("after hello clip=[" + ClipboardNow() + "]");

      // toggle back to Chinese and try pinyin
      PressVk(0x10);
      Thread.Sleep(400);
      TypeText("nihao");
      Thread.Sleep(600);
      Console.WriteLine("after nihao focused=[" + FocusedInfo() + "]");
      PressVk(0x20);
      Thread.Sleep(700);
      Console.WriteLine("after space focused=[" + FocusedInfo() + "]");
      Console.WriteLine("after space clip=[" + ClipboardNow() + "]");

      string afterSpace = Clipboard.GetText();
      if (string.IsNullOrEmpty(afterSpace) || afterSpace.Trim().Length == 0) {
        PressVk(0x0D);
        Thread.Sleep(500);
        Console.WriteLine("after enter focused=[" + FocusedInfo() + "]");
        Console.WriteLine("after enter clip=[" + ClipboardNow() + "]");
      }
    } catch (Exception ex) {
      Console.WriteLine("ERR: " + ex);
    } finally {
      if (p != null) {
        try { p.Kill(); } catch { }
      }
    }
    Console.WriteLine("DONE");
  }
}
