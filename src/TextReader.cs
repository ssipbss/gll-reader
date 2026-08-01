using System;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace GenDaLangDu {
  public static class TextReader {
    public static string GetFocusedText(out string elementId) {
      elementId = null;
      try {
        AutomationElement el = AutomationElement.FocusedElement;
        if (el == null) return null;
        try {
          int[] rid = el.GetRuntimeId();
          if (rid != null && rid.Length > 0) elementId = string.Join("-", rid);
        } catch { }
        if (elementId == null) {
          elementId = (el.Current.ClassName ?? "") + "|" + (el.Current.Name ?? "");
        }
        string t = TryReadText(el);
        if (t != null) return t;
        AutomationElement cur = el;
        for (int i = 0; i < 12; i++) {
          try { cur = TreeWalker.ControlViewWalker.GetParent(cur); } catch { break; }
          if (cur == null) break;
          t = TryReadText(cur);
          if (t != null) return t;
        }
        try {
          AutomationElementCollection kids = el.FindAll(TreeScope.Children, Condition.TrueCondition);
          for (int i = 0; i < kids.Count && i < 12; i++) {
            t = TryReadText(kids[i]);
            if (t != null) return t;
          }
        } catch { }
        return null;
      } catch {
        return null;
      }
    }

    private static string TryReadText(AutomationElement el) {
      if (el == null) return null;
      try {
        object pattern;
        if (el.TryGetCurrentPattern(ValuePattern.Pattern, out pattern)) {
          string v = ((ValuePattern)pattern).Current.Value;
          if (!string.IsNullOrEmpty(v)) return v;
        }
        if (el.TryGetCurrentPattern(TextPattern.Pattern, out pattern)) {
          TextPattern tp = (TextPattern)pattern;
          TextPatternRange range = tp.DocumentRange;
          try {
            TextPatternRange tail = range.Clone();
            tail.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -5000);
            return tail.GetText(-1);
          } catch {
            return range.GetText(20000);
          }
        }
      } catch { }
      return null;
    }
  }
}
