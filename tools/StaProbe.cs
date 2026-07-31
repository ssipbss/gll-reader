using System;
using System.IO;
using System.Threading;

class StaProbe {
  static void Main() {
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    var done = new ManualResetEvent(false);
    Exception threadErr = null;
    string result = "";
    Thread t = new Thread(delegate() {
      try {
        Type vt = Type.GetTypeFromProgID("SAPI.SpVoice");
        dynamic sp = Activator.CreateInstance(vt);
        result = "created; ";
        try {
          dynamic cat = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpObjectTokenCategory"));
          result += "cat; ";
          cat.SetId("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Speech_OneCore\\Voices", false);
          result += "setid; ";
          dynamic tokens = cat.EnumerateTokens();
          result += "tokens=" + tokens.Count + "; ";
          for (int i = 0; i < tokens.Count; i++) {
            dynamic tok = tokens.Item(i);
            string d = tok.GetDescription();
            if (d.IndexOf("Chinese", StringComparison.OrdinalIgnoreCase) >= 0) {
              sp.Voice = tok;
              result += "selected=" + d + "; ";
              break;
            }
          }
          dynamic fs = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpFileStream"));
          fs.Format.Type = 39;
          fs.Open("test_bin\\sta_probe.wav", 3);
          sp.AudioOutputStream = fs;
          sp.Speak("测试", 0);
          fs.Close();
          result += "wav=" + new FileInfo("test_bin\\sta_probe.wav").Length;
        } catch (Exception ex) {
          result += "ERR: " + ex.Message;
        }
      } catch (Exception ex) {
        threadErr = ex;
      }
      done.Set();
    });
    t.SetApartmentState(ApartmentState.STA);
    t.Start();
    bool ok = done.WaitOne(15000);
    Console.WriteLine("finished=" + ok);
    Console.WriteLine("result=" + result);
    if (threadErr != null) Console.WriteLine("threadErr=" + threadErr.Message);
    Console.WriteLine("DONE");
  }
}
