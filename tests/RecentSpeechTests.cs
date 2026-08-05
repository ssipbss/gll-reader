using System;

namespace GenDaLangDu.Tests {
  public static class RecentSpeechTests {
    public static void RunAll() {
      T.Run("RecentSpeech first is not duplicate", () => {
        RecentSpeech rs = new RecentSpeech();
        DateTime t0 = new DateTime(2026, 8, 5, 10, 0, 0);
        T.True(!rs.IsDuplicate("你", t0, t0), "first not dup");
      });
      T.Run("RecentSpeech same within 400ms no key is duplicate", () => {
        RecentSpeech rs = new RecentSpeech();
        DateTime t0 = new DateTime(2026, 8, 5, 10, 0, 0);
        rs.Mark("你", t0);
        T.True(rs.IsDuplicate("你", t0, t0.AddMilliseconds(100)), "dup within window");
      });
      T.Run("RecentSpeech new key after mark is not duplicate", () => {
        RecentSpeech rs = new RecentSpeech();
        DateTime t0 = new DateTime(2026, 8, 5, 10, 0, 0);
        rs.Mark("你", t0);
        T.True(!rs.IsDuplicate("你", t0.AddMilliseconds(1), t0.AddMilliseconds(100)), "new key not dup");
      });
      T.Run("RecentSpeech after 400ms is not duplicate", () => {
        RecentSpeech rs = new RecentSpeech();
        DateTime t0 = new DateTime(2026, 8, 5, 10, 0, 0);
        rs.Mark("你", t0);
        T.True(!rs.IsDuplicate("你", t0, t0.AddMilliseconds(500)), "expired not dup");
      });
      T.Run("RecentSpeech different text is not duplicate", () => {
        RecentSpeech rs = new RecentSpeech();
        DateTime t0 = new DateTime(2026, 8, 5, 10, 0, 0);
        rs.Mark("你", t0);
        T.True(!rs.IsDuplicate("好", t0, t0.AddMilliseconds(100)), "different text not dup");
      });
    }
  }
}
