using System;
using System.Runtime.InteropServices;
using System.Threading;

/// <summary>
/// 32 位钩子宿主：由 64 位主程序启动，负责安装 32 位版 TSF 钩子，
/// 覆盖 32 位应用程序（如 32 位 WPS、部分 Electron 桌面客户端）。
/// </summary>
public static class GllHook32Host {
  [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern IntPtr LoadLibrary(string name);
  [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
  private static extern IntPtr GetProcAddress(IntPtr hModule, string proc);

  [UnmanagedFunctionPointer(CallingConvention.Winapi)]
  private delegate bool InstallFn();

  public static void Main() {
    try {
      Log("start");
      string dll = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gll_tsf_hook32_v15.dll");
      if (!System.IO.File.Exists(dll)) {
        dll = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gll_tsf_hook32_v14.dll");
      }
      if (!System.IO.File.Exists(dll)) {
        dll = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gll_tsf_hook32_v13.dll");
      }
      if (!System.IO.File.Exists(dll)) {
        dll = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gll_tsf_hook32_v12.dll");
      }
      if (!System.IO.File.Exists(dll)) {
        dll = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gll_tsf_hook32_v11.dll");
      }
      if (!System.IO.File.Exists(dll)) {
        dll = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gll_tsf_hook32_v10.dll");
      }
      if (!System.IO.File.Exists(dll)) {
        dll = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gll_tsf_hook32_v9.dll");
      }
      if (!System.IO.File.Exists(dll)) {
        dll = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gll_tsf_hook32_v8.dll");
      }
      if (!System.IO.File.Exists(dll)) {
        dll = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gll_tsf_hook32_v7.dll");
      }
      if (!System.IO.File.Exists(dll)) {
        dll = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gll_tsf_hook32_v6.dll");
      }
      if (!System.IO.File.Exists(dll)) {
        dll = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gll_tsf_hook32_v5.dll");
      }
      if (!System.IO.File.Exists(dll)) {
        dll = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gll_tsf_hook32_v4.dll");
      }
      if (!System.IO.File.Exists(dll)) {
        dll = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gll_tsf_hook32_v3.dll");
      }
      if (!System.IO.File.Exists(dll)) {
        dll = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gll_tsf_hook32_v2.dll");
      }
      if (!System.IO.File.Exists(dll)) {
        dll = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gll_tsf_hook32.dll");
      }
      Log("dll=" + dll + " exists=" + System.IO.File.Exists(dll));
      IntPtr h = LoadLibrary(dll);
      if (h == IntPtr.Zero) { Log("LoadLibrary err=" + Marshal.GetLastWin32Error()); return; }
      Log("loaded");
      IntPtr p = GetProcAddress(h, "GllInstallHook");
      if (p == IntPtr.Zero) p = GetProcAddress(h, "_GllInstallHook@0");
      if (p == IntPtr.Zero) { Log("GetProcAddress err=" + Marshal.GetLastWin32Error()); return; }
      bool ok = Marshal.GetDelegateForFunctionPointer<InstallFn>(p)();
      Log("install=" + ok);
      if (!ok) return;
    } catch {
      return;
    }
    while (true) Thread.Sleep(60000);
  }

  private static void Log(string s) {
    try {
      System.IO.File.AppendAllText(@"C:\tmp\gll_hook32_host.log",
        DateTime.Now.ToString("HH:mm:ss.fff ") + s + "\r\n");
    } catch { }
  }
}
