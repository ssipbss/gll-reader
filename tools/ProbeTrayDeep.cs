using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;

class ProbeTrayDeep {
  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  private static extern IntPtr FindWindow(string cls, string name);

  private static void Log(string s) {
    try { File.AppendAllText("C:\\tmp\\traydeep.log", DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + "\r\n"); } catch { }
  }

  private static void DumpTree(AutomationElement el, int depth) {
    if (el == null || depth > 5) return;
    try {
      Log(new string(' ', depth * 2) + "type=" + el.Current.ControlType.ProgrammaticName +
          " name=[" + (el.Current.Name ?? "").Replace("\r", " ").Replace("\n", " ") + "]" +
          " cls=[" + (el.Current.ClassName ?? "") + "]");
    } catch { }
    try {
      AutomationElementCollection kids = el.FindAll(TreeScope.Children, Condition.TrueCondition);
      foreach (AutomationElement k in kids) DumpTree(k, depth + 1);
    } catch { }
  }

  [STAThread]
  private static void Main() {
    Log("START");
    IntPtr tray = FindWindow("Shell_TrayWnd", null);
    AutomationElement rootEl = AutomationElement.FromHandle(tray);
    AutomationElementCollection btns = rootEl.FindAll(TreeScope.Descendants,
      new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
    foreach (AutomationElement el in btns) {
      string n = el.Current.Name ?? "";
      if (n.IndexOf("托盘输入指示器", StringComparison.Ordinal) >= 0) {
        Log("==== BUTTON name=[" + n.Replace("\r", " ").Replace("\n", " ") + "] ====");
        DumpTree(el, 1);
      }
    }
    Log("DONE");
  }
}
