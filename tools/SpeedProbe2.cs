using System;
using System.Diagnostics;

class SpeedProbe2 {
  static void Main() {
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    try {
      Type t = Type.GetTypeFromProgID("SAPI.SpVoice");
      dynamic sp = Activator.CreateInstance(t);
      dynamic tokens = sp.GetVoices();
      for (int i = 0; i < tokens.Count; i++) {
        dynamic tok = tokens.Item(i);
        string d = tok.GetDescription();
        if (d.IndexOf("Huihui", StringComparison.OrdinalIgnoreCase) >= 0) {
          sp.Voice = tok;
          break;
        }
      }
      sp.Rate = 2;
      sp.Volume = 100;

      // 异步 + WaitForDone
      Console.WriteLine("--- 异步+WaitForDone ---");
      Stopwatch sw = Stopwatch.StartNew();
      sp.Speak("现", 1);
      sp.WaitUntilDone(-1);
      sw.Stop();
      Console.WriteLine("[现] async: " + sw.ElapsedMilliseconds + "ms");

      sw.Restart();
      sp.Speak("现在", 1);
      sp.WaitUntilDone(-1);
      sw.Stop();
      Console.WriteLine("[现在] async: " + sw.ElapsedMilliseconds + "ms");

      sw.Restart();
      for (int i = 0; i < 6; i++) {
        sp.Speak("字", 1);
        sp.WaitUntilDone(-1);
      }
      sw.Stop();
      Console.WriteLine("6 单字 async 总耗时: " + sw.ElapsedMilliseconds + "ms，平均 " + (sw.ElapsedMilliseconds / 6) + "ms/字");

      // 不加 WaitForDone，直接连续异步（SAPI 内部队列）
      Console.WriteLine("--- 连续异步不等待 ---");
      sw.Restart();
      for (int i = 0; i < 6; i++) {
        sp.Speak("字", 1);
      }
      sp.WaitUntilDone(-1);
      sw.Stop();
      Console.WriteLine("6 单字连续异步总耗时: " + sw.ElapsedMilliseconds + "ms");
    } catch (Exception ex) {
      Console.WriteLine("ERR: " + ex);
    }
    Console.WriteLine("DONE");
  }
}
