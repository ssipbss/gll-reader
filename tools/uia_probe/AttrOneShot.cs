using System;
using System.Windows.Automation;
using System.Windows.Automation.Text;

public static class AttrOneShot {
  [STAThread]
  public static void Main() {
    try {
      AutomationElement f = AutomationElement.FocusedElement;
      Console.WriteLine("cls=[" + SafeCls(f) + "] pid=" + SafePid(f));
      object pat;
      if (!f.TryGetCurrentPattern(TextPattern.Pattern, out pat)) {
        Console.WriteLine("NO_TEXTPATTERN");
        return;
      }
      TextPattern tp = (TextPattern)pat;
      TextPatternRange doc = tp.DocumentRange;
      Console.WriteLine("text=[" + doc.GetText(-1) + "]");
      Console.WriteLine("docUline=" + SafeAttr(doc, TextPattern.UnderlineStyleAttribute));
      Console.WriteLine("docBg=" + SafeAttr(doc, TextPattern.BackgroundColorAttribute));
      Console.WriteLine("docOutline=" + SafeAttr(doc, TextPattern.OutlineStylesAttribute));
      Console.WriteLine("docHidden=" + SafeAttr(doc, TextPattern.IsHiddenAttribute));
      Console.WriteLine("docWeight=" + SafeAttr(doc, TextPattern.FontWeightAttribute));
      TextPatternRange[] sel = tp.GetSelection();
      Console.WriteLine("selCount=" + sel.Length);
      for (int i = 0; i < sel.Length; i++) {
        try {
          int s = sel[i].CompareEndpoints(TextPatternRangeEndpoint.Start, doc,
            TextPatternRangeEndpoint.Start);
          int e = sel[i].CompareEndpoints(TextPatternRangeEndpoint.End, doc,
            TextPatternRangeEndpoint.Start);
          Console.WriteLine("sel" + i + " start=" + s + " end=" + e + " span=" + (e - s));
        } catch (Exception ex) {
          Console.WriteLine("sel" + i + "ERR " + ex.GetType().Name);
        }
      }
    } catch (Exception ex) {
      Console.WriteLine("ERR " + ex.GetType().Name + ": " + ex.Message);
    }
  }

  private static string SafeCls(AutomationElement el) {
    try { return el.Current.ClassName; } catch { return "?"; }
  }

  private static int SafePid(AutomationElement el) {
    try { return el.Current.ProcessId; } catch { return -1; }
  }

  private static string SafeAttr(TextPatternRange r, AutomationTextAttribute attr) {
    try {
      object v = r.GetAttributeValue(attr);
      if (v == null) return "null";
      if (v == TextPattern.MixedAttributeValue) return "MIXED";
      return v.ToString();
    } catch (Exception ex) {
      return "ERR " + ex.Message;
    }
  }
}
