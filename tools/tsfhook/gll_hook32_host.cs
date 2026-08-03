using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

/// <summary>
/// 32 位钩子宿主：由 64 位主程序启动，负责给 32 位进程（如 32 位 WPS）
/// 安装 32 位版 TSF/IMM 观察钩子。固定加载 gll_tsf_hook32.dll，
/// 主程序退出（父进程消失）后自动退出，不留孤儿钩子。
/// </summary>
public static class GllHook32Host {
  [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern IntPtr LoadLibrary(string name);
  [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
  private static extern IntPtr GetProcAddress(IntPtr hModule, string proc);

  [UnmanagedFunctionPointer(CallingConvention.Winapi)]
  private delegate bool InstallFn();
  [UnmanagedFunctionPointer(CallingConvention.Winapi)]
  private delegate void SetDebugFn(int on);

  private static void Log(string s) {
    try {
      System.IO.File.AppendAllText(@"C:\tmp\gll_hook32_host.log",
        DateTime.Now.ToString("HH:mm:ss.fff ") + s + "\r\n");
    } catch { }
  }

  public static void Main(string[] args) {
    int parentPid = 0;
    for (int i = 0; i < args.Length - 1; i++) {
      if (args[i] == "--parent") int.TryParse(args[i + 1], out parentPid);
    }
    Log("start parent=" + parentPid);
    string dll = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gll_hook32.dll");
    try {
      string ptr = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gll_hook32.txt");
      if (System.IO.File.Exists(ptr)) {
        string name = System.IO.File.ReadAllText(ptr).Trim();
        if (!string.IsNullOrEmpty(name)) {
          dll = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
        }
      }
    } catch { }
    try {
      string dir = System.IO.Path.GetDirectoryName(dll);
      if (!string.IsNullOrEmpty(dir)) {
        foreach (string f in System.IO.Directory.GetFiles(dir, "gll_hook32_*.dll")) {
          if (!f.Equals(dll, StringComparison.OrdinalIgnoreCase)) {
            try { System.IO.File.Delete(f); } catch { }
          }
        }
      }
    } catch { }
    Log("dll=" + dll);
    IntPtr h = LoadLibrary(dll);
    if (h == IntPtr.Zero) {
      Log("LoadLibrary err=" + Marshal.GetLastWin32Error());
      return;
    }
    IntPtr p = GetProcAddress(h, "GllInstallHook");
    if (p == IntPtr.Zero) p = GetProcAddress(h, "_GllInstallHook@0");
    if (p == IntPtr.Zero) {
      Log("GetProcAddress err=" + Marshal.GetLastWin32Error());
      return;
    }
    bool ok = Marshal.GetDelegateForFunctionPointer<InstallFn>(p)();
    Log("install=" + ok);
    if (!ok) return;
    IntPtr pd = GetProcAddress(h, "GllSetDebug");
    if (pd != IntPtr.Zero) {
      try {
        Marshal.GetDelegateForFunctionPointer<SetDebugFn>(pd)(1);
      } catch { }
    }
    while (true) {
      Thread.Sleep(2000);
      if (parentPid > 0) {
        try {
          Process.GetProcessById(parentPid);
        } catch {
          Log("parent gone");
          return;
        }
      }
    }
  }
}
