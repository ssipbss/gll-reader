using System;
using System.Collections.Generic;
using System.Windows.Automation;
using System.Windows.Forms;

namespace GenDaLangDu {
  /* MainForm 分部实现：任务栏输入法指示器监控（名称通道 + 图标通道）。 */
  public partial class MainForm {
    /// <summary>监控任务栏输入法指示器（"中文模式/英语模式"文字）：
    /// 这是系统实时状态，鼠标切换、Win+空格等任何方式都能感知。</summary>
    private void InitTrayImeWatcher() {
      /* 托盘名称检测由 UI 定时器每秒轮询，这里不同步调用，避免启动被 UIA 拖住 */
    }

    /// <summary>轮询所有任务栏子树上的"输入指示器"按钮名称（不订阅事件、不遍历整个桌面，
    /// 避免 UIA 全树遍历/事件订阅导致卡死或静默崩溃）。每秒一次，代价极小。</summary>
    private static bool IsTrayImeButtonName(string n) {
      return n != null &&
             n.IndexOf("托盘输入指示器", StringComparison.Ordinal) >= 0 &&
             n.IndexOf("要切换输入法", StringComparison.Ordinal) < 0;
    }

    private List<string> QueryTrayImeNames() {
      List<string> names = new List<string>();
      try {
        AutomationElement cached = _trayImeButton;
        if (cached != null) {
          string n = cached.Current.Name ?? "";
          if (IsTrayImeButtonName(n)) {
            names.Add(n);
            return names;
          }
        }
      } catch { }
      _trayImeButton = null;
      foreach (IntPtr tray in Native.EnumerateTaskbars()) {
        try {
          AutomationElement rootEl = AutomationElement.FromHandle(tray);
          if (rootEl == null) continue;
          AutomationElementCollection btns = rootEl.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
          foreach (AutomationElement el in btns) {
            string n = el.Current.Name ?? "";
            if (IsTrayImeButtonName(n)) {
              _trayImeButton = el;
              names.Add(n);
              return names;
            }
          }
        } catch { }
      }
      return names;
    }

    private void RefreshTrayState() {
      try {
        if (_trayFindTask != null && !_trayFindTask.IsCompleted) {
          _trayRefreshPending = true;
          return;
        }
        var task = System.Threading.Tasks.Task.Run(() => QueryTrayImeNames());
        _trayFindTask = task;
        task.ContinueWith(t => {
          try { BeginInvoke((MethodInvoker)delegate { TrayQueryFinished(t); }); } catch { }
        }, System.Threading.Tasks.TaskScheduler.Default);
      } catch (Exception ex) {
        DebugLog("TRAY_IME_FIND_FAIL " + ex.Message);
      }
    }

    private void TrayQueryFinished(System.Threading.Tasks.Task<List<string>> t) {
      try {
        if (t != null && t.IsCompleted && !t.IsFaulted && t.Result != null) {
          foreach (string n in t.Result) ApplyTrayImeState(n);
        }
      } catch { }
      FlushShiftLetters();
      if (ReferenceEquals(_trayFindTask, t)) _trayFindTask = null;
      if (_trayRefreshPending) {
        _trayRefreshPending = false;
        RefreshTrayState();
      }
    }

    private void FindTrayImeElement() {
      try {
        if ((DateTime.Now - _lastTrayImeFindAt).TotalMilliseconds < 1000) return;
        _lastTrayImeFindAt = DateTime.Now;
        RefreshTrayState();
      } catch (Exception ex) {
        DebugLog("TRAY_IME_FIND_FAIL " + ex.Message);
      }
    }

    private void ApplyTrayImeState(string name) {
      if (string.IsNullOrEmpty(name)) return;
      bool? chinese = ParseTrayImeName(name);
      if (chinese == null) return;
      string key = chinese.Value ? "zh" : "en";
      if (key == _trayImeLast) return;
      bool incomingEnglish = !chinese.Value;
      if (_trayImeEnglish.HasValue && _trayImeEnglish.Value != incomingEnglish &&
          (DateTime.Now - _lastStateFlipAt).TotalMilliseconds < 2000) {
        /* 仅防微软五笔异步名称查询返回旧值：翻转/图标确认后 2 秒内拒收矛盾名称 */
        DebugLog("TRAY_IME_STATE_SKIP recent-flip");
        return;
      }
      _lastStateFlipAt = DateTime.Now;
      _trayImeLast = key;
      bool zh = chinese.Value;
      _trayImeEnglish = !zh;
      FlushShiftLetters();
      if (zh) {
        _appStates.SetChineseCurrent();
        _composing = false;
        DebugLog("TRAY_IME_STATE 中文 [" + name.Replace("\r", " ").Replace("\n", " ") + "]");
      } else {
        _appStates.SetEnglishCurrent();
        _composing = false;
        DebugLog("TRAY_IME_STATE 英文 [" + name.Replace("\r", " ").Replace("\n", " ") + "]");
      }
    }

    /// <summary>解析任务栏输入法指示器名称。不同输入法格式不同：
    /// 微软五笔显示"中文模式/英语模式"；多多五笔显示"中文/英文"或"英文/中文"。</summary>
    private static bool? ParseTrayImeName(string name) {
      if (string.IsNullOrEmpty(name)) return null;
      /* 多多五笔的托盘按钮名称固定为“中文/英文”，不随状态变化，
         名称里不含状态信息，必须交给图标通道识别，避免把正确状态改回中文。 */
      if (name.IndexOf("中文/英文", StringComparison.Ordinal) >= 0) return null;
      if (name.IndexOf("英文/中文", StringComparison.Ordinal) >= 0) return null;
      if (name.IndexOf("中文模式", StringComparison.Ordinal) >= 0) return true;
      if (name.IndexOf("英语模式", StringComparison.Ordinal) >= 0) return false;
      if (name.IndexOf("英文模式", StringComparison.Ordinal) >= 0) return false;
      if (name.IndexOf("中文(简体", StringComparison.Ordinal) >= 0) return true;
      if (name.IndexOf("中文", StringComparison.Ordinal) >= 0) return true;
      if (name.IndexOf("英语", StringComparison.Ordinal) >= 0) return false;
      if (name.IndexOf("英文", StringComparison.Ordinal) >= 0) return false;
      return null;
    }

    /// <summary>多多五笔托盘图标通道：认图不猜。
    /// 中文 = icon_5（"中"字形）或 icon_23（橙红禁圈）；英文 = icon_2/icon_22（键盘形）。</summary>
    private void OnTrayImeIconState(bool chinese) {
      try {
        /* 图标是状态权威：确认即应用（低延迟优先），不设矛盾拒收窗口 */
        _lastStateFlipAt = DateTime.Now;
        _trayImeEnglish = !chinese;
        FlushShiftLetters();
        if (chinese) {
          _appStates.SetChineseCurrent();
          DebugLog("TRAY_ICON_STATE 中文(图标)");
        } else {
          _appStates.SetEnglishCurrent();
          DebugLog("TRAY_ICON_STATE 英文(图标)");
        }
        _composing = false;
      } catch {
      }
    }
  }
}
