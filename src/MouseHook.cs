using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

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
    private Thread _thread;
    private uint _threadId;
    private volatile bool _installed;
    private volatile bool _stopping;

    public event Action LeftButtonDown;

    public bool IsInstalled {
      get { return _installed; }
    }

    public void Install() {
      if (_thread != null && _thread.IsAlive) return;
      _stopping = false;
      _installed = false;
      _thread = new Thread(HookLoop);
      _thread.IsBackground = true;
      try { _thread.SetApartmentState(ApartmentState.STA); } catch { }
      _thread.Start();
      for (int i = 0; i < 300 && !_installed; i++) Thread.Sleep(10);
    }

    private void HookLoop() {
      _threadId = Native.GetCurrentThreadId();
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
      _installed = _hookId != IntPtr.Zero;
      try {
        Native.MSG msg;
        while (!_stopping) {
          int r = Native.GetMessage(out msg, IntPtr.Zero, 0, 0);
          if (r <= 0) break;
          Native.TranslateMessage(ref msg);
          Native.DispatchMessage(ref msg);
        }
      } catch { }
      if (_hookId != IntPtr.Zero) {
        UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
      }
      _installed = false;
      _proc = null;
    }

    public void Uninstall() {
      _stopping = true;
      uint tid = _threadId;
      if (tid != 0) {
        try { Native.PostThreadMessage(tid, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero); } catch { }
      }
      Thread t = _thread;
      if (t != null && t.IsAlive) t.Join(2000);
      _thread = null;
      _threadId = 0;
      _hookId = IntPtr.Zero;
      _installed = false;
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