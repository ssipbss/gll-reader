using System;
using System.Runtime.InteropServices;

/// <summary>
/// 测试工具：通过 TSF 正规 API 切换输入法 profile。
/// 用法：TsfImeSwitch.exe activate {clsid} {profileGuid}
/// 例：微软五笔 6A498709-E00B-4C45-A018-8F9E4081AE40 82590C13-F4DD-44F4-BA1D-8667246FDF8E
///     微软拼音 81D4E9C9-1D3B-41BC-9E6C-4B40BF79E35E FA550B04-5AD7-411F-A5A5-3CDFC5249E0E
/// </summary>
public static class TsfImeSwitch {
  [ComImport, Guid("1F02B6C5-7842-4EE6-8A0B-9A24183A95CA"),
   InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  private interface ITfInputProcessorProfiles {
    void Register([In] ref Guid rclsid);
    void Unregister([In] ref Guid rclsid);
    void AddLanguageProfile([In] ref Guid rclsid, ushort langid, [In] ref Guid guidProfile,
      [In, MarshalAs(UnmanagedType.LPWStr)] string pchDesc, uint cchDesc,
      [In, MarshalAs(UnmanagedType.LPWStr)] string pchIconFile, uint cchFile, uint uIconIndex);
    void RemoveLanguageProfile([In] ref Guid rclsid, ushort langid, [In] ref Guid guidProfile);
    void EnumInputProcessorInfo([Out] out IntPtr ppEnum);
    void GetDefaultLanguageProfile(ushort langid, [In] ref Guid catid,
      [Out] out Guid pclsid, [Out] out Guid pguidProfile);
    void SetDefaultLanguageProfile(ushort langid, [In] ref Guid rclsid, [In] ref Guid guidProfiles);
    void ActivateLanguageProfile([In] ref Guid rclsid, ushort langid, [In] ref Guid guidProfiles);
    void GetActiveLanguageProfile([In] ref Guid rclsid, out ushort plangid, out Guid pguidProfile);
    void GetLanguageProfileDescription([In] ref Guid rclsid, ushort langid,
      [In] ref Guid guidProfile, [Out, MarshalAs(UnmanagedType.BStr)] out string pbstrProfile);
    void GetCurrentLanguage(out ushort plangid);
    void ChangeCurrentLanguage(ushort langid);
    void GetLanguageList(out IntPtr ppLangId, out uint pulCount);
    void EnumLanguageProfiles(ushort langid, out IntPtr ppEnum);
    void EnableLanguageProfile([In] ref Guid rclsid, ushort langid,
      [In] ref Guid guidProfile, [MarshalAs(UnmanagedType.Bool)] bool fEnable);
    void IsEnabledLanguageProfile([In] ref Guid rclsid, ushort langid,
      [In] ref Guid guidProfile, [MarshalAs(UnmanagedType.Bool)] out bool pfEnable);
    void EnableLanguageProfileByDefault([In] ref Guid rclsid, ushort langid,
      [In] ref Guid guidProfile, [MarshalAs(UnmanagedType.Bool)] bool fEnable);
    void SubstituteKeyboardLayout([In] ref Guid rclsid, ushort langid,
      [In] ref Guid guidProfile, IntPtr hKL);
  }

  [ComImport, Guid("3D61BF11-AC5F-42C8-A4CB-931BCC28C744"),
   InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  private interface IEnumTfLanguageProfiles {
    void Clone(out IEnumTfLanguageProfiles ppEnum);
    void Next(uint ulCount, IntPtr pProfile, out uint pcFetch);
    void Reset();
    void Skip(uint ulCount);
  }

  [StructLayout(LayoutKind.Explicit, Size = 56)]
  private struct TF_LANGUAGEPROFILE {
    [FieldOffset(0)] public Guid clsid;
    [FieldOffset(16)] public ushort langid;
    [FieldOffset(20)] public Guid catid;
    [FieldOffset(36)] public int fActive;
    [FieldOffset(40)] public Guid guidProfile;
  }

  [DllImport("ole32.dll")]
  private static extern int CoInitializeEx(IntPtr pv, uint dwCoInit);
  [DllImport("ole32.dll")]
  private static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter,
    uint dwClsContext, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
  [DllImport("ole32.dll")]
  private static extern int CoUninitialize();
  [DllImport("user32.dll")]
  private static extern IntPtr GetKeyboardLayout(uint idThread);
  [DllImport("user32.dll")]
  private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin,
    uint wMsgFilterMax, uint wRemoveMsg);
  [DllImport("user32.dll")]
  private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin,
    uint wMsgFilterMax);
  [DllImport("user32.dll")]
  private static extern bool TranslateMessage(ref MSG lpMsg);
  [DllImport("user32.dll")]
  private static extern IntPtr DispatchMessage(ref MSG lpMsg);

  [StructLayout(LayoutKind.Sequential)]
  private struct MSG {
    public IntPtr hwnd;
    public uint message;
    public IntPtr wParam;
    public IntPtr lParam;
    public uint time;
    public int ptX;
    public int ptY;
  }

  private const uint CLSCTX_INPROC_SERVER = 1;
  private const uint COINIT_APARTMENTTHREADED = 2;

  public static void Main(string[] args) {
    CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);
    try {
      Guid clsidProfiles = new Guid("33C53A50-F456-4884-B049-85FD643ECFED");
      Guid iidProfiles = new Guid("1F02B6C5-7842-4EE6-8A0B-9A24183A95CA");
      object obj;
      int hr = CoCreateInstance(ref clsidProfiles, IntPtr.Zero, CLSCTX_INPROC_SERVER,
                                ref iidProfiles, out obj);
      Console.WriteLine("CoCreate hr=0x" + hr.ToString("X8"));
      if (hr != 0 || obj == null) return;
      ITfInputProcessorProfiles profiles = (ITfInputProcessorProfiles)obj;
      ushort lang;
      profiles.GetCurrentLanguage(out lang);
      Console.WriteLine("curLang=0x" + lang.ToString("X4") +
                        " kbd=0x" + GetKeyboardLayout(0).ToInt64().ToString("X8"));
      if (args.Length >= 1 && args[0] == "list") {
        try {
          IntPtr enumPtr;
          profiles.EnumLanguageProfiles(0x0804, out enumPtr);
          if (enumPtr == IntPtr.Zero) {
            Console.WriteLine("Enum null");
          } else {
            IEnumTfLanguageProfiles en = (IEnumTfLanguageProfiles)
              Marshal.GetObjectForIUnknown(enumPtr);
            Marshal.Release(enumPtr);
            TF_LANGUAGEPROFILE[] buf = new TF_LANGUAGEPROFILE[8];
            IntPtr p = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(TF_LANGUAGEPROFILE)) * 8);
            for (;;) {
              uint fetched;
              en.Next(8, p, out fetched);
              if (fetched == 0) break;
              for (uint i = 0; i < fetched; i++) {
                TF_LANGUAGEPROFILE prof = (TF_LANGUAGEPROFILE)Marshal.PtrToStructure(
                  IntPtr.Add(p, (int)i * Marshal.SizeOf(typeof(TF_LANGUAGEPROFILE))),
                  typeof(TF_LANGUAGEPROFILE));
                string desc = "";
                try {
                  profiles.GetLanguageProfileDescription(ref prof.clsid, prof.langid,
                    ref prof.guidProfile, out desc);
                } catch { }
                Console.WriteLine("clsid=" + prof.clsid + " lang=0x" + prof.langid.ToString("X4") +
                                  " active=" + (prof.fActive != 0) + " cat=" + prof.catid +
                                  " prof=" + prof.guidProfile + " desc=[" + desc + "]");
              }
            }
            Marshal.FreeHGlobal(p);
            Marshal.ReleaseComObject(en);
          }
        } catch (Exception ex) {
          Console.WriteLine("List EX: " + ex.GetType().Name);
        }
      } else if (args.Length >= 3 && args[0] == "activate") {
        Guid clsid = new Guid(args[1]);
        Guid profile = new Guid(args[2]);
        try {
          MSG msg;
          PeekMessage(out msg, IntPtr.Zero, 0, 0, 0);
          profiles.ActivateLanguageProfile(ref clsid, 0x0804, ref profile);
          Console.WriteLine("Activate OK kbd=0x" + GetKeyboardLayout(0).ToInt64().ToString("X8"));
          DateTime end = DateTime.Now.AddMilliseconds(500);
          while (DateTime.Now < end && GetMessage(out msg, IntPtr.Zero, 0, 0)) {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
          }
        } catch (Exception ex) {
          Console.WriteLine("Activate EX: " + ex.GetType().Name + " hr=" +
                            (ex is COMException ? ((COMException)ex).HResult.ToString("X8") : "?"));
        }
      }
    } finally {
      CoUninitialize();
    }
  }
}
