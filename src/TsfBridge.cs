using System;
using System.Runtime.InteropServices;
using System.Text;

namespace GenDaLangDu {
  /// <summary>
  /// TSF/IMM 观察钩子的主程序侧桥：
  /// 1) 创建 GLL_TSF_NOTIFY 隐藏窗口，接收钩子 DLL 发来的上屏/组字消息；
  /// 2) 安装/卸载 64 位钩子 DLL；
  /// 3) 通过共享内存实时读取“输入法正在组字”状态（按键线程直接读，无消息延迟）。
  /// </summary>
  public sealed class TsfNotifyWindow : IDisposable {
    private const string WndClass = "GLL_TSF_NOTIFY";
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_NCDESTROY = 0x0082;

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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassW(ref WNDCLASS wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint ex, string cls, string name, uint style,
      int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GlobalGetAtomNameW(ushort atom, StringBuilder buf, int size);
    [DllImport("kernel32.dll")]
    private static extern ushort GlobalDeleteAtom(ushort atom);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string name);

    private static WndProc _staticProc;
    private IntPtr _hWnd;
    private uint _commitMsg;
    private uint _stateMsg;

    public event Action<uint, string> CommitReceived;
    public event Action<uint, bool> StateReceived;

    public IntPtr Handle {
      get { return _hWnd; }
    }

    public void Create() {
      if (_hWnd != IntPtr.Zero) return;
      _commitMsg = RegisterWindowMessageW("GLL_TSF_COMMIT");
      _stateMsg = RegisterWindowMessageW("GLL_TSF_STATE");
      _staticProc = WndProcImpl;
      WNDCLASS wc = new WNDCLASS();
      wc.lpfnWndProc = _staticProc;
      wc.hInstance = GetModuleHandle(null);
      wc.lpszClassName = WndClass;
      if (RegisterClassW(ref wc) == 0 && Marshal.GetLastWin32Error() != 1410 /* ERROR_CLASS_ALREADY_EXISTS */) {
        return;
      }
      _hWnd = CreateWindowExW(0, WndClass, "GLL TSF Notify", 0,
                              0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
    }

    private IntPtr WndProcImpl(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam) {
      if (_commitMsg != 0 && msg == _commitMsg) {
        try {
          ushort atom = (ushort)(lParam.ToInt64() & 0xFFFF);
          var sb = new StringBuilder(512);
          uint n = GlobalGetAtomNameW(atom, sb, 512);
          GlobalDeleteAtom(atom);
          if (n > 0) {
            string s = sb.ToString();
            int bar = s.IndexOf('|');
            uint pid;
            if (bar > 0 && uint.TryParse(s.Substring(0, bar), out pid)) {
              string text = s.Substring(bar + 1);
              Action<uint, string> h = CommitReceived;
              if (h != null) h(pid, text);
            }
          }
        } catch { }
        return IntPtr.Zero;
      }
      if (_stateMsg != 0 && msg == _stateMsg) {
        try {
          ushort atom = (ushort)(lParam.ToInt64() & 0xFFFF);
          var sb = new StringBuilder(64);
          uint n = GlobalGetAtomNameW(atom, sb, 64);
          GlobalDeleteAtom(atom);
          if (n > 0) {
            string s = sb.ToString();
            int bar = s.IndexOf('|');
            uint pid;
            if (bar > 0 && uint.TryParse(s.Substring(0, bar), out pid)) {
              string state = s.Substring(bar + 1);
              Action<uint, bool> h = StateReceived;
              if (h != null) h(pid, state == "1");
            }
          }
        } catch { }
        return IntPtr.Zero;
      }
      if (msg == WM_NCDESTROY) {
        _hWnd = IntPtr.Zero;
      }
      return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    public void Dispose() {
      if (_hWnd != IntPtr.Zero) {
        try { DestroyWindow(_hWnd); } catch { }
        _hWnd = IntPtr.Zero;
      }
    }
  }

  /// <summary>钩子 DLL 加载与共享内存状态读取。</summary>
  public sealed class TsfBridge : IDisposable {
    private const string ShmName = @"Local\GLL_TSF_STATE_V3";
    private const int ShmEntries = 4096;
    private const int ShmMagic = 0x474C4C33;

    [StructLayout(LayoutKind.Sequential)]
    private struct ShmEntry {
      public int pid;
      public int composing;
      public int tick;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShmHeader {
      public int magic;
      public int version;
      public int count;
      public int reserved;
      [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4096)]
      public ShmEntry[] entries;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string proc);
    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr hModule);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenFileMappingW(uint dwDesiredAccess, bool bInheritHandle, string name);
    [DllImport("kernel32.dll")]
    private static extern IntPtr MapViewOfFile(IntPtr hMapping, uint dwDesiredAccess, uint offHigh, uint offLow, UIntPtr bytes);
    [DllImport("kernel32.dll")]
    private static extern bool UnmapViewOfFile(IntPtr addr);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr h);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool InstallFn();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void SetDebugFn(int on);

    private IntPtr _module;
    private IntPtr _shmFile;
    private IntPtr _shmView;
    private ShmHeader _header;
    private bool _installed;
    public string LastError = "";

    public bool Installed {
      get { return _installed; }
    }

    public bool Install(string dllPath, bool debug) {
      try {
        if (_installed) { LastError = ""; return true; }
        LastError = "";
        if (string.IsNullOrEmpty(dllPath)) { LastError = "empty path"; return false; }
        if (!System.IO.File.Exists(dllPath)) {
          LastError = "file not exists: " + dllPath;
          return false;
        }
        _module = LoadLibrary(dllPath);
        if (_module == IntPtr.Zero) {
          LastError = "LoadLibrary err=" + Marshal.GetLastWin32Error();
          return false;
        }
        IntPtr pInstall = GetProcAddress(_module, "GllInstallHook");
        if (pInstall == IntPtr.Zero) {
          LastError = "GetProcAddress err=" + Marshal.GetLastWin32Error();
          FreeLibrary(_module);
          _module = IntPtr.Zero;
          return false;
        }
        InstallFn fn = (InstallFn)Marshal.GetDelegateForFunctionPointer(pInstall, typeof(InstallFn));
        _installed = fn();
        if (!_installed) LastError = "GllInstallHook returned false";
        if (debug) {
          IntPtr pDebug = GetProcAddress(_module, "GllSetDebug");
          if (pDebug != IntPtr.Zero) {
            SetDebugFn df = (SetDebugFn)Marshal.GetDelegateForFunctionPointer(pDebug, typeof(SetDebugFn));
            df(1);
          }
        }
        OpenShm();
        return _installed;
      } catch (Exception ex) {
        LastError = "exception: " + ex.Message;
        return false;
      }
    }

    public bool Uninstall() {
      try {
        if (_module != IntPtr.Zero) {
          IntPtr p = GetProcAddress(_module, "GllUninstallHook");
          if (p != IntPtr.Zero) {
            InstallFn fn = (InstallFn)Marshal.GetDelegateForFunctionPointer(p, typeof(InstallFn));
            try { fn(); } catch { }
          }
          FreeLibrary(_module);
          _module = IntPtr.Zero;
        }
        _installed = false;
        return true;
      } catch {
        return false;
      }
    }

    private void OpenShm() {
      try {
        if (_shmView != IntPtr.Zero) return;
        _shmFile = OpenFileMappingW(0x0004 /* FILE_MAP_READ */, false, ShmName);
        if (_shmFile == IntPtr.Zero) return;
        _shmView = MapViewOfFile(_shmFile, 0x0004, 0, 0, UIntPtr.Zero);
        if (_shmView == IntPtr.Zero) {
          CloseHandle(_shmFile);
          _shmFile = IntPtr.Zero;
          return;
        }
        _header = (ShmHeader)Marshal.PtrToStructure(_shmView, typeof(ShmHeader));
        if (_header.magic != ShmMagic || _header.count != ShmEntries) {
          UnmapViewOfFile(_shmView);
          CloseHandle(_shmFile);
          _shmView = IntPtr.Zero;
          _shmFile = IntPtr.Zero;
        }
      } catch { }
    }

    /// <summary>输入法是否正在组字（按键线程可直接调用，无锁无消息延迟）。</summary>
    public bool IsComposing(uint pid) {
      if (pid == 0) return false;
      if (_shmView == IntPtr.Zero) OpenShm();
      if (_shmView == IntPtr.Zero) return false;
      try {
        int index = (int)(pid % ShmEntries);
        IntPtr p = IntPtr.Add(_shmView, 16 + index * 12);
        int entryPid = Marshal.ReadInt32(p);
        if (entryPid != (int)pid) return false;
        return Marshal.ReadInt32(p, 4) != 0;
      } catch {
        return false;
      }
    }

    public void Dispose() {
      Uninstall();
      if (_shmView != IntPtr.Zero) {
        try { UnmapViewOfFile(_shmView); } catch { }
        _shmView = IntPtr.Zero;
      }
      if (_shmFile != IntPtr.Zero) {
        try { CloseHandle(_shmFile); } catch { }
        _shmFile = IntPtr.Zero;
      }
    }
  }
}
