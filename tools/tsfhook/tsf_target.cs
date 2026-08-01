using System;
using System.Drawing;
using System.Windows.Forms;

public class TargetForm : Form {
  private TextBox _tb;
  private string _last = "";

  public TargetForm() {
    Text = "GLL Target";
    _tb = new TextBox();
    _tb.Multiline = true;
    _tb.Dock = DockStyle.Fill;
    _tb.Font = new Font("Microsoft YaHei", 14F);
    _tb.WordWrap = false;
    Controls.Add(_tb);
    Load += delegate {
      try { System.IO.File.AppendAllText(@"C:\tmp\tsf_target.log", "LOADED\r\n"); } catch { }
    };
    var t = new Timer();
    t.Interval = 300;
    t.Tick += delegate {
      try {
        if (_tb.Text != _last) {
          _last = _tb.Text;
          System.IO.File.AppendAllText(@"C:\tmp\tsf_target.log",
            DateTime.Now.ToString("HH:mm:ss.fff") + " TEXT=[" + _tb.Text + "]\r\n");
        }
      } catch { }
    };
    t.Start();
  }

  [STAThread]
  public static void Main() {
    try { System.IO.File.Delete(@"C:\tmp\tsf_target.log"); } catch { }
    Application.EnableVisualStyles();
    Application.Run(new TargetForm());
  }
}
