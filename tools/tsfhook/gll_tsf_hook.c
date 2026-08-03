/*
 * gll_tsf_hook.dll - 归零归零 输入法提交观察钩子（v3 重写）
 *
 * 设计参考（成熟开源实现，非自创）：
 *   - NVDA 读屏软件  nvdaHelper/remote/tsf.cpp
 *   - Chromium      ui/base/ime/win/tsf_event_router.cc
 *
 * 通道：TSF（微软五笔等 TSF 输入法）：
 *   WinEvent 钩子（EVENT_SYSTEM_FOREGROUND / EVENT_OBJECT_FOCUS）在目标线程
 *   初始化监听；用 msctf!TF_GetThreadMgr 取线程现有线程管理器（不自己创建）；
 *   监听 ITfThreadMgrEventSink + ITfTextEditSink（base context）。
 *   “组合区消失”= 上屏事件，从 ITfEditRecord 读出刚上屏的文本。
 *   组合进行中只发状态（正在组字），绝不读候选内容。
 * IMM（多多五笔）由主程序原有跨进程 IMM 轮询覆盖，本 DLL 不装全局消息钩子，
 * 避免把 DLL 注入到所有 GUI 进程（历史锁定/卡顿根源）。
 *
 * 安全红线：
 *   - 绝不在键盘钩子回调里创建 COM/TSF 对象（历史闪退根源）；
 *   - 所有 TSF 调用包 SEH，异常即禁用该线程监听，不再重试；
 *   - 每线程独立对象，线程退出/进程退出时清理；
 *   - 通知主程序走 GlobalAddAtom + PostMessage，零阻塞。
 *
 * 进程间状态（组字中）：
 *   - 共享内存 Local\GLL_TSF_STATE_V3，主程序按键时直接读，无消息延迟。
 */
#include <windows.h>
#include <stdarg.h>
#include <wchar.h>
#include <stddef.h>
#include <stdlib.h>
#include <string.h>

/* ---------------- 最小 COM 声明（TCC 头不含 objbase.h） ---------------- */
HRESULT __stdcall CoInitializeEx(void *pvReserved, DWORD dwCoInit);
void __stdcall CoUninitialize(void);
int __stdcall WideCharToMultiByte(UINT CodePage, DWORD dwFlags,
                                  const WCHAR *lpWideCharStr, int cchWideChar,
                                  char *lpMultiByteStr, int cbMultiByte,
                                  const char *lpDefaultChar, BOOL *lpUsedDefaultChar);
#define CP_UTF8 65001
#define COINIT_APARTMENTTHREADED 0x2
#ifndef RPC_E_CHANGED_MODE
#define RPC_E_CHANGED_MODE 0x80010106L
#endif

#define GLL_WND_CLASS L"GLL_TSF_NOTIFY"
#define GLL_WORKER_CLASS L"GLL_TSF_WORKER"
#define GLL_MAX_TEXT 512
#define TF_GTP_INCL_TEXT 1
#define TF_TF_MOVESTART 1
#define TF_INVALID_COOKIE 0xFFFFFFFF

/* ---------------- GUID ---------------- */
static const GUID IID_IUnknown =
  {0x00000000,0x0000,0x0000,{0xC0,0x00,0x00,0x00,0x00,0x00,0x00,0x46}};
static const GUID IID_ITfThreadMgr =
  {0xAA80E801,0x2021,0x11D2,{0x93,0xE0,0x00,0x60,0xB0,0x67,0xB8,0x6E}};
static const GUID IID_ITfThreadMgrEventSink =
  {0xAA80E80E,0x2021,0x11D2,{0x93,0xE0,0x00,0x60,0xB0,0x67,0xB8,0x6E}};
static const GUID IID_ITfDocumentMgr =
  {0xAA80E7F4,0x2021,0x11D2,{0x93,0xE0,0x00,0x60,0xB0,0x67,0xB8,0x6E}};
static const GUID IID_ITfContext =
  {0xAA80E7FD,0x2021,0x11D2,{0x93,0xE0,0x00,0x60,0xB0,0x67,0xB8,0x6E}};
static const GUID IID_ITfSource =
  {0x4EA48A35,0x60AE,0x446F,{0x8F,0xD6,0xE6,0xA8,0xD8,0x24,0x59,0xF7}};
static const GUID IID_ITfTextEditSink =
  {0x8127D409,0xCCD3,0x4683,{0x96,0x7A,0xB4,0x3D,0x5B,0x48,0x2B,0xF7}};
static const GUID IID_ITfEditRecord =
  {0x42D4D099,0x7C1A,0x4A89,{0xB8,0x36,0x6C,0x6F,0x22,0x16,0x0D,0xF0}};
static const GUID IID_IEnumTfRanges =
  {0xF99D3F40,0x8E32,0x11D2,{0xBF,0x46,0x00,0x10,0x5A,0x27,0x99,0xB5}};
static const GUID IID_ITfRange =
  {0xAA80E7FF,0x2021,0x11D2,{0x93,0xE0,0x00,0x60,0xB0,0x67,0xB8,0x6E}};
static const GUID IID_ITfContextComposition =
  {0xD40C8AAE,0xAC92,0x4FC7,{0x9A,0x11,0x0E,0xE0,0xE2,0x3A,0xA3,0x9B}};
static const GUID IID_IEnumITfCompositionView =
  {0x5EFD22BA,0x7838,0x46CB,{0x88,0xE2,0xCA,0xDB,0x14,0x12,0x4F,0x8F}};
static const GUID IID_ITfCompositionView =
  {0xD7540241,0xF9A1,0x4364,{0xBE,0xFC,0xDB,0xCD,0x2C,0x43,0x95,0xB7}};

/* ---------------- 接口 vtable 声明 ---------------- */
typedef struct ITfThreadMgr ITfThreadMgr;
typedef struct ITfSource ITfSource;
typedef struct ITfDocumentMgr ITfDocumentMgr;
typedef struct ITfContext ITfContext;
typedef struct ITfEditRecord ITfEditRecord;
typedef struct IEnumTfRanges IEnumTfRanges;
typedef struct ITfRange ITfRange;
typedef struct ITfContextComposition ITfContextComposition;
typedef struct IEnumITfCompositionView IEnumITfCompositionView;
typedef struct ITfCompositionView ITfCompositionView;

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

typedef struct ITfContextCompositionVtbl {
  HRESULT (STDMETHODCALLTYPE *QueryInterface)(ITfContextComposition*, REFIID, void**);
  ULONG   (STDMETHODCALLTYPE *AddRef)(ITfContextComposition*);
  ULONG   (STDMETHODCALLTYPE *Release)(ITfContextComposition*);
  HRESULT (STDMETHODCALLTYPE *StartComposition)(ITfContextComposition*, DWORD, void*, void*, void**);
  HRESULT (STDMETHODCALLTYPE *EnumCompositions)(ITfContextComposition*, void**);
  HRESULT (STDMETHODCALLTYPE *FindComposition)(ITfContextComposition*, DWORD, void*, void**);
} ITfContextCompositionVtbl;
struct ITfContextComposition { ITfContextCompositionVtbl *lpVtbl; };

typedef struct IEnumITfCompositionViewVtbl {
  HRESULT (STDMETHODCALLTYPE *QueryInterface)(IEnumITfCompositionView*, REFIID, void**);
  ULONG   (STDMETHODCALLTYPE *AddRef)(IEnumITfCompositionView*);
  ULONG   (STDMETHODCALLTYPE *Release)(IEnumITfCompositionView*);
  HRESULT (STDMETHODCALLTYPE *Clone)(IEnumITfCompositionView*, void**);
  HRESULT (STDMETHODCALLTYPE *Next)(IEnumITfCompositionView*, ULONG, void**, ULONG*);
  HRESULT (STDMETHODCALLTYPE *Reset)(IEnumITfCompositionView*);
  HRESULT (STDMETHODCALLTYPE *Skip)(IEnumITfCompositionView*, ULONG);
} IEnumITfCompositionViewVtbl;
struct IEnumITfCompositionView { IEnumITfCompositionViewVtbl *lpVtbl; };

typedef struct ITfCompositionViewVtbl {
  HRESULT (STDMETHODCALLTYPE *QueryInterface)(ITfCompositionView*, REFIID, void**);
  ULONG   (STDMETHODCALLTYPE *AddRef)(ITfCompositionView*);
  ULONG   (STDMETHODCALLTYPE *Release)(ITfCompositionView*);
  HRESULT (STDMETHODCALLTYPE *GetOwner)(ITfCompositionView*, void**);
  HRESULT (STDMETHODCALLTYPE *GetRange)(ITfCompositionView*, void**);
} ITfCompositionViewVtbl;
struct ITfCompositionView { ITfCompositionViewVtbl *lpVtbl; };

/* ---------------- 共享内存（组字状态，主程序按键时直接读） ---------------- */
#define GLL_SHM_NAME L"Local\\GLL_TSF_STATE_V3"
#define GLL_SHM_MAGIC 0x474C4C33
#define GLL_SHM_VERSION 3
#define GLL_SHM_ENTRIES 4096

typedef struct GllShmEntry {
  volatile LONG pid;
  volatile LONG composing;
  volatile LONG tick;
} GllShmEntry;

typedef struct GllShmHeader {
  LONG magic;
  LONG version;
  LONG count;
  LONG reserved;
  GllShmEntry entries[GLL_SHM_ENTRIES];
} GllShmHeader;

static HANDLE g_shmFile;
static GllShmHeader *g_shm;

static void ShmEnsure(void) {
  if (g_shm) return;
  g_shmFile = CreateFileMappingW(INVALID_HANDLE_VALUE, NULL, PAGE_READWRITE,
                                 0, sizeof(GllShmHeader), GLL_SHM_NAME);
  if (!g_shmFile) return;
  g_shm = (GllShmHeader*)MapViewOfFile(g_shmFile, FILE_MAP_ALL_ACCESS, 0, 0, 0);
  if (!g_shm) {
    CloseHandle(g_shmFile);
    g_shmFile = NULL;
    return;
  }
  if (g_shm->magic != GLL_SHM_MAGIC) {
    memset(g_shm, 0, sizeof(GllShmHeader));
    g_shm->magic = GLL_SHM_MAGIC;
    g_shm->version = GLL_SHM_VERSION;
    g_shm->count = GLL_SHM_ENTRIES;
  }
}

static void ShmSetComposing(DWORD pid, BOOL composing) {
  if (!g_shm) ShmEnsure();
  if (!g_shm) return;
  GllShmEntry *e = &g_shm->entries[pid % GLL_SHM_ENTRIES];
  if (e->pid != (LONG)pid && e->pid != 0 &&
      (DWORD)e->tick != 0 && (GetTickCount() - (DWORD)e->tick) < 30000) {
    /* 槽位被其他进程占用且活跃：换一个探测槽，避免互相覆盖 */
    e = &g_shm->entries[(pid * 2654435761u) % GLL_SHM_ENTRIES];
  }
  InterlockedExchange(&e->pid, (LONG)pid);
  InterlockedExchange(&e->composing, composing ? 1 : 0);
  InterlockedExchange(&e->tick, (LONG)GetTickCount());
}

/* ---------------- 日志 ---------------- */
static int g_debug = 1;
static WCHAR g_logPath[MAX_PATH] = L"C:\\tmp\\gll_tsf_hook.log";

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
  HANDLE h = CreateFileW(g_logPath, FILE_APPEND_DATA,
                         FILE_SHARE_READ | FILE_SHARE_WRITE, NULL,
                         OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
  if (h == INVALID_HANDLE_VALUE) return;
  DWORD written = 0;
  WriteFile(h, utf8, (DWORD)n, &written, NULL);
  CloseHandle(h);
}

/* ---------------- 通知主程序 ---------------- */
static HWND g_hwndCache;
static UINT g_commitMsg;
static UINT g_stateMsg;
static WCHAR g_lastSent[GLL_MAX_TEXT];
static DWORD g_lastSentTick;

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

static HWND FindNotifyWindow(void) {
  if (g_hwndCache && IsWindow(g_hwndCache)) return g_hwndCache;
  g_hwndCache = FindWindowW(GLL_WND_CLASS, NULL);
  return g_hwndCache;
}

static void PostAtomMsg(UINT msg, DWORD pid, const WCHAR *payload) {
  HWND hwnd = FindNotifyWindow();
  if (!hwnd) return;
  if (!msg) return;
  int plen = lstrlenW(payload);
  if (plen > 220) plen = 220;
  WCHAR atomText[256];
  wsprintfW(atomText, L"%lu|", pid);
  wcsncat(atomText, payload, plen);
  atomText[255] = 0;
  ATOM atom = GlobalAddAtomW(atomText);
  if (!atom) return;
  if (!PostMessageW(hwnd, msg, (WPARAM)pid, (LPARAM)atom)) {
    GlobalDeleteAtom(atom);
  }
}

static void SendCommit(DWORD pid, const WCHAR *text, int len) {
  if (len <= 0 || len >= GLL_MAX_TEXT) return;
  if (!HasCjk(text, len)) return;
  /* 同一文本 400ms 内只发一次（IMM/TSF 双通道可能重复） */
  if (wcsncmp(g_lastSent, text, len) == 0 && g_lastSent[len] == 0 &&
      (GetTickCount() - g_lastSentTick) < 400) return;
  wcsncpy(g_lastSent, text, len);
  g_lastSent[len] = 0;
  g_lastSentTick = GetTickCount();
  if (!g_commitMsg) g_commitMsg = RegisterWindowMessageW(L"GLL_TSF_COMMIT");
  if (!g_commitMsg) return;
  PostAtomMsg(g_commitMsg, pid, text);
  Dbg(L"COMMIT pid=%lu text=[%s]", pid, text);
}

static void SendState(DWORD pid, BOOL composing) {
  if (!g_stateMsg) g_stateMsg = RegisterWindowMessageW(L"GLL_TSF_STATE");
  if (!g_stateMsg) return;
  WCHAR payload[16];
  wsprintfW(payload, L"%u", composing ? 1 : 0);
  PostAtomMsg(g_stateMsg, pid, payload);
  Dbg(L"STATE pid=%lu composing=%d", pid, composing ? 1 : 0);
}

/* ---------------- TSF 观察者 ---------------- */
typedef struct GllObserver GllObserver;

typedef struct GllThreadSinkVtbl {
  HRESULT (STDMETHODCALLTYPE *QueryInterface)(struct GllThreadSink*, REFIID, void**);
  ULONG   (STDMETHODCALLTYPE *AddRef)(struct GllThreadSink*);
  ULONG   (STDMETHODCALLTYPE *Release)(struct GllThreadSink*);
  HRESULT (STDMETHODCALLTYPE *OnInitDocumentMgr)(struct GllThreadSink*, ITfDocumentMgr*);
  HRESULT (STDMETHODCALLTYPE *OnUninitDocumentMgr)(struct GllThreadSink*, ITfDocumentMgr*);
  HRESULT (STDMETHODCALLTYPE *OnSetFocus)(struct GllThreadSink*, ITfDocumentMgr*, ITfDocumentMgr*);
  HRESULT (STDMETHODCALLTYPE *OnPushContext)(struct GllThreadSink*, ITfContext*);
  HRESULT (STDMETHODCALLTYPE *OnPopContext)(struct GllThreadSink*, ITfContext*);
} GllThreadSinkVtbl;

typedef struct GllEditSinkVtbl {
  HRESULT (STDMETHODCALLTYPE *QueryInterface)(struct GllEditSink*, REFIID, void**);
  ULONG   (STDMETHODCALLTYPE *AddRef)(struct GllEditSink*);
  ULONG   (STDMETHODCALLTYPE *Release)(struct GllEditSink*);
  HRESULT (STDMETHODCALLTYPE *OnEndEdit)(struct GllEditSink*, ITfContext*, DWORD, ITfEditRecord*);
} GllEditSinkVtbl;

typedef struct GllThreadSink {
  const GllThreadSinkVtbl *lpVtbl;
  GllObserver *owner;
} GllThreadSink;

typedef struct GllEditSink {
  const GllEditSinkVtbl *lpVtbl;
  GllObserver *owner;
} GllEditSink;

struct GllObserver {
  GllThreadSink threadSink;
  GllEditSink editSink;
  LONG refs;
  ITfThreadMgr *tm;
  ITfSource *threadSrc;
  DWORD threadCookie;
  ITfSource *editSrc;
  DWORD editCookie;
  volatile LONG inComposition;
  volatile LONG disabled;
  volatile LONG comInit;
  DWORD pid;
  DWORD tid;
  GllObserver *next;
};

static DWORD g_tlsSlot = 0xFFFFFFFF;
static DWORD g_tlsCtxSlot = 0xFFFFFFFF;
static CRITICAL_SECTION g_listLock;
static GllObserver *g_observerList;
static HWINEVENTHOOK g_winEventHook;
static HMODULE g_hinst;
static UINT g_initMsg;
static LONG g_firstEventLogged;
static LONG g_firstBlockedLogged;

typedef struct GllThreadCtx {
  HWND hwnd;
  GllObserver *obs;
  DWORD lastAttemptTick;
} GllThreadCtx;

static void ObserverListAdd(GllObserver *o) {
  EnterCriticalSection(&g_listLock);
  o->next = g_observerList;
  g_observerList = o;
  LeaveCriticalSection(&g_listLock);
}

static void ObserverListRemove(GllObserver *o) {
  EnterCriticalSection(&g_listLock);
  GllObserver **pp = &g_observerList;
  while (*pp) {
    if (*pp == o) {
      *pp = o->next;
      break;
    }
    pp = &(*pp)->next;
  }
  LeaveCriticalSection(&g_listLock);
}

static void ObserverRemoveEditSink(GllObserver *o) {
  if (o->editCookie != TF_INVALID_COOKIE && o->editSrc) {
    o->editSrc->lpVtbl->UnadviseSink(o->editSrc, o->editCookie);
    o->editCookie = TF_INVALID_COOKIE;
  }
  if (o->editSrc) {
    o->editSrc->lpVtbl->Release(o->editSrc);
    o->editSrc = NULL;
  }
}

static void ObserverUpdateEditSink(GllObserver *o, ITfDocumentMgr *dm) {
  ObserverRemoveEditSink(o);
  if (!dm) return;
  ITfContext *ctx = NULL;
  if (FAILED(dm->lpVtbl->GetBase(dm, (void**)&ctx)) || !ctx) return;
  HRESULT hr = ctx->lpVtbl->QueryInterface(ctx, &IID_ITfSource, (void**)&o->editSrc);
  ctx->lpVtbl->Release(ctx);
  if (FAILED(hr) || !o->editSrc) {
    o->editSrc = NULL;
    return;
  }
  hr = o->editSrc->lpVtbl->AdviseSink(o->editSrc, &IID_ITfTextEditSink,
                                      (void*)&o->editSink, &o->editCookie);
  if (FAILED(hr)) {
    ObserverRemoveEditSink(o);
    Dbg(L"TSF edit sink advise fail hr=0x%08x", (unsigned)hr);
  }
}

static BOOL ObserverHasComposition(ITfContext *ctx) {
  ITfContextComposition *cc = NULL;
  if (FAILED(ctx->lpVtbl->QueryInterface(ctx, &IID_ITfContextComposition,
                                         (void**)&cc)) || !cc) return FALSE;
  IEnumITfCompositionView *en = NULL;
  HRESULT hr = cc->lpVtbl->EnumCompositions(cc, (void**)&en);
  cc->lpVtbl->Release(cc);
  if (FAILED(hr) || !en) return FALSE;
  ITfCompositionView *view = NULL;
  ULONG fetched = 0;
  hr = en->lpVtbl->Next(en, 1, (void**)&view, &fetched);
  if (view) view->lpVtbl->Release(view);
  en->lpVtbl->Release(en);
  return hr == S_OK && fetched == 1;
}

static WCHAR *ObserverReadEditText(DWORD ec, ITfEditRecord *rec) {
  IEnumTfRanges *en = NULL;
  HRESULT hr = rec->lpVtbl->GetTextAndPropertyUpdates(rec, TF_GTP_INCL_TEXT,
                                                       NULL, 0, (void**)&en);
  if (FAILED(hr) || !en) return NULL;
  WCHAR *out = (WCHAR*)malloc(sizeof(WCHAR));
  if (!out) { en->lpVtbl->Release(en); return NULL; }
  out[0] = 0;
  int n = 0;
  for (;;) {
    ITfRange *range = NULL;
    ULONG fetched = 0;
    if (FAILED(en->lpVtbl->Next(en, 1, (void**)&range, &fetched)) || fetched == 0) break;
    BOOL empty = FALSE;
    if (SUCCEEDED(range->lpVtbl->IsEmpty(range, ec, &empty)) && !empty) {
      for (;;) {
        WCHAR buf[256];
        DWORD len = 255;
        HRESULT hr2 = range->lpVtbl->GetText(range, ec, TF_TF_MOVESTART,
                                              buf, len, &len);
        if (FAILED(hr2) || len == 0) break;
        WCHAR *tmp = (WCHAR*)realloc(out, (n + len + 1) * sizeof(WCHAR));
        if (!tmp) break;
        out = tmp;
        memcpy(out + n, buf, len * sizeof(WCHAR));
        n += (int)len;
        out[n] = 0;
        if (SUCCEEDED(range->lpVtbl->IsEmpty(range, ec, &empty)) && empty) break;
      }
    }
    range->lpVtbl->Release(range);
    if (n >= GLL_MAX_TEXT - 1) break;
  }
  en->lpVtbl->Release(en);
  return out;
}

static HRESULT STDMETHODCALLTYPE Edit_OnEndEditImpl(GllEditSink *self,
                                                     ITfContext *ctx,
                                                     DWORD ec,
                                                     ITfEditRecord *rec) {
  GllObserver *o = self->owner;
  if (!o || o->disabled || !ctx || !rec) return S_OK;
  DWORD pid = o->pid;
  BOOL hasComp = ObserverHasComposition(ctx);
  if (!hasComp) {
    if (o->inComposition) {
      o->inComposition = FALSE;
      ShmSetComposing(pid, FALSE);
      SendState(pid, FALSE);
      WCHAR *text = ObserverReadEditText(ec, rec);
      if (text) {
        if (text[0]) SendCommit(pid, text, lstrlenW(text));
        free(text);
      }
    }
    return S_OK;
  }
  if (!o->inComposition) {
    o->inComposition = TRUE;
    ShmSetComposing(pid, TRUE);
    SendState(pid, TRUE);
    Dbg(L"TSF composition start");
  }
  return S_OK;
}

static HRESULT STDMETHODCALLTYPE Edit_OnEndEdit(GllEditSink *self,
                                                 ITfContext *ctx,
                                                 DWORD ec,
                                                 ITfEditRecord *rec) {
  return Edit_OnEndEditImpl(self, ctx, ec, rec);
}

static HRESULT STDMETHODCALLTYPE Thread_OnSetFocus(GllThreadSink *self,
                                                    ITfDocumentMgr *dimFocus,
                                                    ITfDocumentMgr *dimPrevFocus) {
  GllObserver *o = self ? self->owner : NULL;
  if (o && !o->disabled) ObserverUpdateEditSink(o, dimFocus);
  return S_OK;
}

static HRESULT STDMETHODCALLTYPE Thread_OnPushContext(GllThreadSink *self, ITfContext *pic) {
  return S_OK;
}

static HRESULT STDMETHODCALLTYPE Thread_OnPopContext(GllThreadSink *self, ITfContext *pic) {
  return S_OK;
}

static HRESULT STDMETHODCALLTYPE Thread_OnInitDocumentMgr(GllThreadSink *self, ITfDocumentMgr *dm) {
  return S_OK;
}

static HRESULT STDMETHODCALLTYPE Thread_OnUninitDocumentMgr(GllThreadSink *self, ITfDocumentMgr *dm) {
  return S_OK;
}

static HRESULT STDMETHODCALLTYPE Thread_QueryInterface(GllThreadSink *self, REFIID riid, void **ppv) {
  if (!ppv) return E_INVALIDARG;
  if (IsEqualIID(riid, &IID_IUnknown) || IsEqualIID(riid, &IID_ITfThreadMgrEventSink)) {
    *ppv = self;
    if (self->owner) self->owner->refs++;
    return S_OK;
  }
  if (IsEqualIID(riid, &IID_ITfTextEditSink) && self->owner) {
    *ppv = &self->owner->editSink;
    self->owner->refs++;
    return S_OK;
  }
  *ppv = NULL;
  return E_NOINTERFACE;
}

static HRESULT STDMETHODCALLTYPE Edit_QueryInterface(GllEditSink *self, REFIID riid, void **ppv) {
  if (!ppv) return E_INVALIDARG;
  if (IsEqualIID(riid, &IID_IUnknown) || IsEqualIID(riid, &IID_ITfTextEditSink)) {
    *ppv = self;
    if (self->owner) self->owner->refs++;
    return S_OK;
  }
  if (IsEqualIID(riid, &IID_ITfThreadMgrEventSink) && self->owner) {
    *ppv = &self->owner->threadSink;
    self->owner->refs++;
    return S_OK;
  }
  *ppv = NULL;
  return E_NOINTERFACE;
}

static ULONG STDMETHODCALLTYPE Thread_AddRef(GllThreadSink *self) {
  return self && self->owner ? (ULONG)InterlockedIncrement(&self->owner->refs) : 1;
}

static ULONG STDMETHODCALLTYPE Thread_Release(GllThreadSink *self) {
  return self && self->owner ? (ULONG)InterlockedDecrement(&self->owner->refs) : 1;
}

static ULONG STDMETHODCALLTYPE Edit_AddRef(GllEditSink *self) {
  return self && self->owner ? (ULONG)InterlockedIncrement(&self->owner->refs) : 1;
}

static ULONG STDMETHODCALLTYPE Edit_Release(GllEditSink *self) {
  return self && self->owner ? (ULONG)InterlockedDecrement(&self->owner->refs) : 1;
}

static const GllThreadSinkVtbl g_threadSinkVtbl = {
  Thread_QueryInterface, Thread_AddRef, Thread_Release,
  Thread_OnInitDocumentMgr, Thread_OnUninitDocumentMgr,
  Thread_OnSetFocus, Thread_OnPushContext, Thread_OnPopContext
};

static const GllEditSinkVtbl g_editSinkVtbl = {
  Edit_QueryInterface, Edit_AddRef, Edit_Release, Edit_OnEndEdit
};

static void ObserverCleanup(GllObserver *o) {
  if (!o) return;
  ObserverRemoveEditSink(o);
  if (o->threadSrc && o->threadCookie != TF_INVALID_COOKIE) {
    o->threadSrc->lpVtbl->UnadviseSink(o->threadSrc, o->threadCookie);
    o->threadCookie = TF_INVALID_COOKIE;
  }
  if (o->threadSrc) {
    o->threadSrc->lpVtbl->Release(o->threadSrc);
    o->threadSrc = NULL;
  }
  if (o->tm) {
    o->tm->lpVtbl->Release(o->tm);
    o->tm = NULL;
  }
  if (o->inComposition) {
    ShmSetComposing(o->pid, FALSE);
    SendState(o->pid, FALSE);
    o->inComposition = FALSE;
  }
  if (o->comInit) CoUninitialize();
  free(o);
}

static GllObserver *ObserverCreateAndInit(void) {
  GllObserver *o = (GllObserver*)calloc(1, sizeof(GllObserver));
  if (!o) return NULL;
  o->threadSink.lpVtbl = &g_threadSinkVtbl;
  o->threadSink.owner = o;
  o->editSink.lpVtbl = &g_editSinkVtbl;
  o->editSink.owner = o;
  o->refs = 1;
  o->threadCookie = TF_INVALID_COOKIE;
  o->editCookie = TF_INVALID_COOKIE;
  o->pid = GetCurrentProcessId();
  o->tid = GetCurrentThreadId();

  HRESULT hr = CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);
  if (hr == RPC_E_CHANGED_MODE) {
    /* 线程已是其他模式：不接管，继续尝试拿线程管理器 */
    o->comInit = 0;
  } else {
    o->comInit = 1;
  }

  HMODULE msctf = LoadLibraryW(L"msctf.dll");
  if (!msctf) { ObserverCleanup(o); return NULL; }
  typedef HRESULT (WINAPI *TF_GetThreadMgr_t)(ITfThreadMgr**);
  TF_GetThreadMgr_t getTm = (TF_GetThreadMgr_t)GetProcAddress(msctf, "TF_GetThreadMgr");
  FreeLibrary(msctf);
  if (!getTm) { ObserverCleanup(o); return NULL; }
  if (FAILED(getTm(&o->tm)) || !o->tm) {
    Dbg(L"TSF init: TF_GetThreadMgr fail");
    ObserverCleanup(o);
    return NULL;
  }
  hr = o->tm->lpVtbl->QueryInterface(o->tm, &IID_ITfSource, (void**)&o->threadSrc);
  if (FAILED(hr) || !o->threadSrc) {
    Dbg(L"TSF init: ITfSource fail");
    ObserverCleanup(o);
    return NULL;
  }
  hr = o->threadSrc->lpVtbl->AdviseSink(o->threadSrc, &IID_ITfThreadMgrEventSink,
                                        (void*)&o->threadSink, &o->threadCookie);
  if (FAILED(hr)) {
    Dbg(L"TSF init: thread sink advise fail hr=0x%08x", (unsigned)hr);
    ObserverCleanup(o);
    return NULL;
  }
  ITfDocumentMgr *dm = NULL;
  if (SUCCEEDED(o->tm->lpVtbl->GetFocus(o->tm, (void**)&dm)) && dm) {
    ObserverUpdateEditSink(o, dm);
    dm->lpVtbl->Release(dm);
  }
  Dbg(L"TSF observer ready tid=%lu", o->tid);
  return o;
}

/* ---------------- 延迟初始化（不在 WinEvent 回调里做 TSF COM） ---------------- */
static BOOL IsBlockedProcess(void) {
  WCHAR path[MAX_PATH];
  DWORD n = GetModuleFileNameW(NULL, path, MAX_PATH);
  if (!n) return FALSE;
  WCHAR *name = path;
  WCHAR *p;
  for (p = path; *p; p++) {
    if (*p == L'\\' || *p == L'/') name = p + 1;
  }
  static const WCHAR *blocked[] = {
    L"explorer.exe", L"dwm.exe", L"SearchHost.exe", L"Widgets.exe",
    L"ShellExperienceHost.exe", L"StartMenuExperienceHost.exe",
    L"TextInputHost.exe", L"ApplicationFrameHost.exe", L"RuntimeBroker.exe",
    L"GameBar.exe", L"gamebarpresencewriter.exe", L"svchost.exe",
    L"\u5F52\u96F6\u5F52\u96F6.exe"
  };
  int i;
  for (i = 0; i < (int)(sizeof(blocked) / sizeof(blocked[0])); i++) {
    if (lstrcmpiW(name, blocked[i]) == 0) return TRUE;
  }
  return FALSE;
}

static LRESULT CALLBACK WorkerWndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
  if (msg == g_initMsg) {
    Dbg(L"WORKER init msg received");
    if (g_tlsSlot == 0xFFFFFFFF) return 0;
    GllThreadCtx *ctx = (GllThreadCtx*)TlsGetValue(g_tlsCtxSlot);
    if (!ctx || ctx->obs) return 0;
    /* 初始化失败后 30 秒内不再重试，避免对不稳定线程反复施压 */
    if (ctx->lastAttemptTick != 0 &&
        (GetTickCount() - ctx->lastAttemptTick) < 30000) return 0;
    ctx->lastAttemptTick = GetTickCount();
    ShmEnsure();
    GllObserver *o = ObserverCreateAndInit();
    if (!o) {
      Dbg(L"WORKER observer init failed");
      return 0;
    }
    ctx->obs = o;
    TlsSetValue(g_tlsSlot, o);
    ObserverListAdd(o);
    Dbg(L"WORKER observer ready");
    return 0;
  }
  if (msg == WM_NCDESTROY) {
    if (g_tlsSlot != 0xFFFFFFFF && g_tlsCtxSlot != 0xFFFFFFFF) {
      GllThreadCtx *ctx = (GllThreadCtx*)TlsGetValue(g_tlsCtxSlot);
      if (ctx && ctx->obs) {
        TlsSetValue(g_tlsSlot, NULL);
        ObserverListRemove(ctx->obs);
        ObserverCleanup(ctx->obs);
        ctx->obs = NULL;
      }
    }
  }
  return DefWindowProcW(hwnd, msg, wp, lp);
}

static void EnsureWorkerAndPostInit(void);

static void CALLBACK WinEventProc(HWINEVENTHOOK hook, DWORD event, HWND hwnd,
                                  LONG idObject, LONG idChild, DWORD tid, DWORD time) {
  if (event != EVENT_SYSTEM_FOREGROUND && event != EVENT_OBJECT_FOCUS) return;
  if (g_tlsSlot == 0xFFFFFFFF || g_tlsCtxSlot == 0xFFFFFFFF) return;
  if (IsBlockedProcess()) {
    if (InterlockedExchange(&g_firstBlockedLogged, 1) == 0) {
      Dbg(L"WINEVENT blocked process");
    }
    return;
  }
  if (!g_initMsg) g_initMsg = RegisterWindowMessageW(L"GLL_TSF_INIT_MSG");
  if (!g_initMsg) return;
  if (InterlockedExchange(&g_firstEventLogged, 1) == 0) {
    Dbg(L"WINEVENT first event tid=%lu", tid);
  }
  EnsureWorkerAndPostInit();
}

/* 在前台线程上创建隐藏窗口并触发延迟初始化（WinEvent 与前台消息钩子共用） */
static void EnsureWorkerAndPostInit(void) {
  ShmEnsure();
  GllThreadCtx *ctx = (GllThreadCtx*)TlsGetValue(g_tlsCtxSlot);
  if (!ctx) {
    ctx = (GllThreadCtx*)calloc(1, sizeof(GllThreadCtx));
    if (!ctx) {
      Dbg(L"WINEVENT calloc fail");
      return;
    }
    /* 每个进程只注册一次隐藏窗口类 */
    WNDCLASSW wc;
    memset(&wc, 0, sizeof(wc));
    wc.lpfnWndProc = WorkerWndProc;
    wc.hInstance = g_hinst;
    wc.lpszClassName = GLL_WORKER_CLASS;
    RegisterClassW(&wc);
    ctx->hwnd = CreateWindowExW(0, GLL_WORKER_CLASS, L"", 0,
                                0, 0, 0, 0, HWND_MESSAGE, NULL, g_hinst, NULL);
    if (!ctx->hwnd) {
      Dbg(L"WINEVENT create window fail err=%lu", GetLastError());
      free(ctx);
      return;
    }
    TlsSetValue(g_tlsCtxSlot, ctx);
    Dbg(L"WINEVENT worker window ok hwnd=%p", (void*)ctx->hwnd);
  }
  if (!ctx->obs) {
    BOOL posted = PostMessageW(ctx->hwnd, g_initMsg, 0, 0);
    if (!posted) Dbg(L"WINEVENT post fail err=%lu", GetLastError());
  }
}

/* ---------------- 导出 ---------------- */
static HHOOK g_threadHooks[16];
static DWORD g_threadHookTids[16];

static LRESULT CALLBACK ForegroundHookProc(int nCode, WPARAM wParam, LPARAM lParam) {
  if (nCode == HC_ACTION) {
    EnsureWorkerAndPostInit();
  }
  return CallNextHookEx(NULL, nCode, wParam, lParam);
}

__declspec(dllexport) BOOL WINAPI GllHookThread(DWORD tid) {
  if (tid == 0) return FALSE;
  int i;
  for (i = 0; i < 16; i++) {
    if (g_threadHookTids[i] == tid && g_threadHooks[i]) return TRUE;
  }
  HHOOK h = SetWindowsHookExW(WH_GETMESSAGE, ForegroundHookProc, g_hinst, tid);
  if (!h) {
    Dbg(L"thread hook fail tid=%lu err=%lu", tid, GetLastError());
    return FALSE;
  }
  for (i = 0; i < 16; i++) {
    if (!g_threadHooks[i]) {
      g_threadHooks[i] = h;
      g_threadHookTids[i] = tid;
      Dbg(L"thread hook ok tid=%lu", tid);
      return TRUE;
    }
  }
  UnhookWindowsHookEx(h);
  Dbg(L"thread hook slots full");
  return FALSE;
}

__declspec(dllexport) BOOL WINAPI GllUnhookThread(DWORD tid) {
  int i;
  for (i = 0; i < 16; i++) {
    if (g_threadHookTids[i] == tid && g_threadHooks[i]) {
      UnhookWindowsHookEx(g_threadHooks[i]);
      g_threadHooks[i] = NULL;
      g_threadHookTids[i] = 0;
      Dbg(L"thread hook removed tid=%lu", tid);
      return TRUE;
    }
  }
  return FALSE;
}

__declspec(dllexport) BOOL WINAPI GllInstallHook(void) {
  ShmEnsure();
  if (!g_winEventHook) {
    g_winEventHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_OBJECT_FOCUS,
                                     g_hinst, WinEventProc, 0, 0,
                                     WINEVENT_INCONTEXT | WINEVENT_SKIPOWNPROCESS);
    Dbg(L"WinEvent install -> %s", g_winEventHook ? L"ok" : L"fail");
  }
  return g_winEventHook ? TRUE : FALSE;
}

__declspec(dllexport) BOOL WINAPI GllUninstallHook(void) {
  if (g_winEventHook) {
    UnhookWinEvent(g_winEventHook);
    g_winEventHook = NULL;
  }
  Dbg(L"hooks uninstalled");
  return TRUE;
}

__declspec(dllexport) void WINAPI GllSetDebug(int on) {
  g_debug = on;
}

__declspec(dllexport) BOOL WINAPI GllIsComposing(DWORD pid) {
  if (!g_shm) ShmEnsure();
  if (!g_shm) return FALSE;
  GllShmEntry *e = &g_shm->entries[pid % GLL_SHM_ENTRIES];
  if (e->pid != (LONG)pid) return FALSE;
  return e->composing != 0;
}

BOOL WINAPI DllMain(HINSTANCE hinst, DWORD reason, LPVOID reserved) {
  switch (reason) {
  case DLL_PROCESS_ATTACH:
    g_hinst = hinst;
    InitializeCriticalSection(&g_listLock);
    g_tlsSlot = TlsAlloc();
    g_tlsCtxSlot = TlsAlloc();
    break;
  case DLL_THREAD_DETACH:
    if (g_tlsSlot != 0xFFFFFFFF) {
      GllThreadCtx *ctx = (GllThreadCtx*)TlsGetValue(g_tlsCtxSlot);
      if (ctx) {
        if (ctx->obs) {
          TlsSetValue(g_tlsSlot, NULL);
          ObserverListRemove(ctx->obs);
          ObserverCleanup(ctx->obs);
          ctx->obs = NULL;
        }
        free(ctx);
        TlsSetValue(g_tlsCtxSlot, NULL);
      }
    }
    break;
  case DLL_PROCESS_DETACH:
    GllUninstallHook();
    {
      int i;
      for (i = 0; i < 16; i++) {
        if (g_threadHooks[i]) {
          UnhookWindowsHookEx(g_threadHooks[i]);
          g_threadHooks[i] = NULL;
          g_threadHookTids[i] = 0;
        }
      }
    }
    if (g_tlsSlot != 0xFFFFFFFF) {
      GllThreadCtx *ctx = (GllThreadCtx*)TlsGetValue(g_tlsCtxSlot);
      if (ctx) {
        if (ctx->obs) {
          TlsSetValue(g_tlsSlot, NULL);
          ObserverListRemove(ctx->obs);
          ObserverCleanup(ctx->obs);
          ctx->obs = NULL;
        }
        free(ctx);
        TlsSetValue(g_tlsCtxSlot, NULL);
      }
      while (g_observerList) {
        GllObserver *o2 = g_observerList;
        g_observerList = o2->next;
        ObserverCleanup(o2);
      }
      TlsFree(g_tlsSlot);
      g_tlsSlot = 0xFFFFFFFF;
      if (g_tlsCtxSlot != 0xFFFFFFFF) {
        TlsFree(g_tlsCtxSlot);
        g_tlsCtxSlot = 0xFFFFFFFF;
      }
    }
    if (g_shm) {
      UnmapViewOfFile(g_shm);
      g_shm = NULL;
    }
    if (g_shmFile) {
      CloseHandle(g_shmFile);
      g_shmFile = NULL;
    }
    DeleteCriticalSection(&g_listLock);
    break;
  }
  return TRUE;
}
