using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;

namespace GenDaLangDu {
  public sealed class Speaker : IDisposable {
    private enum ItemKind { SpeakZh, SpeakEn, SetVoices, Cancel, Stop }

    private sealed class WorkItem {
      public ItemKind Kind;
      public string Text;
    }

    private readonly BlockingCollection<WorkItem> _queue = new BlockingCollection<WorkItem>();
    private int _prevBatchCount = 1;
    private DateTime _lastEnqueueAt = DateTime.MinValue;
    private DateTime _prevEnqueueAt = DateTime.MinValue;
    private readonly ManualResetEvent _ready = new ManualResetEvent(false);
    private Thread _thread;
    private dynamic _zh;
    private dynamic _en;
    private volatile string _zhVoice = "";
    private volatile string _enVoice = "";
    private volatile int _rate = 1;
    private volatile int _volume = 100;
    private bool _disposed;
    private SpeechSynthesizer _zhRt;
    private SpeechSynthesizer _enRt;
    private volatile bool _zhIsRt;
    private volatile bool _enIsRt;
    private static CancellationTokenSource _rtCancel = new CancellationTokenSource();

    public Action<string> Log { get; set; }

    public int Rate {
      get { return _rate; }
      set { _rate = value; }
    }

    public int Volume {
      get { return _volume; }
      set { _volume = value; }
    }

    public Speaker() {
      _thread = new Thread(Worker);
      _thread.IsBackground = true;
      try { _thread.SetApartmentState(ApartmentState.STA); } catch { }
      _thread.Start();
      _ready.WaitOne(5000);
    }

    private void Worker() {
      Diag("W_BEGIN");
      try {
        Type t = Type.GetTypeFromProgID("SAPI.SpVoice");
        if (t != null) {
          Diag("W_CREATE");
          _zh = Activator.CreateInstance(t);
          _en = Activator.CreateInstance(t);
          Diag("W_SEL_ZH");
          SelectVoice(_zh, _zhVoice, "Chinese");
          Diag("W_SEL_EN");
          SelectVoice(_en, _enVoice, "English");
          Diag("W_INIT_OK");
        }
      } catch (Exception ex) {
        if (Log != null) Log("SPEAKER_INIT_ERR:" + ex.Message);
        Diag("W_INIT_ERR " + ex.Message);
      }
      try {
        _zhRt = new SpeechSynthesizer();
        _enRt = new SpeechSynthesizer();
        Diag("W_RT_OK");
      } catch { }
      _ready.Set();

      while (!_disposed) {
        WorkItem first;
        try { first = _queue.Take(); } catch { break; }
        try {
          ProcessBatch(first);
        } catch (Exception ex) {
          if (Log != null) Log("WORKER_ERR:" + ex.Message);
        }
      }
    }

    private void ProcessBatch(WorkItem first) {
      Diag("W_BATCH kind=" + first.Kind);
      List<WorkItem> items = new List<WorkItem>();
      items.Add(first);
      WorkItem tmp;
      bool got = _queue.TryTake(out tmp, 0);
      if (got) items.Add(tmp);
      while (_queue.TryTake(out tmp, 0)) items.Add(tmp);
      while (_queue.TryTake(out tmp, 0)) items.Add(tmp);
      Diag("W_DRAINED " + items.Count);
      System.Text.StringBuilder zh = new System.Text.StringBuilder();
      System.Text.StringBuilder en = new System.Text.StringBuilder();
      bool cancelled = false;
      bool stop = false;

      foreach (WorkItem it in items) {
        Diag("W_ITEM " + it.Kind);
        switch (it.Kind) {
          case ItemKind.SpeakZh:
            if (!cancelled) {
              zh.Append(it.Text);
            }
            break;
          case ItemKind.SpeakEn:
            if (!cancelled) {
              en.Append(it.Text.ToUpperInvariant());
            }
            break;
          case ItemKind.SetVoices:
            try {
              ApplyVoices(_zhVoice, _enVoice);
            } catch (Exception ex) {
              if (Log != null) Log("SETVOICES_ERR:" + ex.Message);
            }
            break;
          case ItemKind.Cancel:
            cancelled = true;
            zh.Length = 0;
            en.Length = 0;
            Cancel(_zh);
            Cancel(_en);
            break;
          case ItemKind.Stop:
            stop = true;
            cancelled = true;
            zh.Length = 0;
            en.Length = 0;
            Cancel(_zh);
            Cancel(_en);
            break;
        }
      }

      int speakCount = 0;
      foreach (WorkItem it in items) {
        if (it.Kind == ItemKind.SpeakZh || it.Kind == ItemKind.SpeakEn) speakCount++;
      }
      _prevBatchCount = speakCount;

      if (zh.Length > 0) {
        if (Log != null) Log("ZH_MERGE [" + zh + "]");
        if (_zhIsRt && _zhRt != null) SpeakRtSync(_zhRt, zh.ToString(), "ZH", _rate, false);
        else SpeakSync(_zh, zh.ToString(), "ZH", ref _lastRateZh, ref _lastVolumeZh, _rate, false);
      }
      if (en.Length > 0) {
        if (Log != null) Log("EN_MERGE [" + en + "]");
        if (_enIsRt && _enRt != null) SpeakRtSync(_enRt, en.ToString(), "EN", Math.Min(10, _rate + 2), true);
        else SpeakSync(_en, en.ToString(), "EN", ref _lastRateEn, ref _lastVolumeEn, Math.Min(10, _rate + 2), true);
      }
      if (stop) _disposed = true;
    }

    private static System.Media.SoundPlayer _currentPlayer;

    private int _lastRateZh = int.MinValue;
    private int _lastVolumeZh = int.MinValue;
    private int _lastRateEn = int.MinValue;
    private int _lastVolumeEn = int.MinValue;
    private void SpeakSync(dynamic voice, string text, string tag, ref int lastRate, ref int lastVolume, int rate, bool xml) {
      if (voice == null) return;
      try {
        if (rate != lastRate) { voice.Rate = rate; lastRate = rate; }
        if (_volume != lastVolume) { voice.Volume = _volume; lastVolume = _volume; }
      } catch { }
      string wav = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gll_speech.wav");
      try {
        dynamic fs = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpFileStream"));
        try { fs.Open(wav, 3); } catch { }
        voice.AudioOutputStream = fs;
        try {
          if (xml) voice.Speak(BuildSayAs(text), 8);
          else voice.Speak(text, 0);
        } catch (Exception ex) {
          if (xml) { try { voice.Speak(text, 0); } catch { } }
          if (Log != null) Log(tag + "_ERR:" + ex.Message);
        }
        try { fs.Close(); } catch { }
        try { voice.AudioOutputStream = null; } catch { }
      } catch (Exception ex) {
        if (Log != null) Log(tag + "_ERR:" + ex.Message);
        return;
      }
      try {
        string playPath = TrimWavSilence(wav);
        using (System.Media.SoundPlayer p = new System.Media.SoundPlayer(playPath)) {
          p.PlaySync();
          _currentPlayer = null;
        }
      } catch (Exception ex) {
        if (Log != null) Log(tag + "_ERR:" + ex.Message);
      }
    }

    private void SpeakRtSync(SpeechSynthesizer synth, string text, string tag, int rate, bool xml) {
      if (synth == null) return;
      try {
        try {
          synth.Options.SpeakingRate = Math.Max(0.5, Math.Min(6.0, 1.0 + rate * 0.1));
        } catch { }
        string wav = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gll_speech_rt.wav");
        try {
          SpeechSynthesisStream stream = null;
          try {
            var op = xml ? synth.SynthesizeSsmlToStreamAsync(BuildSayAs(text))
                         : synth.SynthesizeTextToStreamAsync(text);
            stream = op.AsTask(_rtCancel.Token).Result;
          } catch {
            if (xml) {
              var op2 = synth.SynthesizeTextToStreamAsync(text);
              stream = op2.AsTask(_rtCancel.Token).Result;
            } else {
              throw;
            }
          }
          using (stream) {
            using (var reader = new DataReader(stream.GetInputStreamAt(0))) {
              uint size = (uint)stream.Size;
              var load = reader.LoadAsync(size).AsTask(_rtCancel.Token);
              load.Wait();
              byte[] buf = new byte[size];
              reader.ReadBytes(buf);
              System.IO.File.WriteAllBytes(wav, buf);
            }
          }
          string playPath = TrimWavSilence(wav);
          using (System.Media.SoundPlayer p = new System.Media.SoundPlayer(playPath)) {
            p.PlaySync();
            _currentPlayer = null;
          }
        } catch (Exception ex) {
          if (Log != null) Log(tag + "_ERR:" + ex.Message);
        }
      } catch (Exception ex) {
        if (Log != null) Log(tag + "_ERR:" + ex.Message);
      }
    }

    private static bool IsNaturalName(string desc) {
      return !string.IsNullOrEmpty(desc) &&
             desc.IndexOf("natural", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool VoiceMatches(string a, string b) {
      if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
      if (a.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0) return true;
      if (b.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0) return true;
      return false;
    }

    private static bool TrySelectRtVoice(SpeechSynthesizer synth, string desc) {
      if (synth == null || string.IsNullOrEmpty(desc)) return false;
      try {
        foreach (var v in SpeechSynthesizer.AllVoices) {
          if (VoiceMatches(v.DisplayName, desc)) {
            synth.Voice = v;
            return true;
          }
        }
      } catch { }
      return false;
    }

    private static string TrimWavSilence(string path) {
      try {
        byte[] b = System.IO.File.ReadAllBytes(path);
        if (b.Length < 44) return path;
        int pos = 12;
        int sampleRate = 22050;
        int channels = 1;
        int bits = 16;
        int dataOffset = -1;
        int dataSize = 0;
        while (pos < b.Length - 8) {
          string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
          int size = BitConverter.ToInt32(b, pos + 4);
          if (id == "fmt ") {
            channels = BitConverter.ToInt16(b, pos + 10);
            sampleRate = BitConverter.ToInt32(b, pos + 12);
            bits = BitConverter.ToInt16(b, pos + 22);
          } else if (id == "data") {
            dataOffset = pos + 8;
            dataSize = size;
            break;
          }
          pos += 8 + size + (size % 2);
        }
        if (dataOffset < 0 || dataSize <= 0) return path;
        int bps = (bits / 8) * channels;
        if (bps <= 0) return path;
        int total = dataSize / bps;
        if (total <= 0) return path;
        int threshold = bits == 16 ? 60 : 6;
        int first = -1;
        int last = -1;
        for (int i = 0; i < total; i++) {
          int v = bits == 16
            ? BitConverter.ToInt16(b, dataOffset + i * bps)
            : (b[dataOffset + i * bps] - 128);
          if (Math.Abs(v) > threshold) {
            if (first < 0) first = i;
            last = i;
          }
        }
        if (first < 0 || last < first) return path;
        int margin = Math.Max(1, (int)(sampleRate * 0.015));
        int start = Math.Max(0, first - margin);
        int end = Math.Min(total, last + margin);
        int newSize = (end - start) * bps;
        if (newSize <= 0) return path;
        byte[] nb = new byte[dataOffset + newSize];
        Array.Copy(b, 0, nb, 0, dataOffset);
        Array.Copy(b, dataOffset + start * bps, nb, dataOffset, newSize);
        byte[] s1 = BitConverter.GetBytes(newSize);
        Array.Copy(s1, 0, nb, dataOffset - 4, 4);
        byte[] s2 = BitConverter.GetBytes(nb.Length - 8);
        Array.Copy(s2, 0, nb, 4, 4);
        string trimmed = path + ".t.wav";
        System.IO.File.WriteAllBytes(trimmed, nb);
        return trimmed;
      } catch {
        return path;
      }
    }

    private static bool HasCjk(string s) {
      if (string.IsNullOrEmpty(s)) return false;
      foreach (char c in s) {
        if ((c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF) || (c >= 0xF900 && c <= 0xFAFF)) return true;
      }
      return false;
    }

    private static string BuildSayAs(string text) {
      string t = text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
      return "<say-as interpret-as=\"characters\">" + t + "</say-as>";
    }

    private static void Diag(string msg) { }

    private static void Cancel(dynamic voice) {
      try { if (_currentPlayer != null) _currentPlayer.Stop(); } catch { }
      try { if (voice != null) voice.Speak("", 2); } catch { }
      try { if (_rtCancel != null) _rtCancel.Cancel(); } catch { }
    }

    private static bool SelectVoice(dynamic voice, string desc, string lang) {
      bool ok = false;
      if (!string.IsNullOrEmpty(desc)) {
        try { ok = SelectOneCoreVoice(voice, desc, null); } catch { }
      }
      if (!ok && !string.IsNullOrEmpty(desc)) {
        try {
          dynamic tokens = voice.GetVoices();
          for (int i = 0; i < tokens.Count; i++) {
            dynamic tok = tokens.Item(i);
            string d = tok.GetDescription();
            if (string.Equals(d, desc, StringComparison.OrdinalIgnoreCase) ||
                d.StartsWith(desc, StringComparison.OrdinalIgnoreCase)) {
              voice.Voice = tok;
              ok = true;
              break;
            }
          }
        } catch { }
      }
      if (!ok) {
        try {
          dynamic tokens = voice.GetVoices();
          for (int i = 0; i < tokens.Count; i++) {
            dynamic tok = tokens.Item(i);
            string d = tok.GetDescription();
            if (d.IndexOf(lang, StringComparison.OrdinalIgnoreCase) >= 0) {
              voice.Voice = tok;
              ok = true;
              break;
            }
          }
        } catch { }
      }
      if (!ok) {
        try { ok = SelectOneCoreVoice(voice, null, lang); } catch { }
      }
      return ok;
    }

    private void ApplyVoices(string zhDesc, string enDesc) {
      Diag("AV_BEGIN");
      try { Cancel(_zh); } catch { }
      try { Cancel(_en); } catch { }
      try { if (_rtCancel != null) _rtCancel.Cancel(); } catch { }
      _rtCancel = new CancellationTokenSource();
      dynamic oldZh = _zh;
      dynamic oldEn = _en;
      try {
        Type t = Type.GetTypeFromProgID("SAPI.SpVoice");
        dynamic newZh = Activator.CreateInstance(t);
        dynamic newEn = Activator.CreateInstance(t);
        Diag("AV_SEL_ZH");
        bool zhSapi = SelectVoice(newZh, zhDesc, "Chinese");
        Diag("AV_SEL_EN");
        bool enSapi = SelectVoice(newEn, enDesc, "English");
        Diag("AV_SWAP");
        _zh = newZh;
        _en = newEn;
        _zhIsRt = !zhSapi || IsNaturalName(zhDesc);
        _enIsRt = !enSapi || IsNaturalName(enDesc);
        if (_zhIsRt) _zhIsRt = TrySelectRtVoice(_zhRt, zhDesc);
        if (_enIsRt) _enIsRt = TrySelectRtVoice(_enRt, enDesc);
      } catch (Exception ex) {
        Diag("AV_ERR " + ex.Message);
      }
      try { Marshal.FinalReleaseComObject(oldZh); } catch { }
      try { Marshal.FinalReleaseComObject(oldEn); } catch { }
      Diag("AV_DONE");
    }

    private static bool SelectOneCoreVoice(dynamic voice, string desc, string lang) {
      try {
        Diag("SV_CAT");
        dynamic cat = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpObjectTokenCategory"));
        cat.SetId("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Speech_OneCore\\Voices", false);
        Diag("SV_ENUM");
        dynamic tokens = cat.EnumerateTokens();
        Diag("SV_TOKENS " + tokens.Count);
        for (int i = 0; i < tokens.Count; i++) {
          dynamic tok = tokens.Item(i);
          string d = tok.GetDescription();
          bool match = !string.IsNullOrEmpty(desc)
            ? (string.Equals(d, desc, StringComparison.OrdinalIgnoreCase) ||
               d.StartsWith(desc, StringComparison.OrdinalIgnoreCase))
            : d.IndexOf(lang, StringComparison.OrdinalIgnoreCase) >= 0;
          if (match) {
            Diag("SV_ASSIGN " + d);
            voice.Voice = tok;
            Diag("SV_ASSIGNED");
            return true;
          }
        }
      } catch { }
      return false;
    }

    private static List<string> OneCoreVoiceNames() {
      List<string> names = new List<string>();
      try {
        dynamic cat = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpObjectTokenCategory"));
        cat.SetId("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Speech_OneCore\\Voices", false);
        dynamic tokens = cat.EnumerateTokens();
        for (int i = 0; i < tokens.Count; i++) {
          dynamic tok = tokens.Item(i);
          names.Add(tok.GetDescription());
        }
      } catch { }
      return names;
    }

    public List<string> GetVoices() {
      List<string> list = new List<string>();
      try {
        dynamic v = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpVoice"));
        dynamic tokens = v.GetVoices();
        for (int i = 0; i < tokens.Count; i++) {
          dynamic tok = tokens.Item(i);
          string d = tok.GetDescription();
          if (d.IndexOf(" Online ", StringComparison.OrdinalIgnoreCase) < 0) list.Add(d);
        }
        Marshal.FinalReleaseComObject(v);
      } catch { }
      try {
        foreach (string name in OneCoreVoiceNames()) {
          if (!list.Contains(name)) list.Add(name);
        }
      } catch { }
      try {
        foreach (var v in SpeechSynthesizer.AllVoices) {
          string dn = v.DisplayName;
          bool legacy = dn.StartsWith("Microsoft Huihui", StringComparison.OrdinalIgnoreCase) ||
                        dn.StartsWith("Microsoft Yaoyao", StringComparison.OrdinalIgnoreCase) ||
                        dn.StartsWith("Microsoft Kangkang", StringComparison.OrdinalIgnoreCase) ||
                        dn.StartsWith("Microsoft Zira", StringComparison.OrdinalIgnoreCase) ||
                        dn.StartsWith("Microsoft David", StringComparison.OrdinalIgnoreCase);
          if (!legacy && !list.Contains(dn)) list.Add(dn);
        }
      } catch { }
      return list;
    }

    public void RefreshVoices(string zhDesc, string enDesc) {
      _zhVoice = zhDesc ?? "";
      _enVoice = enDesc ?? "";
      try { _queue.Add(new WorkItem { Kind = ItemKind.SetVoices }); } catch { }
    }

    public void WarmUp() {
    }

    public void ApplyRateVolume() { }

    public void SpeakZh(string text) {
      if (string.IsNullOrEmpty(text)) return;
      if (Log != null) Log("ZH:" + text);
      _prevEnqueueAt = _lastEnqueueAt;
      _lastEnqueueAt = DateTime.Now;
      _queue.Add(new WorkItem { Kind = ItemKind.SpeakZh, Text = text });
    }

    public void SpeakEn(string text) {
      if (string.IsNullOrEmpty(text)) return;
      if (Log != null) Log("EN:" + text);
      _prevEnqueueAt = _lastEnqueueAt;
      _lastEnqueueAt = DateTime.Now;
      _queue.Add(new WorkItem { Kind = ItemKind.SpeakEn, Text = text });
    }

    public void FlushAll() { }

    public void Stop() {
      try { _queue.Add(new WorkItem { Kind = ItemKind.Cancel }); } catch { }
    }

    public void Dispose() {
      if (_disposed) return;
      try { _queue.Add(new WorkItem { Kind = ItemKind.Stop }); } catch { }
      if (_thread != null && _thread.IsAlive) _thread.Join(2000);
      try { _ready.Dispose(); } catch { }
    }
  }
}
