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

    private readonly BlockingCollection<SpeechItem> _queue = new BlockingCollection<SpeechItem>();
    private readonly BlockingCollection<PreparedSpeech> _readyQueue = new BlockingCollection<PreparedSpeech>();
    private DateTime _lastEnqueueAt = DateTime.MinValue;
    private DateTime _prevEnqueueAt = DateTime.MinValue;
    private static long _nextSpeechId;
    private readonly ManualResetEvent _initReady = new ManualResetEvent(false);
    private Thread _thread;
    private Thread _playerThread;
    private volatile int _generation;
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
    private volatile bool _speaking;
    private volatile bool _preparing;
    private static CancellationTokenSource _rtCancel = new CancellationTokenSource();

    private sealed class PreparedSpeech {
      public int Generation;
      public string Path;
      public string Tag;
    }

    public Action<string> Log { get; set; }

    public int Rate {
      get { return _rate; }
      set { _rate = value; }
    }

    public int Volume {
      get { return _volume; }
      set { _volume = value; }
    }

    /// <summary>是否有语音正在朗读或排队（按键音效据此让路，避免覆盖中文朗读）</summary>
    public bool IsBusy {
      get { return _speaking || _preparing || _queue.Count > 0 || _readyQueue.Count > 0; }
    }

    public Speaker() {
      /* 播放线程：只负责按顺序播放已合成好的音频；
         合成线程（Worker）在播放期间预合成后续内容，隐藏合成延迟 */
      _playerThread = new Thread(PlayerLoop);
      _playerThread.IsBackground = true;
      try { _playerThread.SetApartmentState(ApartmentState.STA); } catch { }
      _playerThread.Start();
      _thread = new Thread(Worker);
      _thread.IsBackground = true;
      try { _thread.SetApartmentState(ApartmentState.STA); } catch { }
      _thread.Start();
      _initReady.WaitOne(5000);
    }

    private void PlayerLoop() {
      while (true) {
        PreparedSpeech ps;
        try { ps = _readyQueue.Take(); } catch { break; }
        if (ps.Tag == null) break;
        /* Stop/Cancel 后作废：跳过预合成的过期内容并清理文件 */
        if (ps.Generation != _generation) {
          try { System.IO.File.Delete(ps.Path); } catch { }
          continue;
        }
        _speaking = true;
        try {
          PlayWavBlocking(ps.Path);
        } finally {
          _speaking = false;
        }
        try { System.IO.File.Delete(ps.Path); } catch { }
      }
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
      _initReady.Set();

      while (!_disposed) {
        SpeechItem first;
        try { first = _queue.Take(); } catch { break; }
        try {
          ProcessBatchCore(first);
        } catch (Exception ex) {
          if (Log != null) Log("WORKER_ERR:" + ex.Message);
        }
      }
      try { _readyQueue.Add(new PreparedSpeech { Tag = null }); } catch { }
    }

    private void ProcessBatch(SpeechItem first) {
      ProcessBatchCore(first);
    }

    private void ProcessBatchCore(SpeechItem first) {
      List<SpeechItem> items = new List<SpeechItem>();
      items.Add(first);
      SpeechItem tmp;
      while (_queue.TryTake(out tmp, 0)) items.Add(tmp);
      if (Log != null) {
        System.Text.StringBuilder ids = new System.Text.StringBuilder();
        foreach (SpeechItem it in items) {
          if (ids.Length > 0) ids.Append(",");
          ids.Append(it.Id);
        }
        Log("W_BATCH ids=[" + ids + "]");
      }
      SpeechBatchPlan plan = SpeechBatchPlanner.Plan(items);
      if (plan.SetVoices) {
        try {
          ApplyVoices(_zhVoice, _enVoice);
        } catch (Exception ex) {
          if (Log != null) Log("SETVOICES_ERR:" + ex.Message);
        }
      }
      if (Log != null) {
        Log("W_PLAN zh=[" + plan.Zh + "] en=[" + plan.En + "] words=" +
            plan.EnWords.Count + " stop=" + (plan.Stop ? 1 : 0) +
            " cancel=" + (plan.Cancelled ? 1 : 0));
      }
      if (plan.Cancelled) {
        Cancel(_zh);
        Cancel(_en);
      }
      _preparing = true;
      try {
        PrepareBatchSpeech(new System.Text.StringBuilder(plan.Zh),
          new System.Text.StringBuilder(plan.En), plan.EnWords, plan.EnSsmls);
      } finally {
        _preparing = false;
      }
      if (plan.Stop) _disposed = true;
    }

    /// <summary>把一批计划文本合成为音频后投入就绪队列（播放线程按序播放）。
    /// 合成不阻塞播放：播放当前音频期间即可预合成下一批。</summary>
    private void PrepareBatchSpeech(System.Text.StringBuilder zh,
        System.Text.StringBuilder enPending,
        System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, bool>> enWords,
        System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>> enSsmls) {
      int gen = _generation;
      if (zh.Length > 0) {
        if (Log != null) Log("ZH_MERGE [" + zh + "]");
        string playPath = null;
        if (_zhIsRt && _zhRt != null) playPath = SpeakRtSync(_zhRt, zh.ToString(), "ZH", _rate, false);
        else playPath = SpeakSync(_zh, zh.ToString(), "ZH", ref _lastRateZh, ref _lastVolumeZh, _rate, false);
        if (playPath != null) _readyQueue.Add(new PreparedSpeech { Generation = gen, Path = playPath, Tag = "ZH" });
        zh.Clear();
      }
      if (enPending.Length > 0) {
        enWords.Add(new System.Collections.Generic.KeyValuePair<string, bool>(
          enPending.ToString(), false));
        enPending.Clear();
      }
      foreach (var enItem in enWords) {
        string enText = enItem.Key;
        bool asWord = enItem.Value;
        if (enText.Length == 0) continue;
        if (Log != null) Log("EN_MERGE [" + enText + "] word=" + (asWord ? 1 : 0));
        int enRate = Math.Max(-10, _rate - 2);
        if (asWord) enRate = Math.Min(10, enRate + 3);
        string playPath = null;
        if (_enIsRt && _enRt != null) playPath = SpeakRtSync(_enRt, enText, "EN", enRate, !asWord);
        else playPath = SpeakSync(_en, enText, "EN", ref _lastRateEn, ref _lastVolumeEn, enRate, !asWord);
        if (playPath != null) _readyQueue.Add(new PreparedSpeech { Generation = gen, Path = playPath, Tag = "EN" });
      }
      enWords.Clear();
      foreach (var ssmlItem in enSsmls) {
        string plain = ssmlItem.Key;
        string ssml = ssmlItem.Value;
        if (Log != null) Log("EN_SSML [" + plain + "]");
        int enRate = Math.Max(-10, _rate - 2);
        enRate = Math.Min(10, enRate + 3);
        string playPath = null;
        if (_enIsRt && _enRt != null) playPath = SpeakRtSync(_enRt, plain, "EN", enRate, false, ssml);
        else playPath = SpeakSync(_en, plain, "EN", ref _lastRateEn, ref _lastVolumeEn, enRate, false, ssml);
        if (playPath != null) _readyQueue.Add(new PreparedSpeech { Generation = gen, Path = playPath, Tag = "EN" });
      }
      enSsmls.Clear();
    }

    private static volatile bool _stopRequested;

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern uint mciSendString(string command, System.Text.StringBuilder returnString, int returnLength, IntPtr hwndCallback);

    private int _lastRateZh = int.MinValue;
    private int _lastVolumeZh = int.MinValue;
    private int _lastRateEn = int.MinValue;
    private int _lastVolumeEn = int.MinValue;
    private static long _wavSeq;
    /// <summary>每个语音用唯一临时文件：不复用固定槽位，
    /// 杜绝"播放线程取件与合成线程选槽"之间的覆盖竞态（曾导致读错字/重复读/漏读）。</summary>
    private static string NextWavPath() {
      return System.IO.Path.Combine(System.IO.Path.GetTempPath(),
        "gll_" + Interlocked.Increment(ref _wavSeq).ToString() + ".wav");
    }

    /// <summary>SAPI 合成到唯一临时 WAV 并裁剪静音；返回可播放的文件路径（不播放，由播放线程播）。</summary>
    private string SpeakSync(dynamic voice, string text, string tag, ref int lastRate, ref int lastVolume, int rate, bool xml, string rawSsml = null) {
      if (voice == null) return null;
      string wav = NextWavPath();
      try {
        if (rate != lastRate) { voice.Rate = rate; lastRate = rate; }
        if (_volume != lastVolume) { voice.Volume = _volume; lastVolume = _volume; }
      } catch { }
      try {
        dynamic fs = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpFileStream"));
        try { fs.Open(wav, 3); } catch { }
        voice.AudioOutputStream = fs;
        try {
          if (rawSsml != null) voice.Speak(rawSsml, 8);
          else if (xml) voice.Speak(BuildSayAs(text), 8);
          else voice.Speak(text, 0);
        } catch (Exception ex) {
          if (rawSsml != null || xml) { try { voice.Speak(text, 0); } catch { } }
          if (Log != null) Log(tag + "_ERR:" + ex.Message);
        }
        try { fs.Close(); } catch { }
        try { voice.AudioOutputStream = null; } catch { }
      } catch (Exception ex) {
        if (Log != null) Log(tag + "_ERR:" + ex.Message);
        try { System.IO.File.Delete(wav); } catch { }
        return null;
      }
      return FinishWav(wav, tag);
    }

    /// <summary>WinRT 合成到唯一临时 WAV 并裁剪静音；返回可播放的文件路径（不播放，由播放线程播）。</summary>
    private string SpeakRtSync(SpeechSynthesizer synth, string text, string tag, int rate, bool xml, string rawSsml = null) {
      if (synth == null) return null;
      string wav = NextWavPath();
      try {
        try {
          synth.Options.SpeakingRate = Math.Max(0.5, Math.Min(6.0, 1.0 + rate * 0.1));
        } catch { }
        try {
          SpeechSynthesisStream stream = null;
          try {
            var op = rawSsml != null ? synth.SynthesizeSsmlToStreamAsync(rawSsml)
                     : xml ? synth.SynthesizeSsmlToStreamAsync(BuildSayAs(text))
                     : synth.SynthesizeTextToStreamAsync(text);
            stream = op.AsTask(_rtCancel.Token).Result;
          } catch {
            if (rawSsml != null || xml) {
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
        } catch (Exception ex) {
          if (Log != null) Log(tag + "_ERR:" + ex.Message);
          try { System.IO.File.Delete(wav); } catch { }
          return null;
        }
      } catch (Exception ex) {
        if (Log != null) Log(tag + "_ERR:" + ex.Message);
        try { System.IO.File.Delete(wav); } catch { }
        return null;
      }
      return FinishWav(wav, tag);
    }

    /// <summary>裁剪静音并清理中间文件；返回最终可播放路径（裁剪失败则用原文件）。</summary>
    private string FinishWav(string wav, string tag) {
      string playPath;
      try {
        playPath = TrimWavSilence(wav);
      } catch (Exception ex) {
        if (Log != null) Log(tag + "_ERR:" + ex.Message);
        playPath = wav;
      }
      if (!System.IO.File.Exists(playPath)) playPath = wav;
      if (playPath != wav) {
        try { System.IO.File.Delete(wav); } catch { }
      }
      if (Log != null) Log(tag + "_SYNTH_END");
      return System.IO.File.Exists(playPath) ? playPath : null;
    }

    /// <summary>用 mciSendString 播放 WAV：可被其它线程立即停止（SoundPlayer.Stop 跨线程无效）。</summary>
    private void PlayWavBlocking(string path) {
      const string alias = "gll_snd";
      try {
        /* 停止请求后到达的音频直接丢弃，不再出声（合成无法中断，但可以不放出来） */
        if (_stopRequested) {
          _stopRequested = false;
          return;
        }
        mciSendString("close " + alias, null, 0, IntPtr.Zero);
        uint er = mciSendString("open \"" + path + "\" type waveaudio alias " + alias, null, 0, IntPtr.Zero);
        if (er != 0 && Log != null) Log("PLAY_OPEN_ERR " + er + " " + path);
        if (Log != null) Log("PLAY_OPEN_END");
        mciSendString("play " + alias, null, 0, IntPtr.Zero);
        _stopRequested = false;
        if (Log != null) Log("PLAY_START " + System.IO.Path.GetFileName(path));
        while (true) {
          if (_stopRequested) {
            mciSendString("stop " + alias, null, 0, IntPtr.Zero);
            break;
          }
          System.Text.StringBuilder sb = new System.Text.StringBuilder(64);
          mciSendString("status " + alias + " mode", sb, 64, IntPtr.Zero);
          string mode = sb.ToString();
          if (!mode.StartsWith("playing", StringComparison.OrdinalIgnoreCase)) break;
          Thread.Sleep(50);
        }
        mciSendString("close " + alias, null, 0, IntPtr.Zero);
        if (Log != null) Log("PLAY_DONE " + System.IO.Path.GetFileName(path));
      } catch { }
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
        /* 阈值调低：字母等短音的软起音/尾音不再被误判为静音裁掉 */
        int threshold = bits == 16 ? 4 : 3;
        /* 短音频（<300ms，如单个字母）不裁剪，避免首尾被截 */
        if (total < (int)(sampleRate * 0.30)) return path;
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
        int headMargin = Math.Max(1, (int)(sampleRate * 0.010));
        int tailMargin = Math.Max(1, (int)(sampleRate * 0.015));
        int start = Math.Max(0, first - headMargin);
        int end = Math.Min(total, last + tailMargin);
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
      try {
        _stopRequested = true;
        mciSendString("stop gll_snd", null, 0, IntPtr.Zero);
      } catch { }
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
      try { _queue.Add(new SpeechItem { Kind = SpeechItemKind.SetVoices }); } catch { }
    }

    public void WarmUp() {
    }

    public void ApplyRateVolume() { }

    public void SpeakZh(string text) {
      if (string.IsNullOrEmpty(text)) return;
      _stopRequested = false;
      long id = Interlocked.Increment(ref _nextSpeechId);
      if (Log != null) Log("ZH:" + id + ":" + text);
      _prevEnqueueAt = _lastEnqueueAt;
      _lastEnqueueAt = DateTime.Now;
      _queue.Add(new SpeechItem { Id = id, Kind = SpeechItemKind.SpeakZh, Text = text });
    }

    public void SpeakEn(string text) {
      if (string.IsNullOrEmpty(text)) return;
      _stopRequested = false;
      long id = Interlocked.Increment(ref _nextSpeechId);
      if (Log != null) Log("EN:" + id + ":" + text);
      _prevEnqueueAt = _lastEnqueueAt;
      _lastEnqueueAt = DateTime.Now;
      _queue.Add(new SpeechItem { Id = id, Kind = SpeechItemKind.SpeakEn, Text = text });
    }

    public void SpeakEnWord(string text) {
      if (string.IsNullOrEmpty(text)) return;
      _stopRequested = false;
      long id = Interlocked.Increment(ref _nextSpeechId);
      if (Log != null) Log("ENW:" + id + ":" + text);
      _prevEnqueueAt = _lastEnqueueAt;
      _lastEnqueueAt = DateTime.Now;
      _queue.Add(new SpeechItem { Id = id, Kind = SpeechItemKind.SpeakEnWord, Text = text });
    }

    public void SpeakEnSsml(string plain, string ssml) {
      if (string.IsNullOrEmpty(ssml)) {
        SpeakEnWord(plain);
        return;
      }
      _stopRequested = false;
      long id = Interlocked.Increment(ref _nextSpeechId);
      if (Log != null) Log("ENS:" + id + ":" + plain);
      _prevEnqueueAt = _lastEnqueueAt;
      _lastEnqueueAt = DateTime.Now;
      _queue.Add(new SpeechItem { Id = id, Kind = SpeechItemKind.SpeakEnSsml, Text = plain, Ssml = ssml });
    }



    public void Stop() {
      try {
        /* 立即打断正在播放的音频（播放线程正阻塞在播放中，队列命令无法处理）；
           代际号 +1 使已预合成未播放的内容全部作废 */
        _stopRequested = true;
        mciSendString("stop gll_snd", null, 0, IntPtr.Zero);
        Interlocked.Increment(ref _generation);
        SpeechItem tmp;
        while (_queue.TryTake(out tmp)) { }
        _queue.Add(new SpeechItem { Kind = SpeechItemKind.Cancel });
      } catch { }
    }

    public void Dispose() {
      if (_disposed) return;
      try { _queue.Add(new SpeechItem { Kind = SpeechItemKind.Stop }); } catch { }
      if (_thread != null && _thread.IsAlive) _thread.Join(2000);
      try { _readyQueue.Add(new PreparedSpeech { Tag = null }); } catch { }
      if (_playerThread != null && _playerThread.IsAlive) _playerThread.Join(2000);
      try { _readyQueue.Dispose(); } catch { }
      try { _queue.Dispose(); } catch { }
    }
  }
}
