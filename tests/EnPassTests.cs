using System;
using System.Threading;

namespace GenDaLangDu.Tests {
  public static class EnPassTests {
    public static void RunAll() {
      T.Run("EnPass accumulates and confirms over 4 letters", () => {
        EnPassTracker t = new EnPassTracker(confirmMs: 10, expireMs: 60);
        string got = null;
        t.Confirmed += s => { got = s; };
        t.Note("w", "e1");
        t.Note("q", "e1");
        t.Note("k", "e1");
        t.Note("l", "e1");
        t.Note("z", "e1");
        Thread.Sleep(30);
        T.True(t.Check(), "check confirms");
        T.Eq(got, "wqklz", "confirmed letters");
        T.True(!t.IsArmed, "disarmed");
      });
      T.Run("EnPass Cancel disarms", () => {
        EnPassTracker t = new EnPassTracker(confirmMs: 10, expireMs: 60);
        t.Note("a", "e1");
        t.Cancel("test");
        T.True(!t.IsArmed, "disarmed after cancel");
      });
      T.Run("EnPass short candidate expires", () => {
        EnPassTracker t = new EnPassTracker(confirmMs: 10, expireMs: 60);
        t.Note("a", "e1");
        Thread.Sleep(100);
        T.True(t.Check(), "expired check");
        T.True(!t.IsArmed, "disarmed after expire");
      });
      T.Run("EnPass element change resets candidate", () => {
        EnPassTracker t = new EnPassTracker(confirmMs: 10, expireMs: 60);
        t.Note("a", "e1");
        t.Note("b", "e2");
        T.Eq(t.Letters, "b", "reset to new element");
      });
    }
  }
}
