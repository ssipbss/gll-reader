using System;
using GenDaLangDu;

class StateTest {
  private static int _fails;

  private static void Check(bool cond, string name) {
    Console.WriteLine((cond ? "PASS " : "FAIL ") + name);
    if (!cond) _fails++;
  }

  private static void Main() {
    AppStateTracker t = new AppStateTracker();

    Check(!t.IsEnglishCurrent(), "无焦点默认中文");

    t.SetCurrentPid(100);
    Check(!t.IsEnglish(100), "新进程默认中文");
    t.SetCurrentPid(200);
    Check(!t.IsEnglish(200), "第二个进程默认中文");

    t.SetEnglish(200);
    Check(t.IsEnglish(200), "进程200切英文");
    Check(!t.IsEnglish(100), "进程100不受影响");

    t.SetCurrentPid(200);
    Check(t.IsEnglishCurrent(), "当前进程200为英文");

    t.SetChinese(200);
    Check(!t.IsEnglish(200), "自愈中文生效");

    t.ToggleChinese(200, true);
    Check(!t.IsEnglish(200), "翻回中文");
    t.ToggleChinese(200, false);
    Check(t.IsEnglish(200), "翻到英文");

    t.SetCurrentPid(300);
    Check(!t.IsEnglish(300), "新进程300默认中文");

    /* pid 被新进程复用：启动时间变化 → 状态归零 */
    AppStateTracker.StartTimeProvider = delegate(uint pid) {
      if (pid == 400) return new DateTime(2026, 1, 1);
      return DateTime.Now;
    };
    t.SetCurrentPid(400);
    t.SetEnglish(400);
    Check(t.IsEnglish(400), "400切英文");

    AppStateTracker.StartTimeProvider = delegate(uint pid) {
      if (pid == 400) return new DateTime(2026, 2, 1);
      return DateTime.Now;
    };
    t.SetCurrentPid(400);
    Check(!t.IsEnglish(400), "pid复用进程重启归零中文");

    /* 大量进程清理不崩溃 */
    for (uint i = 500; i < 600; i++) {
      t.SetCurrentPid(i);
    }
    Check(true, "大量进程切换不崩溃");

    Console.WriteLine(_fails == 0 ? "ALL PASS" : (_fails + " FAILED"));
    Environment.Exit(_fails == 0 ? 0 : 1);
  }
}
