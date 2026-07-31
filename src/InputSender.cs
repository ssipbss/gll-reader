using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace GenDaLangDu {
  public static class InputSender {
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT {
      public int type;
      public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion {
      [FieldOffset(0)]
      public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT {
      public ushort wVk;
      public ushort wScan;
      public uint dwFlags;
      public uint time;
      public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short VkKeyScan(char ch);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    public static bool LastOk = true;

    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private static void SendKey(ushort vk, ushort scan, bool up) {
      INPUT inp = new INPUT();
      inp.type = INPUT_KEYBOARD;
      inp.U.ki.wVk = vk;
      inp.U.ki.wScan = scan;
      inp.U.ki.dwFlags = up ? KEYEVENTF_KEYUP : 0;
      inp.U.ki.time = 0;
      inp.U.ki.dwExtraInfo = IntPtr.Zero;
      uint r = SendInput(1, new INPUT[] { inp }, Marshal.SizeOf(typeof(INPUT)));
      if (r == 0) LastOk = false;
    }

    public static void PressVk(ushort vk) {
      ushort scan = (ushort)MapVirtualKey(vk, 0);
      SendKey(vk, scan, false);
      SendKey(vk, scan, true);
      Thread.Sleep(30);
    }

    public static ushort ScanOf(ushort vk) {
      return (ushort)MapVirtualKey(vk, 0);
    }

    public static void TypeText(string s) {
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
        Thread.Sleep(35);
      }
    }
  }
}
