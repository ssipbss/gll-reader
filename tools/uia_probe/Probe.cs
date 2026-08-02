using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Windows.Automation;

namespace UiaTextProbe {
  public class ProbeForm : Form {
    private static readonly object LogLock = new object();
    private static readonly string LogPath = @"C:\tmp\uia_probe\probe.log";
    private AutomationElement _cur = null;
    private string _lastText = null;
    private int _evtCount = 0;
    private Timer _focusTimer;
    private Timer _hbTimer;
    private readonly string _initWindow = null;
    private readonly string _initHwnd = null;
    private readonly bool _selfTest = false;
    private TextBox _selfBox;
    private Timer _selfTimer;

    public ProbeForm(string initWindow, string initHwnd, bool selfTest) {
      _initWindow = initWindow;
      _initHwnd = initHwnd;
      _selfTest = selfTest;
      Text = "UIA Text Probe";
      WindowState = FormWindowState.Minimized;
      ShowInTaskbar = false;
      Load += OnLoad;
      FormClosing += (s, e) => {
        try { Automation.RemoveAllEventHandlers(); } catch { }
        Log("PROBE_STOP");
      };
    }

    private void OnLoad(object sender, EventArgs e) {
      Log("PROBE_START");
      _focusTimer = new Timer();
      _focusTimer.Interval = 300;
      _focusTimer.Tick += OnFocusTick;
      _focusTimer.Start();
      _hbTimer = new Timer();
      _hbTimer.Interval = 5000;
      _hbTimer.Tick += (s2, e2) => Log("HB evt=" + _evtCount);
      _hbTimer.Start();
      Log("FOCUS_POLL_START");
      if (_initHwnd != null) {
        try {
          AutomationElement el = AutomationElement.FromHandle(
            new IntPtr(long.Parse(_initHwnd, NumberStyles.HexNumber)));
          Log("INIT_HWND=" + _initHwnd);
          Subscribe(el);
        } catch (Exception ex) {
          Log("INIT_HWND_ERR " + ex.Message);
        }
      }
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
      if (_selfTest) {
        try {
          _selfBox = new TextBox { Visible = false };
          Controls.Add(_selfBox);
          AutomationElement self = AutomationElement.FromHandle(_selfBox.Handle);
          Log("SELFTEST_SUBSCRIBE");
          Subscribe(self);
          _selfTimer = new Timer();
          _selfTimer.Interval = 2000;
          int n = 0;
          _selfTimer.Tick += (s2, e2) => {
            n++;
            _selfBox.Text = "self-test-" + n;
            Log("SELFTEST_SET text=self-test-" + n);
          };
          _selfTimer.Start();
        } catch (Exception ex) {
          Log("SELFTEST_ERR " + ex.Message);
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
            if (el.Current.ProcessId == _cur.Current.ProcessId &&
                el.Current.NativeWindowHandle == _cur.Current.NativeWindowHandle) return;
          } catch { }
        }
        string cls = "", name = "", ctl = "", pid = "";
        try { cls = el.Current.ClassName; } catch { }
        try { name = el.Current.Name; } catch { }
        try { ctl = el.Current.ControlType.ProgrammaticName; } catch { }
        try { pid = el.Current.ProcessId.ToString(); } catch { }
        Log("FOCUS cls=[" + cls + "] ctl=[" + ctl + "] pid=" + pid + " name=[" + Trunc(name, 60) + "]");
        Subscribe(el);
      } catch (Exception ex) {
        Log("FOCUS_ERR " + ex.Message);
      }
    }

    private void Subscribe(AutomationElement el) {
      _cur = el;
      _lastText = null;
      try {
        object pat;
        if (!el.TryGetCurrentPattern(TextPattern.Pattern, out pat)) {
          Log("SKIP_NO_TEXTPATTERN");
          return;
        }
        string cls = "", ctl = "", name = "", aid = "";
        string hwnd = "";
        try { cls = el.Current.ClassName; } catch { }
        try { ctl = el.Current.ControlType.ProgrammaticName; } catch { }
        try { name = el.Current.Name; } catch { }
        try { aid = el.Current.AutomationId; } catch { }
        try { hwnd = "0x" + el.Current.NativeWindowHandle.ToString("X"); } catch { }
        Log("SUBSCRIBE hwnd=" + hwnd + " cls=[" + cls + "] ctl=[" + ctl + "] aid=[" + aid + "] name=[" + Trunc(name, 50) + "]");
        Automation.AddAutomationEventHandler(TextPattern.TextChangedEvent, el,
          TreeScope.Element | TreeScope.Descendants, OnTextChanged);
        Automation.AddAutomationPropertyChangedEventHandler(el, TreeScope.Element, OnPropChanged,
          ValuePattern.ValueProperty, AutomationElement.NameProperty);
        Log("SUBSCRIBED");
      } catch (Exception ex) {
        Log("SUB_ERR " + ex.Message);
      }
    }

    private void OnTextChanged(object sender, AutomationEventArgs e) {
      try {
        AutomationElement el = sender as AutomationElement;
        if (el == null) return;
        _evtCount++;
        string cls = "";
        try { cls = el.Current.ClassName; } catch { }
        string txt = GetText(el);
        string head = "EVT#" + _evtCount + " TextChanged cls=[" + cls + "]";
        if (txt == null) {
          Log(head + " text=<null>");
          return;
        }
        string delta = "";
        if (_lastText != null && _lastText != txt) {
          delta = " old=[" + Trunc(_lastText, 120) + "]";
        }
        _lastText = txt;
        Log(head + " new=[" + Trunc(txt, 160) + "]" + delta);
      } catch (Exception ex) {
        Log("EVT_ERR " + ex.Message);
      }
    }

    private void OnPropChanged(object sender, AutomationPropertyChangedEventArgs e) {
      try {
        _evtCount++;
        string v = e.NewValue == null ? "<null>" : e.NewValue.ToString();
        Log("EVT#" + _evtCount + " PROP " + e.Property.ProgrammaticName + "=[" + Trunc(v, 120) + "]");
      } catch (Exception ex) {
        Log("PROP_ERR " + ex.Message);
      }
    }

    private static string GetText(AutomationElement el) {
      try {
        object pat;
        if (el.TryGetCurrentPattern(TextPattern.Pattern, out pat)) {
          TextPattern tp = (TextPattern)pat;
          return tp.DocumentRange.GetText(-1);
        }
      } catch { }
      return null;
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
      string initWindow = null, initHwnd = null;
      string[] args = Environment.GetCommandLineArgs();
      for (int i = 1; i < args.Length; i++) {
        if (args[i] == "--window" && i + 1 < args.Length) initWindow = args[i + 1].Replace("0x", "");
        if (args[i] == "--hwnd" && i + 1 < args.Length) initHwnd = args[i + 1].Replace("0x", "");
      }
      bool selfTest = Array.IndexOf(args, "--selftest") >= 0;
      Application.EnableVisualStyles();
      Application.Run(new ProbeForm(initWindow, initHwnd, selfTest));
    }
  }
}
