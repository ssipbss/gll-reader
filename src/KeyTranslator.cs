using System;
using System.Collections.Generic;
using System.Text;

namespace GenDaLangDu {
  public static class KeyTranslator {
    private const string CN_DIGITS = "零一二三四五六七八九";

    public static string GetChars(uint vk, uint scan) {
      byte[] state = new byte[256];
      try { Native.GetKeyboardState(state); } catch { }
      SetDown(state, 0x10, 0x8000);
      SetDown(state, 0x11, 0x8000);
      SetDown(state, 0x12, 0x8000);
      SetDown(state, 0x14, 0x0001);
      SetDown(state, 0x90, 0x0001);
      SetDown(state, 0x91, 0x0001);
      state[vk] = 0x80;
      try {
        IntPtr hwnd = Native.GetForegroundWindow();
        uint pid;
        uint tid = Native.GetWindowThreadProcessId(hwnd, out pid);
        IntPtr hkl = Native.GetKeyboardLayout(tid);
        StringBuilder sb = new StringBuilder(8);
        int ret = Native.ToUnicodeEx(vk, scan, state, sb, 8, 0, hkl);
        if (ret == -1) {
          try { Native.ToUnicodeEx(vk, scan, state, new StringBuilder(8), 8, 0, hkl); } catch { }
          return "";
        }
        if (ret > 0) return sb.ToString();
      } catch { }
      return "";
    }

    private static void SetDown(byte[] state, int vk, int mask) {
      short s = 0;
      try { s = Native.GetAsyncKeyState(vk); } catch { }
      state[vk] = ((s & mask) != 0) ? (byte)0x80 : (byte)0x00;
    }

    public static string GetKeyName(uint vk) {
      switch (vk) {
        case 0x08: return "退格";
        case 0x09: return "制表";
        case 0x0D: return "回车";
        case 0x10: return "Shift";
        case 0x11: return "Ctrl";
        case 0x12: return "Alt";
        case 0x13: return "暂停";
        case 0x14: return "大写锁定";
        case 0x1B: return "Esc";
        case 0x20: return "空格";

        case 0x2C: return "截屏";
        case 0x2D: return "插入";
        case 0x2E: return "删除";
        case 0x5B: return "Win";
        case 0x5C: return "Win";
        case 0x5D: return "菜单";
        case 0x6A: return "乘号";
        case 0x6B: return "加号";
        case 0x6D: return "减号";
        case 0x6E: return "小数点";
        case 0x6F: return "除号";
        case 0x90: return "数字锁定";
        case 0x91: return "滚动锁定";
        case 0xA0: return "Shift";
        case 0xA1: return "Shift";
        case 0xA2: return "Ctrl";
        case 0xA3: return "Ctrl";
        case 0xA4: return "Alt";
        case 0xA5: return "Alt";
      }
      if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x70 + 1).ToString();
      return null;
    }

    public static bool IsLatinLetter(char c) {
      return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
             (c >= 'ａ' && c <= 'ｚ') || (c >= 'Ａ' && c <= 'Ｚ');
    }

    public static char NormalizeLatin(char c) {
      if (c >= 'ａ' && c <= 'ｚ') return (char)(c - 0xFF41 + 'a');
      if (c >= 'Ａ' && c <= 'Ｚ') return (char)(c - 0xFF21 + 'A');
      return c;
    }

    public static bool IsCjk(char c) {
      return (c >= 0x4E00 && c <= 0x9FFF) ||
             (c >= 0x3400 && c <= 0x4DBF) ||
             (c >= 0xF900 && c <= 0xFAFF);
    }

    public static string DigitToChinese(char c) {
      if (c >= '０' && c <= '９') c = (char)(c - 0xFF10 + '0');
      if (c >= '0' && c <= '9') return CN_DIGITS[c - '0'].ToString();
      return c.ToString();
    }

    private static readonly Dictionary<char, string> _punct = new Dictionary<char, string> {
      {'.', "句号"}, {',', "逗号"}, {'?', "问号"}, {'!', "感叹号"},
      {';', "分号"}, {':', "冒号"}, {'\'', "单引号"}, {'"', "双引号"},
      {'(', "左括号"}, {')', "右括号"}, {'[', "左方括号"}, {']', "右方括号"},
      {'{', "左花括号"}, {'}', "右花括号"}, {'<', "小于号"}, {'>', "大于号"},
      {'/', "斜杠"}, {'\\', "反斜杠"}, {'|', "竖线"}, {'`', "反引号"},
      {'~', "波浪号"}, {'@', "艾特"}, {'#', "井号"}, {'$', "美元符号"},
      {'%', "百分号"}, {'^', "脱字符"}, {'&', "和号"}, {'*', "星号"},
      {'-', "减号"}, {'_', "下划线"}, {'=', "等号"}, {'+', "加号"},
      {'，', "逗号"}, {'。', "句号"}, {'？', "问号"}, {'！', "感叹号"},
      {'；', "分号"}, {'：', "冒号"}, {'、', "顿号"}, {'“', "左引号"},
      {'”', "右引号"}, {'‘', "左引号"}, {'’', "右引号"}, {'（', "左括号"},
      {'）', "右括号"}, {'【', "左方括号"}, {'】', "右方括号"}, {'《', "书名号左"},
      {'》', "书名号右"}, {'…', "省略号"}, {'—', "破折号"}, {'·', "间隔号"}
    };

    public static string PunctName(char c) {
      string n;
      if (_punct.TryGetValue(c, out n)) return n;
      return null;
    }
  }
}
