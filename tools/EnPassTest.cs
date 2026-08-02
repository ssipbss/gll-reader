using System;
using GenDaLangDu;

class EnPassTest {
  private static int _fails;
  private static string _confirmed;

  private static void Check(bool cond, string name) {
    Console.WriteLine((cond ? "PASS " : "FAIL ") + name);
    if (!cond) _fails++;
  }

  private static EnPassTracker NewTracker() {
    EnPassTracker t = new EnPassTracker(200);
    _confirmed = null;
    t.Confirmed += delegate(string s) { _confirmed = s; };
    return t;
  }

  private static void Main() {
    EnPassTracker t = NewTracker();
    Check(!t.IsArmed, "初始无候选");

    t.Note("d", "el1");
    Check(t.IsArmed, "记录后进入候选");
    Check(t.Letters == "d", "候选字母正确");
    Check(t.Check() == false, "未超时不确认");
    Check(_confirmed == null, "未超时无确认事件");

    System.Threading.Thread.Sleep(300);
    Check(t.Check() == true, "超时确认");
    Check(_confirmed == "d", "确认事件携带字母");
    Check(!t.IsArmed, "确认后清空");

    t = NewTracker();
    t.Note("d", "el1");
    t.Note("s", "el1");
    Check(t.Letters == "ds", "同元素字母累积");
    t.Note("f", "el2");
    Check(t.Letters == "f", "元素切换重置");

    t = NewTracker();
    t.Note("d", "el1");
    t.Cancel("zh");
    Check(!t.IsArmed, "取消后清空");
    System.Threading.Thread.Sleep(300);
    Check(t.Check() == false && _confirmed == null, "取消后不确认");

    t = NewTracker();
    t.Note("a", "el1");
    t.Note("b", "el1");
    t.Note("c", "el1");
    t.Note("d", "el1");
    t.Note("e", "el1");
    Check(_confirmed == "abcde", "超过4字母立即确认");
    Check(!t.IsArmed, "立即确认后清空");

    Console.WriteLine(_fails == 0 ? "ALL PASS" : (_fails + " FAILED"));
    Environment.Exit(_fails == 0 ? 0 : 1);
  }
}
