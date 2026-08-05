using System;
using System.Collections.Generic;

namespace GenDaLangDu.Tests {
  public static class T {
    private static int _passed;
    private static int _failed;
    private static readonly List<string> _failures = new List<string>();

    public static void Run(string name, Action action) {
      try {
        action();
        _passed++;
        Console.WriteLine("PASS " + name);
      } catch (Exception ex) {
        _failed++;
        _failures.Add(name + " :: " + ex.Message);
        Console.WriteLine("FAIL " + name + " :: " + ex.Message);
      }
    }

    public static void Eq<T>(T actual, T expected, string what) {
      if (!object.Equals(actual, expected)) {
        throw new Exception(what + ": expected [" + expected + "] got [" + actual + "]");
      }
    }

    public static void True(bool cond, string what) {
      if (!cond) throw new Exception("expected true: " + what);
    }

    public static int Finish() {
      Console.WriteLine("----");
      Console.WriteLine("passed=" + _passed + " failed=" + _failed);
      if (_failures.Count > 0) {
        Console.WriteLine("FAILURES:");
        foreach (string f in _failures) Console.WriteLine("  " + f);
        return 1;
      }
      return 0;
    }
  }
}
