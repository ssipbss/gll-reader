using System;

namespace GenDaLangDu.Tests {
  public static class Program {
    public static int Main() {
      TextDiffTests.RunAll();
      RecentSpeechTests.RunAll();
      SpeechBatchTests.RunAll();
      EnPassTests.RunAll();
      return T.Finish();
    }
  }
}
