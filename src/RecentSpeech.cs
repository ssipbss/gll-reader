using System;

namespace GenDaLangDu {
  /// <summary>最近朗读去重：同一文本在 400ms 内且期间没有新按键才视为重复。</summary>
  public sealed class RecentSpeech {
    private string _lastSpoken = "";
    private DateTime _lastSpokenAt = DateTime.MinValue;

    public bool IsDuplicate(string text, DateTime lastKeyAt, DateTime now) {
      if (_lastSpoken != text) return false;
      if ((now - _lastSpokenAt).TotalMilliseconds >= 400) return false;
      if (lastKeyAt > _lastSpokenAt) return false;
      return true;
    }

    public void Mark(string text, DateTime now) {
      _lastSpoken = text;
      _lastSpokenAt = now;
    }
  }
}