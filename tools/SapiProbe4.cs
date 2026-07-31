using System;
using System.Diagnostics;
using System.Threading;

class SapiProbe4 {
  static dynamic _sp;

  static void Poll(string label, int seconds) {
    for (int i = 0; i < seconds * 5; i++) {
      Thread.Sleep(200);
      int st = 0;
      try { st = (int)_sp.Status.RunningState; } catch (Exception ex) { Console.WriteLine("state err: " + ex.Message); return; }
      if (st == 0) {
        Console.WriteLine(label + " DONE at " + ((i + 1) * 200) + "ms");
        return;
      }
    }
    Console.WriteLine(label + " STUCK after " + (seconds * 1000) + "ms");
  }

  static void Main() {
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    try {
      Type t = Type.GetTypeFromProgID("SAPI.SpVoice");
      _sp = Activator.CreateInstance(t);
      dynamic tokens = _sp.GetVoices();
      for (int i = 0; i < tokens.Count; i++) {
        dynamic tok = tokens.Item(i);
        string d = tok.GetDescription();
        if (d.IndexOf("Chinese", StringComparison.OrdinalIgnoreCase) >= 0) _sp.Voice = tok;
      }
      _sp.Rate = 3;
      _sp.Volume = 100;

      Console.WriteLine("A: flag3 async+purge 一");
      _sp.Speak("一", 3);
      Poll("A", 4);

      Console.WriteLine("B: flag0 sync 二");
      Stopwatch sw = Stopwatch.StartNew();
      _sp.Speak("二", 0);
      sw.Stop();
      Console.WriteLine("B returned after " + sw.ElapsedMilliseconds + "ms state=" + _sp.Status.RunningState);
      Poll("B", 4);

      Console.WriteLine("C: flag1 async 三");
      _sp.Speak("三", 1);
      Poll("C", 4);

      Console.WriteLine("D: flag3 async+purge 四");
      _sp.Speak("四", 3);
      Poll("D", 4);
    } catch (Exception ex) {
      Console.WriteLine("ERR: " + ex);
    }
    Console.WriteLine("DONE");
  }
}
