using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace GenDaLangDu {
  /// <summary>
  /// 通过 WPS 自带 COM 自动化接口读取文档文字和光标位置。
  /// 适用于 WPS 文字/表格/演示（UIA 无法读取的 WPS 自绘控件）。
  /// </summary>
  public static class WpsComReader {
    private static object _app;
    private static string _appProgId;
    private static long _badUntilTick;
    private static string _docKey;
    private static int _winBase;
    private const int TailChars = 5000;
    private const int HeadChars = 300;

    public static string GetFocusedText(out string elementId, out string diag, out int caret) {
      elementId = null;
      diag = null;
      caret = -1;
      try {
        string proc = ForegroundProcessName();
        string progId = null;
        if (string.Equals(proc, "wps", StringComparison.OrdinalIgnoreCase)) progId = "KWPS.Application";
        else if (string.Equals(proc, "et", StringComparison.OrdinalIgnoreCase)) progId = "KET.Application";
        else if (string.Equals(proc, "wpp", StringComparison.OrdinalIgnoreCase)) progId = "KWPP.Application";
        if (progId == null) return null;
        if (Environment.TickCount < _badUntilTick) return null;
        object app = GetApp(progId);
        if (app == null) return null;
        string r;
        if (progId == "KWPS.Application") r = ReadWord(app, out elementId, out diag, out caret);
        else if (progId == "KET.Application") r = ReadSheet(app, out elementId, out diag, out caret);
        else r = ReadSlide(app, out elementId, out diag, out caret);
        return r;
      } catch (Exception ex) {
        MarkBad();
        diag = "wpscom-err " + ex.Message;
        return null;
      }
    }

    private static object GetApp(string progId) {
      try {
        if (_app != null && _appProgId == progId) return _app;
        ReleaseApp();
        _app = Marshal.GetActiveObject(progId);
        _appProgId = progId;
        return _app;
      } catch {
        MarkBad();
        return null;
      }
    }

    private static void ReleaseApp() {
      try {
        if (_app != null) Marshal.ReleaseComObject(_app);
      } catch { }
      _app = null;
      _appProgId = null;
    }

    private static void MarkBad() {
      _badUntilTick = Environment.TickCount + 2000;
    }

    private static string ReadWord(object app, out string elementId, out string diag, out int caret) {
      elementId = null;
      diag = null;
      caret = -1;
      object doc = GetProp(app, "ActiveDocument");
      if (doc == null || doc is string) return null;
      object sel = GetProp(app, "Selection");
      int selStart = ToInt(GetProp(sel, "Start"));
      int selEnd = ToInt(GetProp(sel, "End"));
      object allRange = GetProp(doc, "Range");
      int docEnd = ToInt(GetProp(allRange, "End"));
      string name = ToStr(GetProp(doc, "Name"));
      elementId = "wpsw|" + name;
      string key = elementId;
      if (key != _docKey) {
        _docKey = key;
        _winBase = Math.Max(0, selStart - TailChars);
      } else {
        if (selStart > _winBase + TailChars + HeadChars) {
          _winBase = Math.Max(0, selStart - TailChars);
        } else if (selStart < _winBase) {
          _winBase = Math.Max(0, selStart - TailChars);
        }
      }
      int from = _winBase;
      int to = Math.Min(docEnd, Math.Max(from, selEnd + HeadChars));
      if (to < from) to = from;
      object rng = Call(doc, "Range", from, to);
      string text = ToStr(GetProp(rng, "Text"));
      if (text == null) return null;
      caret = selStart - from;
      diag = "wpscom kwps caret=" + selStart + " len=" + docEnd + " win=" + from + "-" + to;
      return text;
    }

    private static string ReadSheet(object app, out string elementId, out string diag, out int caret) {
      elementId = null;
      diag = null;
      caret = -1;
      object cell = GetProp(app, "ActiveCell");
      if (cell == null || cell is string) return null;
      string addr = ToStr(GetProp(cell, "Address"));
      object value = GetProp(cell, "Value");
      string text = value == null ? "" : value.ToString();
      object sheet = GetProp(app, "ActiveSheet");
      string sheetName = ToStr(GetProp(sheet, "Name"));
      elementId = "wpse|" + sheetName + "|" + addr;
      diag = "wpscom ket " + sheetName + "!" + addr;
      return text;
    }

    private static string ReadSlide(object app, out string elementId, out string diag, out int caret) {
      elementId = null;
      diag = null;
      caret = -1;
      object pres = GetProp(app, "ActivePresentation");
      if (pres == null || pres is string) return null;
      object win = GetProp(app, "ActiveWindow");
      object sel = GetProp(win, "Selection");
      object tr = GetProp(sel, "TextRange");
      string text = ToStr(GetProp(tr, "Text"));
      if (text == null) return null;
      string name = ToStr(GetProp(pres, "Name"));
      elementId = "wpsp|" + name;
      diag = "wpscom wpp";
      return text;
    }

    private static string ForegroundProcessName() {
      try {
        IntPtr h = Native.GetForegroundWindow();
        if (h == IntPtr.Zero) return null;
        uint pid;
        Native.GetWindowThreadProcessId(h, out pid);
        if (pid == 0) return null;
        using (Process p = Process.GetProcessById((int)pid)) {
          return p.ProcessName;
        }
      } catch {
        return null;
      }
    }

    private static object GetProp(object o, string name) {
      return o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, null, CultureInfo.InvariantCulture);
    }

    private static object Call(object o, string name, params object[] args) {
      return o.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, o, args, CultureInfo.InvariantCulture);
    }

    private static int ToInt(object v) {
      if (v == null) return 0;
      try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { return 0; }
    }

    private static string ToStr(object v) {
      return v == null ? null : v.ToString();
    }
  }
}
