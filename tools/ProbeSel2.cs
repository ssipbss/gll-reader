using System;
using System.IO;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;

class ProbeSel2 {
  private static string _log;

  private static void Log(string s) {
    try { File.AppendAllText(_log, DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + "\r\n"); } catch { }
  }

  [STAThread]
  private static void Main(string[] args) {
    _log = args.Length > 0 ? args[0] : "C:\\tmp\\probesel2.log";
    for (int i = 0; i < 14; i++) {
      try {
        AutomationElement el = AutomationElement.FocusedElement;
        if (el == null) { Log("focus=null"); continue; }
        object pat;
        if (!el.TryGetCurrentPattern(TextPattern.Pattern, out pat)) { Log("no-textpattern type=" + el.Current.ControlType.ProgrammaticName); continue; }
        TextPattern tp = (TextPattern)pat;
        TextPatternRange[] sel = tp.GetSelection();
        string detail = "sel=" + (sel == null ? -1 : sel.Length);
        if (sel != null && sel.Length > 0) {
          try {
            int cmp = sel[0].CompareEndpoints(TextPatternRangeEndpoint.Start, sel[0], TextPatternRangeEndpoint.End);
            detail += " cmp=" + cmp;
          } catch (Exception ex) {
            detail += " cmp_FAIL=" + ex.Message;
          }
          try {
            System.Windows.Rect[] rs = sel[0].GetBoundingRectangles();
            if (rs == null || rs.Length == 0) {
              detail += " rects=0";
            } else {
              double w = 0;
              for (int j = 0; j < rs.Length; j++) w += rs[j].Width;
              detail += " rects=" + rs.Length + " w=" + w.ToString("0.0");
            }
          } catch (Exception ex) {
            detail += " rects_FAIL=" + ex.Message;
          }
        }
        Log(detail + " cls=" + (el.Current.ClassName ?? "") + " name=" + (el.Current.Name ?? ""));
      } catch (Exception ex) {
        Log("ERR " + ex.Message);
      }
      Thread.Sleep(500);
    }
    Log("DONE");
  }
}
