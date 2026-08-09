using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

class RawInputProbe : Form {
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
  [StructLayout(LayoutKind.Sequential)]
  private struct RAWKEYBOARD {
    public ushort MakeCode;
    public ushort Flags;
    public ushort Reserved;
    public ushort VKey;
    public uint Message;
    public uint ExtraInformation;
  }
  [StructLayout(LayoutKind.Explicit)]
  private struct RAWINPUTDATA {
    [FieldOffset(0)] public RAWINPUTHEADER header;
    [FieldOffset(16)] public RAWKEYBOARD keyboard;
  }
  [StructLayout(LayoutKind.Sequential)]
  private struct RAWINPUT {
    public RAWINPUTHEADER header;
    public RAWINPUTDATA data;
  }

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);
  [DllImport("user32.dll")]
  private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);
  [DllImport("user32.dll")]
  private static extern IntPtr GetForegroundWindow();

  private const uint RID_INPUT = 0x10000003;
  private const uint RIDEV_INPUTSINK = 0x00000100;
  private const uint WM_INPUT = 0x00FF;
  private static int _count = 0;

  protected override void WndProc(ref Message m) {
    if (m.Msg == WM_INPUT) {
      uint size = 0;
      GetRawInputData(m.LParam, RID_INPUT, IntPtr.Zero, ref size, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER)));
      if (size > 0) {
        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try {
          if (GetRawInputData(m.LParam, RID_INPUT, buf, ref size, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER))) == size) {
            RAWINPUT ri = (RAWINPUT)Marshal.PtrToStructure(buf, typeof(RAWINPUT));
            if (ri.header.dwType == 1) { // RIM_TYPEKEYBOARD
              ushort vk = ri.data.keyboard.VKey;
              uint msg = ri.data.keyboard.Message;
              bool up = (msg == 0x101 || msg == 0x105);
              _count++;
              Console.WriteLine("RAW vk=0x" + vk.ToString("X") + " up=" + up + " msg=0x" + msg.ToString("X") + " total=" + _count);
            }
          }
        } finally { Marshal.FreeHGlobal(buf); }
      }
    }
    base.WndProc(ref m);
  }

  [STAThread]
  private static void Main() {
    RawInputProbe f = new RawInputProbe();
    f.ShowInTaskbar = false;
    f.Opacity = 0;
    f.Show();
    Application.DoEvents();
    IntPtr h = f.Handle;
    RAWINPUTDEVICE[] devs = new RAWINPUTDEVICE[1];
    devs[0].usUsagePage = 0x01;
    devs[0].usUsage = 0x06; // keyboard
    devs[0].dwFlags = RIDEV_INPUTSINK; // background input
    devs[0].hwndTarget = h;
    bool ok = RegisterRawInputDevices(devs, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
    Console.WriteLine("register=" + ok + " err=" + Marshal.GetLastWin32Error() + " hwnd=0x" + h.ToInt64().ToString("X"));
    DateTime t0 = DateTime.Now;
    while ((DateTime.Now - t0).TotalSeconds < 60) {
      Application.DoEvents();
      Thread.Sleep(20);
    }
    Console.WriteLine("done total=" + _count);
  }
}