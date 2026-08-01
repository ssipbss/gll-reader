using System;
using System.Runtime.InteropServices;

namespace GenDaLangDu {
  public sealed class ImeState {
    public long Hkl;
    public bool HasImc;
    public bool IsOpen;
    public bool IsComposing;
    public bool IsChineseMode;
    public bool IsChineseLayout;
    public int ConversionMode;
    public int SentenceMode;
    public string Composition = "";
    public string Result = "";
  }

  public sealed class ImeMonitor {
    private const int IME_CMODE_NATIVE = 0x0001;

    public ImeState GetState() {
      ImeState st = new ImeState();
      try {
        IntPtr hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) {
          st.IsChineseLayout = IsChineseLayoutHkl(0);
          st.IsChineseMode = st.IsChineseLayout;
          return st;
        }
        uint pid;
        uint tid = Native.GetWindowThreadProcessId(hwnd, out pid);
        st.Hkl = Native.GetKeyboardLayout(tid).ToInt64();
        st.IsChineseLayout = IsChineseLayoutHkl(tid);

        uint curTid = Native.GetCurrentThreadId();
        bool attached = false;
        try { attached = Native.AttachThreadInput(curTid, tid, true); } catch { }
        IntPtr imc = Native.ImmGetContext(hwnd);
        if (imc == IntPtr.Zero) {
          if (attached) { try { Native.AttachThreadInput(curTid, tid, false); } catch { } }
          st.IsChineseMode = st.IsChineseLayout;
          return st;
        }
        st.HasImc = true;
        try {
          st.IsOpen = Native.ImmGetOpenStatus(imc);
          int conv = 0;
          int sent = 0;
          try { Native.ImmGetConversionStatus(imc, out conv, out sent); } catch { }
          st.ConversionMode = conv;
          st.SentenceMode = sent;
          st.Composition = ReadString(imc, Native.GCS_COMPSTR);
          st.Result = ReadString(imc, Native.GCS_RESULTSTR);
          st.IsComposing = st.Composition.Length > 0;
        } finally {
          Native.ImmReleaseContext(hwnd, imc);
          if (attached) { try { Native.AttachThreadInput(curTid, tid, false); } catch { } }
        }

        st.IsChineseMode = st.IsComposing;
        if (!st.IsChineseMode) {
          if (st.HasImc && st.IsOpen) {
            st.IsChineseMode = (st.ConversionMode & IME_CMODE_NATIVE) != 0;
          } else {
            st.IsChineseMode = st.IsChineseLayout;
          }
        }
      } catch { }
      return st;
    }

    private static long _lastChineseHklTick = long.MinValue;

    private static bool IsChineseLayoutHkl(uint tid) {
      try {
        IntPtr hkl = Native.GetKeyboardLayout(tid);
        long v = hkl.ToInt64();
        bool zh = (v & 0xFFFF) == 0x0804;
        if (!zh) {
          zh = v == 0xE0200804 || v == 0xE0210804 || v == 0xE0220804 || v == 0xE0230804;
        }
        if (zh) {
          _lastChineseHklTick = Environment.TickCount;
        } else if (Environment.TickCount - _lastChineseHklTick < 1500) {
          zh = true;
        }
        return zh;
      } catch { }
      return false;
    }

    private static string ReadString(IntPtr imc, uint index) {
      try {
        int len = Native.ImmGetCompositionString(imc, index, IntPtr.Zero, 0);
        if (len <= 0) return "";
        IntPtr buf = Marshal.AllocHGlobal(len);
        try {
          Native.ImmGetCompositionString(imc, index, buf, len);
          return Marshal.PtrToStringUni(buf, len / 2);
        } finally {
          Marshal.FreeHGlobal(buf);
        }
      } catch {
        return "";
      }
    }
  }
}
