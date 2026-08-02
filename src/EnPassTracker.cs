using System;

namespace GenDaLangDu {
  /// <summary>
  /// 英文直通候选：记忆状态是中文时，文档里出现纯字母插入先缓冲，
  /// 短时间后未被中文替换/取消则确认英文直通（鼠标点输入法图标切英文场景）。
  /// 五笔组字码也会先出现在文档里，靠"中文上屏取消"来排除。
  /// </summary>
  public sealed class EnPassTracker {
    private sealed class Candidate {
      public string Letters = "";
      public DateTime LastSeenAt;
      public string ElementId;
    }

    private Candidate _c;
    private readonly int _confirmMs;

    /// <summary>确认英文直通时触发，参数为缓冲的字母串。</summary>
    public event Action<string> Confirmed;

    /// <summary>日志输出（主程序注入 DebugLog）。</summary>
    public Action<string> Log;

    public EnPassTracker(int confirmMs = 400) {
      _confirmMs = confirmMs;
    }

    public bool IsArmed {
      get { return _c != null; }
    }

    public string Letters {
      get { return _c == null ? "" : _c.Letters; }
    }

    /// <summary>记录一段纯字母插入。返回 true 表示已接管（调用方不再处理）。</summary>
    public bool Note(string ins, string elementId) {
      if (string.IsNullOrEmpty(ins)) return false;
      if (_c == null || _c.ElementId != elementId) {
        _c = new Candidate { Letters = ins, LastSeenAt = DateTime.Now, ElementId = elementId };
      } else {
        _c.Letters += ins;
        _c.LastSeenAt = DateTime.Now;
      }
      LogLine("EN_PASS_ARM [" + _c.Letters + "]");
      /* 五笔码最长4个字母，超过4个必是英文直通，立即确认 */
      if (_c.Letters.Length > 4) {
        Confirm();
      }
      return true;
    }

    public void Cancel(string reason) {
      if (_c == null) return;
      LogLine("EN_PASS_CANCEL [" + _c.Letters + "] " + reason);
      _c = null;
    }

    /// <summary>定时检查：超时且未被取消则确认。返回 true 表示刚确认。</summary>
    public bool Check() {
      if (_c == null) return false;
      if ((DateTime.Now - _c.LastSeenAt).TotalMilliseconds < _confirmMs) return false;
      Confirm();
      return true;
    }

    private void Confirm() {
      Candidate c = _c;
      _c = null;
      if (c == null || c.Letters.Length == 0) return;
      LogLine("EN_PASS_CONFIRM [" + c.Letters + "]");
      Action<string> h = Confirmed;
      if (h != null) h(c.Letters);
    }

    private void LogLine(string line) {
      Action<string> l = Log;
      if (l != null) l(line);
    }
  }
}
