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
    /* 确认等待300ms、低字母过期清理300ms（测试用小窗口） */
    EnPassTracker t = new EnPassTracker(300, 300);
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

    System.Threading.Thread.Sleep(400);
    Check(t.Check() == true, "停顿后候选过期清理");
    Check(!t.IsArmed, "过期清理后清空");
    Check(_confirmed == null, "1-4个字母永不确认英文");

    t = NewTracker();
    t.Note("h", "el1");
    t.Note("e", "el1");
    t.Note("l", "el1");
    t.Note("l", "el1");
    t.Note("o", "el1");
    Check(_confirmed == null, "超过4个字母但未停顿不立即确认");
    System.Threading.Thread.Sleep(400);
    Check(t.Check() == true, "超过4个字母+停顿后确认英文");
    Check(_confirmed == "hello", "确认事件携带完整字母");
    Check(!t.IsArmed, "确认后清空");

    /* 拼音码场景：shenme 累积超过4个字母，但上屏中文会取消，不误读 */
    t = NewTracker();
    t.Note("s", "el1");
    t.Note("h", "el1");
    t.Note("e", "el1");
    t.Note("n", "el1");
    t.Note("m", "el1");
    t.Note("e", "el1");
    t.Cancel("zh");
    System.Threading.Thread.Sleep(400);
    Check(t.Check() == false && _confirmed == null, "拼音码上屏中文后取消，不误读");

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
    System.Threading.Thread.Sleep(400);
    Check(t.Check() == false && _confirmed == null, "取消后不确认");

    t = NewTracker();
    t.Note("a", "el1");
    t.Note("b", "el1");
    System.Threading.Thread.Sleep(400);
    t.Note("c", "el1");
    Check(t.Letters == "c", "过期后再输入不串味累积（从新字母重新开始）");

    Console.WriteLine(_fails == 0 ? "ALL PASS" : (_fails + " FAILED"));
    Environment.Exit(_fails == 0 ? 0 : 1);
  }
}
