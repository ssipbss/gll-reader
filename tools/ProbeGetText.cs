using System;
using System.IO;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;

class ProbeGetText {
  private static string _log;

  private static void Log(string s) {
    try { File.AppendAllText(_log, DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + "\r\n"); } catch { }
  }

  [STAThread]
  private static void Main(string[] args) {
    _log = args.Length > 0 ? args[0] : "C:\\tmp\\probegettext.log";
    for (int i = 0; i < 16; i++) {
      try {
        AutomationElement el = AutomationElement.FocusedElement;
        if (el == null) { Log("focus=null"); continue; }
        object pat;
        if (!el.TryGetCurrentPattern(TextPattern.Pattern, out pat)) { Log("no-textpattern"); continue; }
        TextPatternRange[] sel = ((TextPattern)pat).GetSelection();
        if (sel == null || sel.Length == 0) { Log("no-sel"); continue; }
        try {
          string t = sel[0].GetText(5000);
          Log("GOT len=" + (t == null ? -1 : t.Length));
        } catch (Exception ex) {
          Log("GETTEXT_EX " + ex.Message);
        }
      } catch (Exception ex) {
        Log("ERR " + ex.Message);
      }
      Thread.Sleep(600);
    }
    Log("DONE");
  }
}
