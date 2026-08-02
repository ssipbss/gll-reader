using System;

namespace GenDaLangDu {
  [Serializable]
  public class AppSettings {
    public string ZhVoice = "";
    public string EnVoice = "";
    public int Rate = 3;
    public int Volume = 100;
    public bool Letters = true;
    public bool Digits = true;
    public bool Punct = true;
    public bool Func = true;
    public bool Modifiers = true;
    public bool DebugLog = false;
    public bool ClickSpeak = false;
    public bool ClickSpeakInitialized = false;
  }
}
