using System;
using System.Threading;
using System.Windows.Forms;

class ShiftTapTest : Form {
  protected override void OnShown(EventArgs e) {
    base.OnShown(e);
    Text = "ShiftTapTest";
    ClientSize = new System.Drawing.Size(320, 120);
    TextBox tb = new TextBox();
    tb.Location = new System.Drawing.Point(20, 30);
    tb.Width = 260;
    tb.Text = "这是一段用于测试选中朗读的文字内容，朗读按钮应该出现在鼠标旁边。";
    Controls.Add(tb);
    tb.Focus();
    Activate();
    /* 仅作为"可输入焦点"宿主，等待主程序测试序列模拟按键后自动关闭 */
    Thread t = new Thread(delegate() {
      Thread.Sleep(10000);
      try {
        Invoke((MethodInvoker)delegate { Close(); });
      } catch { }
    });
    t.IsBackground = true;
    t.Start();
  }

  [STAThread]
  private static void Main() {
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);
    Application.Run(new ShiftTapTest());
  }
}
