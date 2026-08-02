using System;
using System.Runtime.InteropServices;
using System.Threading;

class KeyProbe {
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
  private static extern uint MapVirtualKey(uint uCode, uint uMapType);

  [DllImport("user32.dll")]
  private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

  private const int INPUT_KEYBOARD = 1;
  private const uint KEYEVENTF_KEYUP = 0x0002;
  private const uint KEYEVENTF_SCANCODE = 0x0008;

  private static void SendKey(ushort vk, bool up) {
    INPUT inp = new INPUT();
    inp.type = INPUT_KEYBOARD;
    inp.U.ki.wVk = vk;
    inp.U.ki.wScan = (ushort)MapVirtualKey(vk, 0);
    inp.U.ki.dwFlags = up ? KEYEVENTF_KEYUP : 0;
    inp.U.ki.time = 0;
    inp.U.ki.dwExtraInfo = IntPtr.Zero;
    SendInput(1, new INPUT[] { inp }, Marshal.SizeOf(typeof(INPUT)));
  }

  private static void SendCtrlCScan() {
    /* 用扫描码方式注入（部分应用对虚拟键注入敏感） */
    byte ctrlScan = (byte)MapVirtualKey(0x11, 0);
    byte cScan = (byte)MapVirtualKey(0x43, 0);
    INPUT[] seq = new INPUT[4];
    seq[0] = Key(0x11, ctrlScan, 0, KEYEVENTF_SCANCODE);
    seq[1] = Key(0x43, cScan, 0, KEYEVENTF_SCANCODE);
    seq[2] = Key(0x43, cScan, KEYEVENTF_KEYUP, KEYEVENTF_SCANCODE);
    seq[3] = Key(0x11, ctrlScan, KEYEVENTF_KEYUP, KEYEVENTF_SCANCODE);
    SendInput(4, seq, Marshal.SizeOf(typeof(INPUT)));
  }

  private static INPUT Key(ushort vk, byte scan, uint flags, uint extraFlags) {
    INPUT inp = new INPUT();
    inp.type = INPUT_KEYBOARD;
    inp.U.ki.wVk = vk;
    inp.U.ki.wScan = scan;
    inp.U.ki.dwFlags = flags | extraFlags;
    inp.U.ki.time = 0;
    inp.U.ki.dwExtraInfo = IntPtr.Zero;
    return inp;
  }

  private static void SendCtrlCKeybd() {
    keybd_event(0x11, 0x1D, 0, UIntPtr.Zero);
    keybd_event(0x43, 0x2E, 0, UIntPtr.Zero);
    keybd_event(0x43, 0x2E, KEYEVENTF_KEYUP, UIntPtr.Zero);
    keybd_event(0x11, 0x1D, KEYEVENTF_KEYUP, UIntPtr.Zero);
  }

  private static void Main(string[] args) {
    /* 用法：KeyProbe ctrl+a / ctrl+c */
    string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "ctrl+a";
    if (cmd == "ctrl+a") {
      SendKey(0x11, false);
      SendKey(0x41, false);
      SendKey(0x41, true);
      SendKey(0x11, true);
    } else if (cmd == "ctrl+c" || cmd == "ctrl+c:scan") {
      if (cmd == "ctrl+c:scan") { SendCtrlCScan(); Thread.Sleep(50); return; }
      SendKey(0x11, false);
      SendKey(0x43, false);
      SendKey(0x43, true);
      SendKey(0x11, true);
    } else if (cmd == "ctrl+c:keybd") {
      SendCtrlCKeybd();
    }
    Thread.Sleep(50);
  }
}
