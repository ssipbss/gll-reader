using System;
using System.Collections.Generic;

namespace GenDaLangDu {
  public enum SpeechItemKind { SpeakZh, SpeakEn, SpeakEnWord, SpeakEnSsml, SetVoices, Cancel, Stop }

  public sealed class SpeechItem {
    public long Id;
    public SpeechItemKind Kind;
    public string Text;
    public string Ssml;
  }

  public sealed class SpeechBatchPlan {
    public string Zh = "";
    public string En = "";
    public List<KeyValuePair<string, bool>> EnWords = new List<KeyValuePair<string, bool>>();
    public List<KeyValuePair<string, string>> EnSsmls = new List<KeyValuePair<string, string>>();
    public bool Cancelled;
    public bool Stop;
    public bool SetVoices;
  }

  /// <summary>批量合并语义与 Speaker.ProcessBatchCore 一致，抽成纯函数以便测试。</summary>
  public static class SpeechBatchPlanner {
    public static SpeechBatchPlan Plan(IList<SpeechItem> items) {
      SpeechBatchPlan p = new SpeechBatchPlan();
      System.Text.StringBuilder zh = new System.Text.StringBuilder();
      System.Text.StringBuilder enPending = new System.Text.StringBuilder();
      bool cancelled = false;
      bool stop = false;
      foreach (SpeechItem it in items) {
        switch (it.Kind) {
          case SpeechItemKind.SpeakZh:
            if (!cancelled) zh.Append(it.Text);
            break;
          case SpeechItemKind.SpeakEn:
            if (!cancelled) enPending.Append(it.Text.ToUpperInvariant());
            break;
          case SpeechItemKind.SpeakEnWord:
            if (!cancelled) {
              if (enPending.Length > 0) {
                p.EnWords.Add(new KeyValuePair<string, bool>(enPending.ToString(), false));
                enPending.Clear();
              }
              p.EnWords.Add(new KeyValuePair<string, bool>(it.Text, true));
            }
            break;
          case SpeechItemKind.SpeakEnSsml:
            if (!cancelled) p.EnSsmls.Add(new KeyValuePair<string, string>(it.Text, it.Ssml));
            break;
          case SpeechItemKind.SetVoices:
            p.SetVoices = true;
            break;
          case SpeechItemKind.Cancel:
            cancelled = true;
            zh.Length = 0;
            enPending.Length = 0;
            p.EnWords.Clear();
            p.EnSsmls.Clear();
            break;
          case SpeechItemKind.Stop:
            stop = true;
            cancelled = true;
            zh.Length = 0;
            enPending.Length = 0;
            p.EnWords.Clear();
            p.EnSsmls.Clear();
            break;
        }
      }
      p.Zh = zh.ToString();
      p.En = enPending.ToString();
      p.Cancelled = cancelled;
      p.Stop = stop;
      return p;
    }
  }
}