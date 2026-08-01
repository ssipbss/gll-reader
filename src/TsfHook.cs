using System;
using System.Runtime.InteropServices;
using System.Text;

namespace GenDaLangDu {
  /// <summary>
  /// TSF 提交观察宿主：创建隐藏窗口接收 gll_tsf_hook.dll 转发来的
  /// 输入法上屏文本（精确提交事件，不依赖文档差异）。
  /// </summary>
  internal static class TsfHook {
    public static event Action<string> CommitReceived;

    private const string WndClass = "GLL_TSF_NOTIFY";
    private const int WM_COPYDATA = 0x004A;
    private const int GLL_COPYDATA_ID = 0x47544C;
    private const int GllMsgSize = 4 + 512 * 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT {
      public IntPtr dwData;
      public int cbData;
      public IntPtr lpData;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS {
      public uint style;
      public WndProc lpfnWndProc;
      public int cbClsExtra;
      public int cbWndExtra;
      public IntPtr hInstance;
      public IntPtr hIcon;
      public IntPtr hCursor;
      public IntPtr hbrBackground;
      public string lpszMenuName;
      public string lpszClassName;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool InstallFn();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string proc);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern ushort RegisterClassW(ref WNDCLASS wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint ex, string cls, string name, uint style,
      int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string name);

    private static WndProc _wndProc;
    private static IntPtr _hwnd;
    private static IntPtr _hmod;
    private static IntPtr _install;
    private static IntPtr _uninstall;
    private static bool _started;
    private static string _lastError = "";

    public static string LastError {
      get { return _lastError; }
    }

    public static bool IsActive {
      get { return _hwnd != IntPtr.Zero && _install != IntPtr.Zero; }
    }

    public static void Init() {
      if (_started) return;
      _started = true;
      try {
        _wndProc = WndProcImpl;
        WNDCLASS wc = new WNDCLASS();
        wc.lpfnWndProc = _wndProc;
        wc.hInstance = GetModuleHandle(null);
        wc.lpszClassName = WndClass;
        if (RegisterClassW(ref wc) == 0) {
          int err = Marshal.GetLastWin32Error();
          if (err != 1410) { /* 1410 = 类已存在 */
            _lastError = "RegisterClass err=" + err;
            return;
          }
        }
        _hwnd = CreateWindowExW(0, WndClass, "GLL_TSF", 0, 0, 0, 0, 0,
          IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) {
          _lastError = "CreateWindow err=" + Marshal.GetLastWin32Error();
          return;
        }
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        string dll = System.IO.Path.Combine(dir, "gll_tsf_hook64.dll");
        if (!System.IO.File.Exists(dll)) {
          dll = System.IO.Path.Combine(Environment.CurrentDirectory, "gll_tsf_hook64.dll");
        }
        if (!System.IO.File.Exists(dll)) {
          _lastError = "gll_tsf_hook64.dll 不存在";
          return;
        }
        _hmod = LoadLibrary(dll);
        if (_hmod == IntPtr.Zero) {
          _lastError = "LoadLibrary err=" + Marshal.GetLastWin32Error();
          return;
        }
        _install = GetProcAddress(_hmod, "GllInstallHook");
        _uninstall = GetProcAddress(_hmod, "GllUninstallHook");
        if (_install == IntPtr.Zero) {
          _lastError = "GllInstallHook 导出缺失";
          return;
        }
        bool ok = Marshal.GetDelegateForFunctionPointer<InstallFn>(_install)();
        if (!ok) {
          _lastError = "GllInstallHook 失败";
          return;
        }
        _lastError = "";
      } catch (Exception ex) {
        _lastError = ex.Message;
      }
    }

    public static void Shutdown() {
      try {
        if (_uninstall != IntPtr.Zero) {
          Marshal.GetDelegateForFunctionPointer<InstallFn>(_uninstall)();
        }
      } catch { }
      try {
        if (_hmod != IntPtr.Zero) FreeLibrary(_hmod);
      } catch { }
      try {
        if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
      } catch { }
      _hwnd = IntPtr.Zero;
      _hmod = IntPtr.Zero;
      _install = IntPtr.Zero;
      _uninstall = IntPtr.Zero;
      _started = false;
    }

    private static IntPtr WndProcImpl(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam) {
      if (msg == WM_COPYDATA) {
        try {
          COPYDATASTRUCT cds = (COPYDATASTRUCT)Marshal.PtrToStructure(lParam, typeof(COPYDATASTRUCT));
          if (cds.dwData.ToInt32() == GLL_COPYDATA_ID && cds.cbData >= GllMsgSize) {
            byte[] raw = new byte[GllMsgSize];
            Marshal.Copy(cds.lpData, raw, 0, GllMsgSize);
            uint pid = BitConverter.ToUInt32(raw, 0);
            string text = Encoding.Unicode.GetString(raw, 4, 512 * 2);
            int n = text.IndexOf('\0');
            if (n >= 0) text = text.Substring(0, n);
            if (text.Length > 0 && CommitReceived != null) {
              CommitReceived(text);
            }
          }
        } catch { }
        return new IntPtr(1);
      }
      return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
  }
}
