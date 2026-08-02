using System;

namespace GenDaLangDu {
  /// <summary>
  /// 英文直通候选：记忆状态是中文时，文档里出现纯字母插入先缓冲，
  /// 只有累积超过4个字母且未被中文上屏/退格/切换等取消，才确认英文直通
  /// （鼠标点输入法图标切英文场景）。五笔组字码最长4码，靠"中文上屏取消"
  /// 与"超过4个才确认"来排除，中文卡文停顿不会误判。
  /// </summary>
  public sealed class EnPassTracker {
    private sealed class Candidate {
      public string Letters = "";
      public DateTime LastSeenAt;
      public string ElementId;
    }

    private Candidate _c;
    private readonly int _expireMs;

    /// <summary>确认英文直通时触发，参数为缓冲的字母串。</summary>
    public event Action<string> Confirmed;

    /// <summary>日志输出（主程序注入 DebugLog）。</summary>
    public Action<string> Log;

    public EnPassTracker(int expireMs = 2000) {
      _expireMs = expireMs;
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
      /* 候选已超时未清理：直接作废，避免与新一轮输入串味累积 */
      if (_c != null && (DateTime.Now - _c.LastSeenAt).TotalMilliseconds >= _expireMs) {
        LogLine("EN_PASS_EXPIRE [" + _c.Letters + "]");
        _c = null;
      }
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

    /// <summary>定时检查：候选超过一段时间没有新字母则清理（不朗读、不确认），
    /// 防止停顿后与下一次输入串味累积。</summary>
    public bool Check() {
      if (_c == null) return false;
      if ((DateTime.Now - _c.LastSeenAt).TotalMilliseconds < _expireMs) return false;
      LogLine("EN_PASS_EXPIRE [" + _c.Letters + "]");
      _c = null;
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
