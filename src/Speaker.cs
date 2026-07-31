using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace GenDaLangDu {
  public sealed class Speaker : IDisposable {
    private enum ItemKind { SpeakZh, SpeakEn, SetVoices, Cancel, Stop }

    private sealed class WorkItem {
      public ItemKind Kind;
      public string Text;
    }

    private readonly BlockingCollection<WorkItem> _queue = new BlockingCollection<WorkItem>();
    private int _prevBatchCount = 1;
    private readonly ManualResetEvent _ready = new ManualResetEvent(false);
    private Thread _thread;
    private dynamic _zh;
    private dynamic _en;
    private volatile string _zhVoice = "";
    private volatile string _enVoice = "";
    private volatile int _rate = 1;
    private volatile int _volume = 100;
    private bool _disposed;

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
      try {
        Type t = Type.GetTypeFromProgID("SAPI.SpVoice");
        if (t != null) {
          _zh = Activator.CreateInstance(t);
          _en = Activator.CreateInstance(t);
          SelectVoice(_zh, _zhVoice, "Chinese");
          SelectVoice(_en, _enVoice, "English");
        }
      } catch (Exception ex) {
        if (Log != null) Log("SPEAKER_INIT_ERR:" + ex.Message);
      }
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
      List<WorkItem> items = new List<WorkItem>();
      WorkItem tmp;
      bool got = _queue.TryTake(out tmp, 0);
      if (!got && _prevBatchCount >= 2) {
        Thread.Sleep(120);
        got = _queue.TryTake(out tmp, 0);
      }
      if (got) items.Add(tmp);
      while (_queue.TryTake(out tmp, 0)) items.Add(tmp);
      System.Text.StringBuilder zh = new System.Text.StringBuilder();
      System.Text.StringBuilder en = new System.Text.StringBuilder();
      bool cancelled = false;
      bool stop = false;

      foreach (WorkItem it in items) {
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
              SelectVoice(_zh, _zhVoice, "Chinese");
              SelectVoice(_en, _enVoice, "English");
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
        SpeakSync(_zh, zh.ToString(), "ZH");
      }
      if (en.Length > 0) {
        if (Log != null) Log("EN_MERGE [" + en + "]");
        SpeakSync(_en, en.ToString(), "EN");
      }
      if (stop) _disposed = true;
    }

    private void SpeakSync(dynamic voice, string text, string tag) {
      if (voice == null) return;
      try { voice.Rate = _rate; voice.Volume = _volume; } catch { }
      try { voice.Speak(text, 0); }
      catch (Exception ex) { if (Log != null) Log(tag + "_ERR:" + ex.Message); }
    }

    private static void Cancel(dynamic voice) {
      try { if (voice != null) voice.Speak("", 2); } catch { }
    }

    private static void SelectVoice(dynamic voice, string desc, string lang) {
      bool ok = false;
      if (!string.IsNullOrEmpty(desc)) {
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
      if (!ok && !string.IsNullOrEmpty(desc)) {
        try { ok = SelectOneCoreVoice(voice, desc, null); } catch { }
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
        try { SelectOneCoreVoice(voice, null, lang); } catch { }
      }
    }

    private static bool SelectOneCoreVoice(dynamic voice, string desc, string lang) {
      try {
        dynamic cat = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpObjectTokenCategory"));
        cat.SetId("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Speech_OneCore\\Voices", false);
        dynamic tokens = cat.EnumerateTokens();
        for (int i = 0; i < tokens.Count; i++) {
          dynamic tok = tokens.Item(i);
          string d = tok.GetDescription();
          bool match = !string.IsNullOrEmpty(desc)
            ? (string.Equals(d, desc, StringComparison.OrdinalIgnoreCase) ||
               d.StartsWith(desc, StringComparison.OrdinalIgnoreCase))
            : d.IndexOf(lang, StringComparison.OrdinalIgnoreCase) >= 0;
          if (match) {
            voice.Voice = tok;
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
          list.Add(tok.GetDescription());
        }
        Marshal.FinalReleaseComObject(v);
      } catch { }
      try {
        foreach (string name in OneCoreVoiceNames()) {
          if (!list.Contains(name)) list.Add(name);
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
      _queue.Add(new WorkItem { Kind = ItemKind.SpeakZh, Text = text });
    }

    public void SpeakEn(string text) {
      if (string.IsNullOrEmpty(text)) return;
      if (Log != null) Log("EN:" + text);
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
