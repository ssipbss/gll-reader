using System;
using System.Threading;

class SapiProbe2 {
  static dynamic _sp;

  static void Poll(string label, int seconds) {
    for (int i = 0; i < seconds * 5; i++) {
      Thread.Sleep(200);
      int st = 0;
      try { st = (int)_sp.Status.RunningState; } catch (Exception ex) { Console.WriteLine("state err: " + ex.Message); return; }
      if (st == 0) {
        Console.WriteLine(label + " done at " + ((i + 1) * 200) + "ms");
        return;
      }
    }
    Console.WriteLine(label + " STILL RUNNING after " + (seconds * 1000) + "ms");
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

      Console.WriteLine("T1: async speak 一 (no purge)");
      _sp.Speak("一", 1);
      Poll("T1", 4);

      Console.WriteLine("T2: async speak 二 (no purge) while maybe idle");
      _sp.Speak("二", 1);
      Poll("T2", 4);

      Console.WriteLine("T3: async speak space (no purge)");
      _sp.Speak(" ", 1);
      Poll("T3", 3);

      Console.WriteLine("T4: async speak 三 (no purge) after space");
      _sp.Speak("三", 1);
      Poll("T4", 4);
    } catch (Exception ex) {
      Console.WriteLine("ERR: " + ex);
    }
    Console.WriteLine("DONE");
  }
}
