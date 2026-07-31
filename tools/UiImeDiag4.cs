using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

class UiImeDiag4 {
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
  static extern bool SetForegroundWindow(IntPtr hWnd);

  [DllImport("user32.dll")]
  static extern IntPtr GetForegroundWindow();

  [DllImport("user32.dll")]
  static extern bool BringWindowToTop(IntPtr hWnd);

  [DllImport("user32.dll")]
  static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

  [DllImport("user32.dll")]
  static extern bool IsWindowVisible(IntPtr hWnd);

  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

  [DllImport("user32.dll", SetLastError = true)]
  static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

  [DllImport("user32.dll")]
  static extern short VkKeyScan(char ch);

  [DllImport("user32.dll")]
  static extern uint MapVirtualKey(uint uCode, uint uMapType);

  [DllImport("user32.dll")]
  static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

  delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

  [DllImport("user32.dll")]
  static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

  const int INPUT_KEYBOARD = 1;
  const uint KEYEVENTF_KEYUP = 0x0002;
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
    Thread.Sleep(100);
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
      Thread.Sleep(150);
    }
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct RECT {
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
  }

  [DllImport("user32.dll")]
  static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

  [DllImport("user32.dll")]
  static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

  const uint SWP_NOZORDER = 0x0004;
  const uint SWP_NOACTIVATE = 0x0010;

  [DllImport("user32.dll")]
  static extern bool SetCursorPos(int x, int y);

  const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
  const uint MOUSEEVENTF_LEFTUP = 0x0004;

  static void ClickAt(int x, int y) {
    SetCursorPos(x, y);
    Thread.Sleep(200);
    INPUT m = new INPUT();
    m.type = 0;
    m.U.mi.dx = 0;
    m.U.mi.dy = 0;
    m.U.mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
    SendInput(1, new INPUT[] { m }, Marshal.SizeOf(typeof(INPUT)));
    Thread.Sleep(80);
    m.U.mi.dwFlags = MOUSEEVENTF_LEFTUP;
    SendInput(1, new INPUT[] { m }, Marshal.SizeOf(typeof(INPUT)));
    Thread.Sleep(250);
  }

  static string FocusedClass() {
    try {
      AutomationElement el = AutomationElement.FocusedElement;
      return el == null ? "<null>" : (el.Current.ClassName ?? "");
    } catch (Exception ex) {
      return "<err " + ex.Message + ">";
    }
  }

  static string Head(string s) {
    if (string.IsNullOrEmpty(s)) return s ?? "";
    return s.Length <= 30 ? s : s.Substring(0, 30);
  }

  static string FocusedValue() {
    try {
      AutomationElement el = AutomationElement.FocusedElement;
      if (el == null) return "<null>";
      object pattern;
      if (el.TryGetCurrentPattern(ValuePattern.Pattern, out pattern)) {
        return ((ValuePattern)pattern).Current.Value;
      }
      return "<noValue class=" + el.Current.ClassName + ">";
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
      IntPtr target = IntPtr.Zero;
      foreach (IntPtr w in _windows) {
        StringBuilder sb = new StringBuilder(256);
        GetWindowText(w, sb, 256);
        if (target == IntPtr.Zero && sb.ToString().StartsWith("无标题")) target = w;
        if (target == IntPtr.Zero) target = w;
      }
      if (target == IntPtr.Zero) {
        Console.WriteLine("NO_NOTEPAD");
        return;
      }
      ShowWindow(target, SW_RESTORE);
      for (int i = 0; i < 5; i++) {
        BringWindowToTop(target);
        SetForegroundWindow(target);
        Thread.Sleep(400);
        if (GetForegroundWindow() == target) break;
      }
      Console.WriteLine("FG_OK=" + (GetForegroundWindow() == target));
      PressVk(0x1B);
      Thread.Sleep(300);
      string cls = FocusedClass();
      Console.WriteLine("focusedClass=[" + cls + "]");
      if (cls == null || (cls.IndexOf("Edit", StringComparison.OrdinalIgnoreCase) < 0 && cls.IndexOf("RichEdit", StringComparison.OrdinalIgnoreCase) < 0)) {
        Console.WriteLine("clicking edit area");
        RECT r;
        if (GetWindowRect(target, out r)) {
          ClickAt(r.Left + (r.Right - r.Left) / 2, r.Top + (r.Bottom - r.Top) / 2);
          Thread.Sleep(400);
        }
      }
      Console.WriteLine("after focus focusedClass=[" + FocusedClass() + "]");

      // clear the document
      SendKey(0x11, 0, false);
      PressVk(0x41);
      PressVk(0x2E);
      SendKey(0x11, 0, true);
      Thread.Sleep(400);
      Console.WriteLine("CLEARED=[" + FocusedValue() + "]");

      bool ok = false;
      for (int attempt = 1; attempt <= 2 && !ok; attempt++) {
        SendKey(0x11, 0, false);
        PressVk(0x41);
        PressVk(0x2E);
        SendKey(0x11, 0, true);
        Thread.Sleep(400);
        Console.WriteLine("ATTEMPT " + attempt + " cleared=[" + FocusedValue() + "]");
        if (attempt == 1) {
          TypeText("n");
          Thread.Sleep(150);
          PressVk(0x08);
          Thread.Sleep(200);
          Console.WriteLine("after backspace=[" + FocusedValue() + "]");
        }
        foreach (char c in "nihao") {
          TypeText(c.ToString());
          Console.WriteLine("after '" + c + "'=[" + FocusedValue() + "]");
        }
        PressVk(0x20);
        Thread.Sleep(900);
        string r = FocusedValue();
        Console.WriteLine("ATTEMPT " + attempt + " AFTER_SPACE=[" + r + "]");
        bool hasCjk = false;
        if (!string.IsNullOrEmpty(r)) {
          foreach (char c in r) {
            if (c >= 0x4E00 && c <= 0x9FFF) { hasCjk = true; break; }
          }
        }
        if (hasCjk) {
          ok = true;
        } else {
          Console.WriteLine("not Chinese, toggling IME");
          PressVk(0x10);
          Thread.Sleep(500);
        }
      }
      if (ok) {
        TypeText("hao");
        Thread.Sleep(200);
        PressVk(0x20);
        Thread.Sleep(200);
        TypeText("ma");
        Thread.Sleep(200);
        PressVk(0x20);
        Thread.Sleep(200);
        PressVk(0xBC);
        Thread.Sleep(700);
        Console.WriteLine("PHASE1.5 fast=[" + FocusedValue() + "]");
      }
      Thread.Sleep(2500);
      Console.WriteLine("PHASE2 real space");
      PressVk(0x20);
      Thread.Sleep(600);
      Console.WriteLine("PHASE2 after space=[" + FocusedValue() + "]");
      PressVk(0x0D);
      Thread.Sleep(600);
      Console.WriteLine("PHASE2 after enter=[" + FocusedValue() + "]");
      PressVk(0xBE);
      Thread.Sleep(900);
      Console.WriteLine("PHASE2 after dot=[" + FocusedValue() + "]");

      Console.WriteLine("PHASE2 backspace hold test");
      SendKey(0x08, 0x0E, false);
      Thread.Sleep(400);
      SendKey(0x08, 0x0E, false);
      Thread.Sleep(400);
      SendKey(0x08, 0x0E, false);
      Thread.Sleep(400);
      SendKey(0x08, 0x0E, true);
      Thread.Sleep(600);
      Console.WriteLine("after backspace hold=[" + Head(FocusedValue()) + "]");

      Console.WriteLine("PHASE2 punct test");
      TypeText(",");
      Thread.Sleep(500);
      Console.WriteLine("after comma=[" + Head(FocusedValue()) + "]");
      TypeText(";");
      Thread.Sleep(500);
      Console.WriteLine("after semicolon=[" + Head(FocusedValue()) + "]");
      TypeText(":");
      Thread.Sleep(700);
      Console.WriteLine("after colon=[" + Head(FocusedValue()) + "]");

      Console.WriteLine("PHASE2 shift-tap english test");
      PressVk(0x10);
      Thread.Sleep(400);
      TypeText("hello");
      Thread.Sleep(700);
      Console.WriteLine("after shift-tap hello=[" + Head(FocusedValue()) + "]");
      PressVk(0x10);
      Thread.Sleep(400);

      // PHASE3: click into another notepad window's text area (click-silence test)
      IntPtr other = IntPtr.Zero;
      foreach (IntPtr w in _windows) {
        if (w == target) continue;
        StringBuilder sb = new StringBuilder(256);
        GetWindowText(w, sb, 256);
        if (sb.ToString().IndexOf("使用说明", StringComparison.OrdinalIgnoreCase) >= 0) { other = w; break; }
      }
      if (other == IntPtr.Zero) {
        Console.WriteLine("NO_OTHER_NOTEPAD_FOR_CLICK_TEST");
      } else {
        ShowWindow(other, SW_RESTORE);
        RECT ra;
        RECT rb;
        RECT raOrig;
        RECT rbOrig;
        if (GetWindowRect(target, out ra) && GetWindowRect(other, out rb)) {
          raOrig = ra;
          rbOrig = rb;
          SetWindowPos(target, IntPtr.Zero, 50, 100, 800, 600, SWP_NOZORDER | SWP_NOACTIVATE);
          SetWindowPos(other, IntPtr.Zero, 900, 100, 800, 600, SWP_NOACTIVATE | 0x0040);
          Thread.Sleep(600);
          if (GetWindowRect(target, out ra) && GetWindowRect(other, out rb)) {
            Console.WriteLine("PHASE3 rects A=(" + ra.Left + "," + ra.Top + "," + ra.Right + "," + ra.Bottom + ") B=(" + rb.Left + "," + rb.Top + "," + rb.Right + "," + rb.Bottom + ")");
            ClickAt(rb.Left + 150, rb.Top + 120);
            Thread.Sleep(1200);
            Console.WriteLine("PHASE3 after click=[" + Head(FocusedValue()) + "]");
          }
          SetWindowPos(other, IntPtr.Zero, rbOrig.Left, rbOrig.Top, rbOrig.Right - rbOrig.Left, rbOrig.Bottom - rbOrig.Top, SWP_NOZORDER | SWP_NOACTIVATE);
          SetWindowPos(target, IntPtr.Zero, raOrig.Left, raOrig.Top, raOrig.Right - raOrig.Left, raOrig.Bottom - raOrig.Top, SWP_NOZORDER | SWP_NOACTIVATE);
        }
      }
      Thread.Sleep(1500);
    } catch (Exception ex) {
      Console.WriteLine("ERR: " + ex);
    }
    Console.WriteLine("DONE");
  }
}
