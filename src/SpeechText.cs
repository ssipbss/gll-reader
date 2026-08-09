using System;

namespace GenDaLangDu {
  /// <summary>纯文本决策函数：与 MainForm 原静态方法逻辑一致，便于单元测试。</summary>
  public static class SpeechText {
    public static string DiffInserted(string oldT, string newT) {
      if (string.IsNullOrEmpty(newT)) return "";
      if (string.IsNullOrEmpty(oldT)) return newT;
      if (newT.StartsWith(oldT)) return newT.Substring(oldT.Length);
      if (oldT.StartsWith(newT)) return "";
      int p = 0;
      int maxP = Math.Min(oldT.Length, newT.Length);
      while (p < maxP && oldT[p] == newT[p]) p++;
      int sOld = oldT.Length - 1;
      int sNew = newT.Length - 1;
      while (sOld >= p && sNew >= p && oldT[sOld] == newT[sNew]) {
        sOld--;
        sNew--;
      }
      if (sNew >= p) return newT.Substring(p, sNew - p + 1);
      return "";
    }

    public static bool TryCaretDiff(string oldT, string newT, int caret, out string diff) {
      diff = null;
      if (oldT == null || caret < 0) return false;
      int delta = newT.Length - oldT.Length;
      int oldCaret = caret - delta;
      if (oldCaret < 0 || oldCaret > oldT.Length) return false;
      if (caret < 0 || caret > newT.Length) return false;
      int maxP = Math.Min(caret, oldCaret);
      int p = 0;
      while (p < maxP && newT[p] == oldT[p]) p++;
      int sOld = oldT.Length - 1;
      int sNew = newT.Length - 1;
      while (sOld >= p && sNew >= p && oldT[sOld] == newT[sNew]) {
        sOld--;
        sNew--;
      }
      int end = Math.Min(caret, sNew + 1);
      int len = end - p;
      if (len <= 0 || len > 12) return false;
      diff = newT.Substring(p, len);
      return true;
    }

    public static string ComputeInserted(string oldT, string newT, int caret) {
      string d;
      d = ShiftDiff(oldT, newT);
      if (!string.IsNullOrEmpty(d)) {
        d = LastLine(d);
        d = StripHeading(d);
        return d;
      }
      d = DiffInserted(oldT, newT);
      if (!string.IsNullOrEmpty(d)) {
        d = LastLine(d);
        d = StripHeading(d);
        return d;
      }
      if (TryCaretDiff(oldT, newT, caret, out d)) {
        d = LastLine(d);
        d = StripHeading(d);
        return d;
      }
      return "";
    }

    /// <summary>
    /// 平移对齐差异：尝试窗口平移 0~12 字符后新旧窗口完全重合，
    /// 末尾多出的字符即为刚输入的内容。解决连续多字上屏只截到
    /// 末尾、以及文档变长导致窗口整体右移的误判。
    /// </summary>
    public static string ShiftDiff(string oldT, string newT) {
      if (string.IsNullOrEmpty(oldT) || string.IsNullOrEmpty(newT)) return "";
      int maxShift = Math.Min(12, oldT.Length);
      for (int s = 0; s <= maxShift; s++) {
        if (newT.Length - s <= 0) break;
        int cmp = Math.Min(newT.Length - s, oldT.Length - s);
        if (cmp <= 0) continue;
        bool ok = true;
        for (int i = 0; i < cmp; i++) {
          if (newT[i] != oldT[s + i]) { ok = false; break; }
        }
        if (!ok) continue;
        int extra = newT.Length - (oldT.Length - s);
        if (extra > 0 && extra <= 12) return newT.Substring(newT.Length - extra);
        return "";
      }
      return "";
    }

    public static string LastLine(string s) {
      if (string.IsNullOrEmpty(s)) return s;
      int idx = s.LastIndexOf('\n');
      return idx >= 0 ? s.Substring(idx + 1) : s;
    }

    public static bool IsCnNumeral(char c) {
      return "零〇一二三四五六七八九十百千两".IndexOf(c) >= 0;
    }

    public static string StripHeading(string s) {
      if (string.IsNullOrEmpty(s) || s[0] != '第') return s;
      int j = 1;
      bool hasNum = false;
      while (j < s.Length) {
        char c = s[j];
        if (char.IsDigit(c) || IsCnNumeral(c)) {
          hasNum = true;
          j++;
        } else if (c == ' ') {
          j++;
        } else {
          break;
        }
      }
      if (!hasNum || j >= s.Length || s[j] != '章') return s;
      int k = j + 1;
      while (k < s.Length && (s[k] == ' ' || s[k] == '\t' || s[k] == '\r' || s[k] == '\n')) k++;
      string rest = s.Substring(k);
      return rest.Length > 0 ? rest : s;
    }

    public static string FilterForSpeech(string s) {
      System.Text.StringBuilder sb = new System.Text.StringBuilder();
      foreach (char c in s) {
        bool fullWidth = c >= 0xFF00 && c <= 0xFFEF &&
          !(c >= 0xFF10 && c <= 0xFF19) &&
          !(c >= 0xFF21 && c <= 0xFF3A) &&
          !(c >= 0xFF41 && c <= 0xFF5A);
        if (c == '\'' || c == '"') continue;
        if (KeyTranslator.IsCjk(c) || (c >= 0x3000 && c <= 0x9FFF) ||
            (c >= '0' && c <= '9') || (c >= 0xFF10 && c <= 0xFF19) ||
            fullWidth || KeyTranslator.PunctName(c) != null) {
          sb.Append(c);
        }
      }
      return sb.ToString();
    }

    public static bool HasCjk(string s) {
      if (string.IsNullOrEmpty(s)) return false;
      foreach (char c in s) {
        if (KeyTranslator.IsCjk(c)) return true;
      }
      return false;
    }

    public static bool ContainsImeSpeakable(string s) {
      foreach (char c in s) {
        bool fullWidth = c >= 0xFF00 && c <= 0xFFEF &&
          !(c >= 0xFF10 && c <= 0xFF19) &&
          !(c >= 0xFF21 && c <= 0xFF3A) &&
          !(c >= 0xFF41 && c <= 0xFF5A);
        if (KeyTranslator.IsCjk(c) || (c >= 0x3000 && c <= 0x9FFF) ||
            fullWidth || KeyTranslator.PunctName(c) != null) return true;
      }
      return false;
    }

    public static string PunctSpokenForm(string s) {
      bool hasCjk = false;
      foreach (char c in s) {
        if (KeyTranslator.IsCjk(c)) { hasCjk = true; break; }
      }
      if (hasCjk) return s;
      System.Text.StringBuilder sb = new System.Text.StringBuilder();
      foreach (char c in s) {
        string n = KeyTranslator.PunctName(c);
        if (n != null) {
          if (sb.Length > 0) sb.Append("，");
          sb.Append(n);
        } else {
          sb.Append(c);
        }
      }
      return sb.ToString();
    }

    public static bool HasChineseText(string s) {
      if (string.IsNullOrEmpty(s)) return false;
      foreach (char c in s) {
        if (KeyTranslator.IsCjk(c)) return true;
        if (c >= 0x3000 && c <= 0x9FFF) return true;
        bool fullWidth = c >= 0xFF00 && c <= 0xFFEF &&
          !(c >= 0xFF10 && c <= 0xFF19) &&
          !(c >= 0xFF21 && c <= 0xFF3A) &&
          !(c >= 0xFF41 && c <= 0xFF5A);
        if (fullWidth) return true;
      }
      return false;
    }

    public static string StripCompositionLetters(string s) {
      if (string.IsNullOrEmpty(s)) return s;
      bool hasCjk = false;
      foreach (char c in s) {
        if (KeyTranslator.IsCjk(c)) { hasCjk = true; break; }
      }
      if (!hasCjk) return s;
      System.Text.StringBuilder sb = new System.Text.StringBuilder();
      foreach (char c in s) {
        if (KeyTranslator.IsLatinLetter(c)) continue;
        sb.Append(c);
      }
      return sb.ToString();
    }

    public static bool IsPureSpaces(string s) {
      if (string.IsNullOrEmpty(s)) return false;
      foreach (char c in s) {
        if (c != ' ') return false;
      }
      return true;
    }

    public static bool IsPureAsciiLetters(string s) {
      if (string.IsNullOrEmpty(s)) return false;
      foreach (char c in s) {
        if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))) return false;
      }
      return true;
    }
  }
}