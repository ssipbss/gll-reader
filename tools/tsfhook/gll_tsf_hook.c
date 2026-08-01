/*
 * gll_tsf_hook.dll — 归零归零 TSF 提交观察钩子
 *
 * 原理：全局 WH_GETMESSAGE 钩子使本 DLL 被系统加载进每个 GUI 进程；
 * 在前台线程内创建 TSF 线程管理器，监听 ITfTextEditSink::OnEndEdit，
 * 把输入法刚上屏的汉字通过 WM_COPYDATA 发给主程序（窗口类 GLL_TSF_NOTIFY）。
 * 只观察、不修改任何输入。
 */
#include <windows.h>
#include <stdio.h>
#include <stdarg.h>
#include <wchar.h>
#include <stddef.h>

/* ---------------- COM 最小声明（TCC 头不包含 objbase.h） ---------------- */
#ifndef GLL_COM_DECLS
#define GLL_COM_DECLS
static const GUID IID_IUnknown =
  {0x00000000,0x0000,0x0000,{0xC0,0x00,0x00,0x00,0x00,0x00,0x00,0x46}};
HRESULT __stdcall CoInitializeEx(void *pvReserved, DWORD dwCoInit);
HRESULT __stdcall CoInitialize(void *pvReserved);
void __stdcall CoUninitialize(void);
HRESULT __stdcall CoCreateInstance(const GUID *rclsid, void *pUnkOuter,
                                   DWORD dwClsContext, const GUID *riid,
                                   void **ppv);
int __stdcall WideCharToMultiByte(UINT CodePage, DWORD dwFlags,
                                  const WCHAR *lpWideCharStr, int cchWideChar,
                                  char *lpMultiByteStr, int cbMultiByte,
                                  const char *lpDefaultChar, BOOL *lpUsedDefaultChar);
#define CP_UTF8 65001
#define CLSCTX_INPROC_SERVER 1
#define COINIT_APARTMENTTHREADED 0x2
#ifndef RPC_E_CHANGED_MODE
#define RPC_E_CHANGED_MODE 0x80010106L
#endif
#endif

#define GLL_WND_CLASS L"GLL_TSF_NOTIFY"
#define GLL_COPYDATA_ID 0x47544C
#define TF_GTP_INCL_TEXT 1
#define GLL_MAX_TEXT 512

/* IMM 相关常量与声明 */
#define WM_IME_STARTCOMPOSITION 0x010D
#define WM_IME_ENDCOMPOSITION   0x010E
#define WM_IME_COMPOSITION      0x010F
#define WM_IME_NOTIFY           0x0282
#define WM_IME_CHAR             0x0286
#define GCS_RESULTSTR           0x0800
#define GCS_COMPSTR             0x0008

typedef void *HIMC;
HIMC __stdcall ImmGetContext(HWND hWnd);
BOOL __stdcall ImmReleaseContext(HWND hWnd, HIMC hIMC);
int  __stdcall ImmGetCompositionStringW(HIMC hIMC, DWORD dwIndex, void *lpBuf, DWORD dwBufLen);
int  __stdcall ImmGetCompositionStringA(HIMC hIMC, DWORD dwIndex, void *lpBuf, DWORD dwBufLen);
BOOL __stdcall ImmGetOpenStatus(HIMC hIMC);


/* ---------------- GUID ---------------- */
static const GUID CLSID_TF_ThreadMgr =
  {0x529A9E6B,0x6587,0x4F23,{0xAB,0x9E,0x9C,0x7D,0x68,0x3E,0x3C,0x50}};
static const GUID IID_ITfThreadMgr =
  {0xAA80E801,0x2021,0x11D2,{0x93,0xE0,0x00,0x60,0xB0,0x67,0xB8,0x6E}};
static const GUID IID_ITfSource =
  {0x4EA48A35,0x60AE,0x446F,{0x8F,0xD6,0xE6,0xA8,0xD8,0x24,0x59,0xF7}};
static const GUID IID_ITfThreadMgrEventSink =
  {0xAA80E80E,0x2021,0x11D2,{0x93,0xE0,0x00,0x60,0xB0,0x67,0xB8,0x6E}};
static const GUID IID_ITfDocumentMgr =
  {0xAA80E7F4,0x2021,0x11D2,{0x93,0xE0,0x00,0x60,0xB0,0x67,0xB8,0x6E}};
static const GUID IID_ITfContext =
  {0xAA80E7FD,0x2021,0x11D2,{0x93,0xE0,0x00,0x60,0xB0,0x67,0xB8,0x6E}};
static const GUID IID_ITfTextEditSink =
  {0x8127D409,0xCCD3,0x4683,{0x96,0x7A,0xB4,0x3D,0x5B,0x48,0x2B,0xF7}};
static const GUID IID_ITfEditRecord =
  {0x42D4D099,0x7C1A,0x4A89,{0xB8,0x36,0x6C,0x6F,0x22,0x16,0x0D,0xF0}};
static const GUID IID_IEnumTfRanges =
  {0xF99D3F40,0x8E32,0x11D2,{0xBF,0x46,0x00,0x10,0x5A,0x27,0x99,0xB5}};
static const GUID IID_ITfRange =
  {0xAA80E7FF,0x2021,0x11D2,{0x93,0xE0,0x00,0x60,0xB0,0x67,0xB8,0x6E}};
static const GUID GUID_PROP_COMPOSING =
  {0xe12ac060,0xaf15,0x11d2,{0xaf,0xc5,0x00,0x10,0x5a,0x27,0x99,0xb5}};

typedef struct tagVARIANT {
  unsigned short vt;
  unsigned short wReserved1;
  unsigned short wReserved2;
  unsigned short wReserved3;
  char payload[8];
} VARIANT;
typedef struct ITfRange ITfRange;
#define VT_EMPTY 0
#define VT_BOOL 11

typedef struct ITfReadOnlyPropertyVtbl {
  HRESULT (STDMETHODCALLTYPE *QueryInterface)(void*, REFIID, void**);
  ULONG   (STDMETHODCALLTYPE *AddRef)(void*);
  ULONG   (STDMETHODCALLTYPE *Release)(void*);
  HRESULT (STDMETHODCALLTYPE *GetValue)(void*, DWORD, ITfRange*, VARIANT*);
  HRESULT (STDMETHODCALLTYPE *EnumRanges)(void*, DWORD, ITfRange*, void**);
  HRESULT (STDMETHODCALLTYPE *GetContext)(void*, void**);
} ITfReadOnlyPropertyVtbl;

/* ---------------- 接口声明 ---------------- */
typedef struct ITfThreadMgr ITfThreadMgr;
typedef struct ITfSource ITfSource;
typedef struct ITfDocumentMgr ITfDocumentMgr;
typedef struct ITfContext ITfContext;
typedef struct ITfEditRecord ITfEditRecord;
typedef struct IEnumTfRanges IEnumTfRanges;
typedef struct ITfRange ITfRange;

typedef struct ITfThreadMgrVtbl {
  HRESULT (STDMETHODCALLTYPE *QueryInterface)(ITfThreadMgr*, REFIID, void**);
  ULONG   (STDMETHODCALLTYPE *AddRef)(ITfThreadMgr*);
  ULONG   (STDMETHODCALLTYPE *Release)(ITfThreadMgr*);
  HRESULT (STDMETHODCALLTYPE *Activate)(ITfThreadMgr*, DWORD*);
  HRESULT (STDMETHODCALLTYPE *Deactivate)(ITfThreadMgr*);
  HRESULT (STDMETHODCALLTYPE *CreateDocumentMgr)(ITfThreadMgr*, void**);
  HRESULT (STDMETHODCALLTYPE *EnumDocumentMgrs)(ITfThreadMgr*, void**);
  HRESULT (STDMETHODCALLTYPE *GetFocus)(ITfThreadMgr*, void**);
  HRESULT (STDMETHODCALLTYPE *SetFocus)(ITfThreadMgr*, void*);
  HRESULT (STDMETHODCALLTYPE *AssociateFocus)(ITfThreadMgr*, HWND, void*, void**);
  HRESULT (STDMETHODCALLTYPE *IsThreadFocus)(ITfThreadMgr*, BOOL*);
  HRESULT (STDMETHODCALLTYPE *GetFunctionProvider)(ITfThreadMgr*, const GUID*, void**);
  HRESULT (STDMETHODCALLTYPE *EnumFunctionProviders)(ITfThreadMgr*, void**);
  HRESULT (STDMETHODCALLTYPE *GetGlobalCompartment)(ITfThreadMgr*, void**);
} ITfThreadMgrVtbl;
struct ITfThreadMgr { ITfThreadMgrVtbl *lpVtbl; };

typedef struct ITfSourceVtbl {
  HRESULT (STDMETHODCALLTYPE *QueryInterface)(ITfSource*, REFIID, void**);
  ULONG   (STDMETHODCALLTYPE *AddRef)(ITfSource*);
  ULONG   (STDMETHODCALLTYPE *Release)(ITfSource*);
  HRESULT (STDMETHODCALLTYPE *AdviseSink)(ITfSource*, REFIID, void*, DWORD*);
  HRESULT (STDMETHODCALLTYPE *UnadviseSink)(ITfSource*, DWORD);
} ITfSourceVtbl;
struct ITfSource { ITfSourceVtbl *lpVtbl; };

typedef struct ITfDocumentMgrVtbl {
  HRESULT (STDMETHODCALLTYPE *QueryInterface)(ITfDocumentMgr*, REFIID, void**);
  ULONG   (STDMETHODCALLTYPE *AddRef)(ITfDocumentMgr*);
  ULONG   (STDMETHODCALLTYPE *Release)(ITfDocumentMgr*);
  HRESULT (STDMETHODCALLTYPE *CreateContext)(ITfDocumentMgr*, DWORD, DWORD, void*, void**, DWORD*);
  HRESULT (STDMETHODCALLTYPE *Push)(ITfDocumentMgr*, void*);
  HRESULT (STDMETHODCALLTYPE *Pop)(ITfDocumentMgr*, DWORD);
  HRESULT (STDMETHODCALLTYPE *GetTop)(ITfDocumentMgr*, void**);
  HRESULT (STDMETHODCALLTYPE *GetBase)(ITfDocumentMgr*, void**);
  HRESULT (STDMETHODCALLTYPE *EnumContexts)(ITfDocumentMgr*, void**);
} ITfDocumentMgrVtbl;
struct ITfDocumentMgr { ITfDocumentMgrVtbl *lpVtbl; };

typedef struct ITfContextVtbl {
  HRESULT (STDMETHODCALLTYPE *QueryInterface)(ITfContext*, REFIID, void**);
  ULONG   (STDMETHODCALLTYPE *AddRef)(ITfContext*);
  ULONG   (STDMETHODCALLTYPE *Release)(ITfContext*);
  HRESULT (STDMETHODCALLTYPE *RequestEditSession)(ITfContext*, DWORD, void*, DWORD, int*);
  HRESULT (STDMETHODCALLTYPE *InWriteSession)(ITfContext*, DWORD, BOOL*);
  HRESULT (STDMETHODCALLTYPE *GetSelection)(ITfContext*, DWORD, DWORD, DWORD, void*, DWORD*);
  HRESULT (STDMETHODCALLTYPE *SetSelection)(ITfContext*, DWORD, DWORD, void*);
  HRESULT (STDMETHODCALLTYPE *GetStart)(ITfContext*, DWORD, void**);
  HRESULT (STDMETHODCALLTYPE *GetEnd)(ITfContext*, DWORD, void**);
  HRESULT (STDMETHODCALLTYPE *GetActiveView)(ITfContext*, void**);
  HRESULT (STDMETHODCALLTYPE *EnumViews)(ITfContext*, void**);
  HRESULT (STDMETHODCALLTYPE *GetStatus)(ITfContext*, void*);
  HRESULT (STDMETHODCALLTYPE *GetProperty)(ITfContext*, const GUID*, void**);
  HRESULT (STDMETHODCALLTYPE *GetAppProperty)(ITfContext*, const GUID*, void**);
  HRESULT (STDMETHODCALLTYPE *TrackProperties)(ITfContext*, void*, DWORD, void*, DWORD, void**);
  HRESULT (STDMETHODCALLTYPE *EnumProperties)(ITfContext*, void**);
  HRESULT (STDMETHODCALLTYPE *GetDocumentMgr)(ITfContext*, void**);
  HRESULT (STDMETHODCALLTYPE *CreateRangeBackup)(ITfContext*, DWORD, void*, void**);
} ITfContextVtbl;
struct ITfContext { ITfContextVtbl *lpVtbl; };

typedef struct ITfEditRecordVtbl {
  HRESULT (STDMETHODCALLTYPE *QueryInterface)(ITfEditRecord*, REFIID, void**);
  ULONG   (STDMETHODCALLTYPE *AddRef)(ITfEditRecord*);
  ULONG   (STDMETHODCALLTYPE *Release)(ITfEditRecord*);
  HRESULT (STDMETHODCALLTYPE *GetSelectionStatus)(ITfEditRecord*, BOOL*);
  HRESULT (STDMETHODCALLTYPE *GetTextAndPropertyUpdates)(ITfEditRecord*, DWORD, void*, DWORD, void**);
} ITfEditRecordVtbl;
struct ITfEditRecord { ITfEditRecordVtbl *lpVtbl; };

typedef struct IEnumTfRangesVtbl {
  HRESULT (STDMETHODCALLTYPE *QueryInterface)(IEnumTfRanges*, REFIID, void**);
  ULONG   (STDMETHODCALLTYPE *AddRef)(IEnumTfRanges*);
  ULONG   (STDMETHODCALLTYPE *Release)(IEnumTfRanges*);
  HRESULT (STDMETHODCALLTYPE *Clone)(IEnumTfRanges*, void**);
  HRESULT (STDMETHODCALLTYPE *Next)(IEnumTfRanges*, ULONG, void**, ULONG*);
  HRESULT (STDMETHODCALLTYPE *Reset)(IEnumTfRanges*);
  HRESULT (STDMETHODCALLTYPE *Skip)(IEnumTfRanges*, ULONG);
} IEnumTfRangesVtbl;
struct IEnumTfRanges { IEnumTfRangesVtbl *lpVtbl; };

typedef struct ITfRangeVtbl {
  HRESULT (STDMETHODCALLTYPE *QueryInterface)(ITfRange*, REFIID, void**);
  ULONG   (STDMETHODCALLTYPE *AddRef)(ITfRange*);
  ULONG   (STDMETHODCALLTYPE *Release)(ITfRange*);
  HRESULT (STDMETHODCALLTYPE *GetText)(ITfRange*, DWORD, DWORD, WCHAR*, DWORD, DWORD*);
  HRESULT (STDMETHODCALLTYPE *SetText)(ITfRange*, DWORD, DWORD, WCHAR*, int);
  HRESULT (STDMETHODCALLTYPE *GetFormattedText)(ITfRange*, DWORD, void**);
  HRESULT (STDMETHODCALLTYPE *GetEmbedded)(ITfRange*, DWORD, const GUID*, const GUID*, void**);
  HRESULT (STDMETHODCALLTYPE *InsertEmbedded)(ITfRange*, DWORD, DWORD, void*);
  HRESULT (STDMETHODCALLTYPE *ShiftStart)(ITfRange*, DWORD, int, int*, void*);
  HRESULT (STDMETHODCALLTYPE *ShiftEnd)(ITfRange*, DWORD, int, int*, void*);
  HRESULT (STDMETHODCALLTYPE *ShiftStartToRange)(ITfRange*, DWORD, void*, DWORD);
  HRESULT (STDMETHODCALLTYPE *ShiftEndToRange)(ITfRange*, DWORD, void*, DWORD);
  HRESULT (STDMETHODCALLTYPE *ShiftStartRegion)(ITfRange*, DWORD, DWORD, BOOL*);
  HRESULT (STDMETHODCALLTYPE *ShiftEndRegion)(ITfRange*, DWORD, DWORD, BOOL*);
  HRESULT (STDMETHODCALLTYPE *IsEmpty)(ITfRange*, DWORD, BOOL*);
  HRESULT (STDMETHODCALLTYPE *Collapse)(ITfRange*, DWORD, DWORD);
  HRESULT (STDMETHODCALLTYPE *IsEqualStart)(ITfRange*, DWORD, void*, DWORD, BOOL*);
  HRESULT (STDMETHODCALLTYPE *IsEqualEnd)(ITfRange*, DWORD, void*, DWORD, BOOL*);
  HRESULT (STDMETHODCALLTYPE *CompareStart)(ITfRange*, DWORD, void*, DWORD, int*);
  HRESULT (STDMETHODCALLTYPE *CompareEnd)(ITfRange*, DWORD, void*, DWORD, int*);
  HRESULT (STDMETHODCALLTYPE *AdjustForInsert)(ITfRange*, DWORD, DWORD, BOOL*);
  HRESULT (STDMETHODCALLTYPE *GetGravity)(ITfRange*, DWORD*, DWORD*);
  HRESULT (STDMETHODCALLTYPE *SetGravity)(ITfRange*, DWORD, DWORD, DWORD);
  HRESULT (STDMETHODCALLTYPE *Clone)(ITfRange*, void**);
  HRESULT (STDMETHODCALLTYPE *GetContext)(ITfRange*, void**);
} ITfRangeVtbl;
struct ITfRange { ITfRangeVtbl *lpVtbl; };

/* ---------------- 消息结构 ---------------- */
typedef struct GllMsg {
  DWORD pid;
  WCHAR text[GLL_MAX_TEXT];
} GllMsg;

/* ---------------- 全局状态 ---------------- */
static HMODULE g_hinst;
static HHOOK g_hook;
static DWORD g_tlsSlot = 0xFFFFFFFF;
static HWND g_hwndCache;
static int g_debug = 1;
static WCHAR g_lastSent[GLL_MAX_TEXT];
static DWORD g_lastSentTick;
static UINT g_commitMsg;

typedef struct GllSink GllSink;
typedef struct GllSinkVtbl {
  HRESULT (STDMETHODCALLTYPE *QueryInterface)(GllSink*, REFIID, void**);
  ULONG   (STDMETHODCALLTYPE *AddRef)(GllSink*);
  ULONG   (STDMETHODCALLTYPE *Release)(GllSink*);
  HRESULT (STDMETHODCALLTYPE *OnInitDocumentMgr)(GllSink*, ITfDocumentMgr*);
  HRESULT (STDMETHODCALLTYPE *OnUninitDocumentMgr)(GllSink*, ITfDocumentMgr*);
  HRESULT (STDMETHODCALLTYPE *OnSetFocus)(GllSink*, ITfDocumentMgr*, ITfDocumentMgr*);
  HRESULT (STDMETHODCALLTYPE *OnPushContext)(GllSink*, ITfContext*);
  HRESULT (STDMETHODCALLTYPE *OnPopContext)(GllSink*, ITfContext*);
} GllSinkVtbl;

typedef struct GllEditVtbl {
  HRESULT (STDMETHODCALLTYPE *QueryInterface)(GllSink*, REFIID, void**);
  ULONG   (STDMETHODCALLTYPE *AddRef)(GllSink*);
  ULONG   (STDMETHODCALLTYPE *Release)(GllSink*);
  HRESULT (STDMETHODCALLTYPE *OnEndEdit)(GllSink*, ITfContext*, DWORD, ITfEditRecord*);
} GllEditVtbl;

struct GllSink {
  const GllSinkVtbl *lpVtbl;
  const GllEditVtbl *lpEditVtbl;
  LONG refs;
};

typedef struct GllThreadState {
  BOOL initialized;
  int retryCount;
  int lastFocusHr;
  ITfThreadMgr *tm;
  ITfSource *src;
  DWORD tmCookie;
  ITfContext *advisedCtx;
  ITfSource *ctxSrc;
  DWORD editCookie;
} GllThreadState;

static GllSink g_sink;

/* ---------------- 工具函数 ---------------- */
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

static GllThreadState *GetThreadState(void) {
  if (g_tlsSlot == 0xFFFFFFFF) return NULL;
  GllThreadState *st = (GllThreadState*)TlsGetValue(g_tlsSlot);
  if (!st) {
    st = (GllThreadState*)LocalAlloc(LPTR, sizeof(GllThreadState));
    if (st) TlsSetValue(g_tlsSlot, st);
  }
  return st;
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

static BOOL IsForegroundProcess(void) {
  HWND fg = GetForegroundWindow();
  if (!fg) return FALSE;
  DWORD pid;
  GetWindowThreadProcessId(fg, &pid);
  return pid == GetCurrentProcessId();
}

/* 本线程是否为键盘输入线程（拥有焦点窗口，或属于前台顶层窗口） */
static BOOL IsInputThread(void) {
  GUITHREADINFO gui;
  memset(&gui, 0, sizeof(gui));
  gui.cbSize = sizeof(gui);
  if (GetGUIThreadInfo(GetCurrentThreadId(), &gui) && gui.hwndFocus) return TRUE;
  HWND fg = GetForegroundWindow();
  if (fg) {
    DWORD pid;
    return GetWindowThreadProcessId(fg, &pid) == GetCurrentThreadId();
  }
  return FALSE;
}

/* 只对白名单内的进程初始化 TSF，避免影响聊天/其他无关程序 */
static BOOL IsAllowedProcess(void) {
  WCHAR path[MAX_PATH];
  DWORD n = GetModuleFileNameW(NULL, path, MAX_PATH);
  if (!n) return FALSE;
  WCHAR *name = path;
  WCHAR *p;
  for (p = path; *p; p++) {
    if (*p == L'\\' || *p == L'/') name = p + 1;
  }
  WCHAR *dot = wcsrchr(name, L'.');
  if (dot && lstrcmpiW(dot, L".exe") == 0) *dot = 0;
  static const WCHAR *allowed[] = {
    L"wps", L"et", L"wpp", L"chrome", L"msedge", L"bilibili",
    L"notepad", L"everything", L"winword", L"excel", L"powerpnt",
    L"notepad++"
  };
  int i;
  for (i = 0; i < (int)(sizeof(allowed) / sizeof(allowed[0])); i++) {
    if (lstrcmpiW(name, allowed[i]) == 0) return TRUE;
  }
  WCHAR env[512];
  DWORD en = GetEnvironmentVariableW(L"GLL_TSF_ALLOW", env, 512);
  if (en > 0 && en < 512) {
    WCHAR *ctx = NULL;
    WCHAR *tok = wcstok(env, L",");
    while (tok) {
      if (lstrcmpiW(name, tok) == 0) return TRUE;
      tok = wcstok(NULL, L",");
    }
  }
  return FALSE;
}

static void SendCommit(const WCHAR *text, int len) {
  if (len <= 0 || len >= GLL_MAX_TEXT) return;
  if (!HasCjk(text, len)) return;
  /* 同一文本在 400ms 内只发一次（TSF/IMM 双通道可能重复） */
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
  if (!hwnd) {
    Dbg(L"SendCommit skip: no target window");
    return;
  }
  /* 零阻塞通知：全局原子 + PostMessage，绝不等待接收方 */
  if (!g_commitMsg) g_commitMsg = RegisterWindowMessageW(L"GLL_TSF_COMMIT");
  if (!g_commitMsg) {
    Dbg(L"SendCommit skip: RegisterWindowMessage fail");
    return;
  }
  int cap = len;
  if (cap > 220) cap = 220;
  WCHAR atomText[256];
  wsprintfW(atomText, L"%lu|", GetCurrentProcessId());
  wcsncat(atomText, text, cap);
  atomText[255] = 0;
  ATOM atom = GlobalAddAtomW(atomText);
  if (!atom) {
    Dbg(L"SendCommit skip: GlobalAddAtom fail");
    return;
  }
  BOOL posted = PostMessageW(hwnd, g_commitMsg, (WPARAM)GetCurrentProcessId(), (LPARAM)atom);
  if (!posted) {
    Dbg(L"SendCommit skip: PostMessage fail err=%lu", GetLastError());
    GlobalDeleteAtom(atom);
    return;
  }
  Dbg(L"SendCommit OK text=[%s] atom=%u", text, atom);
}

/* 进程内读取 IMM 输入法的上屏结果（读霸同款思路，适用于不走 TSF 文档的应用） */
static void CaptureImeResult(HWND hwnd) {
  if (!IsAllowedProcess()) return;
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

/* ---------------- TSF 逻辑 ---------------- */
static void UnadviseContext(GllThreadState *st) {
  if (st->advisedCtx && st->ctxSrc) {
    st->ctxSrc->lpVtbl->UnadviseSink(st->ctxSrc, st->editCookie);
    st->ctxSrc->lpVtbl->Release(st->ctxSrc);
  }
  if (st->advisedCtx) st->advisedCtx->lpVtbl->Release(st->advisedCtx);
  st->advisedCtx = NULL;
  st->ctxSrc = NULL;
  st->editCookie = 0;
}

static void TryAdoptFocus(GllThreadState *st);

static void AdviseContext(GllThreadState *st, ITfContext *ctx) {
  if (!ctx) return;
  if (st->advisedCtx == ctx) return;
  UnadviseContext(st);
  ITfSource *src = NULL;
  HRESULT hr = ctx->lpVtbl->QueryInterface(ctx, &IID_ITfSource, (void**)&src);
  if (FAILED(hr) || !src) return;
  DWORD cookie = 0;
  hr = src->lpVtbl->AdviseSink(src, &IID_ITfTextEditSink, &g_sink, &cookie);
  if (FAILED(hr)) {
    src->lpVtbl->Release(src);
    Dbg(L"AdviseSink edit fail hr=0x%08x", (unsigned)hr);
    return;
  }
  st->advisedCtx = ctx;
  st->ctxSrc = src;
  st->editCookie = cookie;
  ctx->lpVtbl->AddRef(ctx);
  Dbg(L"edit sink advised cookie=%lu", cookie);
}

static void InitTsf(GllThreadState *st) {
  if (st->initialized) return;
  if (!IsAllowedProcess()) return;
  st->initialized = TRUE; /* 防止递归重入 */
  HRESULT hr = CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);
  if (hr == RPC_E_CHANGED_MODE) {
    hr = CoInitialize(NULL);
    if (FAILED(hr) && hr != RPC_E_CHANGED_MODE) {
      Dbg(L"CoInitialize fail hr=0x%08x", (unsigned)hr);
      return;
    }
  }
  ITfThreadMgr *tm = NULL;
  hr = CoCreateInstance(&CLSID_TF_ThreadMgr, NULL, CLSCTX_INPROC_SERVER,
                        &IID_ITfThreadMgr, (void**)&tm);
  if (FAILED(hr) || !tm) {
    Dbg(L"CoCreateInstance ThreadMgr fail hr=0x%08x", (unsigned)hr);
    return;
  }
  DWORD tid = 0;
  hr = tm->lpVtbl->Activate(tm, &tid);
  if (FAILED(hr)) {
    Dbg(L"Activate fail hr=0x%08x", (unsigned)hr);
    tm->lpVtbl->Release(tm);
    return;
  }
  ITfSource *src = NULL;
  hr = tm->lpVtbl->QueryInterface(tm, &IID_ITfSource, (void**)&src);
  if (FAILED(hr) || !src) {
    tm->lpVtbl->Release(tm);
    return;
  }
  DWORD cookie = 0;
  hr = src->lpVtbl->AdviseSink(src, &IID_ITfThreadMgrEventSink, &g_sink, &cookie);
  if (FAILED(hr)) {
    Dbg(L"AdviseSink thread fail hr=0x%08x", (unsigned)hr);
    src->lpVtbl->Release(src);
    tm->lpVtbl->Release(tm);
    return;
  }
  st->tm = tm;
  st->src = src;
  st->tmCookie = cookie;
  Dbg(L"TSF init ok tid=%lu", tid);
  TryAdoptFocus(st);
}

static void TryAdoptFocus(GllThreadState *st) {
  if (!st->tm || st->advisedCtx) return;
  ITfDocumentMgr *dm = NULL;
  HRESULT hr = st->tm->lpVtbl->GetFocus(st->tm, (void**)&dm);
  if (hr != st->lastFocusHr) {
    st->lastFocusHr = hr;
    Dbg(L"TryAdoptFocus hr=0x%08x dm=%p", (unsigned)hr, (void*)dm);
  }
  if (SUCCEEDED(hr) && dm) {
    ITfContext *ctx = NULL;
    HRESULT hr2 = dm->lpVtbl->GetTop(dm, (void**)&ctx);
    if (SUCCEEDED(hr2) && ctx) Dbg(L"GetTop ctx=%p", (void*)ctx);
    if (SUCCEEDED(hr2) && ctx) {
      AdviseContext(st, ctx);
    }
    dm->lpVtbl->Release(dm);
  }
}

/* ---------------- 回调实现 ---------------- */
static HRESULT STDMETHODCALLTYPE Sink_QueryInterface(GllSink *self, REFIID riid, void **ppv) {
  if (IsEqualIID(riid, &IID_IUnknown) ||
      IsEqualIID(riid, &IID_ITfThreadMgrEventSink)) {
    *ppv = self;
    self->refs++;
    return S_OK;
  }
  if (IsEqualIID(riid, &IID_ITfTextEditSink)) {
    *ppv = &self->lpEditVtbl;
    self->refs++;
    return S_OK;
  }
  *ppv = NULL;
  return E_NOINTERFACE;
}

static ULONG STDMETHODCALLTYPE Sink_AddRef(GllSink *self) {
  return InterlockedIncrement(&self->refs);
}

static ULONG STDMETHODCALLTYPE Sink_Release(GllSink *self) {
  LONG r = InterlockedDecrement(&self->refs);
  return (ULONG)r;
}

static HRESULT STDMETHODCALLTYPE Sink_OnInitDocumentMgr(GllSink *self, ITfDocumentMgr *dm) {
  return S_OK;
}

static HRESULT STDMETHODCALLTYPE Sink_OnUninitDocumentMgr(GllSink *self, ITfDocumentMgr *dm) {
  return S_OK;
}

static HRESULT STDMETHODCALLTYPE Sink_OnSetFocus(GllSink *self, ITfDocumentMgr *dimFocus,
                                                  ITfDocumentMgr *dimPrevFocus) {
  Dbg(L"OnSetFocus dm=%p", (void*)dimFocus);
  GllThreadState *st = GetThreadState();
  if (!st) return S_OK;
  if (dimFocus) {
    ITfContext *ctx = NULL;
    HRESULT hr = dimFocus->lpVtbl->GetTop(dimFocus, (void**)&ctx);
    if (SUCCEEDED(hr) && ctx) {
      AdviseContext(st, ctx);
    }
  }
  return S_OK;
}

static HRESULT STDMETHODCALLTYPE Sink_OnPushContext(GllSink *self, ITfContext *pic) {
  Dbg(L"OnPushContext ctx=%p", (void*)pic);
  GllThreadState *st = GetThreadState();
  if (!st) return S_OK;
  AdviseContext(st, pic);
  return S_OK;
}

static HRESULT STDMETHODCALLTYPE Sink_OnPopContext(GllSink *self, ITfContext *pic) {
  Dbg(L"OnPopContext ctx=%p", (void*)pic);
  GllThreadState *st = GetThreadState();
  if (st && pic && st->advisedCtx == pic) {
    UnadviseContext(st);
  }
  return S_OK;
}

static HRESULT STDMETHODCALLTYPE Edit_QueryInterface(GllSink *self, REFIID riid, void **ppv) {
  GllSink *base = (GllSink*)((char*)self - offsetof(GllSink, lpEditVtbl));
  return Sink_QueryInterface(base, riid, ppv);
}

static ULONG STDMETHODCALLTYPE Edit_AddRef(GllSink *self) {
  GllSink *base = (GllSink*)((char*)self - offsetof(GllSink, lpEditVtbl));
  return Sink_AddRef(base);
}

static ULONG STDMETHODCALLTYPE Edit_Release(GllSink *self) {
  GllSink *base = (GllSink*)((char*)self - offsetof(GllSink, lpEditVtbl));
  return Sink_Release(base);
}

static HRESULT STDMETHODCALLTYPE Edit_OnEndEdit(GllSink *self, ITfContext *pic,
                                                DWORD ecReadOnly, ITfEditRecord *pEditRecord) {
  if (!pEditRecord) return S_OK;
  IEnumTfRanges *penum = NULL;
  HRESULT hr = pEditRecord->lpVtbl->GetTextAndPropertyUpdates(
      pEditRecord, TF_GTP_INCL_TEXT, NULL, 0, (void**)&penum);
  if (FAILED(hr) || !penum) return S_OK;
  int count = 0;
  for (;;) {
    ITfRange *range = NULL;
    ULONG fetched = 0;
    hr = penum->lpVtbl->Next(penum, 1, (void**)&range, &fetched);
    if (FAILED(hr) || fetched == 0) break;
    WCHAR buf[GLL_MAX_TEXT];
    DWORD len = 0;
    hr = range->lpVtbl->GetText(range, ecReadOnly, 0, buf, GLL_MAX_TEXT - 1, &len);
    if (SUCCEEDED(hr) && len > 0) {
      buf[len] = 0;
      count++;
      Dbg(L"OnEndEdit [%s] len=%lu", buf, len);
      SendCommit(buf, (int)len);
    }
    range->lpVtbl->Release(range);
  }
  penum->lpVtbl->Release(penum);
  return S_OK;
}

static const GllSinkVtbl g_sinkVtbl = {
  Sink_QueryInterface, Sink_AddRef, Sink_Release,
  Sink_OnInitDocumentMgr, Sink_OnUninitDocumentMgr,
  Sink_OnSetFocus, Sink_OnPushContext, Sink_OnPopContext
};

static const GllEditVtbl g_editVtbl = {
  Edit_QueryInterface, Edit_AddRef, Edit_Release, Edit_OnEndEdit
};

/* ---------------- 钩子 ---------------- */
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
    GllThreadState *st = GetThreadState();
    if (st) {
      if (!st->initialized) {
        if (IsInputThread()) InitTsf(st);
      } else if (!st->advisedCtx && msg && (
          msg->message == 0x0100 || msg->message == 0x0101 ||   /* WM_KEYDOWN/UP */
          msg->message == WM_IME_STARTCOMPOSITION ||
          msg->message == WM_IME_COMPOSITION ||
          msg->message == WM_IME_ENDCOMPOSITION ||
          msg->message == 0x0102 ||                              /* WM_CHAR */
          msg->message == 0x0006 || msg->message == 0x0007)) {   /* WM_ACTIVATE/SETFOCUS */
        st->retryCount++;
        if (st->retryCount % 4 == 0) {
          TryAdoptFocus(st);
        }
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
    g_tlsSlot = TlsAlloc();
    g_debug = GetEnvironmentVariableW(L"GLL_TSF_DEBUG", NULL, 0) > 0 ? 1 : 0;
    memset(&g_sink, 0, sizeof(g_sink));
    g_sink.lpVtbl = &g_sinkVtbl;
    g_sink.lpEditVtbl = &g_editVtbl;
    g_sink.refs = 1;
    break;
  case DLL_PROCESS_DETACH:
    if (g_hook) UnhookWindowsHookEx(g_hook);
    if (g_tlsSlot != 0xFFFFFFFF) TlsFree(g_tlsSlot);
    break;
  }
  return TRUE;
}
