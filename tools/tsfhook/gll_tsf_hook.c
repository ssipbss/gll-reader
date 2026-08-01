/*
 * gll_tsf_hook.dll — 归零归零 输入法提交观察钩子（精简版）
 *
 * 仅保留一条被动通道：监听 WM_IME_* 消息，读取 IMM 输入法
 * 的上屏结果（GCS_RESULTSTR）并转发给主程序。
 * 不创建任何 TSF/COM 对象，无注入风险、无崩溃面。
 * Chromium 系应用中文本就由按键通道（VK_PACKET）朗读，本 DLL
 * 只补充 WPS 等传统 IMM 路径应用的中文提交。
 */
#include <windows.h>
#include <stdarg.h>
#include <wchar.h>

int __stdcall WideCharToMultiByte(UINT CodePage, DWORD dwFlags,
                                  const WCHAR *lpWideCharStr, int cchWideChar,
                                  char *lpMultiByteStr, int cbMultiByte,
                                  const char *lpDefaultChar, BOOL *lpUsedDefaultChar);
#define CP_UTF8 65001

#define GLL_WND_CLASS L"GLL_TSF_NOTIFY"
#define GLL_MAX_TEXT 512

/* IMM 相关常量 */
#define WM_IME_STARTCOMPOSITION 0x010D
#define WM_IME_ENDCOMPOSITION   0x010E
#define WM_IME_COMPOSITION      0x010F
#define WM_IME_CHAR             0x0286
#define GCS_RESULTSTR           0x0800

typedef void *HIMC;
HIMC __stdcall ImmGetContext(HWND hWnd);
BOOL __stdcall ImmReleaseContext(HWND hWnd, HIMC hIMC);
int  __stdcall ImmGetCompositionStringW(HIMC hIMC, DWORD dwIndex, void *lpBuf, DWORD dwBufLen);

static HMODULE g_hinst;
static HHOOK g_hook;
static HWND g_hwndCache;
static UINT g_commitMsg;
static int g_debug = 0;
static WCHAR g_lastSent[GLL_MAX_TEXT];
static DWORD g_lastSentTick;

static void Dbg(const WCHAR *fmt, ...) {
  if (!g_debug) return;
  WCHAR buf[1024];
  va_list ap;
  va_start(ap, fmt);
  wvsprintfW(buf, fmt, ap);
  va_end(ap);
  SYSTEMTIME st;
  GetLocalTime(&st);
  WCHAR full[1200];
  wsprintfW(full, L"%02u:%02u:%02u.%03u %lu %s\r\n",
            st.wHour, st.wMinute, st.wSecond, st.wMilliseconds,
            GetCurrentProcessId(), buf);
  int len = lstrlenW(full);
  char utf8[2048];
  int n = WideCharToMultiByte(CP_UTF8, 0, full, len, utf8, sizeof(utf8) - 2, NULL, NULL);
  if (n <= 0) return;
  HANDLE h = CreateFileW(L"C:\\tmp\\gll_tsf_hook.log", FILE_APPEND_DATA,
                         FILE_SHARE_READ | FILE_SHARE_WRITE, NULL,
                         OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
  if (h == INVALID_HANDLE_VALUE) return;
  DWORD written = 0;
  WriteFile(h, utf8, (DWORD)n, &written, NULL);
  CloseHandle(h);
}

static BOOL HasCjk(const WCHAR *s, int n) {
  int i;
  for (i = 0; i < n; i++) {
    WCHAR c = s[i];
    if (c >= 0x2E80 && c <= 0x9FFF) return TRUE;
    if (c >= 0xF900 && c <= 0xFAFF) return TRUE;
    if (c >= 0xFF00 && c <= 0xFFEF &&
        !(c >= 0xFF10 && c <= 0xFF19) &&
        !(c >= 0xFF21 && c <= 0xFF3A) &&
        !(c >= 0xFF41 && c <= 0xFF5A)) return TRUE;
  }
  return FALSE;
}

static void SendCommit(const WCHAR *text, int len) {
  if (len <= 0 || len >= GLL_MAX_TEXT) return;
  if (!HasCjk(text, len)) return;
  /* 同一文本 400ms 内只发一次（部分输入法重复投递） */
  if (wcsncmp(g_lastSent, text, len) == 0 && g_lastSent[len] == 0 &&
      (GetTickCount() - g_lastSentTick) < 400) return;
  wcsncpy(g_lastSent, text, len);
  g_lastSent[len] = 0;
  g_lastSentTick = GetTickCount();
  HWND hwnd = g_hwndCache;
  if (!hwnd || !IsWindow(hwnd)) {
    hwnd = FindWindowW(GLL_WND_CLASS, NULL);
    g_hwndCache = hwnd;
  }
  if (!hwnd) return;
  if (!g_commitMsg) g_commitMsg = RegisterWindowMessageW(L"GLL_TSF_COMMIT");
  if (!g_commitMsg) return;
  int cap = len;
  if (cap > 220) cap = 220;
  WCHAR atomText[256];
  wsprintfW(atomText, L"%lu|", GetCurrentProcessId());
  wcsncat(atomText, text, cap);
  atomText[255] = 0;
  ATOM atom = GlobalAddAtomW(atomText);
  if (!atom) return;
  PostMessageW(hwnd, g_commitMsg, (WPARAM)GetCurrentProcessId(), (LPARAM)atom);
  Dbg(L"SendCommit OK text=[%s] atom=%u", text, atom);
}

/* 读取 IMM 输入法的上屏结果（仅汉字，纯被动，不创建任何COM对象） */
static void CaptureImeResult(HWND hwnd) {
  HWND focus = GetFocus();
  HWND target = focus ? focus : hwnd;
  if (!target) return;
  HIMC imc = ImmGetContext(target);
  if (!imc) return;
  WCHAR buf[GLL_MAX_TEXT];
  int n = ImmGetCompositionStringW(imc, GCS_RESULTSTR, buf, (GLL_MAX_TEXT - 1) * 2);
  ImmReleaseContext(target, imc);
  if (n <= 0) return;
  int chars = n / 2;
  if (chars >= GLL_MAX_TEXT) chars = GLL_MAX_TEXT - 1;
  buf[chars] = 0;
  Dbg(L"IMM result [%s] len=%d", buf, chars);
  SendCommit(buf, chars);
}

static LRESULT CALLBACK HookProc(int nCode, WPARAM wParam, LPARAM lParam) {
  if (nCode == HC_ACTION) {
    MSG *msg = (MSG*)lParam;
    if (msg) {
      if (msg->message == WM_IME_ENDCOMPOSITION ||
          msg->message == WM_IME_CHAR ||
          (msg->message == WM_IME_COMPOSITION && (msg->lParam & GCS_RESULTSTR))) {
        CaptureImeResult(msg->hwnd);
      }
    }
  }
  return CallNextHookEx(g_hook, nCode, wParam, lParam);
}

__declspec(dllexport) BOOL WINAPI GllInstallHook(void) {
  if (g_hook) return TRUE;
  g_hook = SetWindowsHookExW(WH_GETMESSAGE, HookProc, g_hinst, 0);
  if (!g_hook) {
    Dbg(L"SetWindowsHookEx fail err=%lu", GetLastError());
    return FALSE;
  }
  Dbg(L"hook installed");
  return TRUE;
}

__declspec(dllexport) BOOL WINAPI GllUninstallHook(void) {
  if (g_hook) {
    UnhookWindowsHookEx(g_hook);
    g_hook = NULL;
    Dbg(L"hook uninstalled");
  }
  return TRUE;
}

__declspec(dllexport) void WINAPI GllSetDebug(int on) {
  g_debug = on;
}

BOOL WINAPI DllMain(HINSTANCE hinst, DWORD reason, LPVOID reserved) {
  switch (reason) {
  case DLL_PROCESS_ATTACH:
    g_hinst = hinst;
    g_debug = GetEnvironmentVariableW(L"GLL_TSF_DEBUG", NULL, 0) > 0 ? 1 : 0;
    break;
  case DLL_PROCESS_DETACH:
    if (g_hook) UnhookWindowsHookEx(g_hook);
    break;
  }
  return TRUE;
}
