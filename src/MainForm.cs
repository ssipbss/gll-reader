using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Xml.Serialization;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace GenDaLangDu {
  public partial class MainForm : Form, ISelectionHost {
    private KeyboardHook _hook = new KeyboardHook();
    private MouseHook _mouseHook = new MouseHook();
    private RawInputMonitor _rawInput;
    private DateTime _lastHookEventAt = DateTime.MinValue;
    private DateTime _lastHookRestartAt = DateTime.MinValue;
    private ImeMonitor _ime = new ImeMonitor();
    private Speaker _speaker;
    private System.Windows.Forms.Timer _imeTimer;
    private System.Windows.Forms.Timer _autoExitTimer;
    private System.Windows.Forms.Timer _injectTimer;
    private System.Windows.Forms.Timer _uiTimer;
    private System.Windows.Forms.Timer _hbTimer;
    private NotifyIcon _tray;
    private ContextMenuStrip _trayMenu;
    private AppSettings _settings = new AppSettings();

    private bool _listening;
    private bool _testMode;
    private string _testLog = null;
    private int _exitMs = 6000;
    private string _autoTextPath = null;
    private string _autoText = null;
    private int _autoIndex;
    private int _autoSpoken;
    private System.Windows.Forms.Timer _autoTimer;
    private string _lastResult = "";
    private string _lastUiText = null;
    private string _lastUiElement = null;
    private int _lastCaret = -1;
    private int _lastCaretAbs = -1;
    private bool _lastCaretAbsValid;
    private DateTime _lastUiTextCheckAt = DateTime.MinValue;
    private DateTime _lastPinyinKeyAt = DateTime.MinValue;
    private DateTime _lastPasteAt = DateTime.MinValue;
    private string _lastPunctName;
    private bool _punctKeyPending;
    private DateTime _lastPunctKeyAt = DateTime.MinValue;
    private System.Windows.Forms.Timer _punctTimer;
    private readonly System.Text.StringBuilder _pendingPuncts = new System.Text.StringBuilder();
    private bool _composing;
    private DateTime _lastMouseDownAt = DateTime.MinValue;
    private DateTime _lastShiftKeyAt = DateTime.MinValue;
    private DateTime _lastKeyAt = DateTime.MinValue;
    private string _trayImeLast = "";
    private DateTime _lastTrayImeFindAt = DateTime.MinValue;
    private TrayImeIconTracker _trayImeIcon;
    private DateTime _lastDeleteSpeakAt = DateTime.MinValue;
    private DateTime _lastDeleteAt = DateTime.MinValue;
    private DateTime _lastZhCommitAt = DateTime.MinValue;
    private string _lastDiffCommitText = null;
    private DateTime _lastDiffCommitAt = DateTime.MinValue;
    private DateTime _lastTypingCommitKeyAt = DateTime.MinValue;
    private DateTime _lastCtrlDownAt = DateTime.MinValue;
    private DateTime _lastAltDownAt = DateTime.MinValue;
    private DateTime _lastWinDownAt = DateTime.MinValue;
    private DateTime _lastShiftDownAt = DateTime.MinValue;
    private string _pendingModName = null;
    private uint _pendingModVk = 0;
    private System.Windows.Forms.Timer _modTimer;
    private const string BackspaceSoundPath = @"C:\Windows\Media\Windows Ding.wav";
    private const string SpaceSoundPath = @"C:\Windows\Media\Windows Critical Stop.wav";
    private System.Collections.Generic.Dictionary<string, System.Media.SoundPlayer> _keySoundPlayers =
      new System.Collections.Generic.Dictionary<string, System.Media.SoundPlayer>();
    private string _pendingKeySoundPath = null;
    private DateTime _pendingKeySoundAt = DateTime.MinValue;
    private DateTime _lastPacketCjkAt = DateTime.MinValue;
    private const int MaxUiDiffLen = 10;

    private AppStateTracker _appStates = new AppStateTracker();
    private DateTime _shiftDownAt = DateTime.MinValue;
    private bool _shiftTapArmed;
    private uint _shiftTapPid;
    private bool _shiftSpeakPending;
    private readonly System.Text.StringBuilder _shiftLetterBuf = new System.Text.StringBuilder();
    private System.Windows.Forms.Timer _shiftLetterTimer;
    private DateTime _shiftPressedAt = DateTime.MinValue;
    private DateTime _shiftLetterDeadline = DateTime.MinValue;
    private bool? _shiftTrayStateAtDown;
    /* 托盘识别是状态唯一权威：程序记忆+Shift翻转、英文直通、字母缓冲、
       IMM 中文布局判断已废弃（试用稳定后删除，见交接文档待办7）。 */
    private bool? _trayImeEnglish;
    private DateTime _lastStateFlipAt = DateTime.MinValue;
    private readonly System.Text.StringBuilder _packetZhBuffer = new System.Text.StringBuilder();
    private System.Windows.Forms.Timer _packetZhTimer;
    private const int PacketZhMergeMs = 150;
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
    private SelectionSpeakController _selection;
    private TsfBridge _tsfBridge;
    private TsfNotifyWindow _tsfNotifyWindow;
    private readonly HashSet<uint> _tsfActivePids = new HashSet<uint>();
    private readonly Dictionary<uint, DateTime> _tsfCommitAt = new Dictionary<uint, DateTime>();
    private bool _tsfCompositionReadByDiff;
    private System.Threading.Tasks.Task<FocusedTextResult> _uiTextTask;
    private DateTime _uiTextQueryAt = DateTime.MinValue;
    private readonly RecentSpeech _recentSpeech = new RecentSpeech();
    private static readonly object _logLock = new object();
    private System.Threading.Tasks.Task<List<string>> _trayFindTask;
    private AutomationElement _trayImeButton;
    private bool _trayRefreshPending;
    private Process _hook32Host;
    private uint _hookedTid;

    public MainForm(string[] args) {
      _loading = true;
      ParseArgs(args);
      try {
        _speaker = new Speaker();
        LogTest("SPEAKER_OK");
      } catch (Exception ex) {
        LogTest("SPEAKER_FAIL: " + ex);
        throw;
      }
      _selection = new SelectionSpeakController(this);
      _rawInput = new RawInputMonitor();
      _rawInput.Register();
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

      _trayImeIcon = new TrayImeIconTracker();
      _trayImeIcon.StateConfirmed += OnTrayImeIconState;
      _trayImeIcon.Log = DebugLog;

      _imeTimer = new System.Windows.Forms.Timer();
      /* 50ms 一次足够捕捉 IME 上屏变化，降低后台轮询对系统的打扰 */
      _imeTimer.Interval = 50;
      _imeTimer.Tick += delegate { CheckIme(); CheckPendingKeySound(); };

      _uiTimer = new System.Windows.Forms.Timer();
      _uiTimer.Interval = 100;
      _uiTimer.Tick += delegate { TrackFocus(); FindTrayImeElement(); if (_trayImeIcon != null) _trayImeIcon.Tick(); CheckUiText(); _selection.Poll(); CheckHookHealth(); };

      _hbTimer = new System.Windows.Forms.Timer();
      _hbTimer.Interval = 30000;
      _hbTimer.Tick += delegate {
        DebugLog("HB");
        EnsureForegroundHook();
        if (_listening && (!_hook.IsInstalled || !_hook.IsHookThreadAlive)) {
          DebugLog("HOOK_RESTART");
          try { _hook.Install(); } catch { }
          if (!_hook.IsInstalled) {
            _listening = false;
            UpdateUi();
          }
        }
      };

      if (_testMode) {
        ShowInTaskbar = false;
        Enabled = false;
        StartListening();
        if (!string.IsNullOrEmpty(_autoTextPath) && System.IO.File.Exists(_autoTextPath)) {
          try {
            _autoText = System.IO.File.ReadAllText(_autoTextPath, System.Text.Encoding.UTF8);
          } catch {
            _autoText = "";
          }
          _autoIndex = 0;
          _autoSpoken = 0;
          _autoTimer = new System.Windows.Forms.Timer();
          _autoTimer.Interval = 400; /* 约150字/分钟 */
          _autoTimer.Tick += delegate { AutoTextTick(); };
          _autoTimer.Start();
          _exitMs = Math.Max(_exitMs, 5000 + (_autoText == null ? 0 : _autoText.Length) * 400 + 15000);
        } else {
          _injectTimer = new System.Windows.Forms.Timer();
          _injectTimer.Interval = 2500;
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
          SimulateKey(0x10);
          SimulateKey(0x41);
          SimulateKey(0x42);
          /* Shift 轻按（120ms 内松开）应翻转当前程序状态 */
          SimulateKey(0x10);
          System.Threading.Thread.Sleep(120);
          SimulateKeyUp(0x10);
          System.Threading.Thread.Sleep(400);
          /* Shift 长按（600ms）不应翻转 */
          SimulateKey(0x10);
          System.Threading.Thread.Sleep(600);
          SimulateKeyUp(0x10);
          System.Threading.Thread.Sleep(400);
          /* 再轻按一次，翻回 */
          SimulateKey(0x10);
          System.Threading.Thread.Sleep(120);
          SimulateKeyUp(0x10);
          /* VK_PACKET 逐字投递汉字：应合并成"什么"一次朗读 */
          SimulatePacket('什');
          SimulatePacket('么');
          /* 中文状态反斜杠键：应念顿号，不念反斜杠 */
          SimulateKey(0xDC);
          LogTest("SIMULATE_DONE");
          };
          _injectTimer.Start();
        }
        _autoExitTimer = new System.Windows.Forms.Timer();
        _autoExitTimer.Interval = _exitMs;
        _autoExitTimer.Tick += delegate { _closingByTrayExit = true; Close(); };
        _autoExitTimer.Start();
      } else {
        StartListening();
        ShowInTaskbar = false;
        WindowState = FormWindowState.Minimized;
      }
    }
    private void LogTest(string line) {
      if (!_testMode || _testLog == null) return;
      try {
        lock (_logLock) {
          File.AppendAllText(_testLog, DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + "\r\n", new System.Text.UTF8Encoding(false));
        }
      } catch { }
    }

    private void DebugLog(string line) {
      if (_testLog != null) {
        try {
          lock (_logLock) {
            string dir = Path.GetDirectoryName(_testLog);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(_testLog, DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + "\r\n", new System.Text.UTF8Encoding(false));
          }
        } catch { }
        return;
      }
      if (_settings == null || !_settings.DebugLog) return;
      try {
        lock (_logLock) {
          File.AppendAllText(Path.Combine(Application.StartupPath, "debug.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + "\r\n", new System.Text.UTF8Encoding(false));
        }
      } catch { }
    }


    private void SimulateKey(uint vk) {
      ushort scan = InputSender.ScanOf((ushort)vk);
      OnKey(this, new KeyHookEventArgs { Vk = vk, Scan = scan, IsUp = false, IsSysKey = false });
    }

    private void SimulateKeyUp(uint vk) {
      ushort scan = InputSender.ScanOf((ushort)vk);
      OnKey(this, new KeyHookEventArgs { Vk = vk, Scan = scan, IsUp = true, IsSysKey = false });
    }

    private void SimulatePacket(char c) {
      OnKey(this, new KeyHookEventArgs { Vk = 0xE7, Scan = (uint)c, IsUp = false, IsSysKey = false });
    }

    /// <summary>自动打字测试：按约150字/分钟节奏模拟五笔码+空格上屏+汉字投递。</summary>
    private void AutoTextTick() {
      if (_autoText == null || _autoIndex >= _autoText.Length) {
        if (_autoTimer != null) _autoTimer.Stop();
        LogTest("AUTO_TEXT_DONE total=" + (_autoText == null ? 0 : _autoText.Length) +
                " hanzi=" + CountHanzi(_autoText) + " spoken=" + _autoSpoken);
        return;
      }
      char c = _autoText[_autoIndex++];
      if (char.IsWhiteSpace(c)) return;
      string pn = KeyTranslator.PunctName(c);
      if (pn != null) {
        SimulatePacket(c);
        return;
      }
      if (!KeyTranslator.IsCjk(c)) {
        SimulatePacket(c);
        return;
      }
      /* 模拟五笔码（2-4个字母）+ 空格上屏 + 汉字投递 */
      int codeLen = 2 + (_autoIndex % 3);
      for (int i = 0; i < codeLen; i++) {
        SimulateKey((uint)('A' + ((i + _autoIndex) % 26)));
      }
      SimulateKey(0x20);
      SimulatePacket(c);
    }

    private static int CountHanzi(string s) {
      if (string.IsNullOrEmpty(s)) return 0;
      int n = 0;
      foreach (char c in s) {
        if (KeyTranslator.IsCjk(c)) n++;
      }
      return n;
    }

    private void ParseArgs(string[] args) {
      for (int i = 0; i < args.Length; i++) {
        if (args[i] == "--test" && i + 1 < args.Length) {
          _testMode = true;
          _testLog = Path.GetFullPath(args[i + 1]);
          i++;
        } else if (args[i] == "--debuglog") {
          _forceDebug = true;
        } else if (args[i] == "--auto-text" && i + 1 < args.Length) {
          _autoTextPath = System.IO.Path.GetFullPath(args[i + 1]);
          i++;
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
      _cboZh.SelectedIndexChanged += delegate { OnVoiceSelected(); };

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
      _cboEn.SelectedIndexChanged += delegate { OnVoiceSelected(); };

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
      _chkClickSpeak = MakeCheck("选中文字时朗读", new Point(252, 74));
      _chkClickSpeak.Checked = true;
      _chkDebug = MakeCheck("记录调试日志", new Point(252, 104));
      _chkDebug.Checked = false;

      cardOpt.Controls.AddRange(new Control[] {
        t2, _chkLetters, _chkDigits, _chkPunct, _chkFunc, _chkModifiers, _chkClickSpeak,
        _chkDebug
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
      ShowInTaskbar = true;
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
        /* TSF 注入仅对白名单内的原生应用启用：记事本/WPS/Office 的 UIA 不可读、
           无 VK_PACKET、无 IMM 结果，中文读取只能靠 TSF 钩子；
           Chromium 系（哔哩哔哩/Edge/Codex/Electron）注入曾致卡死/闪退（v2.40），一律排除 */
        StartTsfHook();
        _lastResult = "";
        _lastUiText = null;
        _imeTimer.Start();
        _uiTimer.Start();
        _hbTimer.Start();
        DebugLog("RAWINPUT_REGISTERED " + (_rawInput != null ? "ok" : "null"));
      }
      UpdateUi();
    }

    private void StopListening() {
      if (!_listening) return;
      _listening = false;
      _hook.Uninstall();
      _mouseHook.Uninstall();
      StopTsfHook();
      _imeTimer.Stop();
      _uiTimer.Stop();
      _hbTimer.Stop();
      _speaker.Stop();
      UpdateUi();
    }

    private void UpdateUi() {
      if (_btnToggle == null) return;
      _btnToggle.Text = _listening ? "暂停监听" : "开始监听";
      _statusPill.Active = _listening;
      _statusPill.Invalidate();
    }

    /// <summary>TSF 注入白名单：仅原生应用（UIA 不可读、无 VK_PACKET/IMM 结果，
    /// 中文读取只能靠 TSF 钩子）。Chromium/Electron 系一律排除（注入曾致卡死/闪退）。</summary>
    private bool IsTsfWhitelistedApp() {
      try {
        IntPtr fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        uint pid;
        Native.GetWindowThreadProcessId(fg, out pid);
        if (pid == 0) return false;
        using (Process p = Process.GetProcessById((int)pid)) {
          string n = p.ProcessName.ToLowerInvariant();
          return n == "notepad" || n == "notepad++" ||
                 n == "wps" || n == "et" || n == "wpp" ||
                 n == "winword" || n == "excel" || n == "powerpnt";
        }
      } catch {
        return false;
      }
    }

    private void StartTsfHook() {
      try {
        /* 白名单：仅原生应用注入（历史证明稳定且需要 TSF 才能读中文）。
           Chromium/Electron 系（哔哩哔哩/Edge/Codex/OpenCode 等）一律不注入，
           它们的中文走文档差异通道，注入曾致卡死/闪退（v2.40 结论）。 */
        if (!IsTsfWhitelistedApp()) {
          DebugLog("TSF_HOOK_SKIP app=" + CurrentForegroundName());
          return;
        }
        if (_tsfNotifyWindow == null) {
          _tsfNotifyWindow = new TsfNotifyWindow();
          _tsfNotifyWindow.CommitReceived += OnTsfCommit;
          _tsfNotifyWindow.StateReceived += OnTsfState;
          _tsfNotifyWindow.Create();
        }
        if (_tsfBridge == null) {
          _tsfBridge = new TsfBridge();
          string dll = Path.Combine(Application.StartupPath, "gll_hook64.dll");
          try {
            string ptr = Path.Combine(Application.StartupPath, "gll_hook64.txt");
            if (File.Exists(ptr)) {
              string name = File.ReadAllText(ptr).Trim();
              if (!string.IsNullOrEmpty(name)) dll = Path.Combine(Application.StartupPath, name);
            }
          } catch { }
          CleanupOldHookDlls("gll_hook64_", dll);
          string dlls = "";
          try {
            dlls = string.Join("|", Directory.GetFiles(Application.StartupPath, "*.dll"));
          } catch (Exception ex2) {
            dlls = "enum-err:" + ex2.Message;
          }
          DebugLog("TSF_HOOK diag cwd=[" + Environment.CurrentDirectory + "] base=[" +
                   AppDomain.CurrentDomain.BaseDirectory + "] full=[" +
                   Path.GetFullPath(dll) + "] exists=" + File.Exists(Path.GetFullPath(dll)) +
                   " bits=" + (IntPtr.Size * 8) + " dlls=[" + dlls + "]");
          bool ok = _tsfBridge.Install(dll, _settings.DebugLog);
          DebugLog("TSF_HOOK install=" + ok + " err=[" + _tsfBridge.LastError + "] dll=" + dll);
        }
        StartHook32Host();
        EnsureForegroundHook();
      } catch (Exception ex) {
        DebugLog("TSF_HOOK_START_ERR " + ex.Message);
      }
    }

    private void EnsureForegroundHook() {
      try {
        if (_tsfBridge == null) return;
        IntPtr h = Native.GetForegroundWindow();
        if (h == IntPtr.Zero) return;
        uint pid;
        uint tid = Native.GetWindowThreadProcessId(h, out pid);
        if (tid == _hookedTid) return;
        /* 白名单强制：非白名单前台应用不注入（看门狗 30 秒轮询也会走到这里） */
        if (!IsTsfWhitelistedApp()) {
          if (_hookedTid != 0) {
            try { _tsfBridge.UnhookThread(_hookedTid); } catch { }
            _hookedTid = 0;
          }
          return;
        }
        if (_hookedTid != 0) {
          _tsfBridge.UnhookThread(_hookedTid);
          _hookedTid = 0;
        }
        if (tid != 0 && _tsfBridge.HookThread(tid)) {
          _hookedTid = tid;
          _tsfBridge.NudgeThread(tid);
          DebugLog("TSF_THREAD_HOOK tid=" + tid + " pid=" + pid);
        }
      } catch (Exception ex) {
        DebugLog("TSF_THREAD_HOOK_ERR " + ex.Message);
      }
    }

    private string CurrentForegroundName() {
      try {
        IntPtr fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero) return "";
        uint pid;
        Native.GetWindowThreadProcessId(fg, out pid);
        if (pid == 0) return "";
        using (Process p = Process.GetProcessById((int)pid)) {
          return p.ProcessName;
        }
      } catch {
        return "";
      }
    }

    private static void CleanupOldHookDlls(string prefix, string current) {
      try {
        string dir = Path.GetDirectoryName(current);
        if (string.IsNullOrEmpty(dir)) return;
        foreach (string f in Directory.GetFiles(dir, prefix + "*.dll")) {
          if (!f.Equals(current, StringComparison.OrdinalIgnoreCase)) {
            try { File.Delete(f); } catch { }
          }
        }
      } catch { }
    }

    private void StartHook32Host() {
      try {
        if (_hook32Host != null && !_hook32Host.HasExited) return;
        string host = Path.Combine(Application.StartupPath, "gll_hook32_host.exe");
        if (!File.Exists(host)) return;
        ProcessStartInfo psi = new ProcessStartInfo(host, "--parent " + Process.GetCurrentProcess().Id);
        psi.WindowStyle = ProcessWindowStyle.Hidden;
        psi.CreateNoWindow = true;
        psi.UseShellExecute = false;
        _hook32Host = Process.Start(psi);
        DebugLog("HOOK32_HOST started");
      } catch (Exception ex) {
        DebugLog("HOOK32_HOST_ERR " + ex.Message);
      }
    }

    private void StopTsfHook() {
      try {
        if (_hookedTid != 0 && _tsfBridge != null) {
          try { _tsfBridge.UnhookThread(_hookedTid); } catch { }
          _hookedTid = 0;
        }
        if (_hook32Host != null) {
          try {
            if (!_hook32Host.HasExited) _hook32Host.Kill();
          } catch { }
          try { _hook32Host.Dispose(); } catch { }
          _hook32Host = null;
        }
        if (_tsfBridge != null) {
          _tsfBridge.Dispose();
          _tsfBridge = null;
        }
      } catch { }
    }

    private void OnTsfCommit(uint pid, string text) {
      try {
        if (!_listening) return;
        if (string.IsNullOrEmpty(text)) return;
        _tsfActivePids.Add(pid);
        _tsfCommitAt[pid] = DateTime.Now;
        /* 上屏提交 = 用户按过上屏键：补登记，供文档差异通道判断“正在打字” */
        _lastKeyAt = DateTime.Now;
        _lastTypingCommitKeyAt = DateTime.Now;
        if (_tsfActivePids.Count > 96) {
          List<uint> dead = new List<uint>();
          foreach (KeyValuePair<uint, DateTime> kv in _tsfCommitAt) {
            if ((DateTime.Now - kv.Value).TotalMilliseconds > 600000) dead.Add(kv.Key);
          }
          foreach (uint d in dead) {
            _tsfCommitAt.Remove(d);
            _tsfActivePids.Remove(d);
          }
        }
        if (!HasChineseText(text)) return;
        /* 中文已上屏 = 组字结束：立即清除内部组字标记，否则紧接的数字会被当成候选键静音 */
        _composing = false;
        /* 缓冲中的字母是编码（已随上屏提交），取消朗读 */
        CancelShiftLetters("tsf");
        /* TSF 是权威通道：清掉可能正在缓冲的 VK_PACKET 同文，避免双读 */
        if (_packetZhBuffer.Length > 0) {
          _packetZhBuffer.Clear();
          if (_packetZhTimer != null) _packetZhTimer.Stop();
          DebugLog("TSF_CANCEL_PACKET_BUFFER");
        }
        bool diffSpoke = _tsfCompositionReadByDiff && text == _lastDiffCommitText &&
                         (DateTime.Now - _lastDiffCommitAt).TotalMilliseconds < 800;
        _tsfCompositionReadByDiff = false;
        if (diffSpoke) {
          DebugLog("TSF_COMMIT_SKIP diff_spoke");
          CancelPendingKeySound();
          return;
        }
        if (HasCjk(text) && !RecentlySpoken(text)) {
          SpeakZh(text);
          RememberSpoken(text);
        } else if (!HasCjk(text)) {
          /* 纯标点/数字/字母提交：按键通道已按名称朗读（逗号/句号等），
             这里再读原文会双读（如 "，" 读成"逗号"后又读原文），且数字会被读成
             "一百二十三"而按键通道读"一二三"——跳过，只保留汉字内容 */
          DebugLog("TSF_COMMIT_SKIP punct [" + text + "]");
        }
        MarkChineseCommit();
        _lastDiffCommitText = text;
        _lastDiffCommitAt = DateTime.Now;
        DebugLog("TSF_COMMIT pid=" + pid + " [" + text + "]");
      } catch (Exception ex) {
        DebugLog("TSF_COMMIT_ERR " + ex.Message);
      }
    }

    private void OnTsfState(uint pid, bool composing) {
      try {
        if (!_listening) return;
        if (composing) {
          _tsfCompositionReadByDiff = false;
          /* 组字开始 = 用户按过字母键：本地按键被 TSF 抑制（KEY_SUPPRESS_TSF）
             时程序看不到按键，这里补登记打字活动，避免新上屏的字被误拦 */
          _lastKeyAt = DateTime.Now;
          _lastPinyinKeyAt = DateTime.Now;
        }
        DebugLog("TSF_STATE pid=" + pid + " composing=" + composing);
      } catch { }
    }

    private void OnKey(object sender, KeyHookEventArgs e) {
      if (!_listening) return;
      if (!_testMode && IsOurProcessForeground()) return;
      /* 朗读选中内容时，按 Esc 立即停止（不朗读 Esc 本身、不拦截其它用途） */
      if (e.Vk == 0x1B && !e.IsUp && !CtrlDown() && !AltDown() && !WinDown() &&
          _selection.StopReadingIfReading()) {
        DebugLog("SEL_STOP_ESC");
        return;
      }
      /* 注入式按键（远程输入/自动化）：不朗读、不处理，
         但登记打字活动，让文档差异通道能把远程打出的中文正常朗读；
         注入的 Ctrl+V 仍记为粘贴，粘贴内容不朗读 */
      if (e.IsInjected) {
        HandleInjectedKey(e);
        return;
      }
      if (e.IsUp) {
        HandleKeyUp(e);
        return;
      }
      if (e.IsAutoRepeat) return;
      if (e.Vk == 0x10 || e.Vk == 0xA0 || e.Vk == 0xA1) {
        _lastTrayImeFindAt = DateTime.Now;
        RefreshTrayState();
        if (_trayImeIcon != null) _trayImeIcon.RefreshNow();
        _shiftPressedAt = DateTime.Now;
        _shiftLetterDeadline = _shiftPressedAt.AddMilliseconds(1200);
        EnsureShiftLetterTimer();
        _shiftTrayStateAtDown = _trayImeEnglish;
      }
      /* TSF/IMM 输入法正在组字（共享内存实时状态）：字母/数字/标点/上屏键全部静默，
         只等输入法上屏事件朗读，绝不读未上屏的码与候选 */
      uint tsfPid = CurrentForegroundPid();
      if (_tsfBridge != null && _tsfBridge.IsComposing(tsfPid) && SuppressTsfComposingKey(e, tsfPid)) return;
      if (e.Vk == 0x08 || e.Vk == 0x2E) {
      } else {
        _lastDeleteSpeakAt = DateTime.MinValue;
      }
      ArmShiftTap(e.Vk);
      _lastKeyAt = DateTime.Now;
      if (ShiftHeld()) _lastShiftKeyAt = DateTime.Now;
      DebugLog("KEY vk=0x" + e.Vk.ToString("X") + " scan=0x" + e.Scan.ToString("X"));
      /* 粘贴标记：Ctrl+V / Shift+Insert，用于差异通道的因果校验 */
      if ((e.Vk == 0x56 && CtrlHeld()) || (e.Vk == 0x2D && ShiftHeld())) {
        _lastPasteAt = DateTime.Now;
        DebugLog("PASTE_KEY vk=0x" + e.Vk.ToString("X"));
      }
      /* 快捷键组合播报：Ctrl/Alt/Win 按住时再按其它键，整组念（Control C / Alt Tab） */
      if (TrySpeakChord(e)) return;
      if (!IsAltKey(e.Vk) && AltDown()) {
        DebugLog("ALT_COMBO_IGNORED vk=0x" + e.Vk.ToString("X"));
        return;
      }

      ImeState ime = _ime.GetState();
      bool chineseMode = !ImeEnglishNow;
      if (!chineseMode) {
        _composing = false;
        _lastPinyinKeyAt = DateTime.MinValue;
      }
      DebugLog("IME hkl=0x" + ime.Hkl.ToString("X") + " imc=" + ime.HasImc + " open=" + ime.IsOpen + " conv=" + ime.ConversionMode +
               " zhLayout=" + ime.IsChineseLayout + " comp=[" + ime.Composition + "] result=[" + ime.Result + "]");

      if (chineseMode && HandleChineseModeKey(e.Vk)) return;

      string keyName = KeyTranslator.GetKeyName(e.Vk);
      if (keyName != null) {
        SpeakNamedKey(e.Vk, keyName);
        ScheduleImeCheck();
        return;
      }

      if (chineseMode && e.Vk >= 0x41 && e.Vk <= 0x5A && !ShiftOrCaps() && !ImeEnglishNow &&
          !(e.Vk == 0x56 && CtrlDown()) && !(e.Vk == 0x2D && ShiftDown())) {
        CheckUiText();
        _composing = true;
        MarkTypingKeys();
      }

      string chars = KeyTranslator.GetChars(e.Vk, e.Scan);
      /* 中文状态下反斜杠键实际输出顿号（多多五笔中文标点），不要念成反斜杠 */
      if (chineseMode && !ImeEnglishNow) {
        chars = chars.Replace('\\', '、');
      }
      DebugLog("CHARS vk=0x" + e.Vk.ToString("X") + " [" + chars + "]");
      if (!string.IsNullOrEmpty(chars)) {
        foreach (char c in chars) {
          ProcessChar(c, chineseMode);
        }
      }
      ScheduleImeCheck();
    }

    /// <summary>注入式按键：只登记打字活动，不朗读（详见 OnKey 内注释）。</summary>
    private void HandleInjectedKey(KeyHookEventArgs e) {
      _lastKeyAt = DateTime.Now;
      DebugLog("INJ_KEY vk=0x" + e.Vk.ToString("X"));
      if ((e.Vk == 0x56 && CtrlHeld()) || (e.Vk == 0x2D && ShiftHeld())) {
        _lastPasteAt = DateTime.Now;
      } else if ((e.Vk >= 0x30 && e.Vk <= 0x39) ||
                 (e.Vk >= 0x41 && e.Vk <= 0x5A) ||
                 e.Vk == 0x20 || e.Vk == 0x0D) {
        MarkTypingKeys();
      }
    }

    /// <summary>TSF 组字中的按键压制：返回 true 表示整键静默（等上屏事件），
    /// 返回 false 表示长按 Shift 的字母直通，继续正常流程。</summary>
    private bool SuppressTsfComposingKey(KeyHookEventArgs e, uint tsfPid) {
      _lastKeyAt = DateTime.Now;
      bool shiftDown = e.Vk == 0x10 || e.Vk == 0xA0 || e.Vk == 0xA1;
      if (shiftDown) {
        _shiftTapArmed = true;
        _shiftDownAt = DateTime.Now;
        _shiftTapPid = tsfPid;
        _shiftSpeakPending = false;
      } else {
        /* 组字期间按过其它键：松开 Shift 时不再补念 Shift */
        _shiftTapArmed = false;
        _shiftSpeakPending = false;
      }
      bool isLetterKey = e.Vk >= 0x41 && e.Vk <= 0x5A;
      if (isLetterKey && ShiftOrCaps()) {
        /* 长按 Shift 输入英文：字母直接上屏，即使 TSF 标记组字也不压制 */
        return false;
      }
      /* 组字期间的按键（码/数字候选/上屏键）标记为组字中，
         避免组字状态短暂回落后把候选数字当普通数字朗读 */
      _composing = true;
      MarkTypingKeys();
      DebugLog("KEY_SUPPRESS_TSF vk=0x" + e.Vk.ToString("X") + " pid=" + tsfPid);
      return true;
    }

    /// <summary>Shift 按下时的轻按翻转候选登记（配合 HandleKeyUp 判定）。</summary>
    private void ArmShiftTap(uint vk) {
      bool isShift = vk == 0x10 || vk == 0xA0 || vk == 0xA1;
      if (isShift) {
        bool ctrl = CtrlDown();
        bool alt = AltDown();
        bool win = WinDown();
        if (ctrl || alt || win) {
          _shiftTapArmed = false;
          _shiftSpeakPending = false;
          DebugLog("SHIFT_TAP_CANCEL_COMBO ctrl=" + ctrl + " alt=" + alt + " win=" + win);
        } else {
          _shiftTapArmed = true;
          _shiftDownAt = DateTime.Now;
          _shiftTapPid = CurrentForegroundPid();
          /* 不立即念 Shift：若随后有标点/字母等组合键，只念那个键本身 */
          _shiftSpeakPending = true;
        }
      } else {
        _shiftTapArmed = false;
        _shiftSpeakPending = false;
      }
    }

    /// <summary>快捷键组合播报（Ctrl/Alt/Win + 其它键整组念）；返回 true 表示已播报。</summary>
    private bool TrySpeakChord(KeyHookEventArgs e) {
      if (!_chkModifiers.Checked || _pendingModName == null || IsModifierKey(e.Vk) ||
          !(CtrlHeld() || AltHeld() || WinHeld())) return false;
      string chord = BuildChord(e);
      if (chord == null) return false;
      CancelPendingMod();
      string ssml = BuildChordSsml(e);
      if (ssml != null) _speaker.SpeakEnSsml(chord, ssml);
      else _speaker.SpeakEnWord(chord);
      DebugLog("CHORD [" + chord + "]");
      ScheduleImeCheck();
      return true;
    }

    /// <summary>中文模式下空格/退格/候选键/回车的特殊处理；返回 true 表示整键已处理。</summary>
    private bool HandleChineseModeKey(uint vk) {
      if (vk == 0x20) {
        _lastTypingCommitKeyAt = DateTime.Now;
        /* 中文模式下空格是输入法的上屏键：不响提示音，等上屏内容朗读。
           Shift/大写锁定下的英文直通空格才保留提示音 */
        if (!ImeEnglishNow && !ShiftOrCaps()) {
          if (_composing) {
            CheckUiText();
            if (_composing) {
              _composing = false;
              MarkChineseCommit();
              ScheduleImeCheck();
              return true;
            }
            ScheduleImeCheck();
            return true;
          }
          /* 非组字状态按空格：可读文档的程序（Codex/Edge 等）按键本身不发声，
             由文档差异确认“空格真的上屏”后再响（TrySpeakInserted）；
             WPS 等读不了文档内容的程序，组字状态由 IMM/TSF 精确跟踪，
             非组字按空格即真正的空格：提示音排队，若随后有中文上屏则取消 */
          _lastPinyinKeyAt = DateTime.Now;
          if (TextReader.IsKnownSlowApp()) {
            _pendingKeySoundPath = SpaceSoundPath;
            _pendingKeySoundAt = DateTime.Now;
            DebugLog("KEY_SOUND_DEFER [" + System.IO.Path.GetFileName(SpaceSoundPath) + "]");
            CheckUiText();
          }
          ScheduleImeCheck();
          return true;
        }
        /* 英文直通空格：残留的组字标记清掉，交回功能键音效处理 */
        _composing = false;
        return false;
      }
      if (vk == 0x08 && _composing) {
        CheckUiText();
        if (_composing) {
          ScheduleImeCheck();
          return true;
        }
        ScheduleImeCheck();
        return true;
      }
      if (_composing && IsCandidateControl(vk)) {
        MarkTypingKeys();
        _lastPunctName = null;
        _punctKeyPending = true;
        _lastPunctKeyAt = DateTime.Now;
        CheckUiText();
        if (_composing) {
          MarkChineseCommit();
          ScheduleImeCheck();
          return true;
        }
        ScheduleImeCheck();
        return true;
      }
      if (vk == 0x0D || vk == 0x1B) {
        _composing = false;
        if (vk == 0x0D) _lastTypingCommitKeyAt = DateTime.Now;
      }
      return false;
    }

    /// <summary>有名称的键（修饰键/功能键/退格/空格等）的播报与状态登记。</summary>
    private void SpeakNamedKey(uint vk, string keyName) {
      if (IsModifierKey(vk)) {
        if (vk == 0x11 || vk == 0xA2 || vk == 0xA3) _lastCtrlDownAt = DateTime.Now;
        else if (vk == 0x12 || vk == 0xA4 || vk == 0xA5) _lastAltDownAt = DateTime.Now;
        else if (vk == 0x5B || vk == 0x5C) _lastWinDownAt = DateTime.Now;
        else if (vk == 0x10 || vk == 0xA0 || vk == 0xA1) _lastShiftDownAt = DateTime.Now;
        if (_chkModifiers.Checked) {
          if (!(vk == 0x10 || vk == 0xA0 || vk == 0xA1)) {
            /* Ctrl/Alt/Win 延迟250ms：等待可能的组合键；单独按则松手后念 */
            string en = KeyTranslator.GetKeyNameEn(vk);
            if (en != null) {
              _pendingModName = en;
              _pendingModVk = vk;
              StartModTimer();
            }
          }
        }
        if (_composing) _lastPinyinKeyAt = DateTime.Now;
      } else if (_chkFunc.Checked) {
        if (vk >= 0x70 && vk <= 0x87) _speaker.SpeakEn("F" + (vk - 0x70 + 1).ToString());
        else if (vk == 0x08 || vk == 0x2E) {
          _lastDeleteAt = DateTime.Now;
          if (vk == 0x08) {
            /* 退格键：系统提示音，中文朗读时让路 */
            RequestKeySound(BackspaceSoundPath);
          } else if ((DateTime.Now - _lastDeleteSpeakAt).TotalMilliseconds < 800) {
            DebugLog("DELETE_COALESCE vk=0x" + vk.ToString("X"));
          } else {
            _lastDeleteSpeakAt = DateTime.Now;
            string en = KeyTranslator.GetKeyNameEn(vk);
            _speaker.SpeakEnWord(en != null ? en : keyName);
          }
        } else if (vk == 0x20) {
          /* 空格键：提示音"当"。若80ms内刚有中文提交（竞态），说明是上屏键，不响 */
          bool commitRace = _lastZhCommitAt != DateTime.MinValue &&
                            (DateTime.Now - _lastZhCommitAt).TotalMilliseconds < 80;
          if (!commitRace) {
            RequestKeySound(SpaceSoundPath);
          }
        } else {
          string en = KeyTranslator.GetKeyNameEn(vk);
          _speaker.SpeakEnWord(en != null ? en : keyName);
        }
      }
    }

    /// <summary>按键翻译出的单个字符的处理：字母（含 TSF 裁决）、数字、汉字、标点。</summary>
    private void ProcessChar(char c, bool chineseMode) {
      if (KeyTranslator.IsLatinLetter(c)) {
        char lc2 = char.ToLowerInvariant(KeyTranslator.NormalizeLatin(c));
        uint ltrPid = _appStates.CurrentPid != 0 ? _appStates.CurrentPid : CurrentForegroundPid();
        bool tsfActiveNow = _tsfActivePids.Contains(ltrPid);
        bool tsfCompNow = _tsfBridge != null && _tsfBridge.IsComposing(ltrPid);
        if (ShiftOrCaps()) {
          /* 长按 Shift / 大写锁定：直接英文，立即朗读 */
          MarkTypingKeys();
          if (_chkLetters.Checked) _speaker.SpeakEn(lc2.ToString());
        } else if (_trayImeEnglish == null && InShiftLetterWindow()) {
          /* 托盘状态未知时，Shift 后的字母先缓冲，等状态确认后决定补读或丢弃 */
          _shiftLetterBuf.Append(lc2);
          EnsureShiftLetterTimer();
          _composing = true;
          MarkTypingKeys();
        } else if (tsfCompNow) {
          /* TSF 正在组字：编码，不读 */
          _composing = true;
          MarkTypingKeys();
        } else if (tsfActiveNow) {
          /* TSF 生效但此刻未组字：可能是编码首字母，也可能真是英文；
             缓冲 120ms，由 TSF 稍后状态裁决，绝不猜 */
              if (ImeEnglishNow) {
                /* 托盘已确认英文：直接上屏英文，立即朗读 */
                MarkTypingKeys();
                if (_chkLetters.Checked) _speaker.SpeakEn(lc2.ToString());
              } else {
                /* 托盘仍为中文：字母按五笔码处理，静默 */
                _composing = true;
                MarkTypingKeys();
                DebugLog("TSF_LETTER_SKIP tray-authority");
              }
        } else if ((chineseMode || _composing) && !ImeEnglishNow) {
          _composing = true;
          MarkTypingKeys();
        } else if (_chkLetters.Checked) {
          /* 英文模式读字母时也记录打字痕迹：若随后实际有中文上屏（Shift误判），
             上屏内容仍能通过校验被朗读，并触发英文状态自愈复位 */
          MarkTypingKeys();
          _speaker.SpeakEn(lc2.ToString());
        }
        return;
      }
      if (c == ' ') {
        return;
      }
      if (char.IsDigit(c) || (c >= '０' && c <= '９')) {
        if (!_composing && _chkDigits.Checked) {
          SpeakZh(KeyTranslator.DigitToChinese(c));
        }
        return;
      }
      if (KeyTranslator.IsCjk(c)) {
        _composing = false;
        MarkChineseCommit();
        _lastPacketCjkAt = DateTime.Now;
        bool diffAlreadySpoke = c.ToString() == _lastDiffCommitText &&
                                (DateTime.Now - _lastDiffCommitAt).TotalMilliseconds < 600;
        uint pktPid = _appStates.CurrentPid != 0 ? _appStates.CurrentPid : CurrentForegroundPid();
        DateTime pktTsfAt;
        bool pktTsfActive = _tsfActivePids.Contains(pktPid) &&
                            _tsfCommitAt.TryGetValue(pktPid, out pktTsfAt);
        /* 按键缓冲不依赖 UIA 轮询，慢应用名单（WPS/notepad 等）只应禁止
           差异通道的 UIA 查询，不能连按键通道一起关——否则慢应用中文完全静音 */
        if (!diffAlreadySpoke && !pktTsfActive) {
          BufferPacketZh(c);
        }
        return;
      }
      string pn = KeyTranslator.PunctName(c);
      if (pn != null) {
        if (chineseMode) {
          _composing = false;
          if (_chkPunct.Checked) {
            _lastPunctName = pn;
            _punctKeyPending = true;
            _lastPunctKeyAt = DateTime.Now;
            SchedulePunctSpeak(c);
          }
        } else if (_chkPunct.Checked) {
          SchedulePunctSpeak(c);
        }
        return;
      }
      if (char.IsWhiteSpace(c)) {
        return;
      }
    }

    /// <summary>Shift 松开时判定"单次轻按"：按下到松开 &lt;300ms 且中间无其它键，
    /// 且焦点可输入，才翻转当前程序的中/英记忆状态。</summary>
    private void HandleKeyUp(KeyHookEventArgs e) {
      bool isShift = e.Vk == 0x10 || e.Vk == 0xA0 || e.Vk == 0xA1;
      if (!isShift) return;
      /* Shift 松开：若期间没有按下其它键（单独按），补念 Shift */
      if (_shiftSpeakPending && _chkModifiers.Checked) {
        _shiftSpeakPending = false;
        _speaker.SpeakEnWord("Shift");
        DebugLog("ENW:Shift keyup");
      }
      if (!_shiftTapArmed) return;
      _shiftTapArmed = false;
      double heldMs = (DateTime.Now - _shiftDownAt).TotalMilliseconds;
      /* 多多五笔等输入法按住 Shift 约1秒也会切换；3秒内松开且中间无其它键都算切换 */
      if (heldMs >= 3000) {
        DebugLog("SHIFT_TAP_HOLD_IGNORED ms=" + heldMs.ToString("0"));
        return;
      }
      uint pid = _shiftTapPid;
      if (pid == 0) pid = CurrentForegroundPid();
      if (_shiftTrayStateAtDown.HasValue && _trayImeEnglish == _shiftTrayStateAtDown) {
        bool wasEng = _trayImeEnglish.Value;
        _trayImeEnglish = !wasEng;
        _lastStateFlipAt = DateTime.Now;
        _trayImeLast = wasEng ? "zh" : "en";
        _composing = false;
        DebugLog("SHIFT_TAP_FLIP tray-authority " + (wasEng ? "en->zh" : "zh->en") + " pid=" + pid);
        FlushShiftLetters();
      } else {
        DebugLog("SHIFT_TAP_FLIP_SKIP tray-authority pid=" + pid +
                 " down=" + (_shiftTrayStateAtDown.HasValue ? (_shiftTrayStateAtDown.Value ? "en" : "zh") : "null") +
                 " now=" + (_trayImeEnglish.HasValue ? (_trayImeEnglish.Value ? "en" : "zh") : "null"));
        if (!_shiftTrayStateAtDown.HasValue) {
          /* 状态未知（刚切换窗口被重置）：Shift 轻按即多多五笔的中英切换，
             最常见是从中文切英文——立即按英文处理让字母即时朗读，
             图标通道随后（约1秒）确认并纠正误判 */
          _trayImeEnglish = true;
          _trayImeLast = "en";
          _lastStateFlipAt = DateTime.Now;
          _composing = false;
          if (_trayImeIcon != null) {
            _trayImeIcon.RefreshNow();
            _trayImeIcon.ResetApplied();
          }
          DebugLog("SHIFT_TAP_ASSUME_EN null-state pid=" + pid);
          FlushShiftLetters();
        }
      }
      _shiftTrayStateAtDown = null;
    }

    private uint CurrentForegroundPid() {
      try {
        IntPtr h = Native.GetForegroundWindow();
        if (h == IntPtr.Zero) return 0;
        uint pid;
        Native.GetWindowThreadProcessId(h, out pid);
        return pid;
      } catch {
        return 0;
      }
    }

    Control ISelectionHost.InvokeControl { get { return this; } }
    Speaker ISelectionHost.Speaker { get { return _speaker; } }
    bool ISelectionHost.Listening { get { return _listening; } }
    bool ISelectionHost.TestMode { get { return _testMode; } }
    bool ISelectionHost.ClickSpeakEnabled { get { return _chkClickSpeak != null && _chkClickSpeak.Checked; } }
    bool ISelectionHost.LettersEnabled { get { return _chkLetters != null && _chkLetters.Checked; } }
    bool ISelectionHost.DigitsEnabled { get { return _chkDigits != null && _chkDigits.Checked; } }
    DateTime ISelectionHost.LastKeyAt { get { return _lastKeyAt; } }
    DateTime ISelectionHost.LastMouseDownAt { get { return _lastMouseDownAt; } }
    DateTime ISelectionHost.LastShiftKeyAt { get { return _lastShiftKeyAt; } }
    bool ISelectionHost.IsShiftHeld() { return ShiftHeld(); }
    bool ISelectionHost.IsForegroundResponsive() { return IsForegroundResponsive(); }
    bool ISelectionHost.IsOurProcessForeground() { return IsOurProcessForeground(); }
    uint ISelectionHost.CurrentForegroundPid() { return CurrentForegroundPid(); }
    void ISelectionHost.Log(string line) { DebugLog(line); }

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
          bool diffAlreadySpoke = delta == _lastDiffCommitText &&
                                  (DateTime.Now - _lastDiffCommitAt).TotalMilliseconds < 600;
          bool recentTyping = (DateTime.Now - _lastTypingCommitKeyAt).TotalMilliseconds < 1000;
          /* 中文已上屏：状态自愈为中文（即使朗读条件不满足） */
          if (!string.IsNullOrEmpty(delta) && HasCjk(delta)) {
            _composing = false;
            MarkChineseCommit();
          }
          if (!string.IsNullOrEmpty(delta) && recentTyping && !diffAlreadySpoke && !RecentlySpoken(delta)) {
            SpeakZh(delta);
            RememberSpoken(delta);
            MarkChineseCommit();
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

    private bool InShiftLetterWindow() {
      double ms = (DateTime.Now - _shiftPressedAt).TotalMilliseconds;
      return ms >= 0 && ms < 1200;
    }

    private void EnsureShiftLetterTimer() {
      if (_shiftLetterTimer == null) {
        _shiftLetterTimer = new System.Windows.Forms.Timer();
        _shiftLetterTimer.Interval = 250;
        _shiftLetterTimer.Tick += delegate { ShiftLetterTimerTick(); };
      }
      _shiftLetterTimer.Stop();
      _shiftLetterTimer.Start();
    }

    private void ShiftLetterTimerTick() {
      try {
        if (_trayFindTask != null && !_trayFindTask.IsCompleted && DateTime.Now < _shiftLetterDeadline) {
          _shiftLetterTimer.Start();
          return;
        }
        FlushShiftLetters();
      } catch { }
    }

    private void FlushShiftLetters() {
      if (_shiftLetterTimer != null) _shiftLetterTimer.Stop();
      if (_shiftLetterBuf.Length == 0) return;
      string text = _shiftLetterBuf.ToString();
      _shiftLetterBuf.Clear();
      if (ImeEnglishNow && _chkLetters.Checked) {
        _composing = false;
        foreach (char ch in text) _speaker.SpeakEn(ch.ToString());
        DebugLog("SHIFT_LETTER_READ [" + text + "]");
      } else {
        DebugLog("SHIFT_LETTER_DROP [" + text + "]");
      }
    }

    private void CancelShiftLetters(string reason) {
      if (ImeEnglishNow) return;
      if (_shiftLetterBuf.Length == 0) {
        if (_shiftLetterTimer != null) _shiftLetterTimer.Stop();
        return;
      }
      DebugLog("SHIFT_LETTER_CANCEL [" + _shiftLetterBuf + "] " + reason);
      _shiftLetterBuf.Clear();
      if (_shiftLetterTimer != null) _shiftLetterTimer.Stop();
    }

    /// <summary>VK_PACKET 逐字投递的汉字先短缓冲：150ms 内连续到达的
    /// 多字合并成词朗读（"什""么"→"什么"），避免一字一顿和多音字误读。</summary>
    private void BufferPacketZh(char c) {
      if (_packetZhTimer == null) {
        _packetZhTimer = new System.Windows.Forms.Timer();
        _packetZhTimer.Interval = PacketZhMergeMs;
        _packetZhTimer.Tick += delegate { FlushPacketZh(); };
      }
      _packetZhBuffer.Append(c);
      _packetZhTimer.Stop();
      _packetZhTimer.Start();
    }

    private void FlushPacketZh() {
      if (_packetZhTimer != null) _packetZhTimer.Stop();
      if (_packetZhBuffer.Length == 0) return;
      string text = _packetZhBuffer.ToString();
      _packetZhBuffer.Clear();
      if (string.IsNullOrEmpty(text)) return;
      if (text == _lastDiffCommitText &&
          (DateTime.Now - _lastDiffCommitAt).TotalMilliseconds < 800) {
        DebugLog("VK_PACKET_ZH_MERGE_SKIP tsf");
        return;
      }
      if (RecentlySpoken(text)) return;
      SpeakZh(text);
      RememberSpoken(text);
      if (_testMode) _autoSpoken += text.Length;
      DebugLog("VK_PACKET_ZH_MERGE [" + text + "]");
    }

    private bool RecentlySpoken(string text) { return _recentSpeech.IsDuplicate(text, _lastKeyAt, DateTime.Now); }

    private void RememberSpoken(string text) { _recentSpeech.Mark(text, DateTime.Now); }

    private static bool IsPureAsciiLetters(string s) { return SpeechText.IsPureAsciiLetters(s); }

    private static bool IsPureSpaces(string s) { return SpeechText.IsPureSpaces(s); }

    private static string StripCompositionLetters(string s) { return SpeechText.StripCompositionLetters(s); }

    private bool AllowPunctSpeak(string clean) {
      if (clean.Length > 1) return true;
      string pn = KeyTranslator.PunctName(clean[0]);
      if (pn == null) return true;
      if (!_punctKeyPending) return false;
      if ((DateTime.Now - _lastPunctKeyAt).TotalMilliseconds > 3000) return false;
      if (_lastPunctName != null && _lastPunctName != pn) return false;
      _punctKeyPending = false;
      _lastPunctName = null;
      return true;
    }

    /// <summary>标点延迟200ms朗读：让慢半拍的差异通道先读中文，保证语音顺序与键盘一致。</summary>
    private void SchedulePunctSpeak(char c) {
      string pn = KeyTranslator.PunctName(c);
      if (string.IsNullOrEmpty(pn)) return;
      _pendingPuncts.Append(c);
      if (_punctTimer == null) {
        _punctTimer = new System.Windows.Forms.Timer();
        _punctTimer.Interval = 200;
        _punctTimer.Tick += delegate { FlushPunctSpeak(); };
      }
      _punctTimer.Stop();
      _punctTimer.Start();
    }

    private void FlushPunctSpeak() {
      if (_pendingPuncts.Length == 0) return;
      string text = _pendingPuncts.ToString();
      _pendingPuncts.Clear();
      foreach (char c in text) {
        string pn = KeyTranslator.PunctName(c);
        if (pn == null) continue;
        if (!RecentlySpoken(pn)) {
          SpeakZh(pn);
          RememberSpoken(pn);
        }
      }
      _punctKeyPending = false;
      _lastPunctName = null;
    }

    /// <summary>跟踪前台进程：切换进程时登记状态表（新进程默认中文）。</summary>
    private void TrackFocus() {
      if (!_listening) return;
      if (!_testMode && IsOurProcessForeground()) return;
      try {
        IntPtr h = Native.GetForegroundWindow();
        if (h == IntPtr.Zero) return;
        uint pid;
        Native.GetWindowThreadProcessId(h, out pid);
        if (pid == 0 || pid == (uint)Process.GetCurrentProcess().Id) return;
        if (pid != _appStates.CurrentPid) {
          _trayImeEnglish = null;
          _trayImeLast = "";
          _lastStateFlipAt = DateTime.MinValue;
          /* 状态已失效：重置图标已应用标记，让图标尽快重新确认，缩短未知窗口 */
          if (_trayImeIcon != null) _trayImeIcon.ResetApplied();
          _appStates.SetCurrentPid(pid);
          /* 切到白名单应用（记事本/WPS 等）时启用 TSF 钩子，StartTsfHook 幂等 */
          StartTsfHook();
          /* 切换窗口时隐藏朗读按钮（朗读中可用 Esc 停止） */
          _selection.HideSelectionButton();
          DebugLog("FOCUS pid=" + pid + " app=" + _appStates.GetAppName(pid) +
                   " english=" + _appStates.IsEnglish(pid));
        }
        EnsureForegroundHook();
      } catch { }
    }

    private static uint _respPid;
    private static bool _respOk = true;
    private static DateTime _respAt = DateTime.MinValue;

    private static bool IsForegroundResponsive() {
      try {
        IntPtr h = Native.GetForegroundWindow();
        if (h == IntPtr.Zero) return false;
        uint pid;
        Native.GetWindowThreadProcessId(h, out pid);
        if (pid == _respPid && (DateTime.Now - _respAt).TotalMilliseconds < 1000) {
          return _respOk;
        }
        bool ok;
        using (Process p = Process.GetProcessById((int)pid)) {
          ok = p.Responding;
        }
        _respPid = pid;
        _respOk = ok;
        _respAt = DateTime.Now;
        return ok;
      } catch {
        return true;
      }
    }

    private static bool HasChineseText(string s) { return SpeechText.HasChineseText(s); }

    private static string LastLine(string s) { return SpeechText.LastLine(s); }

    private static string StripHeading(string s) { return SpeechText.StripHeading(s); }

    private static string FilterForSpeech(string s) { return SpeechText.FilterForSpeech(s); }

    private static bool HasCjk(string s) { return SpeechText.HasCjk(s); }
    private static string PunctSpokenForm(string s) { return SpeechText.PunctSpokenForm(s); }

    private static string ComputeInserted(string oldT, string newT, int caret) { return SpeechText.ComputeInserted(oldT, newT, caret); }


    private static bool IsModifierKey(uint vk) {
      return vk == 0x10 || vk == 0x11 || vk == 0x12 || vk == 0x5B || vk == 0x5C ||
             vk == 0xA0 || vk == 0xA1 || vk == 0xA2 || vk == 0xA3 || vk == 0xA4 || vk == 0xA5;
    }

    private static bool IsCandidateControl(uint vk) {
      if (vk == 0x09) return true;
      if (vk >= 0x30 && vk <= 0x39) return true;
      if (vk >= 0x60 && vk <= 0x69) return true;
      if (vk >= 0x21 && vk <= 0x28) return true;
      return false;
    }

    private void MarkChineseCommit() {
      CancelPendingKeySound();
      CancelShiftLetters("zh");
      _appStates.SetChineseCurrent();
      _lastZhCommitAt = DateTime.Now;
    }

    /// <summary>当前是否处于"直接上屏英文"状态：大写锁定开着=英文；
    /// 否则以托盘识别的中/英状态为准（Shift 轻按翻转，中文上屏自愈为中文）。</summary>
    private bool ImeEnglishNow {
      get {
        if (CapsLockOn()) return true;
        return _trayImeEnglish == true;
      }
    }

    private static bool CapsLockOn() {
      try {
        return (Native.GetKeyState(0x14) & 1) != 0;
      } catch {
        return false;
      }
    }

    private static bool ShiftOrCaps() {
      try {
        if ((Native.GetAsyncKeyState(0x10) & 0x8000) != 0) return true;
        if ((Native.GetKeyState(0x14) & 1) != 0) return true;
      } catch { }
      return false;
    }

    private static bool IsAltKey(uint vk) {
      return vk == 0x12 || vk == 0xA4 || vk == 0xA5;
    }

    private static bool AltDown() {
      try {
        if ((Native.GetAsyncKeyState(0x12) & 0x8000) != 0) return true;
        if ((Native.GetAsyncKeyState(0xA4) & 0x8000) != 0) return true;
        if ((Native.GetAsyncKeyState(0xA5) & 0x8000) != 0) return true;
      } catch { }
      return false;
    }

    private static bool CtrlDown() {
      try {
        if ((Native.GetAsyncKeyState(0x11) & 0x8000) != 0) return true;
        if ((Native.GetAsyncKeyState(0xA2) & 0x8000) != 0) return true;
        if ((Native.GetAsyncKeyState(0xA3) & 0x8000) != 0) return true;
      } catch { }
      return false;
    }

    private static bool ShiftDown() {
      try {
        if ((Native.GetAsyncKeyState(0x10) & 0x8000) != 0) return true;
        if ((Native.GetAsyncKeyState(0xA0) & 0x8000) != 0) return true;
        if ((Native.GetAsyncKeyState(0xA1) & 0x8000) != 0) return true;
      } catch { }
      return false;
    }

    private static bool WinDown() {
      try {
        if ((Native.GetAsyncKeyState(0x5B) & 0x8000) != 0) return true;
        if ((Native.GetAsyncKeyState(0x5C) & 0x8000) != 0) return true;
      } catch { }
      return false;
    }

    /* 钩子处理可能滞后于按键释放：300ms内观察到的修饰键按下也算"按住"，
       避免 Ctrl+V 等快键在钩子处理时修饰键已抬起而漏判 */
    private bool CtrlHeld() {
      return CtrlDown() || (DateTime.Now - _lastCtrlDownAt).TotalMilliseconds < 300;
    }

    private bool AltHeld() {
      return AltDown() || (DateTime.Now - _lastAltDownAt).TotalMilliseconds < 300;
    }

    private bool WinHeld() {
      return WinDown() || (DateTime.Now - _lastWinDownAt).TotalMilliseconds < 300;
    }

    private bool ShiftHeld() {
      return ShiftDown() || (DateTime.Now - _lastShiftDownAt).TotalMilliseconds < 300;
    }

    private static bool IsKeyDown(uint vk) {
      try {
        return (Native.GetAsyncKeyState((int)vk) & 0x8000) != 0;
      } catch {
        return false;
      }
    }

    private void StartModTimer() {
      if (_modTimer == null) {
        _modTimer = new System.Windows.Forms.Timer();
        _modTimer.Interval = 250;
        _modTimer.Tick += delegate { FlushPendingMod(); };
      }
      _modTimer.Stop();
      _modTimer.Start();
    }

    private void FlushPendingMod() {
      if (_modTimer != null) _modTimer.Stop();
      if (_pendingModName == null) return;
      /* 修饰键仍按住（可能在犹豫组合键）：继续等待 */
      if (IsKeyDown(_pendingModVk)) {
        StartModTimer();
        return;
      }
      string n = _pendingModName;
      _pendingModName = null;
      _pendingModVk = 0;
      _speaker.SpeakEnWord(n);
      DebugLog("MOD_ALONE [" + n + "]");
    }

    private void CancelPendingMod() {
      _pendingModName = null;
      _pendingModVk = 0;
      if (_modTimer != null) _modTimer.Stop();
    }

    private void PlayKeySound(string path) {
      try {
        if (string.IsNullOrEmpty(path)) return;
        System.Media.SoundPlayer p;
        if (!_keySoundPlayers.TryGetValue(path, out p)) {
          p = new System.Media.SoundPlayer(path);
          p.Load();
          _keySoundPlayers[path] = p;
        }
        p.Play();
        DebugLog("KEY_SOUND [" + System.IO.Path.GetFileName(path) + "]");
      } catch { }
    }

    /// <summary>按键音效让路：中文语音正在念/排队时不立即响，等念完再响；
    /// 连续按键只保留最近一次，避免音效堆积。</summary>
    private void RequestKeySound(string path) {
      if (_speaker.IsBusy) {
        _pendingKeySoundPath = path;
        _pendingKeySoundAt = DateTime.Now;
        DebugLog("KEY_SOUND_DEFER [" + System.IO.Path.GetFileName(path) + "]");
        return;
      }
      PlayKeySound(path);
    }

    private void CheckPendingKeySound() {
      if (_pendingKeySoundPath == null) return;
      if (_speaker.IsBusy) return;
      string path = _pendingKeySoundPath;
      _pendingKeySoundPath = null;
      if ((DateTime.Now - _pendingKeySoundAt).TotalMilliseconds > 3000) {
        DebugLog("KEY_SOUND_STALE_DROP");
        return;
      }
      PlayKeySound(path);
    }

    private void CancelPendingKeySound() {
      _pendingKeySoundPath = null;
    }

    private string BuildChord(KeyHookEventArgs e) {
      System.Collections.Generic.List<string> parts = new System.Collections.Generic.List<string>();
      if (CtrlHeld()) parts.Add("Control");
      if (ShiftHeld()) parts.Add("Shift");
      if (AltHeld()) parts.Add("Alt");
      if (WinHeld()) parts.Add("Windows");
      string key = ChordKeyName(e);
      if (key == null) return null;
      parts.Add(key);
      return string.Join(" ", parts.ToArray());
    }

    /// <summary>组合键若含单个字母（如 Control A），用 say-as characters 包裹字母，
    /// 让中文音色清楚念出字母，不会一带而过。</summary>
    private string BuildChordSsml(KeyHookEventArgs e) {
      string key = ChordKeyName(e);
      if (key == null || key.Length != 1 || key[0] < 'A' || key[0] > 'Z') return null;
      System.Collections.Generic.List<string> parts = new System.Collections.Generic.List<string>();
      if (CtrlHeld()) parts.Add("Control");
      if (ShiftHeld()) parts.Add("Shift");
      if (AltHeld()) parts.Add("Alt");
      if (WinHeld()) parts.Add("Windows");
      parts.Add("<say-as interpret-as=\"characters\">" + key + "</say-as>");
      return string.Join(" ", parts.ToArray());
    }

    private string ChordKeyName(KeyHookEventArgs e) {
      if (e.Vk >= 0x41 && e.Vk <= 0x5A) return ((char)('A' + (int)(e.Vk - 0x41))).ToString();
      if (e.Vk >= 0x30 && e.Vk <= 0x39) return DigitWordEn(e.Vk - 0x30);
      if (e.Vk >= 0x60 && e.Vk <= 0x69) return DigitWordEn(e.Vk - 0x60);
      return KeyTranslator.GetKeyNameEn(e.Vk);
    }

    private static string DigitWordEn(uint d) {
      switch (d) {
        case 0: return "Zero";
        case 1: return "One";
        case 2: return "Two";
        case 3: return "Three";
        case 4: return "Four";
        case 5: return "Five";
        case 6: return "Six";
        case 7: return "Seven";
        case 8: return "Eight";
        case 9: return "Nine";
      }
      return null;
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
      if (!_settings.ClickSpeakInitialized) {
        /* 旧版本升级：选中朗读默认开启（旧设置里没有这个开关的初始化标记） */
        _settings.ClickSpeak = true;
        _settings.ClickSpeakInitialized = true;
        _chkClickSpeak.Checked = true;
      }
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

    private void OnVoiceSelected() {
      if (_loading) return;
      try {
        _speaker.RefreshVoices(CurrentZhVoice(), CurrentEnVoice());
        SaveSettings();
        DebugLog("VOICE_SELECTED zh=[" + CurrentZhVoice() + "] en=[" + CurrentEnVoice() + "]");
      } catch { }
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
        _settings.ClickSpeakInitialized = true;
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
      StopTsfHook();
      if (_tsfNotifyWindow != null) {
        try { _tsfNotifyWindow.Dispose(); } catch { }
        _tsfNotifyWindow = null;
      }
      DisposeTimers();
      DisposeKeySoundPlayers();
      if (_tray != null) {
        _tray.Visible = false;
        _tray.Dispose();
      }
      if (_selection != null) {
        _selection.Dispose();
        _selection = null;
      }
      if (_trayImeIcon != null) {
        _trayImeIcon.Dispose();
        _trayImeIcon = null;
      }
      LogTest("EXIT");
      if (_speaker != null) _speaker.Dispose();
      base.OnFormClosing(e);
    }

    protected override void OnLoad(EventArgs e) {
      base.OnLoad(e);
      _hook.KeyEvent += OnKeyBridge;
      _mouseHook.LeftButtonDown += delegate {
        _lastMouseDownAt = DateTime.Now;
        _selection.NotifyMouseDown();
        /* Shift+点击 = 选择手势 */
        if (ShiftHeld()) _lastShiftKeyAt = DateTime.Now;
        /* 按住 Shift 点选文字时，松开不视为中英切换 */
        _shiftTapArmed = false;
      };
      _mouseHook.LeftButtonUp += delegate {
        _selection.NotifyMouseUp();
      };
      /* TSF 钩子已停用：最近无任何有效提交事件，且注入是历史闪退根源；
         中文朗读由输入法事件/文本差异通道覆盖 */
      _selection.InitWatcher();
      InitTrayImeWatcher();
    }

    private void OnKeyBridge(object sender, KeyHookEventArgs e) {
      _lastHookEventAt = DateTime.Now;
      try { BeginInvoke((MethodInvoker)delegate { OnKey(sender, e); }); } catch { }
    }

    /// <summary>钩子静默失效自愈：Raw Input 有按键活动而钩子 2 秒未投递 → 重启钩子。
    /// 现有看门狗只查"线程存活"，查不出"活着但不投递"的静默失效。</summary>
    private void CheckHookHealth() {
      if (!_listening || _rawInput == null) return;
      if (!_hook.IsInstalled) return;
      if ((DateTime.Now - _lastHookEventAt).TotalMilliseconds < 2000) return;
      if ((DateTime.Now - _rawInput.LastEventAt).TotalMilliseconds > 1000) return;
      if ((DateTime.Now - _lastHookRestartAt).TotalMilliseconds < 10000) return;
      _lastHookRestartAt = DateTime.Now;
      DebugLog("HOOK_SILENT_RESTART");
      try { _hook.Uninstall(); } catch { }
      try { _hook.Install(); } catch { }
      if (!_hook.IsInstalled) {
        _listening = false;
        UpdateUi();
      }
    }

    protected override void OnFormClosed(FormClosedEventArgs e) {
      _hook.KeyEvent -= OnKeyBridge;
      _hook.Dispose();
      _mouseHook.Dispose();
      if (_rawInput != null) {
        try { _rawInput.Dispose(); } catch { }
        _rawInput = null;
      }
      base.OnFormClosed(e);
    }

    private void DisposeTimers() {
      System.Windows.Forms.Timer[] timers = new System.Windows.Forms.Timer[] {
        _imeTimer, _uiTimer, _hbTimer, _autoTimer, _autoExitTimer, _injectTimer,
        _punctTimer, _modTimer, _shiftLetterTimer, _packetZhTimer
      };
      foreach (System.Windows.Forms.Timer t in timers) {
        if (t == null) continue;
        try { t.Stop(); t.Dispose(); } catch { }
      }
    }

    private void DisposeKeySoundPlayers() {
      foreach (System.Media.SoundPlayer p in _keySoundPlayers.Values) {
        try { p.Dispose(); } catch { }
      }
      _keySoundPlayers.Clear();
    }

  }
}
