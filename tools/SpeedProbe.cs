using System;
using System.Diagnostics;
using System.Threading;

class SpeedProbe {
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

      TimeIt(sp, "现");
      TimeIt(sp, "现在");
      TimeIt(sp, "现在停顿");
      TimeIt(sp, "现在停顿好像严重了");

      Console.WriteLine("--- 连续单字 6 次 ---");
      Stopwatch sw = Stopwatch.StartNew();
      for (int i = 0; i < 6; i++) {
        sp.Speak("字", 0);
      }
      sw.Stop();
      Console.WriteLine("6 个单字总耗时: " + sw.ElapsedMilliseconds + "ms，平均 " + (sw.ElapsedMilliseconds / 6) + "ms/字");
    } catch (Exception ex) {
      Console.WriteLine("ERR: " + ex);
    }
    Console.WriteLine("DONE");
  }

  static void TimeIt(dynamic sp, string text) {
    Stopwatch sw = Stopwatch.StartNew();
    sp.Speak(text, 0);
    sw.Stop();
    Console.WriteLine("[" + text + "] 耗时 " + sw.ElapsedMilliseconds + "ms");
  }
}
