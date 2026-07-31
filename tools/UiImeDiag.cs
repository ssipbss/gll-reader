using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

class UiImeDiag {
  [StructLayout(LayoutKind.Sequential)]
  public struct INPUT {
    public int type;
    public InputUnion U;
  }

  [StructLayout(LayoutKind.Explicit)]
  public struct InputUnion {
    [FieldOffset(0)]
    public KEYBDINPUT ki;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct KEYBDINPUT {
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
  }

  [DllImport("user32.dll")]
  static extern IntPtr GetForegroundWindow();

  [DllImport("user32.dll")]
  static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

  [DllImport("user32.dll")]
  static extern IntPtr GetKeyboardLayout(uint tid);

  [DllImport("user32.dll")]
  static extern bool SetForegroundWindow(IntPtr hWnd);

  [DllImport("user32.dll", SetLastError = true)]
  static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

  [DllImport("user32.dll")]
  static extern short VkKeyScan(char ch);

  [DllImport("user32.dll")]
  static extern uint MapVirtualKey(uint uCode, uint uMapType);

  [DllImport("user32.dll")]
  static extern bool GetKeyboardState(byte[] lpKeyState);

  [DllImport("user32.dll")]
  static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState, StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);

  [DllImport("imm32.dll")]
  static extern IntPtr ImmGetContext(IntPtr hWnd);

  [DllImport("imm32.dll")]
  static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

  const int INPUT_KEYBOARD = 1;
  const uint KEYEVENTF_KEYUP = 0x0002;

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
    Thread.Sleep(60);
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
      Thread.Sleep(80);
    }
  }

  static void CtrlA() {
    SendKey(0x11, 0, false);
    PressVk(0x41);
    SendKey(0x11, 0, true);
    Thread.Sleep(80);
  }

  static string FocusedText() {
    try {
      AutomationElement el = AutomationElement.FocusedElement;
      if (el == null) return "<null>";
      object pattern;
      if (el.TryGetCurrentPattern(ValuePattern.Pattern, out pattern)) {
        return ((ValuePattern)pattern).Current.Value;
      }
      if (el.TryGetCurrentPattern(TextPattern.Pattern, out pattern)) {
        return ((TextPattern)pattern).DocumentRange.GetText(-1);
      }
      return "<noPattern name=" + el.Current.Name + ">";
    } catch (Exception ex) {
      return "<err " + ex.Message + ">";
    }
  }

  static string DiffInserted(string oldT, string newT) {
    if (string.IsNullOrEmpty(newT)) return "";
    if (string.IsNullOrEmpty(oldT)) return newT;
    if (newT.StartsWith(oldT)) return newT.Substring(oldT.Length);
    if (oldT.StartsWith(newT)) return "";
    int p = 0;
    int maxP = Math.Min(oldT.Length, newT.Length);
    while (p < maxP && oldT[p] == newT[p]) p++;
    int sOld = oldT.Length - 1;
    int sNew = newT.Length - 1;
    while (sOld >= p && sNew >= p && oldT[sOld] == newT[sNew]) {
      sOld--;
      sNew--;
    }
    if (sNew >= p) return newT.Substring(p, sNew - p + 1);
    return "";
  }

  static void LogT(string label, uint vk, uint scan) {
    try {
      IntPtr hwnd = GetForegroundWindow();
      uint pid;
      uint tid = GetWindowThreadProcessId(hwnd, out pid);
      IntPtr hkl = GetKeyboardLayout(tid);
      byte[] state = new byte[256];
      GetKeyboardState(state);
      StringBuilder sb = new StringBuilder(16);
      int r = ToUnicodeEx(vk, scan, state, sb, 16, 0, hkl);
      Console.WriteLine(label + " ToUnicodeEx(" + r + ") = [" + sb + "]");
    } catch (Exception ex) {
      Console.WriteLine(label + " ToUnicodeEx err: " + ex.Message);
    }
  }

  [STAThread]
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
      Console.WriteLine("notepad hwnd=0x" + h.ToInt64().ToString("X"));
      if (h != IntPtr.Zero) SetForegroundWindow(h);
      Thread.Sleep(600);
      Console.WriteLine("fg=0x" + GetForegroundWindow().ToInt64().ToString("X"));

      IntPtr imc = h == IntPtr.Zero ? IntPtr.Zero : ImmGetContext(h);
      Console.WriteLine("immGetContext=0x" + imc.ToInt64().ToString("X"));
      if (imc != IntPtr.Zero) ImmReleaseContext(h, imc);

      string before = FocusedText();
      Console.WriteLine("before=[" + before + "]");
      Console.WriteLine("fg before type=0x" + GetForegroundWindow().ToInt64().ToString("X"));

      TypeText("nihao");
      Thread.Sleep(600);
      string mid = FocusedText();
      Console.WriteLine("after nihao=[" + mid + "]");
      Console.WriteLine("fg before commit=0x" + GetForegroundWindow().ToInt64().ToString("X"));

      bool toggled = false;
      if (mid == "nihao") {
        Console.WriteLine("IME in English mode, toggling with Shift");
        PressVk(0x10);
        Thread.Sleep(400);
        CtrlA();
        PressVk(0x2E);
        Thread.Sleep(400);
        TypeText("nihao");
        Thread.Sleep(600);
        mid = FocusedText();
        Console.WriteLine("after retype=[" + mid + "]");
        toggled = true;
      }

      PressVk(0x20);
      Thread.Sleep(700);
      string after = FocusedText();
      Console.WriteLine("after commit=[" + after + "]");
      Console.WriteLine("inserted=[" + DiffInserted(mid, after) + "]");

      try {
        ClipboardSet();
        Console.WriteLine("clipboard=[" + System.Windows.Forms.Clipboard.GetText() + "]");
      } catch (Exception ex) {
        Console.WriteLine("clipboard err: " + ex.Message);
      }

      string tp = FocusedTextBoth();
      Console.WriteLine("focusedBoth=[" + tp + "]");

      LogT("space", 0x20, 0x39);
      LogT("A", 0x41, 0x1E);

      if (toggled) {
        PressVk(0x10);
        Thread.Sleep(300);
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

  static void ClipboardSet() {
    SendKey(0x11, 0, false);
    PressVk(0x41);
    PressVk(0x43);
    SendKey(0x11, 0, true);
    Thread.Sleep(400);
  }

  static string FocusedTextBoth() {
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
      return "V=[" + v + "] T=[" + t + "]";
    } catch (Exception ex) {
      return "<err " + ex.Message + ">";
    }
  }
}
