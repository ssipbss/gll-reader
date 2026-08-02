using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

class ProbeMouseLL {
  [StructLayout(LayoutKind.Sequential)]
  private struct MSLLHOOKSTRUCT {
    public int ptX;
    public int ptY;
    public uint mouseData;
    public uint flags;
    public uint time;
    public IntPtr dwExtraInfo;
  }

  private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

  [DllImport("user32.dll", SetLastError = true)]
  private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
  [DllImport("user32.dll")]
  private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
  [DllImport("user32.dll")]
  private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMin, uint wMax);
  [DllImport("user32.dll")]
  private static extern bool TranslateMessage(ref MSG lpMsg);
  [DllImport("user32.dll")]
  private static extern IntPtr DispatchMessage(ref MSG lpMsg);
  [DllImport("kernel32.dll")]
  private static extern IntPtr GetModuleHandle(string name);

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

  private const int WH_MOUSE_LL = 14;
  private static string _log;
  private static HookProc _proc;
  private static IntPtr _hook;

  private static IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam) {
    if (nCode >= 0) {
      try {
        MSLLHOOKSTRUCT m = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
        File.AppendAllText(_log, DateTime.Now.ToString("HH:mm:ss.fff") + " msg=0x" +
          ((uint)wParam.ToInt64()).ToString("X") + " flags=0x" + m.flags.ToString("X") + "\r\n");
      } catch { }
    }
    return CallNextHookEx(_hook, nCode, wParam, lParam);
  }

  private static void Main(string[] args) {
    _log = args.Length > 0 ? args[0] : "C:\\tmp\\promouse.log";
    _proc = Callback;
    _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
    MSG msg;
    DateTime end = DateTime.Now.AddSeconds(8);
    while (DateTime.Now < end && GetMessage(out msg, IntPtr.Zero, 0, 0)) {
      TranslateMessage(ref msg);
      DispatchMessage(ref msg);
    }
  }
}
