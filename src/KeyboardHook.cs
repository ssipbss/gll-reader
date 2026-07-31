using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GenDaLangDu {
  public sealed class KeyHookEventArgs : EventArgs {
    public uint Vk;
    public uint Scan;
    public bool IsUp;
    public bool IsSysKey;
    public bool IsAutoRepeat;
  }

  public sealed class KeyboardHook : IDisposable {
    private Native.LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;
    private bool[] _down = new bool[256];

    public static Action<string> DebugLog;


    public event EventHandler<KeyHookEventArgs> KeyEvent;

    public bool IsInstalled {
      get { return _hookId != IntPtr.Zero; }
    }

    public void Install() {
      if (IsInstalled) return;
      _proc = Callback;
      IntPtr hMod = IntPtr.Zero;
      try {
        using (Process p = Process.GetCurrentProcess()) {
          hMod = Native.GetModuleHandle(p.MainModule.ModuleName);
        }
      } catch { }
      _hookId = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc, hMod, 0);
      if (_hookId == IntPtr.Zero) {
        _hookId = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc, IntPtr.Zero, 0);
      }
    }

    public void Uninstall() {
      if (_hookId != IntPtr.Zero) {
        Native.UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
      }
      _proc = null;
      Array.Clear(_down, 0, _down.Length);
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam) {
      if (nCode >= 0) {
        try {
          uint msg = (uint)wParam.ToInt64();
          if (msg == Native.WM_KEYDOWN || msg == Native.WM_KEYUP ||
              msg == Native.WM_SYSKEYDOWN || msg == Native.WM_SYSKEYUP) {
            Native.KBDLLHOOKSTRUCT kbd = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
            KeyHookEventArgs args = new KeyHookEventArgs();
            args.Vk = kbd.vkCode;
            args.Scan = kbd.scanCode;
            args.IsUp = (kbd.flags & Native.LLKHF_UP) != 0;
            args.IsSysKey = (msg == Native.WM_SYSKEYDOWN || msg == Native.WM_SYSKEYUP);
            if (!args.IsUp) {
              args.IsAutoRepeat = _down[kbd.vkCode & 0xFF];
              _down[kbd.vkCode & 0xFF] = true;
            } else {
              _down[kbd.vkCode & 0xFF] = false;
            }
            if (DebugLog != null) DebugLog("HOOK vk=0x" + kbd.vkCode.ToString("X") + " up=" + args.IsUp);
            EventHandler<KeyHookEventArgs> h = KeyEvent;
            if (h != null) h(this, args);
          }
        } catch { }
      }
      return Native.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose() {
      Uninstall();
    }
  }
}
