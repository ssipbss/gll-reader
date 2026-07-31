using System;
using System.Threading;

class SapiProbe {
  static void Main() {
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    try {
      Type t = Type.GetTypeFromProgID("SAPI.SpVoice");
      if (t == null) {
        Console.WriteLine("NO_SAPI");
        return;
      }
      dynamic sp = Activator.CreateInstance(t);
      dynamic tokens = sp.GetVoices();
      Console.WriteLine("voices=" + tokens.Count);
      for (int i = 0; i < tokens.Count; i++) {
        dynamic tok = tokens.Item(i);
        string d = tok.GetDescription();
        Console.WriteLine("voice " + i + ": " + d);
        if (d.IndexOf("Chinese", StringComparison.OrdinalIgnoreCase) >= 0) {
          sp.Voice = tok;
        }
      }
      sp.Rate = 3;
      sp.Volume = 100;
      Console.WriteLine("warmup speak(space, async+purge)");
      sp.Speak(" ", 3);
      Thread.Sleep(400);
      Console.WriteLine("state after warmup=" + sp.Status.RunningState);
      Console.WriteLine("async speak 存在即是合理");
      sp.Speak("存在即是合理", 1);
      for (int i = 0; i < 30; i++) {
        Thread.Sleep(200);
        int st = 0;
        try { st = (int)sp.Status.RunningState; } catch (Exception ex) { Console.WriteLine("state err: " + ex.Message); }
        Console.WriteLine("t=" + ((i + 1) * 200) + "ms state=" + st);
        if (st == 0 && i > 2) break;
      }
    } catch (Exception ex) {
      Console.WriteLine("ERR: " + ex);
    }
    Console.WriteLine("DONE");
  }
}
