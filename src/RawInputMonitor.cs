using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GenDaLangDu {
  /// <summary>Raw Input 键盘监听（后台模式）：不做任何按键处理，
  /// 只更新"最近收到按键"时间戳，作为键盘钩子静默失效的对照信号。
  /// 钩子"已安装但收不到按键"时，这里仍能看到系统按键活动。</summary>
  public sealed class RawInputMonitor : NativeWindow, IDisposable {
    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE {
      public ushort usUsagePage;
      public ushort usUsage;
      public uint dwFlags;
      public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER {
      public uint dwType;
      public uint dwSize;
      public IntPtr hDevice;
      public IntPtr wParam;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

    private const uint RID_INPUT = 0x10000003;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint WM_INPUT = 0x00FF;
    private const uint RIM_TYPEKEYBOARD = 1;

    private DateTime _lastEventAt = DateTime.MinValue;

    public DateTime LastEventAt {
      get { return _lastEventAt; }
    }

    public RawInputMonitor() {
      CreateHandle(new CreateParams());
    }

    public void Register() {
      try {
        RAWINPUTDEVICE[] devs = new RAWINPUTDEVICE[1];
        devs[0].usUsagePage = 0x01;
        devs[0].usUsage = 0x06;
        devs[0].dwFlags = RIDEV_INPUTSINK;
        devs[0].hwndTarget = Handle;
        RegisterRawInputDevices(devs, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
      } catch { }
    }

    protected override void WndProc(ref Message m) {
      if (m.Msg == WM_INPUT) {
        try {
          uint size = 0;
          GetRawInputData(m.LParam, RID_INPUT, IntPtr.Zero, ref size, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER)));
          if (size >= (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER))) {
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try {
              if (GetRawInputData(m.LParam, RID_INPUT, buf, ref size, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER))) == size) {
                RAWINPUTHEADER h = (RAWINPUTHEADER)Marshal.PtrToStructure(buf, typeof(RAWINPUTHEADER));
                if (h.dwType == RIM_TYPEKEYBOARD) _lastEventAt = DateTime.Now;
              }
            } finally {
              Marshal.FreeHGlobal(buf);
            }
          }
        } catch { }
      }
      base.WndProc(ref m);
    }

    public void Dispose() {
      DestroyHandle();
    }
  }
}
