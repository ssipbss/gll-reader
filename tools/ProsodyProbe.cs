using System;
using System.IO;
using System.Text;

class ProsodyProbe {
  static void Main() {
    Console.OutputEncoding = Encoding.UTF8;
    string dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
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

      string[] tests = {
        "字",
        "<prosody rate=\"+25%\">字</prosody>",
        "<prosody rate=\"+50%\">字</prosody>",
        "<prosody rate=\"+50%\" pitch=\"-15%\">字</prosody>",
        "<prosody rate=\"+100%\" pitch=\"-25%\">字</prosody>"
      };
      for (int i = 0; i < tests.Length; i++) {
        string wav = Path.Combine(dir, "prosody" + i + ".wav");
        dynamic fs = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpFileStream"));
        fs.Format.Type = 39;
        fs.Open(wav, 3);
        sp.AudioOutputStream = fs;
        try {
          sp.Speak(tests[i], 8);
          Console.WriteLine("测试" + i + " OK");
        } catch (Exception ex) {
          Console.WriteLine("测试" + i + " ERR: " + ex.Message);
        }
        fs.Close();
        Console.WriteLine("  时长 " + GetMs(wav) + "ms");
      }
    } catch (Exception ex) {
      Console.WriteLine("ERR: " + ex);
    }
    Console.WriteLine("DONE");
  }

  static int GetMs(string wav) {
    try {
      byte[] bytes = File.ReadAllBytes(wav);
      int pos = 12;
      int byteRate = 1;
      int dataSize = 0;
      while (pos < bytes.Length - 8) {
        string id = Encoding.ASCII.GetString(bytes, pos, 4);
        int size = BitConverter.ToInt32(bytes, pos + 4);
        if (id == "fmt ") byteRate = BitConverter.ToInt32(bytes, pos + 16);
        if (id == "data") { dataSize = size; break; }
        pos += 8 + size + (size % 2);
      }
      return (int)Math.Round(dataSize * 1000.0 / byteRate);
    } catch { return 0; }
  }
}
