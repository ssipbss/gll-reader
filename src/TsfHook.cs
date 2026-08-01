using System;
using System.Runtime.InteropServices;
using System.Text;

namespace GenDaLangDu {
  /// <summary>
  /// TSF 提交观察宿主：创建隐藏窗口接收 gll_tsf_hook.dll 转发来的
  /// 输入法上屏文本（精确提交事件，不依赖文档差异）。
  /// </summary>
  internal static class TsfHook {
    public static event Action<uint, string> CommitReceived;

    private const string WndClass = "GLL_TSF_NOTIFY";
    private const int WM_COPYDATA = 0x004A;
    private static uint _commitMsg;

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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort GlobalAddAtomW(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GlobalGetAtomNameW(ushort atom, StringBuilder buf, int size);
    [DllImport("kernel32.dll")]
    private static extern ushort GlobalDeleteAtom(ushort atom);

    private static WndProc _wndProc;
    private static IntPtr _hwnd;
    private static IntPtr _hmod;
    private static IntPtr _install;
    private static IntPtr _uninstall;
    private static bool _started;
    private static string _lastError = "";
    private static System.Diagnostics.Process _helper32;

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
        _commitMsg = RegisterWindowMessageW("GLL_TSF_COMMIT");
        string dll = FindDll("gll_tsf_hook64_v3.dll");
        if (dll == null) dll = FindDll("gll_tsf_hook64_v2.dll");
        if (dll == null) dll = FindDll("gll_tsf_hook64.dll");
        if (dll == null) {
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
        /* 启动 32 位钩子宿主，覆盖 32 位程序（32 位 WPS、部分桌面客户端） */
        string host32 = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gll_hook32_host.exe");
        if (!System.IO.File.Exists(host32)) {
          host32 = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gll_hook32_host.exe");
        }
        if (System.IO.File.Exists(host32)) {
          try {
            _helper32 = new System.Diagnostics.Process();
            _helper32.StartInfo.FileName = host32;
            _helper32.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            _helper32.StartInfo.CreateNoWindow = true;
            _helper32.Start();
          } catch { }
        }
        _lastError = "";
      } catch (Exception ex) {
        _lastError = ex.Message;
      }
    }

    private static string FindDll(string name) {
      try {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        string exeDll = System.IO.Path.Combine(dir, name);
        if (!System.IO.File.Exists(exeDll)) {
          exeDll = System.IO.Path.Combine(Environment.CurrentDirectory, name);
        }
        if (System.IO.File.Exists(exeDll)) return exeDll;
        string localDir = System.IO.Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GuiLingGuiLing");
        string localDll = System.IO.Path.Combine(localDir, name);
        try {
          System.IO.Directory.CreateDirectory(localDir);
        } catch { }
        if (System.IO.File.Exists(localDll)) return localDll;
      } catch { }
      return null;
    }

    public static void Shutdown() {
      try {
        if (_helper32 != null && !_helper32.HasExited) _helper32.Kill();
      } catch { }
      _helper32 = null;
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
      if (_commitMsg != 0 && msg == _commitMsg) {
        try {
          ushort atom = (ushort)(lParam.ToInt64() & 0xFFFF);
          if (atom != 0) {
            StringBuilder sb = new StringBuilder(512);
            uint n = GlobalGetAtomNameW(atom, sb, 512);
            GlobalDeleteAtom(atom);
            if (n > 0) {
              string s = sb.ToString();
              int bar = s.IndexOf('|');
              string text = bar >= 0 ? s.Substring(bar + 1) : s;
              if (text.Length > 0 && CommitReceived != null) {
                uint pid = (uint)wParam.ToInt64();
                CommitReceived(pid, text);
              }
            }
          }
        } catch { }
        return IntPtr.Zero;
      }
      return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
  }
}
