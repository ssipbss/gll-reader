# 测试地基 + 语音追踪（A 阶段）实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给归零归零建立可回归的自动测试，并让"中文丢字"问题能被端到端追踪；同时清理已确认的死代码，不改动现有朗读行为。

**Architecture:** 把 MainForm 里无副作用的文本决策抽到 `SpeechText`（纯函数）和 `RecentSpeech`（去重状态）；把 Speaker 的批量合并抽到 `SpeechBatchPlanner`（纯函数）；语音队列里的每一项带自增编号，日志改成线程安全写入，使"入队 → 取批 → 合并 → 播放"每一环都能对上。

**Tech Stack:** C# / .NET Framework 4.8，系统自带 csc.exe，PowerShell 测试脚本。

## Global Constraints

- 所有源文件保持 UTF-8（编译用 `/codepage:65001`）。
- 本轮只做重构、测试与追踪，**不改变朗读行为**。
- 构建前必须先结束正在运行的 `归零归零.exe`，否则发布目录被占用。
- 每个任务结束都要跑 `tests\run_tests.ps1`（或对应测试）确认全绿。
- 中文注释与字符串必须原样保留，避免编码乱码。

---

### Task 1: 测试骨架（先写会失败的测试）

**Files:**
- Create: `tests/TestRunner.cs`
- Create: `tests/TextDiffTests.cs`
- Create: `tests/RecentSpeechTests.cs`
- Create: `tests/SpeechBatchTests.cs`
- Create: `tests/EnPassTests.cs`
- Create: `tests/run_tests.ps1`
- Create: `tests.rsp`

**Interfaces:**
- Consumes: 尚不存在的 `SpeechText`、`RecentSpeech`、`SpeechBatchPlanner`、已有的 `EnPassTracker`。
- Produces: `tests\test_bin\tests.exe`，退出码 0 表示全部通过。

- [ ] **Step 1: 编写极简测试运行器和用例**

  测试运行器提供 `T.Run(name, action)`、`T.Eq(actual, expected)`；用例覆盖：
  - 文本差异：追加、中间插入、删除、光标基准、窗口平移、过滤组字残留、过滤引号、剥离"第X章"标题、纯空格/纯字母判断。
  - 最近朗读去重：400ms 内同文本且无新按键视为重复；有新按键或超时不算重复。
  - 批量合并：多个中文合并、英文与中文不互吞、Cancel 清空同批、Stop 终止、EnWord 前先冲掉待读字母。
  - EnPass：累计、取消、超过 4 个字母且停顿确认、不足 4 个过期清理、切换元素重置。

- [ ] **Step 2: 运行测试确认失败**

  Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\run_tests.ps1`
  Expected: 编译失败（SpeechText/RecentSpeech/SpeechBatchPlanner 不存在）。

### Task 2: 抽取 SpeechText 纯函数

**Files:**
- Create: `src/SpeechText.cs`
- Modify: `src/MainForm.cs`（15 个静态方法改为委托调用）

**Interfaces:**
- Produces: `public static class SpeechText`，方法名与 MainForm 原静态方法一致：`DiffInserted`、`TryCaretDiff`、`ComputeInserted`、`ShiftDiff`、`LastLine`、`StripHeading`、`IsCnNumeral`、`FilterForSpeech`、`HasCjk`、`ContainsImeSpeakable`、`PunctSpokenForm`、`HasChineseText`、`StripCompositionLetters`、`IsPureSpaces`、`IsPureAsciiLetters`。

- [ ] **Step 1: 确认测试仍红（类型缺失）**
- [ ] **Step 2: 新增 `src/SpeechText.cs`（把 MainForm 中对应方法体原样搬入，依赖 `KeyTranslator`）**
- [ ] **Step 3: MainForm 中原方法改为一行委托，保证生产路径与测试路径同一份逻辑**
- [ ] **Step 4: 跑测试，全绿**

### Task 3: 抽取 RecentSpeech 去重状态

**Files:**
- Create: `src/RecentSpeech.cs`
- Modify: `src/MainForm.cs`（移除 `_lastSpoken/_lastSpokenAt` 字段，`RecentlySpoken/RememberSpoken` 委托）

**Interfaces:**
- Produces: `public sealed class RecentSpeech`，`bool IsDuplicate(string text, DateTime lastKeyAt, DateTime now)`、`void Mark(string text, DateTime now)`。

- [ ] **Step 1: 测试红（类型缺失）**
- [ ] **Step 2: 新增 `src/RecentSpeech.cs` 并接入 MainForm**
- [ ] **Step 3: 跑测试，全绿**

### Task 4: SpeechBatchPlanner + 语音编号追踪 + 死代码清理

**Files:**
- Create: `src/SpeechBatch.cs`
- Modify: `src/Speaker.cs`
- Modify: `src/MainForm.cs`（移除 `_speaker.FlushAll()` 调用）

**Interfaces:**
- Produces: `public enum SpeechItemKind`、`public sealed class SpeechItem { long Id; Kind; Text; Ssml; }`、`public sealed class SpeechBatchPlan`、`public static class SpeechBatchPlanner.Plan(IList<SpeechItem>)`。
- Speaker 公开行为不变；日志增加 `ZH:<id>:<text>`、`W_BATCH ids=[...]`、`W_PLAN zh=[...]`。

- [ ] **Step 1: 测试红（SpeechBatchPlanner 缺失）**
- [ ] **Step 2: 新增 `src/SpeechBatch.cs`，语义与现有 `ProcessBatchCore` 完全一致（含 Cancel 清空同批、Stop 终止）**
- [ ] **Step 3: Speaker 改用 SpeechItem/SpeechBatchPlanner；`SpeakZh/SpeakEn/SpeakEnWord/SpeakEnSsml` 分配自增编号并记入日志；取批与计划结果记日志；删除重复的 `while (_queue.TryTake(...))` 循环**
- [ ] **Step 4: 删除空方法 `FlushAll()` 及其唯一调用点**
- [ ] **Step 5: 跑测试，全绿**

### Task 5: 日志线程安全

**Files:**
- Modify: `src/MainForm.cs`

**Interfaces:**
- 无外部接口变化。

- [ ] **Step 1: `DebugLog`/`LogTest` 用静态锁包住 `File.AppendAllText`，避免多线程同文件追加互相吞行**
- [ ] **Step 2: 编译主程序，跑测试与既有模拟测试**

### Task 6: 构建、自测、提交

- [ ] **Step 1: 结束运行中的 `归零归零.exe`，运行 `build.ps1`**
- [ ] **Step 2: 运行 `tests\run_tests.ps1` 与 `测试\run_test.ps1`，全绿**
- [ ] **Step 3: 启动 `发布\归零归零.exe` 冒烟**
- [ ] **Step 4: 提交 git（信息：refactor: extract pure speech/text logic, add trace IDs and thread-safe logging, add test harness）**
