using System;
using System.Windows.Forms;

namespace GenDaLangDu {
  /* MainForm 分部实现：文档差异朗读通道（UIA 抓焦点文本、差异增量朗读判定）。 */
  public partial class MainForm {
    private bool TrySpeakInserted(string ins) {
      if (string.IsNullOrEmpty(ins)) return false;
      uint pid0 = CurrentForegroundPid();
      if (_tsfBridge != null && _tsfBridge.IsComposing(pid0)) {
        DebugLog("UI_INSERT_SKIP tsf_composing");
        return false;
      }
      /* 文本里真正出现了空格（而不是按了空格键）：先于 TSF 去重保护处理，
         否则“中文刚上屏后立刻按真实空格”会被 tsf_recent 误拦 */
      if (IsPureSpaces(ins)) {
        if (HasTypingSignature()) {
          RequestKeySound(SpaceSoundPath);
          DebugLog("UI_SPACE [" + ins.Length + "]");
        } else {
          DebugLog("UI_SPACE_SKIP typing");
        }
        return true;
      }
      /* 粘贴保护：Ctrl+V/Shift+Insert 后 2 秒内的差异一律不读。
         独立计时不依赖打字痕迹（_lastPasteAt > _lastPinyinKeyAt 会被
         粘贴后的按键覆盖而失效，导致粘贴内容被差异通道朗读） */
      if ((DateTime.Now - _lastPasteAt).TotalMilliseconds < 2000) {
        DebugLog("UI_INSERT_SKIP paste");
        return false;
      }
      DateTime tsfAt0;
      if (_tsfActivePids.Contains(pid0) && _tsfCommitAt.TryGetValue(pid0, out tsfAt0)) {
        double since0 = (DateTime.Now - tsfAt0).TotalMilliseconds;
        if (since0 < 1500) {
          /* 差异一次抓到“字+空格”：字由 TSF 通道读过，空格仍要响 */
          if (ins.IndexOf(' ') >= 0 && HasTypingSignature()) {
            RequestKeySound(SpaceSoundPath);
            DebugLog("UI_SPACE_MIXED recent_tsf");
          }
          DebugLog("UI_INSERT_SKIP tsf_recent");
          return false;
        }
        if (since0 < 300000) {
          DebugLog("UI_INSERT_SKIP tsf_authoritative");
          return false;
        }
      }
      /* 鼠标切英文的直通字母：记忆状态仍是中文时，先按"候选"缓冲，
         超过4个字母且停顿后未被中文替换（拼音/五笔组字上屏）则确认英文并朗读 */
      if (!ImeEnglishNow && IsPureAsciiLetters(ins)) {
        if (TrayOnlyStateMode) {
          DebugLog("UI_INSERT_SKIP enpass_off");
          return false;
        }
        if ((DateTime.Now - _lastLetterKeyAt).TotalMilliseconds >= 2000) {
          DebugLog("UI_INSERT_SKIP nokey_letters");
          return false;
        }
        if (_enPassTracker.Note(ins, _lastUiElement)) return false;
      }
      /* 退格/删除后1秒内，若期间没有新的按键，差异不朗读（删除不会产生新增，误读的'插入'不可信）；
         若删除后用户已继续打字，则正常朗读，避免把删除后马上打出的字吞掉 */
      if ((DateTime.Now - _lastDeleteAt).TotalMilliseconds < 1000 &&
          _lastDeleteAt > _lastKeyAt) {
        DebugLog("UI_INSERT_SKIP delete");
        return false;
      }
      /* 按键通道刚读到汉字提交（VK_PACKET）时，差异通道让路，避免双读 */
      if ((DateTime.Now - _lastPacketCjkAt).TotalMilliseconds < 2000) {
        DebugLog("UI_INSERT_SKIP packet");
        return false;
      }
      string clean = StripCompositionLetters(ins);
      if (clean.Length == 0) {
        DebugLog("UI_INSERT_SKIP clean0");
        return false;
      }
      /* 末尾标点若正由按键通道延迟朗读（200ms内），从差异文本剥离，避免双读 */
      while (clean.Length > 0) {
        string pn = KeyTranslator.PunctName(clean[clean.Length - 1]);
        if (pn != null && _punctKeyPending && _lastPunctName == pn) {
          clean = clean.Substring(0, clean.Length - 1);
        } else {
          break;
        }
      }
      if (clean.Length == 0) {
        DebugLog("UI_INSERT_SKIP punct_tail");
        return false;
      }
      /* 关闭"朗读标点"时不再把标点转成名称念（保留原标点让语音自然停顿） */
      string spk = _chkPunct.Checked
        ? PunctSpokenForm(FilterForSpeech(clean))
        : FilterForSpeech(clean);
      if (string.IsNullOrEmpty(spk)) {
        DebugLog("UI_INSERT_SKIP spk");
        return false;
      }
      if (!HasChineseText(clean)) {
        DebugLog("UI_INSERT_SKIP nozh");
        return false;
      }
      /* 中文已上屏（无论朗读是否通过、是打字还是粘贴/语音输入）：
         输入法状态自愈为中文 */
      MarkChineseCommit();
      /* 因果校验：差异结果必须能用最近的按键解释（打过五笔字母+提交），
         粘贴（Ctrl+V/Shift+Insert）的内容与按键对不上，不朗读 */
      if (!HasTypingSignature()) {
        DebugLog("UI_INSERT_SKIP typing");
        return false;
      }
      if (!AllowPunctSpeak(clean)) {
        DebugLog("UI_INSERT_SKIP punct");
        return false;
      }
      if (RecentlySpoken(spk)) {
        DebugLog("UI_INSERT_SKIP recent");
        return false;
      }
      _composing = false;
      SpeakZh(spk);
      RememberSpoken(spk);
      if (ins.IndexOf(' ') >= 0) {
        /* 差异一次抓到“字+空格”时：先读字，空格提示音排在其后 */
        RequestKeySound(SpaceSoundPath);
        DebugLog("UI_SPACE_MIXED [" + ins + "]");
      }
      _lastDiffCommitText = spk;
      _lastDiffCommitAt = DateTime.Now;
      if (_tsfActivePids.Contains(pid0)) _tsfCompositionReadByDiff = true;
      MarkChineseCommit();
      DebugLog("UI_INSERT [" + ins + "]");
      return true;
    }

    private void MarkTypingKeys() {
      _lastPinyinKeyAt = DateTime.Now;
      _lastTypingCommitKeyAt = DateTime.Now;
    }

    private bool HasTypingSignature() {
      /* 必须有最近的字词输入痕迹（字母/上屏键在1秒内），粘贴等程序性插入无此痕迹 */
      bool typed = (DateTime.Now - _lastPinyinKeyAt).TotalMilliseconds < 2000 &&
                   (DateTime.Now - _lastTypingCommitKeyAt).TotalMilliseconds < 1000;
      bool pasted = _lastPasteAt > _lastPinyinKeyAt &&
                    (DateTime.Now - _lastPasteAt).TotalMilliseconds < 2000;
      return typed && !pasted;
    }

    private void CheckUiText() {
      if (!_listening) return;
      if (!_testMode && IsOurProcessForeground()) return;
      /* 前台程序未响应时，UIA 调用可能无限期挂起整个界面线程；先探测再轮询 */
      if (!IsForegroundResponsive()) {
        DebugLog("UI_SKIP_NOT_RESPONDING");
        return;
      }
      /* 空闲降频：没有按键/鼠标活动且不在组字时，文档轮询从 100ms 降到 400ms，
         有输入立即恢复满频，不影响朗读时机。 */
      bool uiActive = (DateTime.Now - _lastKeyAt).TotalMilliseconds < 1500 ||
                      (DateTime.Now - _lastMouseDownAt).TotalMilliseconds < 1500 ||
                      _composing;
      if (!uiActive) {
        if (_lastUiTextCheckAt != DateTime.MinValue &&
            (DateTime.Now - _lastUiTextCheckAt).TotalMilliseconds < 400) return;
        /* 闲置 8 秒后释放整篇文档快照，下次输入时重建基线（不会误读旧文字） */
        if (_lastUiText != null &&
            (DateTime.Now - _lastKeyAt).TotalMilliseconds > 8000 &&
            (DateTime.Now - _lastMouseDownAt).TotalMilliseconds > 8000) {
          _lastUiText = null;
          _lastCaret = -1;
          _lastCaretAbs = -1;
          _lastCaretAbsValid = false;
        }
      }
      _lastUiTextCheckAt = DateTime.Now;
      ImeState st = _ime.GetState();
      if (!st.IsChineseMode) {
        /* 不再清空基线：输入法中英文切换时文档内容没变，
           清空会导致切回中文后把旧文字当新输入重读 */
        return;
      }
      if (_uiTextTask != null && !_uiTextTask.IsCompleted) {
        /* 查询卡住超过 2 秒：放弃旧查询并允许发起新查询，
           避免差异通道被一个永不返回的 UIA 调用永久堵死 */
        if ((DateTime.Now - _uiTextQueryAt).TotalMilliseconds > 2000) {
          _uiTextTask = null;
          DebugLog("UI_QUERY_ABANDON");
        } else {
          DebugLog("UI_QUERY_BUSY");
          return;
        }
      }
      var ftask = System.Threading.Tasks.Task.Run(() => {
        var r = new FocusedTextResult();
        r.Text = TextReader.GetFocusedText(out r.ElementId, out r.UiDiag, out r.Caret, out r.CaretAbs);
        return r;
      });
      _uiTextTask = ftask;
      _uiTextQueryAt = DateTime.Now;
      /* 不阻塞界面线程：查询完成后回到界面线程再处理结果，
         界面线程不再被慢速 UIA 调用拖住（上屏通知、按键、鼠标都不再排队等它） */
      ftask.ContinueWith(t => {
        try {
          if (_uiTextTask != t) return; /* 已被新查询替代或放弃，结果作废 */
          _uiTextTask = null;
          if (t.IsFaulted || t.IsCanceled) return;
          FocusedTextResult fr = t.Result;
          if (InvokeRequired) {
            try { BeginInvoke((MethodInvoker)(() => ApplyUiTextResult(fr))); } catch { }
          } else {
            ApplyUiTextResult(fr);
          }
        } catch { }
      }, System.Threading.Tasks.TaskScheduler.Default);
    }

    private void ApplyUiTextResult(FocusedTextResult fr) {
      string t = fr.Text;
      string elementId = fr.ElementId;
      string uiDiag = fr.UiDiag;
      int caret = fr.Caret;
      bool caretAbs = fr.CaretAbs;
      if (t == null) {
        if (_lastUiElement != elementId) {
          _lastUiElement = elementId;
          DebugLog("UI_TEXT_NULL [" + (elementId ?? "") + "] " + (uiDiag ?? ""));
        }
        return;
      }
      if (t.Length == 0 && _lastUiElement != elementId) {
        _lastUiElement = elementId;
        DebugLog("UI_TEXT_EMPTY [" + (elementId ?? "") + "] " + (uiDiag ?? ""));
        return;
      }
      if (_lastUiElement != elementId) {
        _enPassTracker.Cancel("element");
        string prevText = _lastUiText;
        _lastUiElement = elementId;
        _lastUiText = t;
        _lastCaret = caret;
        _lastCaretAbs = caret;
        _lastCaretAbsValid = caretAbs;
        DebugLog("UI_ELEMENT [" + (elementId ?? "") + "]");
        if (prevText != null && t != null && t.Length > prevText.Length && t.StartsWith(prevText)) {
          string ins = ComputeInsertedSmart(prevText, t, caret, caretAbs);
          DebugLog("UI_ELEMENT_DIFF [" + ins + "]");
          if (ins.Length <= 20 && ins.Trim().Length > 0) {
            if (ins.Length > MaxUiDiffLen) {
              DebugLog("UI_DIFF_SKIP_LONG [" + ins + "]");
            } else {
              if (_lastMouseDownAt > _lastKeyAt) {
                _lastMouseDownAt = DateTime.MinValue;
                DebugLog("UI_CLICK_IGNORED [" + ins + "]");
                return;
              }
              TrySpeakInserted(ins);
            }
          }
        }
        return;
      }
      if (_composing) {
        if (_lastUiText != null && t != _lastUiText) {
          string ins = ComputeInserted(_lastUiText, t, caret);
          int delta = t.Length - _lastUiText.Length;
          _lastUiText = t;
          _lastCaret = caret;
          _lastCaretAbs = caret;
          _lastCaretAbsValid = caretAbs;
          if (!string.IsNullOrEmpty(ins) && ins.Length <= 20 && ins.Trim().Length > 0) {
            DebugLog("UI_DIFF [" + ins + "] caret=" + caret + " delta=" + delta);
            if (ins.Length > MaxUiDiffLen) {
              DebugLog("UI_DIFF_SKIP_LONG [" + ins + "]");
            } else {
              if (TrySpeakInserted(ins)) return;
            }
          }
        }
        if ((DateTime.Now - _lastPinyinKeyAt).TotalMilliseconds > 2000) {
          _composing = false;
          _lastUiText = t;
          _lastCaret = caret;
          _lastCaretAbs = caret;
          _lastCaretAbsValid = caretAbs;
        }
        return;
      }
      if (_lastUiText == null) {
        _lastUiText = t;
        _lastUiElement = elementId;
        _lastCaret = caret;
        _lastCaretAbs = caret;
        _lastCaretAbsValid = caretAbs;
        return;
      }
      if (t == _lastUiText) return;
      string inserted = ComputeInsertedSmart(_lastUiText, t, caret, caretAbs);
      int deltaLen = t.Length - _lastUiText.Length;
      _lastUiText = t;
      _lastCaret = caret;
      _lastCaretAbs = caret;
      _lastCaretAbsValid = caretAbs;
      if (string.IsNullOrEmpty(inserted)) return;
      DebugLog("UI_DIFF [" + inserted + "] caret=" + caret + " delta=" + deltaLen);
      if (_lastMouseDownAt > _lastKeyAt) {
        _lastMouseDownAt = DateTime.MinValue;
        DebugLog("UI_CLICK_IGNORED [" + inserted + "]");
        return;
      }
      if (inserted.Length > 20) return;
      if (inserted.Trim().Length == 0 && inserted.IndexOf(' ') < 0) return;
      if (inserted.Length > MaxUiDiffLen) {
        DebugLog("UI_DIFF_SKIP_LONG [" + inserted + "]");
        return;
      }
      TrySpeakInserted(inserted);
    }

    /// <summary>
    /// 智能差异：优先用“旧光标→新光标区间”直读刚输入的内容
    /// （文档不足5000字时UIA光标即绝对位置，键盘事件仅做触发辅助），
    /// 失败再回退窗口差异。
    /// </summary>
    private string ComputeInsertedSmart(string oldT, string newT, int caret, bool caretAbs) {
      if (caretAbs && _lastCaretAbsValid && caret >= _lastCaretAbs) {
        int delta = caret - _lastCaretAbs;
        if (delta > 0 && delta <= 12 && _lastCaretAbs >= 0 &&
            _lastCaretAbs <= oldT.Length && caret <= newT.Length) {
          bool sanity = _lastCaretAbs == 0 ||
                        (oldT[_lastCaretAbs - 1] == newT[_lastCaretAbs - 1]);
          if (sanity) {
            string d = newT.Substring(_lastCaretAbs, delta);
            d = LastLine(d);
            d = StripHeading(d);
            return d;
          }
        }
      }
      return SpeechText.ComputeInserted(oldT, newT, caret);
    }

    private sealed class FocusedTextResult {
      public string Text;
      public string ElementId;
      public string UiDiag;
      public int Caret;
      public bool CaretAbs;
    }
  }
}
