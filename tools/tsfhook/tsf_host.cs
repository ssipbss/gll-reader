using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public static class TsfHost {
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  public struct WNDCLASS {
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

  [StructLayout(LayoutKind.Sequential)]
  public struct COPYDATASTRUCT {
    public IntPtr dwData;
    public int cbData;
    public IntPtr lpData;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct MSG {
    public IntPtr hwnd;
    public uint message;
    public IntPtr wParam;
    public IntPtr lParam;
    public uint time;
    public int ptX;
    public int ptY;
  }

  [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
  public struct GllMsg {
    public uint pid;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
    public char[] text;
  }

  public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

  const uint WM_COPYDATA = 0x004A;
  const uint WM_DESTROY = 0x0002;
  const int GLL_COPYDATA_ID = 0x47544C;
  const string WndClass = "GLL_TSF_NOTIFY";

  [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  static extern IntPtr LoadLibrary(string name);
  [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
  static extern IntPtr GetProcAddress(IntPtr hModule, string proc);
  [DllImport("user32.dll", SetLastError = true)]
  static extern ushort RegisterClassW(ref WNDCLASS wc);
  [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  static extern IntPtr CreateWindowExW(uint ex, string cls, string name, uint style,
    int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
  [DllImport("user32.dll")]
  static extern bool GetMessageW(out MSG msg, IntPtr hwnd, uint min, uint max);
  [DllImport("user32.dll")]
  static extern bool TranslateMessage(ref MSG msg);
  [DllImport("user32.dll")]
  static extern IntPtr DispatchMessageW(ref MSG msg);
  [DllImport("user32.dll")]
  static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
  [DllImport("user32.dll")]
  static extern bool DestroyWindow(IntPtr hWnd);
  [DllImport("user32.dll")]
  static extern void PostQuitMessage(int code);
  [DllImport("kernel32.dll")]
  static extern IntPtr GetModuleHandle(string name);

  static string _logPath = @"C:\tmp\gll_tsf_host.log";
  static uint _commitMsg;

  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  static extern uint RegisterWindowMessageW(string name);
  [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
  static extern uint GlobalGetAtomNameW(ushort atom, StringBuilder buf, int size);
  [DllImport("kernel32.dll")]
  static extern ushort GlobalDeleteAtom(ushort atom);

  static void Log(string s) {
    try {
      System.IO.File.AppendAllText(_logPath,
        DateTime.Now.ToString("HH:mm:ss.fff ") + s + "\r\n", Encoding.UTF8);
    } catch { }
    try { Console.WriteLine(s); } catch { }
  }

  static IntPtr WndProcImpl(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam) {
    if (_commitMsg != 0 && msg == _commitMsg) {
      try {
        ushort atom = (ushort)(lParam.ToInt64() & 0xFFFF);
        var sb = new StringBuilder(512);
        uint n = GlobalGetAtomNameW(atom, sb, 512);
        GlobalDeleteAtom(atom);
        if (n > 0) {
          string s = sb.ToString();
          int bar = s.IndexOf('|');
          string text = bar >= 0 ? s.Substring(bar + 1) : s;
          Log("COMMIT atom=[" + text + "]");
        }
      } catch (Exception ex) {
        Log("ATOM_ERR " + ex.Message);
      }
      return IntPtr.Zero;
    }
    if (msg == WM_COPYDATA) {
      try {
        COPYDATASTRUCT cds = (COPYDATASTRUCT)Marshal.PtrToStructure(lParam, typeof(COPYDATASTRUCT));
        if (cds.dwData.ToInt32() == GLL_COPYDATA_ID && cds.cbData >= 520) {
          GllMsg m = (GllMsg)Marshal.PtrToStructure(cds.lpData, typeof(GllMsg));
          string text = new string(m.text);
          int n = text.IndexOf('\0');
          if (n >= 0) text = text.Substring(0, n);
          Log("COMMIT pid=" + m.pid + " text=[" + text + "]");
        }
      } catch (Exception ex) {
        Log("COPYDATA_ERR " + ex.Message);
      }
      return new IntPtr(1);
    }
    if (msg == WM_DESTROY) {
      PostQuitMessage(0);
      return IntPtr.Zero;
    }
    return DefWindowProcW(hWnd, msg, wParam, lParam);
  }

  static WndProc _proc;

  public static void Main(string[] args) {
    if (args.Length > 0) _logPath = args[0];
    try { System.IO.File.Delete(_logPath); } catch { }
    _proc = WndProcImpl;
    _commitMsg = RegisterWindowMessageW("GLL_TSF_COMMIT");
    WNDCLASS wc = new WNDCLASS();
    wc.lpfnWndProc = _proc;
    wc.hInstance = GetModuleHandle(null);
    wc.lpszClassName = WndClass;
    ushort cls = RegisterClassW(ref wc);
    if (cls == 0) {
      Log("RegisterClass fail err=" + Marshal.GetLastWin32Error());
      return;
    }
    IntPtr hwnd = CreateWindowExW(0, WndClass, "GLL TSF Host", 0,
      0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
    if (hwnd == IntPtr.Zero) {
      Log("CreateWindow fail err=" + Marshal.GetLastWin32Error());
      return;
    }
    Log("window created hwnd=0x" + hwnd.ToInt64().ToString("X"));

    string dll = args.Length > 1 ? args[1] : @"C:\tmp\gll_tsf_hook64.dll";
    IntPtr hmod = LoadLibrary(dll);
    if (hmod == IntPtr.Zero) {
      Log("LoadLibrary fail err=" + Marshal.GetLastWin32Error() + " dll=" + dll);
      return;
    }
    IntPtr install = GetProcAddress(hmod, "GllInstallHook");
    if (install == IntPtr.Zero) {
      Log("GetProcAddress GllInstallHook fail err=" + Marshal.GetLastWin32Error());
      return;
    }
    bool ok = Marshal.GetDelegateForFunctionPointer<InstallFn>(install)();
    Log("GllInstallHook -> " + ok);

    Log("READY: 在任意程序里用中文输入法打字");
    MSG msg;
    while (GetMessageW(out msg, IntPtr.Zero, 0, 0)) {
      TranslateMessage(ref msg);
      DispatchMessageW(ref msg);
    }
    Log("EXIT");
  }

  [UnmanagedFunctionPointer(CallingConvention.Winapi)]
  delegate bool InstallFn();
}
