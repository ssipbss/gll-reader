using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

class SendKeysTool {
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

  [DllImport("user32.dll", SetLastError = true)]
  static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

  [DllImport("user32.dll")]
  static extern short VkKeyScan(char ch);

  [DllImport("user32.dll")]
  static extern uint MapVirtualKey(uint uCode, uint uMapType);

  [DllImport("kernel32.dll")]
  static extern IntPtr GetConsoleWindow();

  [DllImport("user32.dll")]
  static extern bool SetForegroundWindow(IntPtr hWnd);

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
    INPUT[] arr = new INPUT[] { inp };
    SendInput(1, arr, Marshal.SizeOf(typeof(INPUT)));
  }

  static void PressKey(ushort vk) {
    ushort scan = (ushort)MapVirtualKey(vk, 0);
    SendKey(vk, scan, false);
    SendKey(vk, scan, true);
    Thread.Sleep(25);
  }

  static void TypeText(string s) {
    foreach (char c in s) {
      short sc = VkKeyScan(c);
      if (sc == -1) continue;
      ushort vk = (ushort)(sc & 0xFF);
      bool shift = (sc & 0x100) != 0;
      bool ctrl = (sc & 0x200) != 0;
      bool alt = (sc & 0x400) != 0;
      if (shift) SendKey(0x10, 0, false);
      if (ctrl) SendKey(0x11, 0, false);
      if (alt) SendKey(0x12, 0, false);
      ushort scan = (ushort)MapVirtualKey(vk, 0);
      SendKey(vk, scan, false);
      SendKey(vk, scan, true);
      if (alt) SendKey(0x12, 0, true);
      if (ctrl) SendKey(0x11, 0, true);
      if (shift) SendKey(0x10, 0, true);
      Thread.Sleep(30);
    }
  }

  static void Main(string[] args) {
    try { SetForegroundWindow(GetConsoleWindow()); } catch { }
    foreach (string a in args) {
      if (a.StartsWith("VK:")) {
        ushort vk = ushort.Parse(a.Substring(3), NumberStyles.HexNumber);
        PressKey(vk);
      } else {
        TypeText(a);
      }
      Thread.Sleep(50);
    }
  }
}
