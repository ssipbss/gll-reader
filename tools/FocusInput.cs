using System;
using System.Windows.Automation;

public static class FocusInput {
  public static void Main(string[] args) {
    int pid = args.Length > 0 ? int.Parse(args[0]) : 0;
    AutomationElement root = null;
    if (pid > 0) {
      root = AutomationElement.FromHandle(FindWindowByPid(pid));
    } else {
      root = AutomationElement.FocusedElement;
    }
    if (root == null) {
      Console.WriteLine("no root");
      return;
    }
    AutomationElement hit = FindEditable(root);
    if (hit == null) {
      Console.WriteLine("no editable found");
      return;
    }
    try {
      hit.SetFocus();
      Console.WriteLine("focus set: class=" + hit.Current.ClassName + " type=" + hit.Current.ControlType.ProgrammaticName);
    } catch (Exception ex) {
      Console.WriteLine("SetFocus err: " + ex.Message);
    }
  }

  private static IntPtr FindWindowByPid(int pid) {
    IntPtr found = IntPtr.Zero;
    EnumWindows(delegate(IntPtr hwnd, IntPtr lParam) {
      uint p;
      GetWindowThreadProcessId(hwnd, out p);
      if (p == (uint)pid && IsWindowVisible(hwnd)) {
        found = hwnd;
        return false;
      }
      return true;
    }, IntPtr.Zero);
    return found;
  }

  private static AutomationElement FindEditable(AutomationElement root) {
    var cond = new AndCondition(
      new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
      new PropertyCondition(AutomationElement.IsKeyboardFocusableProperty, true));
    try {
      AutomationElement el = root.FindFirst(TreeScope.Descendants, cond);
      if (el != null) return el;
    } catch { }
    try {
      var all = root.FindAll(TreeScope.Descendants,
        new PropertyCondition(AutomationElement.IsKeyboardFocusableProperty, true));
      foreach (AutomationElement el in all) {
        try {
          if (el.Current.ClassName != null && el.Current.ClassName.IndexOf("ProseMirror", StringComparison.OrdinalIgnoreCase) >= 0) {
            return el;
          }
        } catch { }
      }
    } catch { }
    return null;
  }

  private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
  [System.Runtime.InteropServices.DllImport("user32.dll")]
  private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
  [System.Runtime.InteropServices.DllImport("user32.dll")]
  private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
  [System.Runtime.InteropServices.DllImport("user32.dll")]
  private static extern bool IsWindowVisible(IntPtr hWnd);
}
