using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GenDaLangDu {
  public sealed class MouseHook : IDisposable {
    private const int WH_MOUSE_LL = 14;
    private const uint WM_LBUTTONDOWN = 0x201;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT {
      public int ptX;
      public int ptY;
      public uint mouseData;
      public uint flags;
      public uint time;
      public IntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private LowLevelMouseProc _proc;
    private IntPtr _hookId = IntPtr.Zero;

    public event Action LeftButtonDown;

    public bool IsInstalled {
      get { return _hookId != IntPtr.Zero; }
    }

    public void Install() {
      if (IsInstalled) return;
      _proc = Callback;
      IntPtr hMod = IntPtr.Zero;
      try {
        using (Process p = Process.GetCurrentProcess()) {
          hMod = GetModuleHandle(p.MainModule.ModuleName);
        }
      } catch { }
      _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, hMod, 0);
      if (_hookId == IntPtr.Zero) {
        _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, IntPtr.Zero, 0);
      }
    }

    public void Uninstall() {
      if (_hookId != IntPtr.Zero) {
        UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
      }
      _proc = null;
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam) {
      if (nCode >= 0 && (uint)wParam.ToInt64() == WM_LBUTTONDOWN) {
        try {
          Action h = LeftButtonDown;
          if (h != null) h();
        } catch { }
      }
      return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose() {
      Uninstall();
    }
  }
}
