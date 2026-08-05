using System;

namespace GenDaLangDu.Tests {
  public static class TextDiffTests {
    public static void RunAll() {
      T.Run("DiffInserted append", () => {
        T.Eq(SpeechText.DiffInserted("你好世界", "你好世界啊"), "啊", "append");
      });
      T.Run("DiffInserted middle", () => {
        T.Eq(SpeechText.DiffInserted("你好世界", "你好abc世界"), "abc", "middle");
      });
      T.Run("DiffInserted delete", () => {
        T.Eq(SpeechText.DiffInserted("你好世界", "你好"), "", "delete");
      });
      T.Run("DiffInserted empty old", () => {
        T.Eq(SpeechText.DiffInserted("", "abc"), "abc", "empty old");
      });
      T.Run("DiffInserted empty new", () => {
        T.Eq(SpeechText.DiffInserted("abc", ""), "", "empty new");
      });
      T.Run("DiffInserted same", () => {
        T.Eq(SpeechText.DiffInserted("abc", "abc"), "", "same");
      });

      T.Run("ComputeInserted tail", () => {
        T.Eq(SpeechText.ComputeInserted("你好世界", "你好世界啊", -1), "啊", "tail");
      });
      T.Run("ComputeInserted middle", () => {
        T.Eq(SpeechText.ComputeInserted("你好世界", "你好abc世界", -1), "abc", "middle");
      });
      T.Run("ComputeInserted caret", () => {
        T.Eq(SpeechText.ComputeInserted("你好世界", "你好啊世界", 3), "啊", "caret path");
      });

      T.Run("TryCaretDiff ok", () => {
        string d;
        T.True(SpeechText.TryCaretDiff("你好世界", "你好啊世界", 3, out d), "try ok");
        T.Eq(d, "啊", "caret diff");
      });
      T.Run("TryCaretDiff bad caret", () => {
        string d;
        T.True(!SpeechText.TryCaretDiff("你好世界", "你好啊世界", 0, out d), "bad caret false");
      });

      T.Run("ShiftDiff scroll", () => {
        T.Eq(SpeechText.ShiftDiff("你好世界", "世界啊"), "啊", "window shift");
      });

      T.Run("LastLine", () => {
        T.Eq(SpeechText.LastLine("第一行\n第二行"), "第二行", "last line");
      });
      T.Run("LastLine single", () => {
        T.Eq(SpeechText.LastLine("只有一行"), "只有一行", "single line");
      });

      T.Run("StripHeading", () => {
        T.Eq(SpeechText.StripHeading("第一章 你好世界"), "你好世界", "chapter heading");
      });
      T.Run("StripHeading no heading", () => {
        T.Eq(SpeechText.StripHeading("你好世界"), "你好世界", "no heading");
      });

      T.Run("FilterForSpeech drops letters and quotes", () => {
        T.Eq(SpeechText.FilterForSpeech("告诉q我'\"，"), "告诉我，", "filter");
      });
      T.Run("StripCompositionLetters", () => {
        T.Eq(SpeechText.StripCompositionLetters("告诉q我"), "告诉我", "strip letters");
        T.Eq(SpeechText.StripCompositionLetters("abc"), "abc", "pure letters unchanged");
      });

      T.Run("IsPureSpaces", () => {
        T.True(SpeechText.IsPureSpaces("   "), "spaces true");
        T.True(!SpeechText.IsPureSpaces("  a"), "mixed false");
        T.True(!SpeechText.IsPureSpaces(""), "empty false");
      });
      T.Run("IsPureAsciiLetters", () => {
        T.True(SpeechText.IsPureAsciiLetters("abc"), "letters true");
        T.True(!SpeechText.IsPureAsciiLetters("abc3"), "digit false");
        T.True(!SpeechText.IsPureAsciiLetters(""), "empty false");
      });

      T.Run("HasCjk", () => {
        T.True(SpeechText.HasCjk("abc你"), "cjk true");
        T.True(!SpeechText.HasCjk("abc"), "ascii false");
      });
      T.Run("HasChineseText", () => {
        T.True(SpeechText.HasChineseText("，"), "fullwidth punct true");
        T.True(!SpeechText.HasChineseText("abc"), "ascii false");
      });
      T.Run("ContainsImeSpeakable", () => {
        T.True(SpeechText.ContainsImeSpeakable("，"), "punct true");
        T.True(!SpeechText.ContainsImeSpeakable("abc"), "ascii false");
      });
      T.Run("PunctSpokenForm", () => {
        string res = SpeechText.PunctSpokenForm("，。");
        T.True(res.Contains("逗号"), "contains 逗号: " + res);
        T.True(res.Contains("句号"), "contains 句号: " + res);
      });
    }
  }
}
