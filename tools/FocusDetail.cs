using System;
using System.Runtime.InteropServices;
using System.Windows.Automation;

class FocusDetail {
  [DllImport("user32.dll")]
  private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

  [STAThread]
  private static void Main() {
    try {
      AutomationElement el = AutomationElement.FocusedElement;
      if (el == null) { Console.WriteLine("no focused element"); return; }
      AutomationElement cur = el;
      int depth = 0;
      while (cur != null && depth < 10) {
        uint pid = 0;
        try {
          IntPtr hwnd = (IntPtr)cur.Current.NativeWindowHandle;
          if (hwnd != IntPtr.Zero) GetWindowThreadProcessId(hwnd, out pid);
        } catch { }
        string name = "";
        try { name = (cur.Current.Name ?? ""); } catch { }
        if (name.Length > 40) name = name.Substring(0, 40);
        Console.WriteLine(new string(' ', depth * 2) + "type=" + cur.Current.ControlType.ProgrammaticName +
          " cls=[" + (cur.Current.ClassName ?? "") + "] pid=" + pid +
          " name=[" + name.Replace("\r", " ").Replace("\n", " ") + "]");
        try {
          cur = TreeWalker.ControlViewWalker.GetParent(cur);
        } catch { break; }
        depth++;
      }
    } catch (Exception ex) {
      Console.WriteLine("ERR " + ex);
    }
  }
}
