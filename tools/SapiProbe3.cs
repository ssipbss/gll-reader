using System;
using System.IO;
using System.Threading;

class SapiProbe3 {
  static void Main() {
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    try {
      Type t = Type.GetTypeFromProgID("SAPI.SpVoice");
      dynamic sp = Activator.CreateInstance(t);
      dynamic tokens = sp.GetVoices();
      for (int i = 0; i < tokens.Count; i++) {
        dynamic tok = tokens.Item(i);
        string d = tok.GetDescription();
        if (d.IndexOf("Chinese", StringComparison.OrdinalIgnoreCase) >= 0) sp.Voice = tok;
      }
      sp.Rate = 3;
      sp.Volume = 100;

      string path = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "probe3.wav");
      dynamic fs = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpFileStream"));
      fs.Format.Type = 39; // SAFT22kHz16BitMono
      fs.Open(path, 3);
      sp.AudioOutputStream = fs;

      Console.WriteLine("async speak 一二三 -> file");
      sp.Speak("一二三", 1);
      for (int i = 0; i < 30; i++) {
        Thread.Sleep(200);
        int st = 0;
        try { st = (int)sp.Status.RunningState; } catch { }
        long len = new FileInfo(path).Length;
        Console.WriteLine("t=" + ((i + 1) * 200) + "ms state=" + st + " wav=" + len);
        if (st == 0 && len > 1000) break;
      }

      Console.WriteLine("async speak space -> file");
      sp.Speak(" ", 1);
      for (int i = 0; i < 15; i++) {
        Thread.Sleep(200);
        int st = 0;
        try { st = (int)sp.Status.RunningState; } catch { }
        long len = new FileInfo(path).Length;
        Console.WriteLine("space t=" + ((i + 1) * 200) + "ms state=" + st + " wav=" + len);
        if (st == 0) break;
      }

      Console.WriteLine("async speak 四 -> file after space");
      sp.Speak("四", 1);
      for (int i = 0; i < 20; i++) {
        Thread.Sleep(200);
        int st = 0;
        try { st = (int)sp.Status.RunningState; } catch { }
        long len = new FileInfo(path).Length;
        Console.WriteLine("four t=" + ((i + 1) * 200) + "ms state=" + st + " wav=" + len);
        if (st == 0 && len > 1000) break;
      }

      fs.Close();
      Console.WriteLine("FINAL wav=" + new FileInfo(path).Length);
    } catch (Exception ex) {
      Console.WriteLine("ERR: " + ex);
    }
    Console.WriteLine("DONE");
  }
}
