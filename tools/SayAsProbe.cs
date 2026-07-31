using System;
using System.IO;

class SayAsProbe {
  static void Main() {
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    try {
      Type t = Type.GetTypeFromProgID("SAPI.SpVoice");
      dynamic sp = Activator.CreateInstance(t);
      dynamic tokens = sp.GetVoices();
      for (int i = 0; i < tokens.Count; i++) {
        dynamic tok = tokens.Item(i);
        string d = tok.GetDescription();
        if (d.IndexOf("Zira", StringComparison.OrdinalIgnoreCase) >= 0) {
          sp.Voice = tok;
          break;
        }
      }
      sp.Rate = 4;
      sp.Volume = 100;
      dynamic fs = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpFileStream"));
      fs.Format.Type = 39;
      fs.Open("test_bin\\sayas.wav", 3);
      sp.AudioOutputStream = fs;
      try {
        sp.Speak("<say-as interpret-as=\"characters\">WE</say-as>", 8);
        Console.WriteLine("XML speak OK");
      } catch (Exception ex) {
        Console.WriteLine("XML speak ERR: " + ex.Message);
      }
      fs.Close();
      Console.WriteLine("wav=" + new FileInfo("test_bin\\sayas.wav").Length);
    } catch (Exception ex) {
      Console.WriteLine("ERR: " + ex);
    }
    Console.WriteLine("DONE");
  }
}
