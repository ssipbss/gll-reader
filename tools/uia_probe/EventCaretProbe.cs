using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace UiaEventCaretProbe {
  public class EventCaretForm : Form {
    private static readonly object LogLock = new object();
    private static readonly string LogPath = @"C:\tmp\uia_probe\event_caret.log";
    private AutomationElement _el = null;
    private AutomationElement _root = null;
    private string _lastLeft = null;
    private int _evtCount = 0;
    private Timer _hbTimer;
    private readonly string _initWindow;

    public EventCaretForm(string initWindow) {
      _initWindow = initWindow;
      Text = "UIA Event Caret Probe";
      WindowState = FormWindowState.Minimized;
      ShowInTaskbar = false;
      Load += OnLoad;
      FormClosing += (s, e) => {
        try { Automation.RemoveAllEventHandlers(); } catch { }
        Log("STOP");
      };
    }

    private void OnLoad(object sender, EventArgs e) {
      Log("START");
      _hbTimer = new Timer();
      _hbTimer.Interval = 2000;
      _hbTimer.Tick += (s2, e2) => Log("HB evt=" + _evtCount);
      _hbTimer.Start();
      if (_initWindow != null) {
        try {
          AutomationElement root = AutomationElement.FromHandle(
            new IntPtr(long.Parse(_initWindow, NumberStyles.HexNumber)));
          _root = root;
          AutomationElement found = FindTextfield(root);
          if (found != null) {
            Log("TEXTFIELD_FOUND");
            Subscribe(found);
          } else {
            Log("TEXTFIELD_NOT_FOUND");
          }
          Automation.AddAutomationEventHandler(TextPattern.TextChangedEvent, root,
            TreeScope.Descendants, OnChanged);
          Log("ROOT_SUBSCRIBED");
        } catch (Exception ex) {
          Log("INIT_ERR " + ex.Message);
        }
      }
    }

    private static AutomationElement FindTextfield(AutomationElement root) {
      try {
        AutomationElement el = root.FindFirst(TreeScope.Descendants,
          new PropertyCondition(AutomationElement.ClassNameProperty, "Textfield"));
        if (el != null) return el;
        el = root.FindFirst(TreeScope.Descendants,
          new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
        if (el != null) return el;
      } catch { }
      return null;
    }

    private void Subscribe(AutomationElement el) {
      _el = el;
      _lastLeft = null;
      try {
        object pat;
        if (!el.TryGetCurrentPattern(TextPattern.Pattern, out pat)) {
          Log("SKIP_NO_TEXTPATTERN");
          return;
        }
        string cls = "";
        try { cls = el.Current.ClassName; } catch { }
        Log("SUBSCRIBE cls=[" + cls + "]");
        Automation.AddAutomationEventHandler(TextPattern.TextChangedEvent, el,
          TreeScope.Element | TreeScope.Descendants, OnChanged);
        Automation.AddAutomationPropertyChangedEventHandler(el, TreeScope.Element, OnPropChanged,
          ValuePattern.ValueProperty, AutomationElement.NameProperty);
        Log("SUBSCRIBED");
      } catch (Exception ex) {
        Log("SUB_ERR " + ex.Message);
      }
    }

    private void OnChanged(object sender, AutomationEventArgs e) {
      try {
        AutomationElement el = sender as AutomationElement;
        if (el == null) return;
        string cls = "";
        try { cls = el.Current.ClassName; } catch { }
        _evtCount++;
        Log("EVT_SRC cls=[" + cls + "]");
        AutomationElement read = el;
        object patObj;
        if (!read.TryGetCurrentPattern(TextPattern.Pattern, out patObj)) return;
        TextPattern tp = (TextPattern)patObj;
        TextPatternRange doc = tp.DocumentRange;
        string text = doc.GetText(-1);
        if (text == null) text = "";
        int caret = -1;
        try {
          AutomationElement focused = null;
          try { focused = AutomationElement.FocusedElement; } catch { }
          string fcls = "";
          try { if (focused != null) fcls = focused.Current.ClassName; } catch { }
          object fp;
          if (focused != null && focused.TryGetCurrentPattern(TextPattern.Pattern, out fp)) {
            TextPattern ftp = (TextPattern)fp;
            TextPatternRange[] sel = ftp.GetSelection();
            if (sel.Length > 0) {
              caret = sel[0].CompareEndpoints(TextPatternRangeEndpoint.Start, ftp.DocumentRange,
                TextPatternRangeEndpoint.Start);
            }
            Log("CARET_SRC focused cls=[" + fcls + "] c=" + caret);
          } else {
            Log("CARET_SRC no-focused-pattern cls=[" + fcls + "]");
          }
          if (caret < 0 && _root != null) {
            AutomationElement tf = FindTextfield(_root);
            if (tf != null) {
              object tp2;
              if (tf.TryGetCurrentPattern(TextPattern.Pattern, out tp2)) {
                TextPattern ftp2 = (TextPattern)tp2;
                TextPatternRange[] sel2 = ftp2.GetSelection();
                if (sel2.Length > 0) {
                  caret = sel2[0].CompareEndpoints(TextPatternRangeEndpoint.Start,
                    ftp2.DocumentRange, TextPatternRangeEndpoint.Start);
                }
                Log("CARET_SRC fresh-textfield c=" + caret);
              }
            }
          }
          if (caret < 0 && _el != null) {
            TextPatternRange[] sel = tp.GetSelection();
            if (sel.Length > 0) {
              caret = sel[0].CompareEndpoints(TextPatternRangeEndpoint.Start, doc,
                TextPatternRangeEndpoint.Start);
            }
            Log("CARET_SRC cached c=" + caret);
          }
        } catch (Exception ex) {
          Log("CARET_ERR " + ex.Message);
        }
        if (caret < 0 || caret > text.Length) caret = text.Length;
        Log("EVT#DEBUG caret=" + caret + " total=" + text.Length);
        string left = caret <= text.Length ? text.Substring(0, caret) : text;
        string delta = ComputeDelta(_lastLeft, left);
        _lastLeft = left;
        Log("EVT#" + _evtCount + " caret=" + caret + " delta=[" + Trunc(delta, 60) +
            "] total=" + text.Length + " left=[" + Trunc(left, 80) + "]");
      } catch (Exception ex) {
        Log("EVT_ERR " + ex.Message);
      }
    }

    private void OnPropChanged(object sender, AutomationPropertyChangedEventArgs e) {
      try {
        AutomationElement el = sender as AutomationElement;
        string cls = "";
        try { cls = el.Current.ClassName; } catch { }
        string v = e.NewValue == null ? "<null>" : e.NewValue.ToString();
        _evtCount++;
        Log("PROP_SRC cls=[" + cls + "] " + e.Property.ProgrammaticName + "=[" + Trunc(v, 80) + "]");
        object patObj;
        if (el != null && el.TryGetCurrentPattern(TextPattern.Pattern, out patObj)) {
          TextPattern tp = (TextPattern)patObj;
          string text = tp.DocumentRange.GetText(-1);
          if (text == null) text = "";
          string delta = ComputeDelta(_lastLeft, text);
          _lastLeft = text;
          Log("PROP_DELTA=[" + Trunc(delta, 60) + "] total=" + text.Length);
        }
      } catch (Exception ex) {
        Log("PROP_ERR " + ex.Message);
      }
    }

    private static string ComputeDelta(string prev, string cur) {
      if (string.IsNullOrEmpty(prev)) return cur;
      if (cur.StartsWith(prev)) return cur.Substring(prev.Length);
      int p = 0;
      while (p < prev.Length && p < cur.Length && prev[p] == cur[p]) p++;
      string removed = prev.Substring(p);
      string added = cur.Substring(p);
      if (added.Length == 0) return "<del:" + removed + ">";
      return "<rep:" + removed + ">" + added;
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
      Application.Run(new EventCaretForm(initWindow));
    }
  }
}
