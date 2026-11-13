# SPEC: M1b 最小 UI

状态: Accepted | 里程碑: M1b | 依据: AGENTS.md §13、ADR-0002/0003/0004
拍板: 2026-11-12 D1=a 实时播放+循环；D2=a 停止-交换-恢复；D3=a Avalonia DrawingContext；D4=a 单窗口固定分区
包版本（NuGet 官方源核实）: Avalonia 11.3.23 / CommunityToolkit.Mvvm 8.4.2 / Avalonia.Headless.NUnit 11.3.23

## 目标

一个 Avalonia 单窗口最小 DAW：加载采样 → Piano Roll 画 4 小节 → Pattern 循环播放 →
Playlist 排 2 段 → Mixer 调音量/pan → 导出 WAV；撤销/重做、存盘/读盘正常。
headless 优先原则不变：所有编辑/渲染逻辑可在无 UI 环境测试，UI 只是薄壳。

## 完成定义（AGENTS.md §13）

加载采样 → 画 4 小节 → Pattern 循环 → Playlist 排 2 段 → 调音量/pan → 导出 WAV；
撤销/存读盘正常；三平台 CI 绿。

## 架构边界（继承 M1a，不新增耦合）

```
Weftone.App（Avalonia + MVVM，组合根，允许 GC）
  ├─ ViewModels（CommunityToolkit.Mvvm，纯 C# 可 headless 单测）
  ├─ Views（Avalonia XAML 壳 + 自绘 Control）
  └─ 组合根 DI：装配 Application + Adapters + Engine
        │
        ▼
Weftone.Application（用例：编辑会话 / 编译 / 渲染 / 存取）
        │  Document → Compile → RenderPlan
        ▼
Weftone.Engine（PlanRenderer + transport + loop，零分配）
        ▲  RenderPlan（不可变）
Weftone.Adapters.Audio.Miniaudio（IAudioBackend，实时出声）
```

- App 是唯一允许引用 Avalonia 的项目；Engine/Application/Domain 不见任何 UI 类型。
- 音频线程只读 RenderPlan，零分配零锁（RtAssert 守护，继承 M1a）。
- 编辑模型（Document 富对象）与实时模型（RenderPlan 扁平数组）分离不变。

## 线程模型（M1b 简化，守住不变量）

- **单写者 = UI 线程**：Document 归 UI 线程独占，命令同步执行；M1b 不引入独立文档线程。
  不变量「跨线程不共享可变对象」成立：音频线程只见不可变 RenderPlan，不见 Document。
- **编译**：命令执行后在 UI 线程同步全量编译（M1b 文档规模小，4 小节/几十音符，<1ms）；
  大工程增量编译与独立文档线程 + 快照留 M2（届时按 ADR-0002 升级）。
- **plan 交付音频线程**：见 D2 交换机制。音频回调内零分配、零锁、零异常。
- **播放位置反馈**：音频线程写位置到预分配字段，UI 用 DispatcherTimer 轮询读取（非 RT 侧），
  不做音频线程→UI 的事件订阅/分配。

## 播放与 transport（Engine 扩展）

- 两种播放模式（传输条切换，致敬 FL Pattern/Song）：
  - **Pattern 模式**：循环当前 Pattern（loop 区间 = pattern 长度），边画边听。
  - **Song 模式**：播放 Playlist 展开的线性时间轴（M1a 编译器已支持），到末尾停止。
- PlanRenderer 新增 loop 支持：position 到 loopEnd 时回绕 loopStart 并重置事件游标；
  回绕零分配。实现取「loop 区间随 RenderPlan 字段下发」（`LoopStartSample/LoopEndSample`），
  Pattern/Song 模式切换经重编译 + 停止-交换-恢复交付，不新增 SetLoop 端口方法。
- 播放/停止/循环状态机放 Application 层 PlaybackService，ViewModel 只发意图。

## RenderPlan 交换（D2）

`PlanRenderer.Load` 当前非线程安全（重置 position/pool 时音频线程可能正在 Render）。
M1b 采用 **停止-交换-恢复**（D2=a）：编辑触发重编译时，PlaybackService 先 Stop 音频后端 →
Load 新 plan → 再 Start，短暂静音换取零并发风险。Load 只在音频后端停止后调用，
音频线程渲染期间绝不 Load。双缓冲原子交换（RCU）留 M2 专项。

## 自绘（D3）

Piano Roll / Playlist / Mixer 三个核心视图自绘，不用每音符一个控件（AGENTS.md §15 反模式）。
- Piano Roll：横轴 tick（小节/拍网格）、纵轴 MIDI key（钢琴键侧栏）；音符矩形 + 力度色；
  视口裁剪（只画可见 tick/key 范围），目标 1 万音符流畅（AGENTS.md §9）。
- Playlist：横轴 tick、纵轨 Pattern clip 块；clip 拖放排布。
- Mixer：每通道音量推子 + pan 旋钮 + 名称；master 推子。
- 几何/命中测试抽成纯函数（tick↔pixel、note hit test），headless 单测覆盖。

## 编辑命令与撤销（Domain 扩展）

M1a 已有 AddNote/RemoveNote/SetTempo/SetChannelMix。M1b 新增：
- MoveNote / ResizeNote（Piano Roll 拖拽）
- AddClip / RemoveClip / MoveClip（Playlist）
- AddChannel / ImportAsset（加载采样建 sampler 通道）
- **拖拽合并**：CommandHistory 增加事务/合并机制（M1a 明确留 M1b）——
  一次拖拽（按下→移动→释放）合并为单条撤销记录；连续同类小步编辑可合并。
- 撤销/重做快捷键 Ctrl+Z / Ctrl+Y，工具栏按钮同步 CanUndo/CanRedo。

## 工程存取与采样导入（Application + Adapters）

- 打开/保存 .weft：复用 WeftProjectStore（M1a），UI 经 OpenFileDialog/SaveFileDialog。
- 采样导入：选 WAV → AssetPreparer 解码/重采样 → 注册 AssetRef + 新建 sampler 通道。
- **未存盘资产内存注册表**：导入后尚未 Save 的采样，解码数据留内存（按 AssetRef 索引）；
  Save 时写入 zip assets/。读盘后从 zip 恢复。Application 层管理，不泄漏到 Engine。
- 标题栏显示工程名 + 脏标记（*）；关闭/新建时提示未保存。

## 导出 WAV

复用 ProjectRenderService（M1a 离线渲染）→ SaveFileDialog 选路径 → WavWriter。
导出走离线路径，与实时播放互不干扰。

## 测试与 CI

- **ViewModel 单测**（NUnit，无 Avalonia 依赖）：编辑命令、撤销合并、播放状态机、脏标记。
- **几何/命中纯函数单测**：tick↔pixel 映射、note/clip hit test、视口裁剪边界。
- **Engine 扩展单测**：loop 回绕正确性、交换并发安全、零分配（RtAssert，双配置）。
- **Avalonia.Headless.NUnit 冒烟**：窗口可开、核心控件存在、基本交互（三平台 CI 可跑，无需显示设备）。
- **CI 不开音频设备**：实时出声本地验证（继承 M0 play 冒烟）；CI 构建 App + 跑 headless/单测。
- 黄金音频基线不变（M1b 不改内部 DSP 路径）；若 loop/交换影响渲染路径需守护。
- 架构测试扩展：App 之外不得出现 Avalonia 类型；占位符扫描不变。

## 竖切节奏（每步可运行可验收）

1. **App 骨架 + 传输条 + 工程存取 + 实时播放接线**：Avalonia 单窗口、MVVM/DI 组合根、
   开/存 .weft、miniaudio + PlanRenderer 实时出声与交换、headless 测试基建。
2. **Piano Roll 自绘 + 音符编辑 + Pattern 循环**：画/移/删/改长度、撤销合并、循环播放、视口裁剪。
3. **Playlist 自绘 + clip 排布 + Pattern/Song 切换**：clip 拖放、两段编排、Song 播放。
4. **Mixer + 采样导入 + 导出 + 全链验收**：volume/pan、加载采样、导出 WAV、DoD 全链走查。

## 验收标准

1. DoD 全链可在 UI 完成：加载采样 → 画 4 小节 → Pattern 循环 → Playlist 排 2 段 →
   调音量/pan → 导出 WAV，导出结果与 headless CLI 渲染一致（同工程同配置）。
2. 撤销/重做覆盖所有编辑（含拖拽合并为单步）；空栈拒绝；存读盘往返 schema 等价。
3. 实时播放零分配（RtAssert 双配置 0）；交换无竞争/无崩溃；loop 回绕正确。
4. Piano Roll 1 万音符视口裁剪后交互流畅（本地基准，软门禁）。
5. 三平台 CI 绿：App 构建 + headless 冒烟 + 全部单测 + 架构测试 + 黄金基线。
6. 模块 README（App/Application 扩展/Engine 扩展）与许可台账（Avalonia MIT、CommunityToolkit MIT）更新。

## 非目标

自动化曲线（M2）、插件宿主（M3）、MIDI 输入/录音、可停靠布局/多窗口、
master limiter、增量编译、独立文档线程 + 快照、IME/高 DPI/无障碍打磨（L2）、拉伸/切片（M4）。

## 待拍板决策（带推荐）

| # | 决策 | 选项 | 推荐 |
|---|---|---|---|
| D1 | 播放模型 | a) 实时播放 + Pattern 循环（miniaudio 出声）  b) 仅离线渲染试听（渲染 WAV 再播） | a（DoD「Pattern 循环」需实时 transport；M0 已出声、M1a 已有 PlanRenderer，接线是自然竖切） |
| D2 | plan 热交换 | a) 停止-交换-恢复（编辑时短暂静音，零并发风险）  b) 双缓冲原子交换（RCU，块边界 pickup，位置连续） | a（M1b 最小且 RT 安全；b 是 ADR-0002 目标架构，涉及旧 plan 退役/voice 生命周期，留 M2 专项） |
| D3 | 自绘技术 | a) Avalonia 自定义 Control + DrawingContext（底层即 Skia）  b) SkiaSharp SKCanvas 直接互操作（CustomDrawOp） | a（满足「Skia 自绘」且跨平台稳、互操作少；1 万音符视口裁剪 DrawingContext 足够；性能不足 M7 再降 SKCanvas） |
| D4 | 窗口布局 | a) 单窗口固定分区（传输条 + Piano Roll/Playlist 切换 + Mixer）  b) 引入 Dock.Avalonia 可停靠 | a（M1b 最小 UI，固定分区够用；docking 是产品方向增量，留后续里程碑） |

## 风险

- **实时交换并发**（D2）：M1b 用停止-交换-恢复规避；若选 b 须专项设计 RCU 退役，触发升级规则需 ADR。
- **三平台 native 音频**：App 需随构建复制 miniaudio native（runtimes/）；CI 构建验证，出声本地验。
- **headless UI 测试跨平台差异**（字体/软件渲染）：冒烟只断言控件存在与基本交互，不做像素级断言。
- **大音符量 UI 性能**：视口裁剪 + 脏区重绘；1 万音符为软门禁，本地基准测量。
- **未存盘资产内存占用**：M1b 采样规模小可接受；大采样池/磁盘缓存留后续。