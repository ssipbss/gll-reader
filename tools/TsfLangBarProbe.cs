using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

/* TSF 语言栏探针：
 * 1. 枚举前台线程的语言栏项目（输入法模式按钮），打印 GUID/样式/状态/文本。
 * 2. 读取该线程 IMM 转换状态作对照。
 * 3. 轮询观察中英切换时哪个字段发生变化。
 */
class TsfLangBarProbe {
  private static string _log = "C:\\tmp\\tsflangbar.log";
  private static bool _watch = true;

  private static void Log(string s) {
    string line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + s;
    Console.WriteLine(line);
    try { File.AppendAllText(_log, line + "\r\n"); } catch { }
  }

  /* ---------- IUnknown 基接口 ---------- */
  [ComImport, Guid("00000000-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  private interface IUnknown { }

  /* ---------- 语言栏 ---------- */
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  private struct TfLangBarItemInfo {
    public Guid clsidService;
    public Guid guidItem;
    public uint dwStyle;
    public uint ulSort;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string szDescription;
  }

  [ComImport, Guid("73540D69-EDEB-4EE9-96C9-23AA30B25916"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  private interface ITfLangBarItem {
    void GetInfo(out TfLangBarItemInfo pInfo);
    void GetStatus(out uint pdwStatus);
    void Show([MarshalAs(UnmanagedType.Bool)] bool fShow);
    void GetTooltipString([MarshalAs(UnmanagedType.BStr)] out string pbstrToolTip);
  }

  [ComImport, Guid("28C7F1D0-DE25-11D2-AFDD-00105A2799B5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  private interface ITfLangBarItemButton : ITfLangBarItem {
    void OnClick(int click, POINT pt, ref RECT prcArea);
    void InitMenu(IntPtr pMenu);
    void OnMenuSelect(uint wID);
    void GetIcon(out IntPtr phIcon);
    void GetText([MarshalAs(UnmanagedType.BStr)] out string pbstrText);
  }

  [ComImport, Guid("A26A0525-3FAE-4FA0-89EE-88A964F9F1B5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  private interface ITfLangBarItemBitmapButton : ITfLangBarItem {
    void OnClick(int click, POINT pt, ref RECT prcArea);
    void InitMenu(IntPtr pMenu);
    void OnMenuSelect(uint wID);
    void GetPreferredSize(ref SIZE pszDefault, out SIZE psz);
    void DrawBitmap(int bmWidth, int bmHeight, uint dwFlags, out IntPtr phbmp);
    void GetTooltipString2();
  }

  [ComImport, Guid("BA468C55-9956-4FB1-A59D-52A7DD7CC6AA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  private interface ITfLangBarItemMgr {
    void EnumItems(out IntPtr ppEnum);
    void GetItem(ref Guid rguid, out ITfLangBarItem ppItem);
    void AddItem(IntPtr punk);
    void RemoveItem(IntPtr punk);
    void AdviseItemSink(IntPtr punk, out uint pdwCookie, ref Guid rguidItem);
    void UnadviseItemSink(uint dwCookie);
    void GetItemFloatingRect(uint dwThreadId, ref Guid rguid, out RECT prc);
    void GetItemsStatus(uint ulCount, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] Guid[] prgguid,
                         [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] uint[] pdwStatus);
    void GetItemNum(out uint pulCount);
    void GetItems(uint ulCount,
                  [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] ITfLangBarItem[] ppItem,
                  [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] TfLangBarItemInfo[] pInfo,
                  [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] uint[] pdwStatus,
                  ref uint pcFetched);
    void AdviseItemsSink(uint ulCount,
                         [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] ppunk,
                         [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] Guid[] pguidItem,
                         [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] uint[] pdwCookie);
    void UnadviseItemsSink(uint ulCount, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] uint[] pdwCookie);
  }

  [ComImport, Guid("87955690-E627-11D2-8DDB-00105A2799B5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  private interface ITfLangBarMgr {
    void AdviseEventSink(IntPtr pSink, IntPtr hwnd, uint dwflags, out uint pdwCookie);
    void UnAdviseEventSink(uint dwCookie);
    void GetThreadMarshalInterface(uint dwThreadId, uint dwType, ref Guid riid, out IntPtr ppunk);
    void GetThreadLangBarItemMgr(uint dwThreadId, out ITfLangBarItemMgr pplbie, out uint pdwThreadid);
    void GetInputProcessorProfiles(uint dwThreadId, out IntPtr ppaip, out uint pdwThreadid);
    void RestoreLastFocus(out uint dwThreadId, [MarshalAs(UnmanagedType.Bool)] bool fPrev);
    void SetModalInput(IntPtr pSink, uint dwThreadId, uint dwFlags);
    void ShowFloating(uint dwFlags);
    void GetShowFloatingStatus(out uint pdwFlags);
  }

  /* ---------- TSF 线程管理器 / 隔离舱 ---------- */
  [ComImport, Guid("AA80E801-2021-11D2-93E0-0060B067B86E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  private interface ITfThreadMgr {
    void Activate(out uint ptid);
    void Deactivate();
    void CreateDocumentMgr(out IntPtr ppdim);
    void EnumDocumentMgrs(out IntPtr ppEnum);
    void GetFocus(out IntPtr ppdimFocus);
    void SetFocus(IntPtr pdimFocus);
    void AssociateFocus(IntPtr hwnd, IntPtr pdimNew, out IntPtr ppdimPrev);
    void IsThreadFocus([MarshalAs(UnmanagedType.Bool)] out bool pfThreadFocus);
    void GetFunctionProvider(ref Guid clsid, out IntPtr ppFuncProv);
    void EnumFunctionProviders(out IntPtr ppEnum);
    void GetGlobalCompartment(out ITfCompartmentMgr ppCompMgr);
  }

  [ComImport, Guid("7DCF57AC-18AD-438B-824D-979BFFB74B7C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  private interface ITfCompartmentMgr {
    void GetCompartment(ref Guid rguidCompartment, out ITfCompartment ppcomp);
    void ClearCompartment(uint tid, ref Guid rguidCompartment);
    void EnumCompartments(out IEnumGUID ppEnum);
  }

  [ComImport, Guid("BB08F7A9-607A-4384-8623-056892B64371"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  private interface ITfCompartment {
    void SetValue(uint tid, ref object pvarValue);
    void GetValue(out object pvarValue);
  }

  [ComImport, Guid("0002E000-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  private interface IEnumGUID {
    void Clone(out IEnumGUID ppEnum);
    void Next(uint celt, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] Guid[] rgelt, out uint pceltFetched);
    void Reset();
    void Skip(uint celt);
  }

  /* ---------- Win32 ---------- */
  [StructLayout(LayoutKind.Sequential)]
  private struct POINT { public int X; public int Y; }
  [StructLayout(LayoutKind.Sequential)]
  private struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)]
  private struct SIZE { public int cx, cy; }

  [DllImport("ole32.dll")]
  private static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid,
    [MarshalAs(UnmanagedType.Interface)] out object ppv);

  [DllImport("user32.dll")]
  private static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")]
  private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
  [DllImport("user32.dll")]
  private static extern bool GetGUIThreadInfo(uint idThread, out GUITHREADINFO lpgui);
  [DllImport("kernel32.dll")]
  private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);
  [DllImport("kernel32.dll")]
  private static extern bool Thread32First(IntPtr hSnapshot, ref THREADENTRY32 lpte);
  [DllImport("kernel32.dll")]
  private static extern bool Thread32Next(IntPtr hSnapshot, ref THREADENTRY32 lpte);
  [DllImport("kernel32.dll")]
  private static extern bool CloseHandle(IntPtr hObject);
  [DllImport("ole32.dll")]
  private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);
  [DllImport("ole32.dll")]
  private static extern void CoUninitialize();
  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  private static extern int GetClassName(IntPtr hWnd, StringBuilder text, int count);
  [DllImport("user32.dll")]
  private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
  [DllImport("user32.dll")]
  private static extern bool DestroyIcon(IntPtr h);
  [DllImport("imm32.dll")]
  private static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);
  [DllImport("imm32.dll")]
  private static extern IntPtr ImmGetContext(IntPtr hWnd);
  [DllImport("imm32.dll")]
  private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);
  [DllImport("imm32.dll")]
  private static extern bool ImmGetConversionStatus(IntPtr hIMC, out uint lpConversion, out uint lpSentence);

  private const uint WM_IME_CONTROL = 0x0283;
  private const uint IMC_GETCONVERSIONMODE = 0x0005;
  private const uint IMC_GETOPENSTATUS = 0x0007;
  private const uint IME_CMODE_NATIVE = 0x0001;

  [StructLayout(LayoutKind.Sequential)]
  private struct GUITHREADINFO {
    public int cbSize;
    public uint flags;
    public IntPtr hwndActive;
    public IntPtr hwndFocus;
    public IntPtr hwndCapture;
    public IntPtr hwndMenuOwner;
    public IntPtr hwndMoveSize;
    public IntPtr hwndCaret;
    public POINT ptCaret;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct THREADENTRY32 {
    public uint dwSize;
    public uint cntUsage;
    public uint th32ThreadID;
    public uint th32OwnerProcessID;
    public int tpBasePri;
    public int tpDeltaPri;
    public uint dwFlags;
  }

  private const uint TH32CS_SNAPTHREAD = 0x00000004;
  private const uint COINIT_APARTMENTTHREADED = 0x2;

  private static uint FocusThreadOf(uint tid) {
    try {
      GUITHREADINFO gti = new GUITHREADINFO();
      gti.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
      if (!GetGUIThreadInfo(tid, out gti)) return 0;
      if (gti.hwndFocus == IntPtr.Zero) return 0;
      uint pid;
      return GetWindowThreadProcessId(gti.hwndFocus, out pid);
    } catch { return 0; }
  }

  private static Guid CLSID_TF_LangBarMgr = new Guid("EBB08C45-6C4A-4FDC-AE53-4EB8C4C7DB8E");
  private static Guid IID_ITfLangBarMgr = new Guid("87955690-E627-11D2-8DDB-00105A2799B5");
  private static Guid IID_ITfThreadMgr = new Guid("AA80E801-2021-11D2-93E0-0060B067B86E");
  private static Guid IID_ITfCompartmentMgr = new Guid("7DCF57AC-18AD-438B-824D-979BFFB74B7C");

  private static readonly uint[] TF_LBI_STYLE_NAMES = {
    0x00000001, 0x00000002, 0x00000004, 0x00000008, 0x00000010, 0x00000020,
    0x00010000, 0x00020000, 0x00040000
  };
  private static readonly string[] TF_LBI_STYLE_LABELS = {
    "HIDDENSTATUSCONTROL", "SHOWNINTRAY", "HIDEONNOOTHERITEMS", "SHOWNINTRAYONLY",
    "HIDDENBYDEFAULT", "TEXTCOLORICON", "BTN_BUTTON", "BTN_MENU", "BTN_TOGGLE"
  };
  private static readonly uint[] TF_LBI_STATUS_NAMES = {
    0x00000001, 0x00000002, 0x00010000
  };
  private static readonly string[] TF_LBI_STATUS_LABELS = {
    "HIDDEN", "DISABLED", "BTN_TOGGLED"
  };

  private static string StyleStr(uint v) {
    List<string> parts = new List<string>();
    for (int i = 0; i < TF_LBI_STYLE_NAMES.Length; i++)
      if ((v & TF_LBI_STYLE_NAMES[i]) != 0) parts.Add(TF_LBI_STYLE_LABELS[i]);
    if (parts.Count == 0) parts.Add("0x" + v.ToString("X8"));
    return string.Join("|", parts.ToArray());
  }

  private static string StatusStr(uint v) {
    List<string> parts = new List<string>();
    for (int i = 0; i < TF_LBI_STATUS_NAMES.Length; i++)
      if ((v & TF_LBI_STATUS_NAMES[i]) != 0) parts.Add(TF_LBI_STATUS_LABELS[i]);
    uint rest = v & ~(0x00000001u | 0x00000002u | 0x00010000u);
    if (rest != 0) parts.Add("0x" + rest.ToString("X8"));
    if (parts.Count == 0) parts.Add("0");
    return string.Join("|", parts.ToArray());
  }

  private static string ProcName(uint pid) {
    try {
      using (System.Diagnostics.Process p = System.Diagnostics.Process.GetProcessById((int)pid))
        return p.ProcessName;
    } catch { return pid.ToString(); }
  }

  private static string FgInfo(out uint tid, out uint pid) {
    IntPtr hwnd = GetForegroundWindow();
    tid = 0; pid = 0;
    if (hwnd == IntPtr.Zero) return "<none>";
    tid = GetWindowThreadProcessId(hwnd, out pid);
    StringBuilder sb = new StringBuilder(256);
    GetWindowText(hwnd, sb, 256);
    string title = sb.ToString();
    sb = new StringBuilder(256);
    GetClassName(hwnd, sb, 256);
    return "hwnd=0x" + hwnd.ToString("X") + " class=[" + sb + "] title=[" + title + "]";
  }

  private static string ImmMode(IntPtr hwnd) {
    try {
      IntPtr imeWnd = ImmGetDefaultIMEWnd(hwnd);
      if (imeWnd == IntPtr.Zero) return "noImeWnd";
      IntPtr mode = SendMessage(imeWnd, WM_IME_CONTROL, (IntPtr)IMC_GETCONVERSIONMODE, IntPtr.Zero);
      IntPtr open = SendMessage(imeWnd, WM_IME_CONTROL, (IntPtr)IMC_GETOPENSTATUS, IntPtr.Zero);
      uint m = (uint)(mode.ToInt32() & 0xFFFF);
      uint o = (uint)(open.ToInt32() & 0xFFFF);
      bool native = (m & IME_CMODE_NATIVE) != 0;
      string state = native ? "中文" : "英文";
      string strict = (o != 0 && native) ? "中文" : "英文";
      return "IMM conv=0x" + mode.ToInt64().ToString("X") + " open=" + o +
          " => " + state + "(strict:" + strict + ")";
    } catch (Exception ex) {
      return "IMM err " + ex.Message;
    }
  }

  private static string ImmMode2(IntPtr hwnd) {
    try {
      IntPtr imc = ImmGetContext(hwnd);
      if (imc == IntPtr.Zero) return "noImc";
      uint conv = 0, sent = 0;
      bool ok = ImmGetConversionStatus(imc, out conv, out sent);
      ImmReleaseContext(hwnd, imc);
      if (!ok) return "ImmGetConversionStatus fail";
      string state = (conv & IME_CMODE_NATIVE) != 0 ? "中文" : "英文";
      return "IMC conv=0x" + conv.ToString("X") + " sent=0x" + sent.ToString("X") + " => " + state;
    } catch (Exception ex) {
      return "IMC err " + ex.Message;
    }
  }

  private static void DumpItems(uint tid, uint pid) {
    try {
      object mgrObj = null;
      int hr = CoCreateInstance(ref CLSID_TF_LangBarMgr, IntPtr.Zero, 0x1 | 0x4, ref IID_ITfLangBarMgr, out mgrObj);
      if (hr != 0 || mgrObj == null) {
        Log("LangBarMgr CoCreate fail hr=0x" + hr.ToString("X8"));
        return;
      }
      ITfLangBarMgr mgr = (ITfLangBarMgr)mgrObj;
      ITfLangBarItemMgr itemMgr;
      uint realTid;
      try {
        mgr.GetThreadLangBarItemMgr(tid, out itemMgr, out realTid);
      } catch (Exception ex) {
        Log("GetThreadLangBarItemMgr fail tid=" + tid + " " + ex.Message);
        return;
      }
      if (itemMgr == null) {
        Log("itemMgr null for tid=" + tid + " (进程 " + ProcName(pid) + " 可能没有语言栏项目)");
        return;
      }
      uint count;
      itemMgr.GetItemNum(out count);
      if (count == 0) {
        Log("items=0 for tid=" + tid + " proc=" + ProcName(pid));
        return;
      }
      if (count > 64) count = 64;
      ITfLangBarItem[] items = new ITfLangBarItem[count];
      TfLangBarItemInfo[] infos = new TfLangBarItemInfo[count];
      uint[] statuses = new uint[count];
      uint fetched = 0;
      itemMgr.GetItems(count, items, infos, statuses, ref fetched);
      Log("---- items=" + fetched + " tid=" + tid + " proc=" + ProcName(pid) + " ----");
      for (int i = 0; i < (int)fetched; i++) {
        TfLangBarItemInfo info = infos[i];
        string text = "";
        string tooltip = "";
        string kind = "";
        try {
          if (items[i] != null) {
            ITfLangBarItemButton btn = items[i] as ITfLangBarItemButton;
            if (btn != null) {
              kind = "Button";
              try {
                string t;
                btn.GetText(out t);
                text = t ?? "";
              } catch { }
              try {
                IntPtr hIcon;
                btn.GetIcon(out hIcon);
                if (hIcon != IntPtr.Zero) { kind += "(icon)"; DestroyIcon(hIcon); }
              } catch { }
            } else {
              ITfLangBarItemBitmapButton bmp = items[i] as ITfLangBarItemBitmapButton;
              if (bmp != null) kind = "BitmapButton";
            }
            try {
              string tt;
              items[i].GetTooltipString(out tt);
              tooltip = tt ?? "";
            } catch { }
          }
        } catch { }
        Log("ITEM[" + i + "] clsid=" + info.clsidService.ToString().ToUpperInvariant() +
            " guid=" + info.guidItem.ToString().ToUpperInvariant() +
            " style=" + StyleStr(info.dwStyle) +
            " sort=" + info.ulSort +
            " desc=[" + info.szDescription + "]" +
            " status=" + StatusStr(statuses[i]) +
            " kind=" + kind +
            " text=[" + text.Replace("\r", "\\r").Replace("\n", "\\n") + "]" +
            " tooltip=[" + tooltip.Replace("\r", "\\r").Replace("\n", "\\n") + "]");
      }
    } catch (Exception ex) {
      Log("DumpItems err " + ex);
    }
  }

  private static void TryCompartments(uint tid) {
    try {
      object mgrObj = null;
      int hr = CoCreateInstance(ref CLSID_TF_LangBarMgr, IntPtr.Zero, 0x1 | 0x4, ref IID_ITfLangBarMgr, out mgrObj);
      if (hr != 0) { Log("comp CoCreate fail 0x" + hr.ToString("X8")); return; }
      ITfLangBarMgr mgr = (ITfLangBarMgr)mgrObj;
      uint[] types = { 0, 1, 2, 3, 4, 8, 0x10, 0x20, 0x10000 };
      for (int i = 0; i < types.Length; i++) {
        IntPtr tmPtr;
        try {
          mgr.GetThreadMarshalInterface(tid, types[i], ref IID_ITfThreadMgr, out tmPtr);
        } catch (Exception ex) {
          Log("GetThreadMarshalInterface dwType=" + types[i] + " fail " + ex.Message);
          continue;
        }
        if (tmPtr == IntPtr.Zero) {
          Log("GetThreadMarshalInterface dwType=" + types[i] + " -> null");
          continue;
        }
        object tmObj = Marshal.GetObjectForIUnknown(tmPtr);
        Log("GetThreadMarshalInterface dwType=" + types[i] + " OK obj=" + tmObj.GetType().FullName);
        try {
          IntPtr qi = IntPtr.Zero;
          int hr2 = Marshal.QueryInterface(tmPtr, ref IID_ITfThreadMgr, out qi);
          Log("  QI ITfThreadMgr hr=0x" + hr2.ToString("X8"));
          if (qi != IntPtr.Zero) Marshal.Release(qi);
        } catch (Exception ex) {
          Log("  QI ITfThreadMgr err " + ex.Message);
        }
        try {
          IntPtr qi = IntPtr.Zero;
          int hr3 = Marshal.QueryInterface(tmPtr, ref IID_ITfCompartmentMgr, out qi);
          Log("  QI ITfCompartmentMgr hr=0x" + hr3.ToString("X8"));
          if (qi != IntPtr.Zero) Marshal.Release(qi);
        } catch (Exception ex) {
          Log("  QI ITfCompartmentMgr err " + ex.Message);
        }
        Guid iidItemMgr = new Guid("BA468C55-9956-4FB1-A59D-52A7DD7CC6AA");
        try {
          IntPtr qi = IntPtr.Zero;
          int hr4 = Marshal.QueryInterface(tmPtr, ref iidItemMgr, out qi);
          Log("  QI ITfLangBarItemMgr hr=0x" + hr4.ToString("X8"));
          if (qi != IntPtr.Zero) Marshal.Release(qi);
        } catch (Exception ex) {
          Log("  QI ITfLangBarItemMgr err " + ex.Message);
        }
        ITfCompartmentMgr compMgr = tmObj as ITfCompartmentMgr;
        if (compMgr != null) {
          DumpCompartments(compMgr, "THREAD(t=" + tid + ")");
        } else {
          ITfThreadMgr tm = tmObj as ITfThreadMgr;
          if (tm != null) {
            ITfCompartmentMgr global;
            try {
              tm.GetGlobalCompartment(out global);
              if (global != null) DumpCompartments(global, "GLOBAL(t=" + tid + ")");
            } catch (Exception ex) {
              Log("GetGlobalCompartment fail " + ex.Message);
            }
          }
        }
        return;
      }
    } catch (Exception ex) {
      Log("TryCompartments err " + ex);
    }
  }

  private static void ScanThreads(uint pid) {
    if (pid == 0) return;
    IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
    if (snap == IntPtr.Zero || snap.ToInt64() == -1) {
      Log("thread snapshot fail");
      return;
    }
    try {
      THREADENTRY32 te = new THREADENTRY32();
      te.dwSize = (uint)Marshal.SizeOf(typeof(THREADENTRY32));
      bool ok = Thread32First(snap, ref te);
      List<uint> tids = new List<uint>();
      while (ok) {
        if (te.th32OwnerProcessID == pid) tids.Add(te.th32ThreadID);
        ok = Thread32Next(snap, ref te);
      }
      Log("scan threads pid=" + pid + " count=" + tids.Count);
      int tried = 0;
      foreach (uint tid in tids) {
        if (tried++ >= 40) break;
        try {
          object mgrObj = null;
          int hr = CoCreateInstance(ref CLSID_TF_LangBarMgr, IntPtr.Zero, 0x1 | 0x4, ref IID_ITfLangBarMgr, out mgrObj);
          if (hr != 0 || mgrObj == null) { Log("scan CoCreate fail 0x" + hr.ToString("X8")); return; }
          ITfLangBarMgr mgr = (ITfLangBarMgr)mgrObj;
          ITfLangBarItemMgr itemMgr;
          uint realTid;
          try {
            mgr.GetThreadLangBarItemMgr(tid, out itemMgr, out realTid);
            if (itemMgr != null) {
              uint count;
              itemMgr.GetItemNum(out count);
              Log("scan tid=" + tid + " items=" + count + " realTid=" + realTid);
              if (count > 0) DumpItems(tid, pid);
            } else {
              Log("scan tid=" + tid + " itemMgr null");
            }
          } catch (Exception ex) {
            Log("scan tid=" + tid + " E_FAIL(" + ex.Message + ")");
          }
        } catch { }
      }
    } finally {
      CloseHandle(snap);
    }
  }

  private static void ProbeThread(uint tid, uint pid, string label) {
    if (tid == 0) {
      Log(label + " tid=0 skip");
      return;
    }
    Log(label + " tid=" + tid + " pid=" + pid + " proc=" + ProcName(pid));
    DumpItems(tid, pid);
    TryCompartments(tid);
    ScanThreads(pid);
  }

  private static void DumpCompartments(ITfCompartmentMgr compMgr, string label) {
    try {
      IEnumGUID en;
      compMgr.EnumCompartments(out en);
      if (en == null) { Log(label + " compartments enum null"); return; }
      int shown = 0;
      while (true) {
        Guid[] one = new Guid[1];
        uint got;
        try {
          en.Next(1, one, out got);
        } catch { break; }
        if (got == 0) break;
        ITfCompartment comp;
        try {
          compMgr.GetCompartment(ref one[0], out comp);
        } catch {
          Log(label + " comp " + one[0].ToString() + " GetCompartment fail");
          continue;
        }
        if (comp == null) continue;
        try {
          object val;
          comp.GetValue(out val);
          Log(label + " COMP " + one[0].ToString().ToUpperInvariant() + " = " + (val == null ? "<null>" : (val + " (" + val.GetType().Name + ")")));
        } catch (Exception ex) {
          Log(label + " COMP " + one[0].ToString().ToUpperInvariant() + " GetValue fail " + ex.Message);
        }
        shown++;
        if (shown > 200) break;
      }
    } catch (Exception ex) {
      Log(label + " dump err " + ex.Message);
    }
  }

  private sealed class ItemSnapshot {
    public uint Status;
    public string Text = "";
    public string Tooltip = "";
  }

  [STAThread]
  private static void Main(string[] args) {
    if (args.Length > 0) _log = args[0];
    if (args.Length > 1 && args[1] == "-once") _watch = false;
    Console.OutputEncoding = Encoding.UTF8;
    Log("=== TSF LangBar Probe START ===");

    uint fgTid = 0, fgPid = 0;
    string fgDesc = FgInfo(out fgTid, out fgPid);
    uint focusTid = FocusThreadOf(fgTid);
    Log("FG " + fgDesc + " tid=" + fgTid + " pid=" + fgPid + " proc=" + ProcName(fgPid) +
        " focusTid=" + focusTid);
    ProbeThread(fgTid, fgPid, "FGTHREAD");
    if (focusTid != 0 && focusTid != fgTid) {
      uint fpid;
      GetWindowThreadProcessId(GetForegroundWindow(), out fpid);
      uint fpid2;
      uint ftid = FocusThreadOf(fgTid);
      IntPtr fhwnd = IntPtr.Zero;
      try {
        GUITHREADINFO gti = new GUITHREADINFO();
        gti.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
        if (GetGUIThreadInfo(fgTid, out gti)) fhwnd = gti.hwndFocus;
      } catch { }
      GetWindowThreadProcessId(fhwnd, out fpid2);
      ProbeThread(ftid, fpid2, "FOCUSTHREAD");
    }

    Dictionary<string, ItemSnapshot> last = new Dictionary<string, ItemSnapshot>();
    string lastImm = "";
    string lastImm2 = "";
    uint lastTid = fgTid;
    uint lastPid = fgPid;
    uint lastFocusTid = focusTid;

    int ticks = 0;
    int maxTicks = _watch ? 180 : 1;
    while (ticks < maxTicks) {
      Thread.Sleep(500);
      ticks++;
      uint tid = 0, pid = 0;
      FgInfo(out tid, out pid);
      uint ft = FocusThreadOf(tid);
      if (tid != lastTid || pid != lastPid || ft != lastFocusTid) {
        lastTid = tid; lastPid = pid;
        lastFocusTid = ft;
        last.Clear();
        Log("== FOCUS CHANGED tid=" + tid + " pid=" + pid + " proc=" + ProcName(pid) +
            " focusTid=" + ft + " ==");
        if (ft != 0 && ft != tid) {
          IntPtr fh = IntPtr.Zero;
          uint fpid = 0;
          try {
            GUITHREADINFO gti = new GUITHREADINFO();
            gti.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
            if (GetGUIThreadInfo(tid, out gti)) fh = gti.hwndFocus;
          } catch { }
          GetWindowThreadProcessId(fh, out fpid);
          ProbeThread(ft, fpid, "FOCUSTHREAD");
        } else {
          DumpItems(tid, pid);
        }
      }
      if (tid == 0) continue;
      try {
        object mgrObj = null;
        int hr = CoCreateInstance(ref CLSID_TF_LangBarMgr, IntPtr.Zero, 0x1 | 0x4, ref IID_ITfLangBarMgr, out mgrObj);
        if (hr == 0 && mgrObj != null) {
          ITfLangBarMgr mgr = (ITfLangBarMgr)mgrObj;
          ITfLangBarItemMgr itemMgr;
          uint realTid;
          try {
            uint targetTid = ft != 0 ? ft : tid;
            mgr.GetThreadLangBarItemMgr(targetTid, out itemMgr, out realTid);
            if (itemMgr != null) {
              uint count;
              itemMgr.GetItemNum(out count);
              if (count > 0 && count <= 64) {
                ITfLangBarItem[] items = new ITfLangBarItem[count];
                TfLangBarItemInfo[] infos = new TfLangBarItemInfo[count];
                uint[] statuses = new uint[count];
                uint fetched = 0;
                itemMgr.GetItems(count, items, infos, statuses, ref fetched);
                for (int i = 0; i < (int)fetched; i++) {
                  string key = infos[i].guidItem.ToString().ToUpperInvariant();
                  string text = "";
                  string tooltip = "";
                  try {
                    ITfLangBarItemButton btn = items[i] as ITfLangBarItemButton;
                    if (btn != null) {
                      string t;
                      btn.GetText(out t);
                      text = t ?? "";
                    }
                    string tt;
                    items[i].GetTooltipString(out tt);
                    tooltip = tt ?? "";
                  } catch { }
                  ItemSnapshot snap = new ItemSnapshot();
                  snap.Status = statuses[i];
                  snap.Text = text;
                  snap.Tooltip = tooltip;
                  ItemSnapshot prev;
                  if (last.TryGetValue(key, out prev)) {
                    if (prev.Status != snap.Status || prev.Text != snap.Text || prev.Tooltip != snap.Tooltip) {
                      Log("CHANGE " + key + " status " + StatusStr(prev.Status) + "->" + StatusStr(snap.Status) +
                          " text [" + prev.Text + "]->[" + snap.Text + "]" +
                          " tooltip [" + prev.Tooltip + "]->[" + snap.Tooltip + "]");
                    }
                  }
                  last[key] = snap;
                }
              }
            }
          } catch { }
        }
      } catch { }
      IntPtr hwnd = GetForegroundWindow();
      string imm = ImmMode(hwnd);
      string imm2 = ImmMode2(hwnd);
      if (imm != lastImm) {
        Log("IMM_CHANGE " + lastImm + " -> " + imm);
        lastImm = imm;
      }
      if (imm2 != lastImm2) {
        Log("IMC_CHANGE " + lastImm2 + " -> " + imm2);
        lastImm2 = imm2;
      }
      if (ticks % 10 == 0)
        Log("tick=" + ticks + " imm=" + imm + " imm2=" + imm2);
    }
    Log("=== DONE ===");
  }
}
