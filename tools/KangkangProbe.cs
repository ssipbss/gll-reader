using System;
using System.IO;
using System.Threading;

class KangkangProbe {
  static void Main(string[] args) {
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    try {
      dynamic cat = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpObjectTokenCategory"));
      cat.SetId("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Speech_OneCore\\Voices", false);
      dynamic tokens = cat.EnumerateTokens();
      Console.WriteLine("tokens=" + tokens.Count);
      for (int idx = 0; idx < tokens.Count; idx++) {
        dynamic tok = tokens.Item(idx);
        string desc = tok.GetDescription();
        Console.WriteLine("testing [" + idx + "] " + desc);
        string wav = "test_bin\\voice" + idx + ".wav";
        try { File.Delete(wav); } catch { }
        dynamic sp = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpVoice"));
        sp.Voice = tok;
        dynamic fs = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpFileStream"));
        fs.Format.Type = 39;
        fs.Open(wav, 3);
        sp.AudioOutputStream = fs;
        var done = new ManualResetEvent(false);
        var thread = new Thread(delegate() {
          try {
            sp.Speak("你好，测试", 0);
          } catch (Exception ex) {
            Console.WriteLine("  speak err: " + ex.Message);
          }
          done.Set();
        });
        thread.Start();
        bool finished = done.WaitOne(15000);
        try { fs.Close(); } catch { }
        long len = File.Exists(wav) ? new FileInfo(wav).Length : 0;
        Console.WriteLine("  finished=" + finished + " wav=" + len);
        if (!finished) {
          try { sp.Speak("", 2); } catch { }
        }
      }
    } catch (Exception ex) {
      Console.WriteLine("ERR: " + ex);
    }
    Console.WriteLine("DONE");
  }
}
