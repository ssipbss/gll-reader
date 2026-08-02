using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace UiaAttrProbe {
  public class AttrForm : Form {
    private static readonly object LogLock = new object();
    private static readonly string LogPath = @"C:\tmp\uia_probe\attr.log";
    private AutomationElement _el = null;
    private AutomationElement _root = null;
    private string _lastLog = "";
    private Timer _timer;
    private readonly string _initWindow;

    public AttrForm(string initWindow) {
      _initWindow = initWindow;
      Text = "UIA Attr Probe";
      WindowState = FormWindowState.Minimized;
      ShowInTaskbar = false;
      Load += OnLoad;
      FormClosing += (s, e) => Log("ATTR_STOP");
    }

    private void OnLoad(object sender, EventArgs e) {
      Log("ATTR_START");
      _timer = new Timer();
      _timer.Interval = 300;
      _timer.Tick += delegate { Snap(); };
      _timer.Start();
      if (_initWindow != null) {
        try {
          AutomationElement root = AutomationElement.FromHandle(
            new IntPtr(long.Parse(_initWindow, NumberStyles.HexNumber)));
          _root = root;
          AutomationElement el = root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ClassNameProperty, "Textfield"));
          if (el == null) {
            el = root.FindFirst(TreeScope.Descendants,
              new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
          }
          if (el != null) {
            _el = el;
            Log("TEXTFIELD_FOUND cls=[" + el.Current.ClassName + "]");
          }
        } catch (Exception ex) {
          Log("INIT_ERR " + ex.Message);
        }
      }
    }

    private void Snap() {
      try {
        AutomationElement el = _el;
        try {
          AutomationElement focused = AutomationElement.FocusedElement;
          object fp;
          if (focused != null && focused.TryGetCurrentPattern(TextPattern.Pattern, out fp)) {
            el = focused;
          }
        } catch { }
        if (_root != null) {
          try {
            AutomationElement fresh = _root.FindFirst(TreeScope.Descendants,
              new PropertyCondition(AutomationElement.ClassNameProperty, "Textfield"));
            if (fresh == null) {
              fresh = _root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            }
            if (fresh != null) el = fresh;
          } catch { }
        }
        if (el == null) return;
        object patObj;
        if (!el.TryGetCurrentPattern(TextPattern.Pattern, out patObj)) return;
        TextPattern tp = (TextPattern)patObj;
        TextPatternRange doc = tp.DocumentRange;
        string text = doc.GetText(-1);
        if (text == null) text = "";
        string docUnderline = AttrName(doc.GetAttributeValue(TextPattern.UnderlineStyleAttribute));
        string docBg = AttrName(doc.GetAttributeValue(TextPattern.BackgroundColorAttribute));
        string caretUnderline = "";
        string selText = "";
        string selUnderline = "";
        int caret = text.Length;
        try {
          TextPatternRange[] sel = tp.GetSelection();
          if (sel.Length > 0) {
            int c = sel[0].CompareEndpoints(TextPatternRangeEndpoint.Start, doc,
              TextPatternRangeEndpoint.Start);
            if (c >= 0 && c <= text.Length) caret = c;
            try { selText = sel[0].GetText(-1); } catch { }
            try { selUnderline = AttrName(sel[0].GetAttributeValue(TextPattern.UnderlineStyleAttribute)); } catch { }
            TextPatternRange r = sel[0].Clone();
          try {
            r.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -1);
            if (r.CompareEndpoints(TextPatternRangeEndpoint.Start, r,
                  TextPatternRangeEndpoint.End) == 0) {
              r.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, 1);
            }
          } catch { }
          caretUnderline = AttrName(r.GetAttributeValue(TextPattern.UnderlineStyleAttribute));
        }
        } catch { }
        string line = "caret=" + caret + " uline=[" + docUnderline + "]/[" + caretUnderline +
                      "] sel=[" + selUnderline + "] selText=[" + Trunc(selText, 20) +
                      "] bg=[" + docBg + "] text=[" + Trunc(text, 40) + "]";
        if (line != _lastLog) {
          _lastLog = line;
          Log(line);
        }
      } catch (Exception ex) {
        Log("SNAP_ERR " + ex.Message);
      }
    }

    private static string AttrName(object v) {
      if (v == null) return "null";
      if (v == TextPattern.MixedAttributeValue) return "MIXED";
      return v.ToString();
    }

    private static string Trunc(string s, int n) {
      if (s == null) return "";
      s = s.Replace("\r", "\\r").Replace("\n", "\\n");
      if (s.Length > n) s = s.Substring(0, n) + "...";
      return s;
    }

    private static void Log(string line) {
      try {
        lock (LogLock) {
          File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + "\r\n",
            new UTF8Encoding(false));
        }
      } catch { }
    }
  }

  public static class Program {
    [STAThread]
    public static void Main() {
      string initWindow = null;
      string[] args = Environment.GetCommandLineArgs();
      for (int i = 1; i < args.Length; i++) {
        if (args[i] == "--window" && i + 1 < args.Length) initWindow = args[i + 1].Replace("0x", "");
      }
      Application.EnableVisualStyles();
      Application.Run(new AttrForm(initWindow));
    }
  }
}
