using System;
using System.IO;
using System.Speech.Synthesis;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

class PlayStopTest {
  [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
  private static extern uint mciSendString(string command, StringBuilder returnString, int returnLength, IntPtr hwndCallback);

  private static volatile bool _stop;

  private static void Main() {
    string wav = Path.Combine(Path.GetTempPath(), "playstop_test.wav");
    using (SpeechSynthesizer s = new SpeechSynthesizer()) {
      s.SetOutputToWaveFile(wav);
      s.Speak("这是一段用于测试停止功能的较长的中文朗读内容，它会持续播放一段时间，用来验证程序能否立即停止播放。一二三四五六七八九十，一二三四五六七八九十，一二三四五六七八九十。");
    }
    DateTime start = DateTime.Now;
    Thread t = new Thread(delegate() {
      mciSendString("open \"" + wav + "\" type waveaudio alias tst", null, 0, IntPtr.Zero);
      mciSendString("play tst", null, 0, IntPtr.Zero);
      while (true) {
        if (_stop) { mciSendString("stop tst", null, 0, IntPtr.Zero); break; }
        StringBuilder sb = new StringBuilder(32);
        mciSendString("status tst mode", sb, 32, IntPtr.Zero);
        if (!sb.ToString().StartsWith("playing", StringComparison.OrdinalIgnoreCase)) break;
        Thread.Sleep(50);
      }
      mciSendString("close tst", null, 0, IntPtr.Zero);
      Console.WriteLine("PLAYSYNC_RETURNED after " + (DateTime.Now - start).TotalSeconds.ToString("0.00") + "s");
    });
    t.Start();
    Thread.Sleep(2000);
    Console.WriteLine("STOP_CALLED at " + (DateTime.Now - start).TotalSeconds.ToString("0.00") + "s");
    _stop = true;
    mciSendString("stop tst", null, 0, IntPtr.Zero);
    t.Join(5000);
    Console.WriteLine("DONE");
  }
}
