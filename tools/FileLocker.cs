using System;
using System.Runtime.InteropServices;

public static class FileLocker {
  [StructLayout(LayoutKind.Sequential)]
  private struct RM_UNIQUE_PROCESS {
    public int dwProcessId;
    public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
  }

  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  private struct RM_PROCESS_INFO {
    public RM_UNIQUE_PROCESS Process;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string strAppName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string strServiceShortName;
    public int ApplicationType;
    public uint AppStatus;
    public uint TSSessionId;
    [MarshalAs(UnmanagedType.Bool)]
    public bool bRestartable;
  }

  [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
  private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);
  [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
  private static extern int RmRegisterResources(uint pSessionHandle, uint nFiles, string[] rgsFilenames,
    uint nApplications, IntPtr rgApplications, uint nServices, string[] rgsServiceNames);
  [DllImport("rstrtmgr.dll")]
  private static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded,
    ref uint pnProcInfo, [In, Out] RM_PROCESS_INFO[] rgAffectedApps, ref uint pdwRebootReasons);
  [DllImport("rstrtmgr.dll")]
  private static extern int RmEndSession(uint pSessionHandle);

  public static void Main(string[] args) {
    if (args.Length < 1) return;
    uint session;
    string key = Guid.NewGuid().ToString();
    if (RmStartSession(out session, 0, key) != 0) {
      Console.WriteLine("RmStartSession failed");
      return;
    }
    try {
      string[] files = new string[] { args[0] };
      int hr = RmRegisterResources(session, 1, files, 0, IntPtr.Zero, 0, null);
      Console.WriteLine("register hr=" + hr);
      if (hr != 0) return;
      uint needed = 0;
      uint count = 0;
      uint reasons = 0;
      hr = RmGetList(session, out needed, ref count, null, ref reasons);
      Console.WriteLine("getlist1 hr=" + hr + " needed=" + needed);
      RM_PROCESS_INFO[] procs = new RM_PROCESS_INFO[needed > 0 ? needed : 64];
      count = (uint)procs.Length;
      hr = RmGetList(session, out needed, ref count, procs, ref reasons);
      Console.WriteLine("getlist2 hr=" + hr + " count=" + count + " reasons=0x" + reasons.ToString("X"));
      for (int i = 0; i < count && i < procs.Length; i++) {
        Console.WriteLine("pid=" + procs[i].Process.dwProcessId + " app=[" + procs[i].strAppName + "]");
      }
    } finally {
      RmEndSession(session);
    }
  }
}
