using System;
using System.Diagnostics;
using System.IO;
using System.Media;

class FilePlayProbe {
  static void Main() {
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    string wav = Path.Combine(Path.GetTempPath(), "gll_probe.wav");
    try {
      Type t = Type.GetTypeFromProgID("SAPI.SpVoice");
      dynamic sp = Activator.CreateInstance(t);
      dynamic tokens = sp.GetVoices();
      for (int i = 0; i < tokens.Count; i++) {
        dynamic tok = tokens.Item(i);
        string d = tok.GetDescription();
        if (d.IndexOf("Huihui Desktop", StringComparison.OrdinalIgnoreCase) >= 0) {
          sp.Voice = tok;
          break;
        }
      }
      sp.Rate = 2;
      sp.Volume = 100;

      for (int i = 0; i < 5; i++) {
        Stopwatch sw = Stopwatch.StartNew();
        dynamic fs = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpFileStream"));
        fs.Open(wav, 3);
        sp.AudioOutputStream = fs;
        sp.Speak("字", 0);
        fs.Close();
        long synthMs = sw.ElapsedMilliseconds;

        sw.Restart();
        using (SoundPlayer p = new SoundPlayer(wav)) {
          p.PlaySync();
        }
        long playMs = sw.ElapsedMilliseconds;
        Console.WriteLine("第" + (i + 1) + "字: 合成 " + synthMs + "ms, 播放 " + playMs + "ms, 合计 " + (synthMs + playMs) + "ms");
      }
    } catch (Exception ex) {
      Console.WriteLine("ERR: " + ex);
    }
    Console.WriteLine("DONE");
  }
}
