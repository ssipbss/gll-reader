using System;
using System.Windows.Forms;

namespace GenDaLangDu {
  static class Program {
    [STAThread]
    static void Main(string[] args) {
      bool createdNew;
      using (System.Threading.Mutex mutex = new System.Threading.Mutex(true, "GenDaLangDu_SingleInstance", out createdNew)) {
        if (!createdNew && !IsTestMode(args)) {
          bool got = false;
          try {
            for (int i = 0; i < 30; i++) {
              if (mutex.WaitOne(100)) {
                got = true;
                break;
              }
            }
          } catch { }
          if (!got) {
            MessageBox.Show("归零归零已经在运行了。", "归零归零");
            return;
          }
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm(args));
      }
    }

    private static bool IsTestMode(string[] args) {
      for (int i = 0; i < args.Length; i++) {
        if (args[i] == "--test") return true;
      }
      return false;
    }
  }
}
