using System;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;

class FocusEditableProbe {
  [DllImport("user32.dll")]
  private static extern IntPtr GetForegroundWindow();

  [DllImport("user32.dll")]
  private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

  private static bool IsEditable() {
    try {
      AutomationElement el = AutomationElement.FocusedElement;
      if (el == null) return false;
      AutomationElement cur = el;
      for (int i = 0; i < 12; i++) {
        if (cur == null) break;
        try {
          object p;
          if (cur.TryGetCurrentPattern(TextPattern.Pattern, out p)) return true;
          if (cur.TryGetCurrentPattern(ValuePattern.Pattern, out p)) return true;
        } catch { }
        try {
          cur = TreeWalker.ControlViewWalker.GetParent(cur);
        } catch {
          break;
        }
      }
      return false;
    } catch {
      return false;
    }
  }

  private static void Main() {
    IntPtr h = GetForegroundWindow();
    uint pid = 0;
    GetWindowThreadProcessId(h, out pid);
    Console.WriteLine("fgpid=" + pid + " editable=" + IsEditable());
  }
}
