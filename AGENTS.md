# AGENTS.md — Weftone（纬音）
Version: 0.1 | Status: Active | Date: 2026-11-12

## 0. 产品与使命
Weftone（纬音）：用 C#/.NET 从零构建的开源、跨平台 DAW。
名字来源：Weft（纬线）+ Tone（音）——Pattern 是纬线，Playlist 是织物。
工作流致敬 FL Studio，但代码、UI、DSP、工程格式全部自研。
采用正向规格驱动开发，不做二进制逆向，不复制任何专有资产，
不复刻 FL 内部架构，不使用 FL / Fruity / Image-Line 商标。

## 1. 已锁定决策
- 方向 A：正向复刻工作流，不做逆向
- 技术栈：C# / .NET 10 LTS，三平台 Windows / macOS / Linux
- 许可：主程序/引擎/自家插件 GPLv3-or-later；Weftone.PluginSdk 独立 MIT
- 策略：wheel-first，外围用轮子，核心自研，适配器隔离
- 插件：CLAP 第一，LV2 第二，VST3 可选非核心模块，VST2 禁止
- 架构：六边形 + 编译器模式 + 数据导向实时层
- 方法论：Architecture Runway + Spec-first + Vertical Slice
- 命名：Weftone.* 命名空间，CLI 名 weftone，工程包扩展名 .weft

L1 目标：可出声、可编曲、可混音、可导出、可加载 CLAP。
L1 非目标：.flp 导入、VST2、AU、移动端、云、视频、
Harmor/NewTone 级插件、逐像素复刻 FL UI。

## 2. 硬约束（物理约束，不是风格偏好）
- 音频实时线程：零分配、零锁、零 LINQ、零装箱、零 string、
  零 async/await、零 DI/反射、零日志、零异常抛出、零 native malloc。
- Engine 不得引用 UI，不得出现第三方类型。
- 编辑模型与实时模型必须分离：Document → Compile → RenderPlan。
- 音频线程永远看不到 Note/Clip/Document 等富对象。
- 渲染必须确定性：同 RID + 同编译配置 + 同 DSP 路径位一致；
  跨 CPU/平台用容差。
- 三平台 CI 必须绿。
- 核心第三方依赖只用宽松许可；GPL/专有依赖只作可选非核心模块。
- 仓库内不得残留 Daw.* / OpenDAW 占位符，CI 强制检查。

## 3. 方法论
不用瀑布，不用裸敏捷。采用：
Architecture Runway + Spec-first + Vertical Slice。

节奏：
- 不可逆决策先定：线程模型、Document↔Engine 边界、许可、
  工程格式、插件策略、命名 → 写 ADR。
- 每周一个可运行竖切：周末必须有能出声/能导出的 demo。
- 看板 WIP=2；Trunk-based，短分支小 PR。
- 风险优先：先打 GC/实时、音频 I/O 延迟、UI 大音符量、插件宿主。
- Headless 优先：先 .weft → WAV，再贴 UI。
- 门禁：SPEC + 测试 + 分配断言 + 性能预算 + 三平台 CI + 文档。

Agent 工作流：
1. 读 AGENTS.md + 模块 README + specs/<FEATURE>.md + 相关 ADR。
2. 无 SPEC 不实现；先写验收测试与黄金音频基线。
3. 小步实现，保持可运行。
4. 跑分配断言、性能预算、架构测试、三平台 CI。
5. 更新文档、ADR、许可台账。
6. 遇到升级规则（第 16 节）必须停下来问维护者。

角色：Planner / DSP / UI / Host / QA / Docs / License。
每个角色只改自己边界内的代码，跨边界需 ADR。

## 4. 代码架构
六边形（Ports & Adapters）+ 编译器模式 + 数据导向实时层。

核心数据流：
ProjectDocument（可编辑/可撤销/可序列化/富对象）
  → Compile（非实时线程）
  → RenderPlan（扁平数组/预分配/无对象图/无第三方类型）
  → AtomicSwap（双缓冲）
  → RealtimeEngine（音频回调只读，零分配零锁）

依赖规则：
- Weftone.Domain：工程文档、命令、撤销；无依赖。
- Weftone.Abstractions：端口接口、DTO、事件、RenderPlan 契约；无依赖。
- Weftone.Application：用例编排、编译器、单写者文档存储；
  依赖 Domain + Abstractions。
- Weftone.Engine：实时图、transport、PDC、DSP；仅依赖 Abstractions。
- Weftone.Dsp：内置 DSP；仅依赖 Abstractions。
- Weftone.Adapters.*：miniaudio/解码/重采样/CLAP/文件；依赖 Abstractions。
- Weftone.App：Avalonia + MVVM，组合根；依赖 Application + Adapters。
- Weftone.PluginSdk：独立 MIT，不引用 GPL 项目代码。

线程模型：
UI线程 → commands → 文档线程(单写者) → snapshots → UI
文档线程 → compile → RenderPlan → 无锁ring → 音频线程
音频线程 → meters/events → ring → UI

最终进程形态：
- Weftone.App：UI 进程，允许 GC。
- Weftone.Engine.Server：NativeAOT 引擎进程，稳态零分配，GC 不触发。
- Weftone.PluginHost：插件宿主进程，崩溃隔离。
- 共享内存环形缓冲传音频/事件/表计；命名管道传控制。
L1 允许进程内实现，但必须经 IEngineTransport / IPluginHost 边界，
禁止 UI 直连 Engine 内部类型。
L1 单进程期间监控 GC 暂停与 xrun；出现 dropout 触发进程拆分 ADR。

模式：
- 命令日志 + 事务 + 拖拽合并做撤销
- 单写者文档存储
- 无锁 SPSC ring 跨线程
- 双缓冲图交换（RCU，退役在非 RT 线程）
- PDC 离线计算 + 延迟线对齐
- 反馈路由用一块延迟打破环
- MVVM 只用于 UI；Piano Roll/Playlist/Mixer 走 Skia 自绘
- DI 只在组合根；音频线程显式注入预分配服务
- Application 层用 Result；音频线程不抛异常

## 5. 技术栈
- .NET 10 LTS / C# 14
- UI：Avalonia 11+ / SkiaSharp / CommunityToolkit.Mvvm
- Engine：C# headless，NativeAOT 可选；Span<T> / SIMD / POH 缓冲
- 音频 I/O：miniaudio（MIT-0）P/Invoke；
  后续 WASAPI exclusive / ASIO 可选需评审 / CoreAudio / JACK/PipeWire
- MIDI：DryWetMidi（MIT）文件/数据模型；设备 I/O 必要时 RtMidi（MIT）桥
- 插件：CLAP（MIT）第一，LV2（ISC）第二；
  VST3 可选非核心模块（GPLv3 路径，发布前许可评审）；VST2 禁止；AU 后期
- 工程格式：.weft（zip 容器）= project.json + assets/（SHA256）+
  presets/ + metadata；schema 版本 + 迁移；L1 JSON，后续可加二进制缓存
- DSP 依赖：Signalsmith Stretch（MIT）、r8brain-free-src（MIT）、
  dr_libs / stb_vorbis / minimp3（宽松/公共领域）
- 测试：NUnit / Verify / CsCheck / BenchmarkDotNet
- 架构测试：NetArchTest / Roslyn analyzer
- Native：CMake + vendored 单头文件库

## 6. 仓库结构
/src
  Weftone.Domain
  Weftone.Abstractions
  Weftone.Application
  Weftone.Engine
  Weftone.Engine.Server（后期）
  Weftone.Dsp
  Weftone.PluginSdk（独立 MIT）
  Weftone.PluginHost
  Weftone.Adapters.Audio.Miniaudio
  Weftone.Adapters.Codecs
  Weftone.Adapters.Midi
  Weftone.Adapters.Plugins.Clap
  Weftone.Ipc
  Weftone.App
/native
  weftone_miniaudio
  clap_bridge
/specs
  PIANO_ROLL.md / PLAYLIST.md / MIXER.md / AUTOMATION.md ...
/tests
  Weftone.ArchTests / Weftone.Engine.Tests / Weftone.GoldenAudio
/tools
  HeadlessRender（CLI: weftone）
/docs/adr
/.github/workflows
LICENSE / THIRD_PARTY_NOTICES.md / CONTRIBUTING.md
Directory.Build.props / Directory.Packages.props

## 7. Wheel-first 策略
外围用轮子，核心自研：
- 直接用轮子：UI 框架、音频 I/O、解码、重采样、拉伸、
  MIDI 文件、序列化、测试。
- 必须自研：音频图、transport、PDC、文档模型、撤销、
  Piano Roll/Playlist 数据模型、自动化曲线。
- 所有第三方库必须经 adapter 接口：
  IAudioBackend / ISampleDecoder / IResampler / ITimeStretcher /
  IPluginHost / IProjectStore。
- Core/Engine 不泄漏第三方类型。
- 替换轮子不得改变 Core/Engine 公共契约。
- 优先宽松许可（MIT/BSD/ISC/Apache/公共领域）；
  GPL/专有依赖只允许作为可选非核心模块/外部进程并单独评审。
- 基础设施轮子可永久保留，别为了自研而自研。

## 8. 许可与商业化护栏
- 主程序/引擎/自家插件：GPLv3-or-later。
  SPDX: GPL-3.0-or-later
- Weftone.PluginSdk：独立 MIT，不引用 GPL 项目代码。SPDX: MIT
- 核心第三方依赖只用宽松许可，保留未来双许可可能。
- GPL/专有依赖只作可选非核心模块/外部进程，需 ADR 评审。
  VST3 / ASIO 属此类。
- 双许可发行版必须剔除 GPL/专有可选模块。
- 主发行版不得包含闭源/专有许可依赖；
  用户自装第三方插件不算分发依赖。
- 外部贡献接入前必须决定 DCO 或 CLA：
  若保留双许可/闭源商业化可能，选 CLA；纯社区路线选 DCO。
  该待决项仅阻塞外部贡献接入，不阻塞 M0。
- 商标：Weftone / 纬音，优先注册第 9 类（软件）、第 15 类（乐器）。
  已知近似 WeftOne（AI 任务板），拼写与品类不同，启用前需正式 clearance。
  禁止使用 FL / Fruity / Image-Line 命名或视觉资产。
- GPLv3 与 Mac App Store 条款冲突；macOS 走直接分发/公证。
- 商业化可行：官方构建+支持、付费插件、内容市场、云服务。
- 付费闭源插件是独立产品，走 MIT SDK + CLAP + 独立进程，不进 GPL 仓库。
- 音频内容/预设/采样包可单独专有许可。
- GPL 允许收费；已发布 GPL 版本不可撤回。
- 维护 LICENSE、THIRD_PARTY_NOTICES.md、依赖许可台账。
- 确认 AI 生成内容权属符合所用工具条款。

## 9. 实时与性能预算
初始目标（可经 ADR 调整）：
- 128 samples @48k、64 轨、8 插件：引擎 CPU < 1.5ms/块。
- 音频回调分配断言：
  GC.GetAllocatedBytesForCurrentThread() 差值 == 0。
  仅 DEBUG / RT_ASSERT 构建启用。
- RT 内禁止 native malloc；分配断言不覆盖 native，需人工/工具审计。
- 回调耗时直方图 p99 记录并上报。
- UI 60fps；Piano Roll 1 万音符视口裁剪后仍流畅。
- 离线导出快于实时。

门禁分级：
- 硬门禁：正确性、架构规则、三平台构建、分配断言。
- 软门禁：性能预算在基准机测量；CI 仅做相对回归冒烟。

实时纪律：
- 允许：Span<T> / float* / struct / 预分配数组 / SIMD。
- 禁止：new / 装箱 / LINQ / 闭包 / string / lock /
  ArrayPool.Rent / Dictionary 扩容 / 事件订阅 / async / 日志 /
  DI / 反射 / 异常 / native malloc。
- 缓冲：GC.AllocateArray<T>(len, pinned: true)（POH）。
- 短 P/Invoke 可用 [SuppressGCTransition]。
- Windows 音频线程挂 MMCSS：AvSetMmThreadCharacteristics("Pro Audio")。
- denormals 用 FTZ/DAZ 或 flush。
- 参数变化必须平滑/斜坡，避免 zipper noise。
- voice stealing 必须淡出，避免 click。
- 设备热插拔、采样率切换、xrun 必须优雅恢复。
- MIDI 输入带单调时间戳，映射到采样位置。
- ring 背压：命令 busy-retry；meter 丢旧数据。
- 图交换用 RCU，退役在非 RT 线程。
- L1 全量编译 off-RT；RenderPlan 池预分配。
- 轮子若在回调里分配，挪到离线/预处理线程。

## 10. 测试与 CI
- Headless 优先：先 .weft → WAV，再贴 UI。
- 黄金音频回归：内部 DSP 同配置 peak <= 1e-6；
  跨平台 <= 1e-4（可 ADR 调整）。
- 属性测试：DSP 不产生 NaN/Inf，增益/静音/极性正确。
- 增加 null test、impulse response、NaN/Inf fuzz、zip slip 测试。
- 架构测试：依赖规则、Domain/Engine 禁第三方类型、
  实时命名空间禁危险 API、占位符检查（Daw.* / OpenDAW）。
- 分配断言接入 CI（DEBUG / RT_ASSERT）。
- CI：GitHub Actions windows / macos / ubuntu 矩阵 + CMake native。
- 模糊测试：工程加载、插件参数、MIDI 事件。
- 许可扫描 + SPDX 检查。

## 11. 安全
- 工程包解压防 zip slip；工程内不执行脚本。
- 插件扫描在独立进程；文件加载 fuzz。
- 不信任用户工程/采样/插件输入。

## 12. 可观测性
- xrun、GC 暂停、RT 耗时直方图经 ring 上报。
- 日志只在非 RT 线程；崩溃转储；插件崩溃隔离。

## 13. ROADMAP
M0 地基：
仓库/CI/架构测试/weftone_miniaudio shim/C# 绑定/正弦出声/
RT 断言/weftone render CLI。
完成定义：三平台 CI 绿 + 正弦出声 + headless 渲染 5s WAV + 分配断言 0。

M1a headless 竖切：
Domain 模型 + .weft 存取 + 命令撤销 + 编译器 + transport +
Sampler 声部 + Mixer + 导出 + 黄金音频。
完成定义：.weft 工程可确定性渲染出 WAV。

M1b 最小 UI：
Piano Roll 自绘 + Playlist 自绘 + Mixer UI + 传输条 + 工程存取。
完成定义：加载采样 → 画 4 小节 → Pattern 循环 → Playlist 排 2 段 →
调音量/pan → 导出 WAV；撤销/存读盘正常；三平台 CI 绿。

后续：
M2 自动化曲线 + autosave/crash recovery。
M3 CLAP 宿主；发布前 out-of-process；
插件 UI 嵌入（HWND/NSView/X11/Wayland）列为高风险。
M4 拉伸/切片。M5 内置插件扩展。
M6 进程隔离（可由 GC/xrun 指标提前触发）。
M7 性能 pass。M8 UI 打磨。
L2 目标：自绘控件的 IME/高 DPI/无障碍。
M1 不做插件 PDC；内部固定延迟已知。

## 14. Definition of Done
SPEC 完成 + 测试绿 + 分配断言 0 + 性能预算达标 +
三平台 CI 绿 + 文档/ADR 更新 + 许可台账更新。

## 15. 反模式（禁止）
- 音频线程里 DI/反射/事件/日志/async
- 每个音符建一个 Avalonia 控件
- 用 NAudio IWaveProvider 当引擎地基
- 跨线程共享可变对象
- 先铺 100 个插件再回头做引擎
- 没有 headless 就先写 UI
- 第三方类型泄漏进 Core/Engine
- 未经评审引入 GPL 核心依赖
- 残留 Daw.* / OpenDAW 占位符

## 16. Agent 纪律与升级规则
纪律：
- 每模块必须有 README 索引；单文件上限 800 行。
- 小 PR + 自检清单；commit 约定 feat/fix/refactor/test/docs/adr。
- 跨架构边界改动必须人工 review + ADR。
- AI 生成代码需确认权属符合所用工具条款。

升级规则（必须停下来问维护者）：
- RT 违规、xrun、许可不明、跨平台破坏
- 性能预算不达标、架构边界变更、引入平台专属依赖
- DCO/CLA 选择、需要引入 GPL 核心依赖、偏离 ADR

ADR 门禁：
ADR 未 Accepted 的模块不得开工；待决项只阻塞其影响范围。

## 17. ADR 索引（必须创建）
- ADR-0001：方法论——架构先行的规格驱动迭代
- ADR-0002：线程模型与实时零分配
- ADR-0003：Document/Engine 分离与编译器模式
- ADR-0004：端口适配器与 wheel-first
- ADR-0005：许可策略与商业化护栏
- ADR-0006：开放工程格式（.weft）
- ADR-0007：插件策略 CLAP/LV2/VST3
- ADR-0008：产品命名与商标（Weftone / 纬音）

所有 ADR 状态为 Accepted 前，相关实现不得开工。
DCO/CLA 待决仅阻塞外部贡献接入，不阻塞 M0。
