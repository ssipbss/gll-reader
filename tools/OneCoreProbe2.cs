using System;
using System.IO;

class OneCoreProbe2 {
  static void Main(string[] args) {
    string outPath = args.Length > 0 ? args[0] : "onecore.wav";
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    try {
      // try category enumeration
      Type catType = Type.GetTypeFromProgID("SAPI.SpObjectTokenCategory");
      Console.WriteLine("catType=" + (catType == null ? "NULL" : "OK"));
      dynamic cat = Activator.CreateInstance(catType);
      cat.SetId("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Speech_OneCore\\Voices", false);
      dynamic tokens = cat.EnumerateTokens();
      Console.WriteLine("onecore tokens=" + tokens.Count);
      for (int i = 0; i < tokens.Count; i++) {
        dynamic tok = tokens.Item(i);
        Console.WriteLine("  " + i + ": " + tok.GetDescription());
      }
      if (tokens.Count > 0) {
        Type voiceType = Type.GetTypeFromProgID("SAPI.SpVoice");
        dynamic sp = Activator.CreateInstance(voiceType);
        sp.Voice = tokens.Item(0);
        Console.WriteLine("selected=" + sp.Voice.GetDescription());
        dynamic fs = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpFileStream"));
        fs.Format.Type = 39;
        fs.Open(outPath, 3);
        sp.AudioOutputStream = fs;
        sp.Speak("你好，我是康康", 0);
        fs.Close();
        Console.WriteLine("wav=" + new FileInfo(outPath).Length);
      }
    } catch (Exception ex) {
      Console.WriteLine("ERR: " + ex.Message);
    }
    Console.WriteLine("DONE");
  }
}
