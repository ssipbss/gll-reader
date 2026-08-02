using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace GenDaLangDu {
  /// <summary>
  /// 按程序（进程）记忆输入法中/英状态。
  /// 规则：每个程序默认中文；进程重启归零；单次轻按 Shift 翻转
  /// （调用方负责检查焦点可输入）；大写锁定=英文由调用方叠加；
  /// 中文上屏自愈为中文、英文直接上屏自愈为英文。
  /// </summary>
  public sealed class AppStateTracker {
    /* 可注入的进程启动时间提供者（测试用），默认读真实进程 */
    internal static Func<uint, DateTime> StartTimeProvider = SafeStartTime;

    private sealed class Entry {
      public bool Chinese = true;
      public DateTime LastActive = DateTime.Now;
      public DateTime ProcStartTime = DateTime.MinValue;
    }

    private readonly Dictionary<uint, Entry> _states = new Dictionary<uint, Entry>();
    private uint _currentPid;

    public uint CurrentPid {
      get { return _currentPid; }
    }

    public bool IsEnglishCurrent() {
      if (_currentPid == 0) return false;
      return IsEnglish(_currentPid);
    }

    public bool IsEnglish(uint pid) {
      Entry e;
      if (pid == 0 || !_states.TryGetValue(pid, out e)) return false;
      return !e.Chinese;
    }

    public bool IsChineseCurrent() {
      return !IsEnglishCurrent();
    }

    /// <summary>切换当前前台进程到 pid；新进程（含 pid 被复用的新实例）默认中文。</summary>
    public void SetCurrentPid(uint pid) {
      if (pid == 0) return;
      Entry e;
      DateTime start = StartTimeProvider(pid);
      if (!_states.TryGetValue(pid, out e)) {
        e = new Entry();
        _states[pid] = e;
      } else if (start != DateTime.MinValue && e.ProcStartTime != DateTime.MinValue &&
                 e.ProcStartTime != start) {
        /* 同一 pid 已被新进程占用：状态归零回中文 */
        e.Chinese = true;
      }
      e.LastActive = DateTime.Now;
      if (start != DateTime.MinValue) e.ProcStartTime = start;
      _currentPid = pid;
      Prune();
    }

    public void ToggleChinese(uint pid, bool chinese) {
      Entry e = GetOrCreate(pid);
      e.Chinese = chinese;
    }

    public void SetChinese(uint pid) {
      GetOrCreate(pid).Chinese = true;
    }

    public void SetEnglish(uint pid) {
      GetOrCreate(pid).Chinese = false;
    }

    public void SetChineseCurrent() {
      if (_currentPid != 0) SetChinese(_currentPid);
    }

    public void SetEnglishCurrent() {
      if (_currentPid != 0) SetEnglish(_currentPid);
    }

    public string GetAppName(uint pid) {
      try {
        using (Process p = Process.GetProcessById((int)pid)) return p.ProcessName;
      } catch {
        return pid.ToString();
      }
    }

    public void Clear() {
      _states.Clear();
      _currentPid = 0;
    }

    private Entry GetOrCreate(uint pid) {
      Entry e;
      if (!_states.TryGetValue(pid, out e)) {
        e = new Entry();
        _states[pid] = e;
      }
      e.LastActive = DateTime.Now;
      return e;
    }

    private static DateTime SafeStartTime(uint pid) {
      try {
        using (Process p = Process.GetProcessById((int)pid)) return p.StartTime;
      } catch {
        return DateTime.MinValue;
      }
    }

    /* 超过 64 个进程时，清理 30 分钟未活跃的旧状态，避免无限增长 */
    private void Prune() {
      if (_states.Count < 64) return;
      DateTime cutoff = DateTime.Now.AddMinutes(-30);
      List<uint> dead = new List<uint>();
      foreach (KeyValuePair<uint, Entry> kv in _states) {
        if (kv.Value.LastActive < cutoff) dead.Add(kv.Key);
      }
      foreach (uint pid in dead) _states.Remove(pid);
    }
  }
}
