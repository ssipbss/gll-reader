using System;

namespace GenDaLangDu {
  /// <summary>
  /// 英文直通候选：记忆状态是中文时，文档里出现纯字母插入先缓冲，
  /// 累积超过4个字母、停顿一小段且未被中文上屏/退格/切换等取消，才确认
  /// 英文直通（鼠标点输入法图标切英文场景）。拼音/五笔组字码靠"中文上屏
  /// 取消"排除，1-4个字母永不确认，中文卡文不会误判。
  /// </summary>
  public sealed class EnPassTracker {
    private sealed class Candidate {
      public string Letters = "";
      public DateTime LastSeenAt;
      public string ElementId;
    }

    private Candidate _c;
    private readonly int _confirmMs;
    private readonly int _expireMs;

    /// <summary>确认英文直通时触发，参数为缓冲的字母串。</summary>
    public event Action<string> Confirmed;

    /// <summary>日志输出（主程序注入 DebugLog）。</summary>
    public Action<string> Log;

    public EnPassTracker(int confirmMs = 300, int expireMs = 2000) {
      _confirmMs = confirmMs;
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
      return true;
    }

    public void Cancel(string reason) {
      if (_c == null) return;
      LogLine("EN_PASS_CANCEL [" + _c.Letters + "] " + reason);
      _c = null;
    }

    /// <summary>定时检查：超过4个字母且停顿一段未上屏中文→确认英文直通；
    /// 不超过4个字母且停顿过久→清理（不朗读、不确认），防止与下一次输入串味。</summary>
    public bool Check() {
      if (_c == null) return false;
      double age = (DateTime.Now - _c.LastSeenAt).TotalMilliseconds;
      if (_c.Letters.Length > 4) {
        if (age < _confirmMs) return false;
        Confirm();
        return true;
      }
      if (age < _expireMs) return false;
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
