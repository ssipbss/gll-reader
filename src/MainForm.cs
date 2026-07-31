using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace GenDaLangDu {
  public class MainForm : Form {
    private KeyboardHook _hook = new KeyboardHook();
    private MouseHook _mouseHook = new MouseHook();
    private ImeMonitor _ime = new ImeMonitor();
    private Speaker _speaker;
    private System.Windows.Forms.Timer _imeTimer;
    private System.Windows.Forms.Timer _autoExitTimer;
    private System.Windows.Forms.Timer _injectTimer;
    private System.Windows.Forms.Timer _uiTimer;
    private NotifyIcon _tray;
    private ContextMenuStrip _trayMenu;
    private AppSettings _settings = new AppSettings();

    private bool _listening;
    private bool _testMode;
    private string _testLog = null;
    private int _exitMs = 6000;
    private string _lastResult = "";
    private string _lastUiText = null;
    private string _lastUiElement = null;
    private string _lastSpoken = "";
    private DateTime _lastPinyinKeyAt = DateTime.MinValue;
    private DateTime _lastChineseCommitAt = DateTime.MinValue;
    private IntPtr _lastChineseCommitHwnd = IntPtr.Zero;
    private bool _composing;
    private bool _imeEnglishMode;
    private DateTime _lastMouseDownAt = DateTime.MinValue;
    private DateTime _lastKeyAt = DateTime.MinValue;
    private DateTime _lastSpokenAt = DateTime.MinValue;
    private bool _closingByTrayExit;
    private bool _loading;
    private bool _forceDebug;

    private RoundedButton _btnToggle;
    private StatusPill _statusPill;
    private ComboBox _cboZh;
    private ComboBox _cboEn;
    private ModernSlider _trkRate;
    private Label _lblRateVal;
    private ModernSlider _trkVol;
    private Label _lblVolVal;
    private CheckBox _chkLetters;
    private CheckBox _chkDigits;
    private CheckBox _chkPunct;
    private CheckBox _chkFunc;
    private CheckBox _chkModifiers;
    private CheckBox _chkDebug;
    private CheckBox _chkClickSpeak;

    public MainForm(string[] args) {
      ParseArgs(args);
      LogTest("CTOR_BEGIN");
      try {
        _speaker = new Speaker();
        LogTest("SPEAKER_OK");
      } catch (Exception ex) {
        LogTest("SPEAKER_FAIL: " + ex);
        throw;
      }
      BuildUi();
      BuildTray();
      LoadSettings();
      LogTest("SETTINGS_LOADED path=" + SettingsPath() + " rate=" + _settings.Rate + " debug=" + _settings.DebugLog + " click=" + _settings.ClickSpeak);
      if (_forceDebug) _settings.DebugLog = true;
      _speaker.Log = DebugLog;
      _speaker.RefreshVoices(CurrentZhVoice(), CurrentEnVoice());
      _speaker.WarmUp();
      LogTest("VOICES=" + string.Join("|", _speaker.GetVoices().ToArray()));
      KeyboardHook.DebugLog = delegate(string line) { LogTest(line); };
      ApplyRateVolume();

      _imeTimer = new System.Windows.Forms.Timer();
      _imeTimer.Interval = 25;
      _imeTimer.Tick += delegate { CheckIme(); _speaker.FlushAll(); };

      _uiTimer = new System.Windows.Forms.Timer();
      _uiTimer.Interval = 100;
      _uiTimer.Tick += delegate { CheckUiText(); };

      if (_testMode) {
        ShowInTaskbar = false;
        Enabled = false;
        StartListening();
        _injectTimer = new System.Windows.Forms.Timer();
        _injectTimer.Interval = 1200;
        _injectTimer.Tick += delegate {
          _injectTimer.Stop();
          SimulateKey(0x41);
          SimulateKey(0x42);
          SimulateKey(0x43);
          SimulateKey(0x31);
          SimulateKey(0x32);
          SimulateKey(0x33);
          SimulateKey(0xBE);
          SimulateKey(0xBC);
          SimulateKey(0xBA);
          SimulateKey(0x20);
          SimulateKey(0x0D);
          SimulateKey(0x09);
          SimulateKey(0x70);
          SimulateKey(0x08);
          SimulateKey(0x2E);
          SimulateKey(0x25);
          LogTest("SIMULATE_DONE");
        };
        _injectTimer.Start();
        _autoExitTimer = new System.Windows.Forms.Timer();
        _autoExitTimer.Interval = _exitMs;
        _autoExitTimer.Tick += delegate { _closingByTrayExit = true; Close(); };
        _autoExitTimer.Start();
      } else {
        StartListening();
      }
      LogTest("STARTED hook=" + _hook.IsInstalled + " listening=" + _listening);
    }

    private void LogTest(string line) {
      if (!_testMode || _testLog == null) return;
      try { File.AppendAllText(_testLog, DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + "\r\n", new System.Text.UTF8Encoding(false)); } catch { }
    }

    private void DebugLog(string line) {
      if (_testLog != null) {
        try {
          string dir = Path.GetDirectoryName(_testLog);
          if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
          File.AppendAllText(_testLog, DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + "\r\n", new System.Text.UTF8Encoding(false));
        } catch { }
        return;
      }
      if (_settings == null || !_settings.DebugLog) return;
      try {
        File.AppendAllText(Path.Combine(Application.StartupPath, "debug.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + "\r\n", new System.Text.UTF8Encoding(false));
      } catch { }
    }


    private void SimulateKey(uint vk) {
      ushort scan = InputSender.ScanOf((ushort)vk);
      OnKey(this, new KeyHookEventArgs { Vk = vk, Scan = scan, IsUp = false, IsSysKey = false });
    }

    private void ParseArgs(string[] args) {
      for (int i = 0; i < args.Length; i++) {
        if (args[i] == "--test" && i + 1 < args.Length) {
          _testMode = true;
          _testLog = Path.GetFullPath(args[i + 1]);
          i++;
        } else if (args[i] == "--debuglog") {
          _forceDebug = true;
        } else if (args[i] == "--exit-ms" && i + 1 < args.Length) {
          int v;
          if (int.TryParse(args[i + 1], out v)) _exitMs = v;
          i++;
        }
      }
    }

    private void BuildUi() {
      Text = "归零归零";
      Font = CreateFont();
      ClientSize = new Size(500, 600);
      FormBorderStyle = FormBorderStyle.FixedSingle;
      MaximizeBox = false;
      StartPosition = FormStartPosition.CenterScreen;
      Icon = GetAppIcon();
      BackColor = UiColors.PageBg;
      Panel header = new Panel();
      header.Dock = DockStyle.Top;
      header.Height = 84;
      header.BackColor = Color.White;

      PictureBox pbIcon = new PictureBox();
      pbIcon.Location = new Point(20, 18);
      pbIcon.Size = new Size(48, 48);
      pbIcon.SizeMode = PictureBoxSizeMode.Zoom;
      pbIcon.BackColor = Color.Transparent;
      try { pbIcon.Image = new Bitmap(GetAppIcon().ToBitmap(), 48, 48); } catch { }

      Label lblTitle = new Label();
      lblTitle.Text = "归零归零";
      lblTitle.Location = new Point(82, 16);
      lblTitle.AutoSize = true;
      lblTitle.Font = new Font(Font.FontFamily, 18F, FontStyle.Bold);
      lblTitle.ForeColor = UiColors.TextDark;

      Label lblSub = new Label();
      lblSub.Text = "打字朗读 · 每个按键清晰可闻";
      lblSub.Location = new Point(84, 50);
      lblSub.AutoSize = true;
      lblSub.Font = new Font(Font.FontFamily, 9F);
      lblSub.ForeColor = UiColors.TextGray;

      _btnToggle = new RoundedButton();
      _btnToggle.Text = "开始监听";
      _btnToggle.Location = new Point(336, 16);
      _btnToggle.Size = new Size(144, 40);
      _btnToggle.Font = new Font(Font.FontFamily, 10F, FontStyle.Bold);
      _btnToggle.ForeColor = Color.White;
      _btnToggle.BgFill = Color.White;
      _btnToggle.FillColor = UiColors.Accent;
      _btnToggle.HoverColor = UiColors.AccentHover;
      _btnToggle.DownColor = UiColors.AccentDown;
      _btnToggle.Click += delegate { ToggleListening(); };

      _statusPill = new StatusPill();
      _statusPill.Location = new Point(336, 62);
      _statusPill.Size = new Size(144, 20);
      _statusPill.Font = new Font(Font.FontFamily, 8.5F, FontStyle.Bold);

      Panel border = new Panel();
      border.Location = new Point(0, 83);
      border.Size = new Size(500, 1);
      border.BackColor = UiColors.CardBorder;

      header.Controls.AddRange(new Control[] { pbIcon, lblTitle, lblSub, _btnToggle, _statusPill, border });


      CardPanel cardVoice = new CardPanel();
      cardVoice.Location = new Point(16, 104);
      cardVoice.Size = new Size(468, 172);

      Label t1 = new Label();
      t1.Text = "声音";
      t1.Location = new Point(20, 12);
      t1.AutoSize = true;
      t1.Font = new Font(Font.FontFamily, 10.5F, FontStyle.Bold);
      t1.ForeColor = UiColors.TextDark;

      Label l1 = new Label();
      l1.Text = "中文语音";
      l1.Location = new Point(20, 46);
      l1.AutoSize = true;
      l1.ForeColor = UiColors.TextGray;
      _cboZh = new ComboBox();
      _cboZh.DropDownStyle = ComboBoxStyle.DropDownList;
      _cboZh.FlatStyle = FlatStyle.Flat;
      _cboZh.Location = new Point(96, 42);
      _cboZh.Width = 330;
      _cboZh.DropDownWidth = 430;

      Label l2 = new Label();
      l2.Text = "字母语音";
      l2.Location = new Point(20, 78);
      l2.AutoSize = true;
      l2.ForeColor = UiColors.TextGray;
      _cboEn = new ComboBox();
      _cboEn.DropDownStyle = ComboBoxStyle.DropDownList;
      _cboEn.FlatStyle = FlatStyle.Flat;
      _cboEn.Location = new Point(96, 74);
      _cboEn.Width = 330;
      _cboEn.DropDownWidth = 430;

      Label l3 = new Label();
      l3.Text = "语速";
      l3.Location = new Point(20, 112);
      l3.AutoSize = true;
      l3.ForeColor = UiColors.TextGray;
      _trkRate = new ModernSlider();
      _trkRate.Location = new Point(86, 106);
      _trkRate.Size = new Size(232, 24);
      _trkRate.Minimum = -5;
      _trkRate.Maximum = 5;
      _trkRate.Value = 1;
      _lblRateVal = new Label();
      _lblRateVal.Location = new Point(328, 106);
      _lblRateVal.AutoSize = true;
      _lblRateVal.ForeColor = UiColors.TextDark;
      _trkRate.ValueChanged += delegate {
        _lblRateVal.Text = _trkRate.Value.ToString();
        ApplyRateVolume();
        if (!_loading) SaveSettings();
      };

      Label l4 = new Label();
      l4.Text = "音量";
      l4.Location = new Point(20, 142);
      l4.AutoSize = true;
      l4.ForeColor = UiColors.TextGray;
      _trkVol = new ModernSlider();
      _trkVol.Location = new Point(86, 136);
      _trkVol.Size = new Size(232, 24);
      _trkVol.Minimum = 0;
      _trkVol.Maximum = 100;
      _trkVol.Value = 100;
      _lblVolVal = new Label();
      _lblVolVal.Location = new Point(328, 136);
      _lblVolVal.AutoSize = true;
      _lblVolVal.ForeColor = UiColors.TextDark;
      _trkVol.ValueChanged += delegate {
        _lblVolVal.Text = _trkVol.Value.ToString();
        ApplyRateVolume();
        if (!_loading) SaveSettings();
      };

      cardVoice.Controls.AddRange(new Control[] {
        t1, l1, _cboZh, l2, _cboEn, l3, _trkRate, _lblRateVal, l4, _trkVol, _lblVolVal
      });

      CardPanel cardOpt = new CardPanel();
      cardOpt.Location = new Point(16, 288);
      cardOpt.Size = new Size(468, 194);

      Label t2 = new Label();
      t2.Text = "朗读选项";
      t2.Location = new Point(20, 12);
      t2.AutoSize = true;
      t2.Font = new Font(Font.FontFamily, 10.5F, FontStyle.Bold);
      t2.ForeColor = UiColors.TextDark;

      _chkLetters = MakeCheck("朗读字母（A-Z）", new Point(20, 44));
      _chkDigits = MakeCheck("朗读数字（1→一）", new Point(20, 74));
      _chkPunct = MakeCheck("朗读标点符号", new Point(20, 104));
      _chkFunc = MakeCheck("朗读功能键", new Point(20, 134));
      _chkModifiers = MakeCheck("朗读修饰键", new Point(252, 44));
      _chkClickSpeak = MakeCheck("点击文字时朗读", new Point(252, 74));
      _chkClickSpeak.Checked = false;
      _chkDebug = MakeCheck("记录调试日志", new Point(252, 104));
      _chkDebug.Checked = false;

      cardOpt.Controls.AddRange(new Control[] {
        t2, _chkLetters, _chkDigits, _chkPunct, _chkFunc, _chkModifiers, _chkClickSpeak, _chkDebug
      });

      RoundedButton btnTestZh = new RoundedButton();
      btnTestZh.Text = "试听中文";
      btnTestZh.Location = new Point(16, 498);
      btnTestZh.Size = new Size(110, 36);
      btnTestZh.BgFill = UiColors.PageBg;
      btnTestZh.FillColor = Color.White;
      btnTestZh.HoverColor = Color.FromArgb(241, 245, 249);
      btnTestZh.DownColor = Color.FromArgb(226, 232, 240);
      btnTestZh.BorderColor = Color.FromArgb(203, 213, 225);
      btnTestZh.ForeColor = Color.FromArgb(51, 65, 85);
      btnTestZh.Click += delegate { _speaker.SpeakZh("欢迎使用归零归零"); };

      RoundedButton btnTestEn = new RoundedButton();
      btnTestEn.Text = "试听字母";
      btnTestEn.Location = new Point(138, 498);
      btnTestEn.Size = new Size(110, 36);
      btnTestEn.BgFill = UiColors.PageBg;
      btnTestEn.FillColor = Color.White;
      btnTestEn.HoverColor = Color.FromArgb(241, 245, 249);
      btnTestEn.DownColor = Color.FromArgb(226, 232, 240);
      btnTestEn.BorderColor = Color.FromArgb(203, 213, 225);
      btnTestEn.ForeColor = Color.FromArgb(51, 65, 85);
      btnTestEn.Click += delegate { _speaker.SpeakEn("A B C"); };

      RoundedButton btnTestFunc = new RoundedButton();
      btnTestFunc.Text = "试听按键";
      btnTestFunc.Location = new Point(260, 498);
      btnTestFunc.Size = new Size(110, 36);
      btnTestFunc.BgFill = UiColors.PageBg;
      btnTestFunc.FillColor = Color.White;
      btnTestFunc.HoverColor = Color.FromArgb(241, 245, 249);
      btnTestFunc.DownColor = Color.FromArgb(226, 232, 240);
      btnTestFunc.BorderColor = Color.FromArgb(203, 213, 225);
      btnTestFunc.ForeColor = Color.FromArgb(51, 65, 85);
      btnTestFunc.Click += delegate { _speaker.SpeakZh("回车，退格，空格，F1"); };

      Label hint = new Label();
      hint.Location = new Point(20, 546);
      hint.Size = new Size(460, 44);
      hint.ForeColor = UiColors.TextGray;
      hint.Font = new Font(Font.FontFamily, 8.5F);
      hint.Text = "提示：启动后自动监听；中文输入法组字上屏后自动读出中文。\r\n若以“管理员身份”运行的软件不出声，请以管理员身份运行本软件。";
      hint.TextAlign = ContentAlignment.TopLeft;

      Controls.AddRange(new Control[] {
        header, cardVoice, cardOpt, btnTestZh, btnTestEn, btnTestFunc, hint
      });
    }

    private ModernCheckBox MakeCheck(string text, Point location) {
      ModernCheckBox c = new ModernCheckBox();
      c.Text = text;
      c.Location = location;
      c.Width = 190;
      c.Checked = true;
      c.CheckedChanged += delegate { if (!_loading) SaveSettings(); };
      return c;
    }
    private void BuildTray() {
      _trayMenu = new ContextMenuStrip();
      _trayMenu.Items.Add("显示主界面", null, delegate { ShowWindow(); });
      _trayMenu.Items.Add("开始监听", null, delegate { StartListening(); });
      _trayMenu.Items.Add("暂停监听", null, delegate { StopListening(); });
      _trayMenu.Items.Add(new ToolStripSeparator());
      _trayMenu.Items.Add("退出", null, delegate { _closingByTrayExit = true; Close(); });

      _tray = new NotifyIcon();
      _tray.Icon = GetAppIcon();
      _tray.Text = "归零归零";
      _tray.ContextMenuStrip = _trayMenu;
      _tray.DoubleClick += delegate { ShowWindow(); };
      _tray.Visible = !_testMode;
    }

    private void ShowWindow() {
      Show();
      WindowState = FormWindowState.Normal;
      Opacity = 1;
      Enabled = true;
      BringToFront();
      Activate();
    }

    private void ToggleListening() {
      if (_listening) StopListening();
      else StartListening();
    }

    private void StartListening() {
      if (_listening) return;
      try {
        _hook.Install();
      } catch (Exception ex) {
        if (!_testMode) MessageBox.Show(this, "启动监听失败：" + ex.Message, "归零归零");
        return;
      }
      _listening = _hook.IsInstalled;
      if (_listening) {
        _mouseHook.Install();
        _lastResult = "";
        _lastUiText = null;
        _imeTimer.Start();
        _uiTimer.Start();
      }
      UpdateUi();
    }

    private void StopListening() {
      if (!_listening) return;
      _listening = false;
      _hook.Uninstall();
      _mouseHook.Uninstall();
      _imeTimer.Stop();
      _uiTimer.Stop();
      _speaker.Stop();
      UpdateUi();
    }

    private void UpdateUi() {
      if (_btnToggle == null) return;
      _btnToggle.Text = _listening ? "暂停监听" : "开始监听";
      _statusPill.Active = _listening;
      _statusPill.Invalidate();
    }

    private void OnKey(object sender, KeyHookEventArgs e) {
      if (!_listening) return;
      if (!_testMode && IsOurProcessForeground()) return;
      if (e.IsUp) return;
      if (e.IsAutoRepeat) return;
      _lastKeyAt = DateTime.Now;
      DebugLog("KEY vk=0x" + e.Vk.ToString("X") + " scan=0x" + e.Scan.ToString("X"));

      ImeState ime = _ime.GetState();
      bool composing = ime.IsComposing;
      bool chineseMode = ime.IsChineseMode || RecentChineseActive();
      if (!chineseMode) {
        _composing = false;
        _lastPinyinKeyAt = DateTime.MinValue;
      }
      DebugLog("IME hkl=0x" + ime.Hkl.ToString("X") + " imc=" + ime.HasImc + " open=" + ime.IsOpen + " conv=" + ime.ConversionMode +
               " zhLayout=" + ime.IsChineseLayout + " comp=[" + ime.Composition + "] result=[" + ime.Result + "]");

      if (chineseMode) {
        if (e.Vk == 0x20) {
          if (_composing) {
            _composing = false;
            MarkChineseCommit();
            ScheduleImeCheck();
            return;
          }
        } else if (e.Vk == 0x08 && _composing) {
          ScheduleImeCheck();
          return;
        } else if (_composing && IsCandidateControl(e.Vk)) {
          MarkChineseCommit();
          ScheduleImeCheck();
          return;
        }
        if (e.Vk == 0x10) {
          _imeEnglishMode = !_imeEnglishMode;
          if (_imeEnglishMode) _composing = false;
        }
        if (e.Vk == 0x0D || e.Vk == 0x1B) _composing = false;
      }

      string keyName = KeyTranslator.GetKeyName(e.Vk);
      if (keyName != null) {
        if (IsModifierKey(e.Vk)) {
          if (_chkModifiers.Checked && !chineseMode) SpeakZh(keyName);
        } else if (_chkFunc.Checked) {
          if (e.Vk >= 0x70 && e.Vk <= 0x87) _speaker.SpeakEn("F" + (e.Vk - 0x70 + 1).ToString());
          else SpeakZh(keyName);
        }
        ScheduleImeCheck();
        return;
      }

      if (chineseMode && e.Vk >= 0x41 && e.Vk <= 0x5A && !ShiftOrCaps() && !_imeEnglishMode) {
        _composing = true;
        _lastPinyinKeyAt = DateTime.Now;
      }

      string chars = KeyTranslator.GetChars(e.Vk, e.Scan);
      DebugLog("CHARS vk=0x" + e.Vk.ToString("X") + " [" + chars + "]");
      if (!string.IsNullOrEmpty(chars)) {
        foreach (char c in chars) {
          if (KeyTranslator.IsLatinLetter(c)) {
            if ((chineseMode || _composing) && !ShiftOrCaps() && !_imeEnglishMode) {
              _composing = true;
              _lastPinyinKeyAt = DateTime.Now;
            } else if (_chkLetters.Checked) {
              _speaker.SpeakEn(KeyTranslator.NormalizeLatin(c).ToString().ToLowerInvariant());
            }
            continue;
          }
          if (c == ' ') {
            if (!chineseMode && _chkFunc.Checked) SpeakZh("空格");
            continue;
          }
          if (char.IsDigit(c)) {
            if (!_composing && _chkDigits.Checked) SpeakZh(KeyTranslator.DigitToChinese(c));
            continue;
          }
          if (KeyTranslator.IsCjk(c)) {
            _composing = false;
            SpeakZh(c.ToString());
            RememberSpoken(c.ToString());
            MarkChineseCommit();
            continue;
          }
          string pn = KeyTranslator.PunctName(c);
          if (pn != null) {
            if (chineseMode) {
              _composing = false;
            } else if (_chkPunct.Checked) {
              SpeakZh(pn);
              RememberSpoken(pn);
            }
            continue;
          }
          if (char.IsWhiteSpace(c)) {
            if (!chineseMode && _chkFunc.Checked) SpeakZh("空格");
            continue;
          }
        }
      }
      ScheduleImeCheck();
    }

    private void ScheduleImeCheck() {
      if (!_listening) return;
      try { BeginInvoke((MethodInvoker)CheckIme); } catch { }
      try { BeginInvoke((MethodInvoker)CheckUiText); } catch { }
    }

    private void CheckIme() {
      if (!_listening) return;
      if (!_testMode && IsOurProcessForeground()) return;
      ImeState st = _ime.GetState();
      if (st.HasImc && !string.IsNullOrEmpty(st.Result)) {
        string r = st.Result;
        if (r != _lastResult) {
          string delta;
          if (_lastResult.Length == 0 || r.StartsWith(_lastResult)) delta = r.Substring(_lastResult.Length);
          else delta = r;
          _lastResult = r;
          if (!string.IsNullOrEmpty(delta) && !RecentlySpoken(delta)) {
            SpeakZh(delta);
            RememberSpoken(delta);
            DebugLog("IME_RESULT [" + delta + "]");
          }
        }
      } else {
        _lastResult = "";
      }
    }

    private void SpeakZh(string text) {
      if (_speaker != null) _speaker.SpeakZh(text);
    }

    private bool RecentlySpoken(string text) {
      if (_lastSpoken != text) return false;
      return (DateTime.Now - _lastSpokenAt).TotalMilliseconds < 400;
    }

    private void RememberSpoken(string text) {
      _lastSpoken = text;
      _lastSpokenAt = DateTime.Now;
    }

    private void CheckUiText() {
      if (!_listening) return;
      if (!_testMode && IsOurProcessForeground()) return;
      ImeState st = _ime.GetState();
      if (!st.IsChineseMode) {
        _lastUiText = null;
        _lastUiElement = null;
        return;
      }
      string elementId;
      string t = TextReader.GetFocusedText(out elementId);
      if (t == null) return;
      if (_lastUiElement != elementId) {
        _lastUiElement = elementId;
        _lastUiText = t;
        DebugLog("UI_ELEMENT [" + (elementId ?? "") + "]");
        return;
      }
      if (_composing) {
        if ((DateTime.Now - _lastPinyinKeyAt).TotalMilliseconds > 2000) {
          _composing = false;
          _lastUiText = t;
        }
        return;
      }
      if (_lastUiText == null) {
        _lastUiText = t;
        _lastUiElement = elementId;
        return;
      }
      if (t == _lastUiText) return;
      string inserted = DiffInserted(_lastUiText, t);
      _lastUiText = t;
      if (string.IsNullOrEmpty(inserted)) return;
      DebugLog("UI_DIFF [" + inserted + "]");
      if (!_chkClickSpeak.Checked && _lastMouseDownAt > _lastKeyAt) {
        _lastMouseDownAt = DateTime.MinValue;
        DebugLog("UI_CLICK_IGNORED [" + inserted + "]");
        return;
      }
      if (inserted.Length > 20) return;
      if (inserted.Trim().Length == 0) return;
      string speakText = PunctSpokenForm(FilterForSpeech(inserted));
      if (string.IsNullOrEmpty(speakText)) return;
      if (RecentlySpoken(speakText)) return;
      SpeakZh(speakText);
      RememberSpoken(speakText);
      MarkChineseCommit();
      DebugLog("UI_INSERT [" + inserted + "]");
    }

    private static string FilterForSpeech(string s) {
      System.Text.StringBuilder sb = new System.Text.StringBuilder();
      foreach (char c in s) {
        bool fullWidth = c >= 0xFF00 && c <= 0xFFEF &&
          !(c >= 0xFF10 && c <= 0xFF19) &&
          !(c >= 0xFF21 && c <= 0xFF3A) &&
          !(c >= 0xFF41 && c <= 0xFF5A);
        if (KeyTranslator.IsCjk(c) || (c >= 0x3000 && c <= 0x9FFF) ||
            fullWidth || KeyTranslator.PunctName(c) != null) {
          sb.Append(c);
        }
      }
      return sb.ToString();
    }

    private static bool HasCjk(string s) {
      if (string.IsNullOrEmpty(s)) return false;
      foreach (char c in s) {
        if (KeyTranslator.IsCjk(c)) return true;
      }
      return false;
    }
    private static bool ContainsImeSpeakable(string s) {
      foreach (char c in s) {
        bool fullWidth = c >= 0xFF00 && c <= 0xFFEF &&
          !(c >= 0xFF10 && c <= 0xFF19) &&
          !(c >= 0xFF21 && c <= 0xFF3A) &&
          !(c >= 0xFF41 && c <= 0xFF5A);
        if (KeyTranslator.IsCjk(c) || (c >= 0x3000 && c <= 0x9FFF) ||
            fullWidth || KeyTranslator.PunctName(c) != null) return true;
      }
      return false;
    }
    private static string PunctSpokenForm(string s) {
      bool hasCjk = false;
      foreach (char c in s) {
        if (KeyTranslator.IsCjk(c)) { hasCjk = true; break; }
      }
      if (hasCjk) return s;
      System.Text.StringBuilder sb = new System.Text.StringBuilder();
      foreach (char c in s) {
        string n = KeyTranslator.PunctName(c);
        if (n != null) {
          if (sb.Length > 0) sb.Append("，");
          sb.Append(n);
        } else {
          sb.Append(c);
        }
      }
      return sb.ToString();
    }

    private static string DiffInserted(string oldT, string newT) {
      if (string.IsNullOrEmpty(newT)) return "";
      if (string.IsNullOrEmpty(oldT)) return newT;
      if (newT.StartsWith(oldT)) return newT.Substring(oldT.Length);
      if (oldT.StartsWith(newT)) return "";
      int p = 0;
      int maxP = Math.Min(oldT.Length, newT.Length);
      while (p < maxP && oldT[p] == newT[p]) p++;
      int sOld = oldT.Length - 1;
      int sNew = newT.Length - 1;
      while (sOld >= p && sNew >= p && oldT[sOld] == newT[sNew]) {
        sOld--;
        sNew--;
      }
      if (sNew >= p) return newT.Substring(p, sNew - p + 1);
      return "";
    }

    private static bool IsModifierKey(uint vk) {
      return vk == 0x10 || vk == 0x11 || vk == 0x12 || vk == 0x5B || vk == 0x5C;
    }

    private static bool IsCandidateControl(uint vk) {
      if (vk == 0x09) return true;
      if (vk >= 0x30 && vk <= 0x39) return true;
      if (vk >= 0x60 && vk <= 0x69) return true;
      if (vk >= 0x21 && vk <= 0x28) return true;
      return false;
    }

    private bool RecentChineseActive() {
      return (DateTime.Now - _lastChineseCommitAt).TotalMilliseconds < 10000;
    }

    private void MarkChineseCommitIfCjk(string text) {
      if (HasCjk(text)) MarkChineseCommit();
    }

    private void MarkChineseCommit() {
      _imeEnglishMode = false;
      _lastChineseCommitAt = DateTime.Now;
    }

    private static bool ShiftOrCaps() {
      try {
        if ((Native.GetAsyncKeyState(0x10) & 0x8000) != 0) return true;
        if ((Native.GetAsyncKeyState(0x14) & 1) != 0) return true;
      } catch { }
      return false;
    }

    private bool IsOurProcessForeground() {
      try {
        IntPtr h = Native.GetForegroundWindow();
        if (h == IntPtr.Zero) return false;
        uint pid;
        Native.GetWindowThreadProcessId(h, out pid);
        return pid == (uint)Process.GetCurrentProcess().Id;
      } catch {
        return false;
      }
    }

    private void LoadSettings() {
      _loading = true;
      try {
        string path = SettingsPath();
        if (!File.Exists(path) && File.Exists(OldSettingsPath())) {
          try {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.Copy(OldSettingsPath(), path, false);
          } catch { }
        }
        if (File.Exists(path)) {
          XmlSerializer ser = new XmlSerializer(typeof(AppSettings));
          using (FileStream fs = File.OpenRead(path)) {
            _settings = (AppSettings)ser.Deserialize(fs);
          }
        }
      } catch {
        _settings = new AppSettings();
      }

      PopulateVoiceCombos();
      if (!SelectVoiceInCombo(_cboZh, _settings.ZhVoice)) SelectLangVoice(_cboZh, "Chinese");
      if (!SelectVoiceInCombo(_cboEn, _settings.EnVoice)) SelectLangVoice(_cboEn, "English");
      _trkRate.Value = Math.Max(-5, Math.Min(5, _settings.Rate));
      _trkVol.Value = Math.Max(0, Math.Min(100, _settings.Volume));
      _chkLetters.Checked = _settings.Letters;
      _chkDigits.Checked = _settings.Digits;
      _chkPunct.Checked = _settings.Punct;
      _chkFunc.Checked = _settings.Func;
      _chkModifiers.Checked = _settings.Modifiers;
      _chkDebug.Checked = _settings.DebugLog;
      _chkClickSpeak.Checked = _settings.ClickSpeak;
      _loading = false;
    }

    private void PopulateVoiceCombos() {
      List<string> voices = _speaker.GetVoices();
      if (voices.Count == 0) voices.Add("");
      _cboZh.Items.Clear();
      _cboEn.Items.Clear();
      foreach (string v in voices) {
        _cboZh.Items.Add(v);
        _cboEn.Items.Add(v);
      }
      if (_cboZh.Items.Count > 0) {
        _cboZh.SelectedIndex = 0;
        _cboEn.SelectedIndex = 0;
      }
    }

    private bool SelectVoiceInCombo(ComboBox cbo, string name) {
      if (string.IsNullOrEmpty(name)) return false;
      for (int i = 0; i < cbo.Items.Count; i++) {
        string item = cbo.Items[i].ToString();
        if (item.StartsWith(name + " |", StringComparison.OrdinalIgnoreCase) || item == name) {
          cbo.SelectedIndex = i;
          return true;
        }
      }
      return false;
    }

    private bool SelectLangVoice(ComboBox cbo, string keyword) {
      for (int i = 0; i < cbo.Items.Count; i++) {
        if (cbo.Items[i].ToString().IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) {
          cbo.SelectedIndex = i;
          return true;
        }
      }
      return false;
    }

    private string CurrentZhVoice() {
      return VoiceNameFromItem(_cboZh.Text);
    }

    private string CurrentEnVoice() {
      return VoiceNameFromItem(_cboEn.Text);
    }

    private static string VoiceNameFromItem(string item) {
      int idx = item.IndexOf(" |");
      return idx >= 0 ? item.Substring(0, idx) : item;
    }

    private void ApplyRateVolume() {
      if (_speaker == null) return;
      _speaker.Rate = _trkRate.Value;
      _speaker.Volume = _trkVol.Value;
      _speaker.ApplyRateVolume();
    }

    private void SaveSettings() {
      if (_testMode || _loading) return;
      try {
        _settings.ZhVoice = CurrentZhVoice();
        _settings.EnVoice = CurrentEnVoice();
        _settings.Rate = _trkRate.Value;
        _settings.Volume = _trkVol.Value;
        _settings.Letters = _chkLetters.Checked;
        _settings.Digits = _chkDigits.Checked;
        _settings.Punct = _chkPunct.Checked;
        _settings.Func = _chkFunc.Checked;
        _settings.Modifiers = _chkModifiers.Checked;
        _settings.DebugLog = _chkDebug.Checked;
        _settings.ClickSpeak = _chkClickSpeak.Checked;
        string settingsDir = Path.GetDirectoryName(SettingsPath());
        if (!string.IsNullOrEmpty(settingsDir)) Directory.CreateDirectory(settingsDir);
        XmlSerializer ser = new XmlSerializer(typeof(AppSettings));
        using (FileStream fs = File.Create(SettingsPath())) {
          ser.Serialize(fs, _settings);
        }
      } catch { }
    }

    private static string SettingsPath() {
      string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GuiLingGuiLing");
      return Path.Combine(dir, "settings.xml");
    }

    private static string OldSettingsPath() {
      return Path.Combine(Application.StartupPath, "settings.xml");
    }

    private static Font CreateFont() {
      try { return new Font("Microsoft YaHei UI", 9F); } catch { }
      try { return new Font("Microsoft YaHei", 9F); } catch { }
      return SystemFonts.MessageBoxFont;
    }

    private static Icon GetAppIcon() {
      try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
      return SystemIcons.Application;
    }

    protected override void OnFormClosing(FormClosingEventArgs e) {
      if (!_testMode && !_closingByTrayExit) {
        e.Cancel = true;
        Hide();
        base.OnFormClosing(e);
        return;
      }
      SaveSettings();
      StopListening();
      if (_imeTimer != null) _imeTimer.Stop();
      if (_uiTimer != null) _uiTimer.Stop();
      if (_tray != null) {
        _tray.Visible = false;
        _tray.Dispose();
      }
      LogTest("EXIT");
      if (_speaker != null) _speaker.Dispose();
      base.OnFormClosing(e);
    }

    protected override void OnLoad(EventArgs e) {
      base.OnLoad(e);
      _hook.KeyEvent += OnKey;
      _mouseHook.LeftButtonDown += delegate { _lastMouseDownAt = DateTime.Now; };
    }

    protected override void OnFormClosed(FormClosedEventArgs e) {
      _hook.KeyEvent -= OnKey;
      _hook.Dispose();
      base.OnFormClosed(e);
    }
  }
}
