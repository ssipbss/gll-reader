using System;
using System.Drawing;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Forms;

namespace GenDaLangDu {
  /// <summary>选区朗读通道所需的宿主依赖，由 MainForm 实现。</summary>
  public interface ISelectionHost {
    Control InvokeControl { get; }
    Speaker Speaker { get; }
    bool Listening { get; }
    bool TestMode { get; }
    bool ClickSpeakEnabled { get; }
    bool LettersEnabled { get; }
    bool DigitsEnabled { get; }
    DateTime LastKeyAt { get; }
    DateTime LastMouseDownAt { get; }
    DateTime LastShiftKeyAt { get; }
    bool IsShiftHeld();
    bool IsForegroundResponsive();
    bool IsOurProcessForeground();
    uint CurrentForegroundPid();
    void Log(string line);
  }

  /// <summary>选区朗读通道：UIA 选区监视、手势判定弹按钮、复制选区并朗读。
  /// 除复制用的 STA 线程外，所有方法都在界面线程调用。</summary>
  public sealed class SelectionSpeakController : IDisposable {
    private readonly ISelectionHost _host;
    private SelectionFloater _floater;

    private AutomationFocusChangedEventHandler _focusHandler;
    private bool _focusHandlerPending;
    private AutomationEventHandler _selectionChangedHandler;
    private AutomationElement _subscribedElement;
    private DateTime _lastSubscribeAttemptAt = DateTime.MinValue;

    private string _pendingFp = "";
    private int _pendingCount;
    private string _shownFp = "";
    private DateTime _goneSince = DateTime.MinValue;
    private bool _fallback;
    private DateTime _fallbackShownAt = DateTime.MinValue;
    private DateTime _lastSpeakRequestAt = DateTime.MinValue;
    private bool _speechEnqueued;
    private DateTime _lastCheckAt = DateTime.MinValue;
    private System.Threading.Tasks.Task<SelectionInfo> _infoTask;
    private DateTime _queryAt = DateTime.MinValue;

    private Native.POINT _mouseDownPos;
    private Native.POINT _mouseUpPos;
    private DateTime _lastDownAtPrev = DateTime.MinValue;
    private Native.POINT _lastDownPosPrev;
    private DateTime _lastClickAt = DateTime.MinValue;
    private DateTime _lastDblClickAt = DateTime.MinValue;
    private DateTime _lastDragSelectAt = DateTime.MinValue;

    public SelectionSpeakController(ISelectionHost host) {
      _host = host;
      _floater = new SelectionFloater();
      _floater.SpeakRequested += OnSpeakRequested;
    }

    private void Log(string line) {
      _host.Log(line);
    }

    /// <summary>鼠标按下：记录位置，检测双击（与上次按下间隔&lt;500ms 且位移&lt;4px）。</summary>
    public void NotifyMouseDown() {
      DateTime now = DateTime.Now;
      Native.GetCursorPos(out _mouseDownPos);
      if (_lastDownAtPrev != DateTime.MinValue &&
          (now - _lastDownAtPrev).TotalMilliseconds < 500) {
        int dx = _mouseDownPos.X - _lastDownPosPrev.X;
        int dy = _mouseDownPos.Y - _lastDownPosPrev.Y;
        if (dx * dx + dy * dy < 16) _lastDblClickAt = now;
      }
      _lastDownAtPrev = now;
      _lastDownPosPrev = _mouseDownPos;
    }

    /// <summary>鼠标抬起：按下到抬起移动了距离 = 拖选动作（WPS 等无选区接口应用的兜底信号），
    /// 否则记为普通单击。</summary>
    public void NotifyMouseUp() {
      Native.POINT up;
      Native.GetCursorPos(out up);
      _mouseUpPos = up;
      int dx = up.X - _mouseDownPos.X;
      int dy = up.Y - _mouseDownPos.Y;
      if (Math.Sqrt(dx * dx + dy * dy) > 16) {
        _lastDragSelectAt = DateTime.Now;
      } else {
        _lastClickAt = DateTime.Now;
      }
    }

    /// <summary>Esc 停止朗读：返回 true 表示此前确实在朗读。</summary>
    public bool StopReadingIfReading() {
      if (_floater == null || !_floater.IsReading) return false;
      _host.Speaker.Stop();
      _floater.SetReading(false);
      _floater.HideNow();
      return true;
    }

    /// <summary>定时轮询入口：重订阅、朗读完成检测、按钮更新。</summary>
    public void Poll() {
      TryResubscribe();
      CheckDone();
      UpdateSelectionButton();
    }

    /// <summary>监听"选中区域变化"事件（高亮变化），选中时弹朗读按钮，
    /// 不选中时隐藏。纯事件驱动，平时零开销。</summary>
    public void InitWatcher() {
      try {
        if (_focusHandler == null) {
          if (IsExplorerForeground()) {
            /* 资源管理器/桌面没有文本选区，跳过 UIA 事件订阅，避免查询被拖住 */
            _focusHandlerPending = true;
            return;
          }
          if (!_host.TestMode && !_host.IsForegroundResponsive()) {
            /* 前台程序卡死时先不注册 UIA 焦点事件，避免启动被拖住；稍后由轮询重试 */
            _focusHandlerPending = true;
            return;
          }
          _focusHandler = delegate(object src, AutomationFocusChangedEventArgs e) {
            /* 不在 UIA 事件回调栈里做 UIA 调用（会导致原生崩溃）；
               也不占用界面线程（UIA 调用可能被忙碌的前台程序挂起）——后台线程订阅 */
            try { System.Threading.Tasks.Task.Run((Action)SubscribeSelectionElement); } catch { }
          };
          /* 注册本身也是 UIA 调用（可能被忙碌程序挂起）：后台执行，不阻塞界面线程 */
          try {
            System.Threading.Tasks.Task.Run(delegate {
              try { Automation.AddAutomationFocusChangedEventHandler(_focusHandler); } catch { }
            });
          } catch { }
        }
        /* 不同步订阅：UIA 查询可能被忙碌的前台程序卡住，后台线程执行并带响应探测 */
        try { System.Threading.Tasks.Task.Run((Action)SubscribeSelectionElement); } catch { }
      } catch { }
    }

    private void SubscribeSelectionElement() {
      /* 焦点变化可能频繁触发（浏览器/编辑器焦点跳动，Codex 实测每几百毫秒一次）：
         最多每 3 秒重新订阅一次，避免对前台程序的 UIA 提供者造成持续压力；
         计时在进入时更新，同一时刻最多只有一个订阅在 UIA 阶段 */
      if ((DateTime.Now - _lastSubscribeAttemptAt).TotalMilliseconds < 3000) return;
      _lastSubscribeAttemptAt = DateTime.Now;
      /* 前台程序未响应时，UIA 查询可能无限期挂起（后台线程，不拖累界面线程）；跳过并等下次重试 */
      if (!_host.TestMode && !_host.IsForegroundResponsive()) {
        Log("SEL_SUBSCRIBE_SKIP not-responding");
        return;
      }
      if (IsExplorerForeground()) {
        Log("SEL_SUBSCRIBE_SKIP explorer");
        return;
      }
      try {
        if (_subscribedElement != null) {
          try {
            Automation.RemoveAutomationEventHandler(
              TextPattern.TextSelectionChangedEvent, _subscribedElement, _selectionChangedHandler);
          } catch { }
          _subscribedElement = null;
        }
        AutomationElement el = AutomationElement.FocusedElement;
        if (el == null) {
          Log("SEL_SUBSCRIBE null");
          return;
        }
        if (_selectionChangedHandler == null) {
          _selectionChangedHandler = delegate(object src, AutomationEventArgs e) {
            try { _host.InvokeControl.BeginInvoke((MethodInvoker)UpdateSelectionButton); } catch { }
          };
        }
        /* 找文本提供者：浏览器/文档里焦点元素可能是正文子元素，
           选区变化事件由支持 TextPattern 的提供者发出，需向上查找并订阅它 */
        AutomationElement target = el;
        AutomationElement cur = el;
        for (int i = 0; i < 12; i++) {
          if (cur == null) break;
          try {
            object p;
            if (cur.TryGetCurrentPattern(TextPattern.Pattern, out p)) {
              target = cur;
              break;
            }
          } catch { }
          try {
            cur = TreeWalker.ControlViewWalker.GetParent(cur);
          } catch {
            break;
          }
        }
        Automation.AddAutomationEventHandler(
          TextPattern.TextSelectionChangedEvent, target,
          TreeScope.Element | TreeScope.Descendants, _selectionChangedHandler);
        _subscribedElement = target;
        Log("SEL_SUBSCRIBE type=" + el.Current.ControlType.ProgrammaticName +
                 " class=" + (el.Current.ClassName ?? ""));
      } catch (Exception ex) {
        Log("SEL_SUBSCRIBE_FAIL " + ex.Message);
      }
    }

    private void TryResubscribe() {
      try {
        if (_focusHandlerPending && _host.IsForegroundResponsive() && !IsExplorerForeground()) {
          _focusHandlerPending = false;
          InitWatcher();
        }
        if (_subscribedElement != null) return;
        if ((DateTime.Now - _lastSubscribeAttemptAt).TotalMilliseconds < 3000) return;
        SubscribeSelectionElement();
      } catch { }
    }

    private void CheckDone() {
      if (_floater == null || !_floater.IsReading) return;
      /* 复制选区阶段（约450ms）朗读尚未入队，IsBusy 为空：此时不得隐藏，
         否则按钮会闪（隐藏→手势重新显示→再隐藏） */
      if (!_speechEnqueued) return;
      if (_host.Speaker.IsBusy) return;
      Log("SEL_DONE_AUTO_HIDE");
      _speechEnqueued = false;
      HideSelectionButton();
    }

    /// <summary>更新选中朗读按钮：事件驱动和"鼠标活动后轮询"共用入口。
    /// 轮询兜底让 WPS 这类不发选区事件的老软件也能弹按钮；
    /// 仅在鼠标/键盘活动后查询，平时零开销。</summary>
    private void UpdateSelectionButton() {
      if (_floater == null) return;
      if (IsExplorerForeground()) {
        HideSelectionButton();
        return;
      }
      if (!_host.ClickSpeakEnabled) {
        HideSelectionButton();
        return;
      }
      if (!_host.Listening || _host.TestMode) {
        HideSelectionButton();
        return;
      }
      if (!_host.TestMode && _host.IsOurProcessForeground()) {
        HideSelectionButton();
        return;
      }
      /* 朗读中：按钮保持"结束朗读"，由 CheckDone/Esc/结束按钮管理隐藏 */
      if (_floater.IsReading) return;

      if ((DateTime.Now - _lastCheckAt).TotalMilliseconds < 150) return;
      _lastCheckAt = DateTime.Now;

      bool visible = _floater.Visible;

      /* 点击按钮本身不隐藏（点击瞬间应用可能已清除选区） */
      bool clickOnButton = (DateTime.Now - _host.LastMouseDownAt).TotalMilliseconds < 1000 &&
        _mouseDownPos.X >= _floater.Left &&
        _mouseDownPos.X <= _floater.Right &&
        _mouseDownPos.Y >= _floater.Top &&
        _mouseDownPos.Y <= _floater.Bottom;

      /* 点击按钮以外区域（普通单击）视为取消选区 */
      bool clickAway = (DateTime.Now - _lastClickAt).TotalMilliseconds < 1000 && !clickOnButton;
      /* 非 Shift 组合的键盘活动视为移动光标/取消选区 */
      bool shiftRecent = (DateTime.Now - _host.LastShiftKeyAt).TotalMilliseconds < 1200 || _host.IsShiftHeld();
      bool keyAway = (DateTime.Now - _host.LastKeyAt).TotalMilliseconds < 1200 && !shiftRecent;
      if (visible && (clickAway || keyAway)) {
        _goneSince = DateTime.MinValue;
        HideSelectionButton();
        _fallback = false;
        Log("SEL_BTN_HIDE " + (clickAway ? "click" : "key"));
        return;
      }

      /* 手势：真实拖动 / 双击 / Shift+点击 / Shift+方向键。
         普通点击、单纯打字、鼠标晃动都不算选择手势，杜绝幽灵按钮。 */
      bool dragRecent = (DateTime.Now - _lastDragSelectAt).TotalMilliseconds < 2500;
      bool dblRecent = (DateTime.Now - _lastDblClickAt).TotalMilliseconds < 1500;
      bool keyRecent = (DateTime.Now - _host.LastKeyAt).TotalMilliseconds < 1500;
      bool gesture = dragRecent || dblRecent || (keyRecent && shiftRecent);

      /* 空闲零 UIA：没有选择手势且按钮未显示（或按钮处于查不到选区的兜底模式）时，
         不向前台应用发 UIA 查询，避免每 150ms 打扰 Word/WPS/资源管理器导致鼠标卡顿 */
      bool needUia = gesture || (visible && !_fallback);
      if (!needUia) return;

      if (_infoTask != null && !_infoTask.IsCompleted) {
        /* 查询卡住超过 2 秒：放弃旧查询并允许发起新查询，
           避免选区按钮通道被一个永不返回的 UIA 调用永久堵死 */
        if ((DateTime.Now - _queryAt).TotalMilliseconds > 2000) {
          _infoTask = null;
          Log("SEL_QUERY_ABANDON");
        } else {
          Log("SEL_QUERY_BUSY");
          return;
        }
      }
      bool visibleSnap = visible;
      bool gestureSnap = gesture;
      bool dragRecentSnap = dragRecent;
      var sit = System.Threading.Tasks.Task.Run(() => TextReader.GetSelectionInfo());
      _infoTask = sit;
      _queryAt = DateTime.Now;
      /* 不阻塞界面线程：查询完成后回到界面线程再处理结果 */
      sit.ContinueWith(t => {
        try {
          if (_infoTask != t) return;
          _infoTask = null;
          if (t.IsFaulted || t.IsCanceled) return;
          SelectionInfo info = t.Result;
          if (_host.InvokeControl.InvokeRequired) {
            try { _host.InvokeControl.BeginInvoke((MethodInvoker)(() => ApplySelectionInfo(info, visibleSnap, gestureSnap, dragRecentSnap))); } catch { }
          } else {
            ApplySelectionInfo(info, visibleSnap, gestureSnap, dragRecentSnap);
          }
        } catch { }
      }, System.Threading.Tasks.TaskScheduler.Default);
    }

    private void ApplySelectionInfo(SelectionInfo info, bool visible, bool gesture, bool dragRecent) {
      if (_floater == null) return;
      /* 查询返回前用户可能已开始朗读：朗读期间按钮归 CheckDone 管理 */
      if (_floater.IsReading) return;
      bool hasSel = info != null;

      /* UIA 模式下选区消失 → 防抖 200ms 后隐藏；
         WPS 等兜底模式（_fallback）查不到 UIA 选区，不能靠这个隐藏（否则会闪） */
      if (visible && !_fallback && !hasSel) {
        if (_goneSince == DateTime.MinValue) {
          _goneSince = DateTime.Now;
        } else if ((DateTime.Now - _goneSince).TotalMilliseconds >= 200) {
          _goneSince = DateTime.MinValue;
          HideSelectionButton();
          _fallback = false;
          Log("SEL_BTN_HIDE noselection");
        }
        return;
      }
      _goneSince = DateTime.MinValue;

      if (!gesture) return;

      if (hasSel) {
        /* 实体验证通过：连续两次一致才显示，锚定选区端点（不跟鼠标） */
        string fp = SelectionFingerprint(info);
        if (fp != _pendingFp) {
          _pendingFp = fp;
          _pendingCount = 0;
        }
        _pendingCount++;
        if (_pendingCount < 2) return;
        if (!visible || fp != _shownFp) {
          Point anchor = ComputeSelectionAnchor(info, dragRecent);
          _floater.ShowFor(anchor);
          _shownFp = fp;
          _fallback = false;
          Log("SEL_BTN_SHOW rects=" + info.Rects.Count + " bounds=" +
                   (int)info.Bounds.X + "," + (int)info.Bounds.Y + " " +
                   (int)info.Bounds.Width + "x" + (int)info.Bounds.Height);
        }
        return;
      }

      /* WPS 等查不到真实选区的老软件：真实拖动手势兜底（白名单），锚定鼠标抬起点 */
      if (dragRecent && IsDragFallbackApp(_host.CurrentForegroundPid())) {
        string fp = "drag@" + _mouseUpPos.X + "," + _mouseUpPos.Y;
        if (fp != _pendingFp) {
          _pendingFp = fp;
          _pendingCount = 0;
        }
        _pendingCount++;
        /* 兜底模式：已显示时不再隐藏（UIA 查不到选区），只有新拖选/点击/键盘才更新；
           防止"显示→无选区隐藏→又显示"的闪烁循环 */
        if (_pendingCount >= 2 && (!visible || _lastDragSelectAt > _fallbackShownAt)) {
          _floater.ShowFor(new Point(_mouseUpPos.X, _mouseUpPos.Y));
          _shownFp = fp;
          _fallback = true;
          _fallbackShownAt = _lastDragSelectAt;
          Log("SEL_BTN_SHOW drag-fallback");
        }
      }
    }

    /// <summary>选区指纹：量化后的各矩形，用于去重（半像素量化容忍提供者抖动）。</summary>
    private static string SelectionFingerprint(SelectionInfo info) {
      System.Text.StringBuilder sb = new System.Text.StringBuilder();
      foreach (System.Windows.Rect rc in info.Rects) {
        sb.Append((int)Math.Round(rc.X / 2)).Append(',')
          .Append((int)Math.Round(rc.Y / 2)).Append(',')
          .Append((int)Math.Round(rc.Width / 2)).Append(',')
          .Append((int)Math.Round(rc.Height / 2)).Append(';');
      }
      return sb.ToString();
    }

    /// <summary>按选区端点计算按钮锚点：向前选放末尾行右端下方、向后选放起始行左端上方；
    /// 空间不足时上下翻转，最终由 SelectionFloater 做屏幕边界钳制。</summary>
    private Point ComputeSelectionAnchor(SelectionInfo info, bool dragRecent) {
      System.Windows.Rect endRect = info.Rects[0];
      bool backward = dragRecent &&
        (_mouseUpPos.Y < _mouseDownPos.Y - 2 ||
         (Math.Abs(_mouseUpPos.Y - _mouseDownPos.Y) <= 2 && _mouseUpPos.X < _mouseDownPos.X - 2));
      if (backward) {
        foreach (System.Windows.Rect rc in info.Rects) {
          if (rc.Top < endRect.Top - 1 ||
              (Math.Abs(rc.Top - endRect.Top) <= 1 && rc.Left < endRect.Left)) endRect = rc;
        }
      } else {
        foreach (System.Windows.Rect rc in info.Rects) {
          if (rc.Bottom > endRect.Bottom + 1 ||
              (Math.Abs(rc.Bottom - endRect.Bottom) <= 1 && rc.Right > endRect.Right)) endRect = rc;
        }
      }
      int endX = backward ? (int)endRect.Left : (int)endRect.Right;
      int endY = backward ? (int)endRect.Top : (int)endRect.Bottom;
      Rectangle wa = Screen.GetWorkingArea(new Point(endX, endY));
      bool below = !backward;
      if (below && endY + 12 + _floater.Height > wa.Bottom) below = false;
      if (!below && endY - 12 - _floater.Height < wa.Top) below = true;
      return new Point(endX, below ? endY + 12 : endY - 12 - _floater.Height);
    }

    /// <summary>隐藏朗读按钮（切换窗口等场景由宿主调用；朗读中可用 Esc 停止）。</summary>
    public void HideSelectionButton() {
      if (_floater == null) return;
      _floater.SetReading(false);
      _floater.HideNow();
    }

    /// <summary>点击朗读按钮：用 Ctrl+C 复制当前选区到剪贴板，读取后恢复剪贴板。
    /// 不用 UIA 读选区文本（某些应用会触发 UIA 原生崩溃）。</summary>
    private void OnSpeakRequested() {
      if (!_host.ClickSpeakEnabled) return;
      /* 按钮 Click 与鼠标钩子兜底可能同时触发，400ms 内只响应一次 */
      if ((DateTime.Now - _lastSpeakRequestAt).TotalMilliseconds < 400) return;
      _lastSpeakRequestAt = DateTime.Now;
      if (_floater.IsReading) {
        /* 结束朗读：停止并隐藏按钮 */
        _host.Speaker.Stop();
        _speechEnqueued = false;
        _floater.SetReading(false);
        _floater.HideNow();
        Log("SEL_STOP");
        return;
      }
      /* 立即进入朗读状态（按钮变"结束朗读"），复制期间保持可见 */
      _speechEnqueued = false;
      _floater.SetReading(true);
      /* 复制流程含剪贴板读写与多次等待（约450ms+），放到独立 STA 线程执行，
         完成后回界面线程继续；Clipboard API 要求 STA，不能用线程池 */
      System.Threading.Thread copyThread = new System.Threading.Thread(delegate() {
        string text = null;
        try { text = CopySelectionText(); } catch { }
        try { _host.InvokeControl.BeginInvoke((MethodInvoker)delegate { FinishSpeak(text); }); } catch { }
      });
      copyThread.IsBackground = true;
      try { copyThread.SetApartmentState(System.Threading.ApartmentState.STA); } catch { }
      copyThread.Start();
    }

    private void FinishSpeak(string text) {
      if (_floater == null) return;
      if (string.IsNullOrEmpty(text)) {
        Log("SEL_SPEAK_EMPTY");
        _floater.SetReading(false);
        _floater.HideNow();
        return;
      }
      string t = text.Trim();
      if (t.Length == 0) {
        Log("SEL_SPEAK_EMPTY");
        _floater.SetReading(false);
        _floater.HideNow();
        return;
      }
      /* 复制成功后把选区收成光标：选区亮着时继续打字会把选中文字替换掉
         （Word/WPS 原生行为），收起后用户可立即正常输入 */
      try {
        InputSender.CollapseSelectionKeybd();
      } catch { }
      _speechEnqueued = true;
      if (SpeechText.HasChineseText(t)) {
        _host.Speaker.SpeakZh(t);
      } else {
        bool hasLetter = false;
        bool hasDigit = false;
        foreach (char ch in t) {
          if (KeyTranslator.IsLatinLetter(ch)) hasLetter = true;
          else if (ch >= '0' && ch <= '9') hasDigit = true;
        }
        if (hasLetter && !_host.LettersEnabled) {
          Log("SEL_SPEAK_LETTERS_OFF");
          _floater.SetReading(false);
          return;
        }
        if (!hasLetter && hasDigit && !_host.DigitsEnabled) {
          Log("SEL_SPEAK_DIGITS_OFF");
          _floater.SetReading(false);
          return;
        }
        _host.Speaker.SpeakEnWord(t);
      }
      Log("SEL_SPEAK [" + TruncateForLog(t) + "]");
    }

    private string CopySelectionText() {
      string saved = "";
      bool hadClip = false;
      try {
        saved = Clipboard.GetText();
        hadClip = true;
      } catch { }
      try {
        IntPtr fg = Native.GetForegroundWindow();
        uint fgPid = 0;
        if (fg != IntPtr.Zero) Native.GetWindowThreadProcessId(fg, out fgPid);
        bool chromium = IsChromiumApp(fgPid);
        /* Word/WPS/Office 自绘控件实测不响应 WM_COPY（0x0301），
           直接键盘 Ctrl+C（只复制不剪切，安全），省掉一次无效等待；
           其它非 Chromium 应用仍先试 WM_COPY，失败再回退。 */
        bool office = IsDragFallbackApp(fgPid);
        bool useWmCopy = !chromium && !office;
        /* 统一用带延迟的 keybd_event：实测 SendInput 在本机被拦截
           （Word 复制无反应），keybd_event 全软件可用且修饰键稳定 */
        uint seqBefore = Native.GetClipboardSequenceNumber();
        Log("SEL_COPY_BEGIN fgpid=" + fgPid + " seq=" + seqBefore);
        /* 不清空剪贴板（清空会让本程序占用剪贴板，Edge 复制不进去）。
           复制后剪贴板序列号变化 = 应用确实写入了新内容，才读取。 */
        Action sendCopy = delegate {
          if (useWmCopy) SendWmCopy();
          else InputSender.PressCtrlCKeybd();
        };
        sendCopy();
        string t = null;
        for (int i = 0; i < 3; i++) {
          System.Threading.Thread.Sleep(150);
          try {
            uint seq = Native.GetClipboardSequenceNumber();
            if (seq != seqBefore) {
              t = Clipboard.GetText();
              if (!string.IsNullOrEmpty(t)) break;
            }
          } catch (Exception ex) {
            try {
              IntPtr owner = Native.GetClipboardOwner();
              uint ownerPid = 0;
              if (owner != IntPtr.Zero) Native.GetWindowThreadProcessId(owner, out ownerPid);
              Log("SEL_COPY_GET_EX " + ex.Message + " owner=" + ownerPid);
            } catch {
              Log("SEL_COPY_GET_EX " + ex.Message);
            }
          }
          if (!string.IsNullOrEmpty(t)) break;
          if (useWmCopy) {
            /* WM_COPY 没生效（Word 自绘控件不响应）→ 回退键盘 Ctrl+C */
            useWmCopy = false;
            Log("SEL_COPY_WMCOPY_FAIL_FALLBACK_KEYBD");
          }
          if (i < 2) {
            /* 序列号没变 = 应用没响应复制，重新发送一次复制键 */
            sendCopy();
          }
        }
        Log("SEL_COPY_END len=" + (t == null ? -1 : t.Length));
        return string.IsNullOrEmpty(t) ? null : t;
      } catch (Exception ex) {
        Log("SEL_COPY_EX " + ex.Message);
        return null;
      } finally {
        try {
          if (hadClip) {
            Clipboard.SetText(saved);
          } else {
            Clipboard.Clear();
          }
        } catch { }
      }
    }

    /// <summary>Chromium 类应用（浏览器/Electron）直接键盘注入复制；
    /// 其它应用先试 WM_COPY，失败后回退键盘 Ctrl+C（只复制不剪切，安全）。</summary>
    private static bool IsChromiumApp(uint pid) {
      try {
        using (System.Diagnostics.Process p = System.Diagnostics.Process.GetProcessById((int)pid)) {
          string n = p.ProcessName.ToLowerInvariant();
          return n == "msedge" || n == "chrome" || n == "chatgpt" ||
                 n == "bilibili" || n == "yuewenedit" || n == "codex" ||
                 n == "electron";
        }
      } catch {
        return false;
      }
    }

    /// <summary>拖选动作兜底：WPS/Office 查不到或迟迟不发布 UIA 选区，
    /// 真实拖动手势直接按鼠标抬起点弹按钮（Word 的 UIA 选区要 2~3 秒才出来，
    /// 等它会让用户误点已选中的文字）。</summary>
    private static bool IsDragFallbackApp(uint pid) {
      try {
        using (System.Diagnostics.Process p = System.Diagnostics.Process.GetProcessById((int)pid)) {
          string n = p.ProcessName.ToLowerInvariant();
          return n == "wps" || n == "et" || n == "wpp" ||
                 n == "winword" || n == "excel" || n == "powerpnt";
        }
      } catch {
        return false;
      }
    }

    /// <summary>优先用 WM_COPY 窗口消息复制选区（Word/WPS/记事本等标准控件安全复制，
    /// 不会像键盘注入那样有误触剪切的可能）；返回是否成功发送。</summary>
    private static bool SendWmCopy() {
      try {
        IntPtr fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        uint pid;
        uint tid = Native.GetWindowThreadProcessId(fg, out pid);
        Native.GUITHREADINFO info = new Native.GUITHREADINFO();
        info.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.GUITHREADINFO));
        if (Native.GetGUIThreadInfo(tid, out info) && info.hwndFocus != IntPtr.Zero) {
          Native.SendMessage(info.hwndFocus, 0x0301, IntPtr.Zero, IntPtr.Zero);
          return true;
        }
      } catch { }
      return false;
    }

    private static bool IsExplorerForeground() {
      try {
        IntPtr h = Native.GetForegroundWindow();
        if (h == IntPtr.Zero) return false;
        uint pid;
        Native.GetWindowThreadProcessId(h, out pid);
        if (pid == 0) return false;
        using (System.Diagnostics.Process p = System.Diagnostics.Process.GetProcessById((int)pid)) {
          return string.Equals(p.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase);
        }
      } catch {
        return false;
      }
    }

    private static string TruncateForLog(string s) {
      if (s == null) return "";
      return s.Length <= 40 ? s : s.Substring(0, 40) + "…";
    }

    public void Dispose() {
      if (_floater != null) {
        _floater.HideNow();
        _floater.Dispose();
        _floater = null;
      }
      try {
        if (_subscribedElement != null && _selectionChangedHandler != null) {
          Automation.RemoveAutomationEventHandler(
            TextPattern.TextSelectionChangedEvent, _subscribedElement, _selectionChangedHandler);
        }
        if (_focusHandler != null) {
          Automation.RemoveAutomationFocusChangedEventHandler(_focusHandler);
        }
      } catch { }
      _subscribedElement = null;
    }
  }
}
