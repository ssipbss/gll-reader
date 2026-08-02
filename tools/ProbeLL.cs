using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

class ProbeLL {
  [StructLayout(LayoutKind.Sequential)]
  private struct KBDLLHOOKSTRUCT {
    public uint vkCode;
    public uint scanCode;
    public uint flags;
    public uint time;
    public IntPtr dwExtraInfo;
  }

  [DllImport("user32.dll", SetLastError = true)]
  private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool UnhookWindowsHookEx(IntPtr hhk);

  [DllImport("user32.dll")]
  private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

  [DllImport("kernel32.dll")]
  private static extern IntPtr GetModuleHandle(string lpModuleName);

  [DllImport("user32.dll")]
  private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

  [DllImport("user32.dll")]
  private static extern bool TranslateMessage(ref MSG lpMsg);

  [DllImport("user32.dll")]
  private static extern IntPtr DispatchMessage(ref MSG lpMsg);

  private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

  [StructLayout(LayoutKind.Sequential)]
  private struct MSG {
    public IntPtr hwnd;
    public uint message;
    public IntPtr wParam;
    public IntPtr lParam;
    public uint time;
    public int ptX;
    public int ptY;
  }

  private const int WH_KEYBOARD_LL = 13;
  private const uint WM_KEYDOWN = 0x0100;
  private const uint WM_KEYUP = 0x0101;
  private const uint WM_SYSKEYDOWN = 0x0104;
  private const uint WM_SYSKEYUP = 0x0105;

  private static string _log;
  private static HookProc _proc;
  private static IntPtr _hook;

  private static IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam) {
    if (nCode >= 0) {
      try {
        uint msg = (uint)wParam.ToInt64();
        if (msg == WM_KEYDOWN || msg == WM_KEYUP || msg == WM_SYSKEYDOWN || msg == WM_SYSKEYUP) {
          KBDLLHOOKSTRUCT kbd = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
          File.AppendAllText(_log, DateTime.Now.ToString("HH:mm:ss.fff") + " msg=0x" + msg.ToString("X") +
            " vk=0x" + kbd.vkCode.ToString("X") + " scan=0x" + kbd.scanCode.ToString("X") +
            " flags=0x" + kbd.flags.ToString("X") + "\r\n");
        }
      } catch { }
    }
    return CallNextHookEx(_hook, nCode, wParam, lParam);
  }

  private static void Main(string[] args) {
    _log = args.Length > 0 ? args[0] : "C:\\tmp\\probell.log";
    _proc = Callback;
    _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
    File.AppendAllText(_log, DateTime.Now.ToString("HH:mm:ss.fff") + " HOOK=" + _hook + "\r\n");
    if (_hook == IntPtr.Zero) return;
    MSG msg;
    DateTime end = DateTime.Now.AddSeconds(8);
    while (DateTime.Now < end && GetMessage(out msg, IntPtr.Zero, 0, 0)) {
      TranslateMessage(ref msg);
      DispatchMessage(ref msg);
    }
    UnhookWindowsHookEx(_hook);
    File.AppendAllText(_log, DateTime.Now.ToString("HH:mm:ss.fff") + " DONE\r\n");
  }
}
