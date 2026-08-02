using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace UiaCaretProbe {
  public class CaretForm : Form {
    private static readonly object LogLock = new object();
    private static readonly string LogPath = @"C:\tmp\uia_probe\caret.log";
    private AutomationElement _cur = null;
    private string _lastLeft = null;
    private int _evtCount = 0;
    private Timer _pollTimer;
    private Timer _focusTimer;
    private readonly string _initWindow;

    public CaretForm(string initWindow) {
      _initWindow = initWindow;
      Text = "UIA Caret Probe";
      WindowState = FormWindowState.Minimized;
      ShowInTaskbar = false;
      Load += OnLoad;
      FormClosing += (s, e) => {
        try { Automation.RemoveAllEventHandlers(); } catch { }
        Log("CARET_STOP");
      };
    }

    private void OnLoad(object sender, EventArgs e) {
      Log("CARET_START");
      _focusTimer = new Timer();
      _focusTimer.Interval = 300;
      _focusTimer.Tick += OnFocusTick;
      _focusTimer.Start();
      _pollTimer = new Timer();
      _pollTimer.Interval = 20;
      _pollTimer.Tick += delegate { Snap("POLL"); };
      _pollTimer.Start();
      if (_initWindow != null) {
        try {
          AutomationElement root = AutomationElement.FromHandle(
            new IntPtr(long.Parse(_initWindow, NumberStyles.HexNumber)));
          AutomationElement found = FindFirstTextEdit(root);
          if (found != null) {
            Log("INIT_WINDOW_FOUND");
            Subscribe(found);
          } else {
            Log("INIT_WINDOW_NOT_FOUND");
          }
        } catch (Exception ex) {
          Log("INIT_WINDOW_ERR " + ex.Message);
        }
      }
    }

    private static AutomationElement FindFirstTextEdit(AutomationElement root) {
      try {
        AutomationElement el = root.FindFirst(TreeScope.Descendants,
          new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
        if (el != null && HasTextPattern(el)) return el;
        el = root.FindFirst(TreeScope.Descendants,
          new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
        if (el != null && HasTextPattern(el)) return el;
      } catch { }
      return null;
    }

    private static bool HasTextPattern(AutomationElement el) {
      try {
        object pat;
        return el.TryGetCurrentPattern(TextPattern.Pattern, out pat);
      } catch {
        return false;
      }
    }

    private void OnFocusTick(object sender, EventArgs e) {
      try {
        AutomationElement el = AutomationElement.FocusedElement;
        if (el == null) return;
        if (_cur != null) {
          try {
            int[] a = el.GetRuntimeId();
            int[] b = _cur.GetRuntimeId();
            if (a != null && b != null && a.Length == b.Length) {
              bool same = true;
              for (int i = 0; i < a.Length; i++) {
                if (a[i] != b[i]) { same = false; break; }
              }
              if (same) return;
            }
          } catch { }
        }
        string cls = "";
        try { cls = el.Current.ClassName; } catch { }
        Log("FOCUS cls=[" + cls + "]");
        Subscribe(el);
      } catch (Exception ex) {
        Log("FOCUS_ERR " + ex.Message);
      }
    }

    private void Subscribe(AutomationElement el) {
      _cur = el;
      _lastLeft = null;
      try {
        if (!HasTextPattern(el)) {
          Log("SKIP_NO_TEXTPATTERN");
          return;
        }
        string cls = "";
        try { cls = el.Current.ClassName; } catch { }
        Log("SUBSCRIBE cls=[" + cls + "]");
        Log("SUBSCRIBED");
      } catch (Exception ex) {
        Log("SUB_ERR " + ex.Message);
      }
    }

    private void OnTextChanged(object sender, AutomationEventArgs e) {
      try {
        AutomationElement el = sender as AutomationElement;
        if (el == null) return;
        if (_cur == null) { _cur = el; _lastLeft = null; }
        if (el != _cur && el.Current.ProcessId == _cur.Current.ProcessId) return;
        Snap("EVT");
      } catch (Exception ex) {
        Log("EVT_ERR " + ex.Message);
      }
    }

    private void Snap(string tag) {
      try {
        AutomationElement el = null;
        try { el = AutomationElement.FocusedElement; } catch { }
        if (el == null || !HasTextPattern(el)) {
          el = _cur;
        }
        if (el == null) return;
        object patObj;
        if (!el.TryGetCurrentPattern(TextPattern.Pattern, out patObj)) return;
        TextPattern tp = (TextPattern)patObj;
        TextPatternRange doc = tp.DocumentRange;
        string text = doc.GetText(-1);
        if (text == null) text = "";
        if (_cur == null || !SameElement(el, _cur)) {
          _cur = el;
          _lastLeft = null;
          Log("ELEMENT_SWITCH total=" + text.Length);
        }
        int caret = text.Length;
        try {
          TextPatternRange[] sel = tp.GetSelection();
          if (sel.Length > 0) {
            int c = sel[0].CompareEndpoints(TextPatternRangeEndpoint.Start, doc,
              TextPatternRangeEndpoint.Start);
            if (c >= 0 && c <= text.Length) caret = c;
          }
        } catch { }
        string left = caret <= text.Length ? text.Substring(0, caret) : text;
        if (left == _lastLeft) return;
        string delta = ComputeDelta(_lastLeft, left);
        _evtCount++;
        Log("SNAP#" + _evtCount + " " + tag + " delta=[" + Trunc(delta, 60) + "] caret=" + caret +
            " total=" + text.Length + " left=[" + Trunc(left, 80) + "]");
        _lastLeft = left;
      } catch (Exception ex) {
        Log("SNAP_ERR " + ex.Message);
      }
    }

    private static bool SameElement(AutomationElement a, AutomationElement b) {
      try {
        int[] x = a.GetRuntimeId();
        int[] y = b.GetRuntimeId();
        if (x == null || y == null || x.Length != y.Length) return false;
        for (int i = 0; i < x.Length; i++) {
          if (x[i] != y[i]) return false;
        }
        return true;
      } catch {
        return false;
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
      Application.Run(new CaretForm(initWindow));
    }
  }
}
