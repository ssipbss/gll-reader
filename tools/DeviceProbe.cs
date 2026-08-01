using System;
using System.Diagnostics;

class DeviceProbe {
  static void Main() {
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    try {
      Type t = Type.GetTypeFromProgID("SAPI.SpVoice");
      dynamic sp = Activator.CreateInstance(t);
      sp.Rate = 2;
      sp.Volume = 100;

      Console.WriteLine("--- 经典慧慧 ---");
      dynamic tokens = sp.GetVoices();
      for (int i = 0; i < tokens.Count; i++) {
        dynamic tok = tokens.Item(i);
        string d = tok.GetDescription();
        if (d.IndexOf("Huihui Desktop", StringComparison.OrdinalIgnoreCase) >= 0) {
          sp.Voice = tok;
          break;
        }
      }
      TimeIt(sp, "一");
      try {
        sp.AllowAudioOutputFormatChangesOnNextSet = false;
        Console.WriteLine("AllowAudioOutputFormatChangesOnNextSet=false 已设置");
      } catch (Exception ex) {
        Console.WriteLine("设置属性失败: " + ex.Message);
      }
      TimeIt(sp, "二");
      TimeIt(sp, "三");
      TimeIt(sp, "四");
      TimeIt(sp, "五");

      Console.WriteLine("--- 康康(OneCore) ---");
      dynamic cat = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpObjectTokenCategory"));
      cat.SetId("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Speech_OneCore\\Voices", false);
      dynamic oneTokens = cat.EnumerateTokens();
      for (int i = 0; i < oneTokens.Count; i++) {
        dynamic tok = oneTokens.Item(i);
        string d = tok.GetDescription();
        if (d.IndexOf("Kangkang", StringComparison.OrdinalIgnoreCase) >= 0) {
          sp.Voice = tok;
          break;
        }
      }
      TimeIt(sp, "一");
      TimeIt(sp, "二");
      TimeIt(sp, "三");
    } catch (Exception ex) {
      Console.WriteLine("ERR: " + ex);
    }
    Console.WriteLine("DONE");
  }

  static void TimeIt(dynamic sp, string text) {
    Stopwatch sw = Stopwatch.StartNew();
    sp.Speak(text, 0);
    sw.Stop();
    Console.WriteLine("[" + text + "] " + sw.ElapsedMilliseconds + "ms");
  }
}
