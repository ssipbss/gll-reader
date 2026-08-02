using System;
using System.IO;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;

class ProbeSelEvent {
  private static string _log;
  private static AutomationFocusChangedEventHandler _focusHandler;
  private static AutomationEventHandler _selHandler;
  private static AutomationElement _subscribed;

  private static void Log(string s) {
    try { File.AppendAllText(_log, DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + "\r\n"); } catch { }
  }

  private static void Subscribe() {
    try {
      if (_subscribed != null) {
        try { Automation.RemoveAutomationEventHandler(TextPattern.TextSelectionChangedEvent, _subscribed, _selHandler); } catch { }
        _subscribed = null;
      }
      AutomationElement el = AutomationElement.FocusedElement;
      if (el == null) { Log("FOCUS null"); return; }
      AutomationElement target = el;
      AutomationElement cur = el;
      for (int i = 0; i < 12; i++) {
        if (cur == null) break;
        try {
          object p;
          if (cur.TryGetCurrentPattern(TextPattern.Pattern, out p)) { target = cur; break; }
        } catch { }
        try { cur = TreeWalker.ControlViewWalker.GetParent(cur); } catch { break; }
      }
      if (_selHandler == null) {
        _selHandler = delegate(object src, AutomationEventArgs e) {
          Log("SEL_EVENT from=" + ((AutomationElement)src).Current.ControlType.ProgrammaticName);
          try {
            AutomationElement fe = AutomationElement.FocusedElement;
            object pat;
            if (fe.TryGetCurrentPattern(TextPattern.Pattern, out pat)) {
              TextPatternRange[] sel = ((TextPattern)pat).GetSelection();
              Log("  selCount=" + (sel == null ? -1 : sel.Length));
              if (sel != null && sel.Length > 0) {
                try {
                  int cmp = sel[0].CompareEndpoints(TextPatternRangeEndpoint.Start, sel[0], TextPatternRangeEndpoint.End);
                  Log("  cmp=" + cmp);
                } catch (Exception ex) {
                  Log("  cmp_FAIL " + ex.Message);
                }
                try {
                  System.Windows.Rect[] rects = sel[0].GetBoundingRectangles();
                  Log("  rects=" + (rects == null ? -1 : rects.Length));
                } catch (Exception ex) {
                  Log("  rects_FAIL " + ex.Message);
                }
              }
            }
          } catch (Exception ex) {
            Log("  detail_FAIL " + ex.Message);
          }
        };
      }
      Automation.AddAutomationEventHandler(
        TextPattern.TextSelectionChangedEvent, target,
        TreeScope.Element | TreeScope.Descendants, _selHandler);
      _subscribed = target;
      Log("SUBSCRIBE type=" + target.Current.ControlType.ProgrammaticName);
    } catch (Exception ex) {
      Log("SUBSCRIBE_FAIL " + ex.Message);
    }
  }

  [STAThread]
  private static void Main(string[] args) {
    _log = args.Length > 0 ? args[0] : "C:\\tmp\\probesel.log";
    _focusHandler = delegate(object src, AutomationFocusChangedEventArgs e) {
      try { Subscribe(); } catch { }
    };
    Automation.AddAutomationFocusChangedEventHandler(_focusHandler);
    Subscribe();
    Log("START");
    Thread.Sleep(10000);
    Log("DONE");
  }
}
