using System;
using System.Collections.Generic;

namespace GenDaLangDu.Tests {
  public static class SpeechBatchTests {
    private static SpeechItem Zh(long id, string text) {
      return new SpeechItem { Id = id, Kind = SpeechItemKind.SpeakZh, Text = text };
    }
    private static SpeechItem En(long id, string text) {
      return new SpeechItem { Id = id, Kind = SpeechItemKind.SpeakEn, Text = text };
    }

    public static void RunAll() {
      T.Run("Batch merges Chinese", () => {
        SpeechBatchPlan p = SpeechBatchPlanner.Plan(new List<SpeechItem> {
          Zh(1, "由"), Zh(2, "于")
        });
        T.Eq(p.Zh, "由于", "zh merge");
      });
      T.Run("Batch keeps Chinese and English separately", () => {
        SpeechBatchPlan p = SpeechBatchPlanner.Plan(new List<SpeechItem> {
          Zh(1, "由"), En(2, "A"), Zh(3, "于")
        });
        T.Eq(p.Zh, "由于", "zh");
        T.Eq(p.En, "A", "en");
      });
      T.Run("Batch Cancel clears same batch", () => {
        SpeechBatchPlan p = SpeechBatchPlanner.Plan(new List<SpeechItem> {
          Zh(1, "由"),
          new SpeechItem { Id = 2, Kind = SpeechItemKind.Cancel }
        });
        T.True(p.Cancelled, "cancelled");
        T.Eq(p.Zh, "", "zh cleared");
      });
      T.Run("Batch Stop terminates and clears", () => {
        SpeechBatchPlan p = SpeechBatchPlanner.Plan(new List<SpeechItem> {
          Zh(1, "由"),
          new SpeechItem { Id = 2, Kind = SpeechItemKind.Stop }
        });
        T.True(p.Stop, "stop");
        T.Eq(p.Zh, "", "zh cleared");
      });
      T.Run("Batch EnWord flushes pending English first", () => {
        SpeechBatchPlan p = SpeechBatchPlanner.Plan(new List<SpeechItem> {
          En(1, "A"),
          new SpeechItem { Id = 2, Kind = SpeechItemKind.SpeakEnWord, Text = "Enter" }
        });
        T.Eq(p.En, "", "pending flushed");
        T.Eq(p.EnWords.Count, 2, "two words");
        T.Eq(p.EnWords[0].Key, "A", "first A");
        T.True(!p.EnWords[0].Value, "first as letters");
        T.Eq(p.EnWords[1].Key, "Enter", "second Enter");
        T.True(p.EnWords[1].Value, "second as word");
      });
      T.Run("Batch SetVoices flag", () => {
        SpeechBatchPlan p = SpeechBatchPlanner.Plan(new List<SpeechItem> {
          new SpeechItem { Id = 1, Kind = SpeechItemKind.SetVoices }
        });
        T.True(p.SetVoices, "set voices");
      });
      T.Run("Batch empty list", () => {
        SpeechBatchPlan p = SpeechBatchPlanner.Plan(new List<SpeechItem>());
        T.Eq(p.Zh, "", "empty zh");
        T.True(!p.Stop, "no stop");
      });
    }
  }
}
