using System;
using System.IO;

class OneCoreProbe {
  static void Main(string[] args) {
    string outPath = args.Length > 0 ? args[0] : "onecore.wav";
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    try {
      Type tokenType = Type.GetTypeFromProgID("SAPI.SpObjectToken");
      Console.WriteLine("tokenType=" + (tokenType == null ? "NULL" : tokenType.FullName));
      dynamic token = Activator.CreateInstance(tokenType);
      token.SetId("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Speech_OneCore\\Voices\\Tokens\\MSTTS_V110_zhCN_KangkangM", false);

      Type voiceType = Type.GetTypeFromProgID("SAPI.SpVoice");
      dynamic sp = Activator.CreateInstance(voiceType);
      sp.Voice = token;
      Console.WriteLine("voice=" + sp.Voice.GetDescription());

      dynamic fs = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpFileStream"));
      fs.Format.Type = 39;
      fs.Open(outPath, 3);
      sp.AudioOutputStream = fs;
      sp.Speak("你好，我是康康", 0);
      fs.Close();
      Console.WriteLine("wav=" + new FileInfo(outPath).Length);
    } catch (Exception ex) {
      Console.WriteLine("ERR: " + ex);
    }
    Console.WriteLine("DONE");
  }
}
