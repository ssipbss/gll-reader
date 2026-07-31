using System;
using System.IO;
using System.Threading;

class StaKangProbe {
  static void Main() {
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    var done = new ManualResetEvent(false);
    string result = "";
    Thread t = new Thread(delegate() {
      try {
        Type vt = Type.GetTypeFromProgID("SAPI.SpVoice");
        dynamic zh = Activator.CreateInstance(vt);
        dynamic en = Activator.CreateInstance(vt);
        result = "voices created; ";
        dynamic cat = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpObjectTokenCategory"));
        cat.SetId("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Speech_OneCore\\Voices", false);
        dynamic tokens = cat.EnumerateTokens();
        result += "tokens=" + tokens.Count + "; ";
        for (int i = 0; i < tokens.Count; i++) {
          dynamic tok = tokens.Item(i);
          string d = tok.GetDescription();
          if (d.IndexOf("Kangkang", StringComparison.OrdinalIgnoreCase) >= 0) {
            result += "found kangkang; ";
            zh.Voice = tok;
            result += "assigned; ";
            break;
          }
        }
        dynamic fs = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpFileStream"));
        fs.Format.Type = 39;
        fs.Open("test_bin\\sta_kang.wav", 3);
        zh.AudioOutputStream = fs;
        zh.Speak("你好", 0);
        fs.Close();
        result += "wav=" + new FileInfo("test_bin\\sta_kang.wav").Length;
      } catch (Exception ex) {
        result += "ERR: " + ex.Message;
      }
      done.Set();
    });
    t.SetApartmentState(ApartmentState.STA);
    t.Start();
    bool ok = done.WaitOne(15000);
    Console.WriteLine("finished=" + ok);
    Console.WriteLine("result=" + result);
    Console.WriteLine("DONE");
  }
}
