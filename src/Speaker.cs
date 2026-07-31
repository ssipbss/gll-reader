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
    private DateTime _lastEnqueueAt = DateTime.MinValue;
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
      bool steady = (DateTime.Now - _lastEnqueueAt).TotalMilliseconds < 700;
      int waitMs = 0;
      if (!got && steady) waitMs = 450;
      else if (!got && _prevBatchCount >= 2) waitMs = 120;
      if (waitMs > 0) {
        Thread.Sleep(waitMs);
        got = _queue.TryTake(out tmp, 0);
      }
      if (got) items.Add(tmp);
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
      Diag("W_SPEAK " + tag + " [" + text + "]");
      try { voice.Rate = _rate; voice.Volume = _volume; } catch { }
      try { voice.Speak(text, 0); }
      catch (Exception ex) { if (Log != null) Log(tag + "_ERR:" + ex.Message); }
    }

    private static void Diag(string msg) { }

    private static void Cancel(dynamic voice) {
      try { if (voice != null) voice.Speak("", 2); } catch { }
    }

    private static void SelectVoice(dynamic voice, string desc, string lang) {
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
        try { SelectOneCoreVoice(voice, null, lang); } catch { }
      }
    }

    private void ApplyVoices(string zhDesc, string enDesc) {
      Diag("AV_BEGIN");
      try { Cancel(_zh); } catch { }
      try { Cancel(_en); } catch { }
      dynamic oldZh = _zh;
      dynamic oldEn = _en;
      try {
        Type t = Type.GetTypeFromProgID("SAPI.SpVoice");
        dynamic newZh = Activator.CreateInstance(t);
        dynamic newEn = Activator.CreateInstance(t);
        Diag("AV_SEL_ZH");
        SelectVoice(newZh, zhDesc, "Chinese");
        Diag("AV_SEL_EN");
        SelectVoice(newEn, enDesc, "English");
        Diag("AV_SWAP");
        _zh = newZh;
        _en = newEn;
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
      _lastEnqueueAt = DateTime.Now;
      _queue.Add(new WorkItem { Kind = ItemKind.SpeakZh, Text = text });
    }

    public void SpeakEn(string text) {
      if (string.IsNullOrEmpty(text)) return;
      if (Log != null) Log("EN:" + text);
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
