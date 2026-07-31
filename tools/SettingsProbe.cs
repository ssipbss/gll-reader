using System;
using System.Threading;
using System.Windows.Automation;

class SettingsProbe {
  static int Main(string[] args) {
    bool toggle = args.Length > 0 && args[0] == "toggle";
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    try {
      AutomationElement win = null;
      for (int i = 0; i < 40; i++) {
        win = AutomationElement.RootElement.FindFirst(TreeScope.Children,
          new PropertyCondition(AutomationElement.NameProperty, "归零归零"));
        if (win != null) break;
        Thread.Sleep(500);
      }
      if (win == null) {
        Console.WriteLine("WINDOW_NOT_FOUND");
        return 2;
      }
      AutomationElement cb = win.FindFirst(TreeScope.Descendants,
        new PropertyCondition(AutomationElement.NameProperty, "记录调试日志"));
      if (cb == null) {
        Console.WriteLine("CHECKBOX_NOT_FOUND");
        return 3;
      }
      object patternObj;
      if (!cb.TryGetCurrentPattern(TogglePattern.Pattern, out patternObj)) {
        Console.WriteLine("NO_TOGGLE_PATTERN");
        return 4;
      }
      TogglePattern tp = (TogglePattern)patternObj;
      ToggleState before = tp.Current.ToggleState;
      Console.WriteLine("BEFORE=" + before);
      if (toggle) {
        tp.Toggle();
        Thread.Sleep(600);
        ToggleState after = tp.Current.ToggleState;
        Console.WriteLine("AFTER=" + after);
      }
      return 0;
    } catch (Exception ex) {
      Console.WriteLine("ERR: " + ex.Message);
      return 1;
    }
  }
}
