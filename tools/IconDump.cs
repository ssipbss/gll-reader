using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

class IconDump {
  [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
  private static extern uint ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, uint count);

  [DllImport("user32.dll")]
  private static extern bool DestroyIcon(IntPtr h);

  private static void Main(string[] args) {
    string file = args[0];
    string outDir = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "dd_icons");
    Directory.CreateDirectory(outDir);
    uint count = ExtractIconEx(file, -1, null, null, 0);
    Console.WriteLine("count=" + count);
    for (int i = 0; i < (int)count; i++) {
      IntPtr[] large = new IntPtr[1];
      IntPtr[] small = new IntPtr[1];
      ExtractIconEx(file, i, large, small, 1);
      try {
        using (Icon ic = Icon.FromHandle(large[0])) {
          using (Bitmap bmp = ic.ToBitmap()) {
            string p = Path.Combine(outDir, "icon_" + i + "_" + bmp.Width + "x" + bmp.Height + ".png");
            bmp.Save(p, ImageFormat.Png);
            Console.WriteLine(p);
          }
        }
      } catch (Exception ex) {
        Console.WriteLine("icon_" + i + " ERR " + ex.Message);
      }
      if (large[0] != IntPtr.Zero) DestroyIcon(large[0]);
      if (small[0] != IntPtr.Zero) DestroyIcon(small[0]);
    }
  }
}
