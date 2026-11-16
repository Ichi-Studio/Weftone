# docs/adr/ADR-0007-plugin-strategy.md
Status: Accepted | Date: 2026-11-12（M3 增补 2026-11-16）

## Context
开源优先、三平台、.NET 宿主 VST3 marshal 困难、许可复杂。

## Decision
CLAP 第一（MIT，C ABI）；LV2 第二（ISC）；VST3 可选非核心模块需评审；
VST2 禁止；AU 后期。
L1 内置插件用 C# DSP；后续 CLAP 导出用 native shim 或 Rust/C++。
发布前插件宿主必须 out-of-process（或明确记录风险）。
插件 UI 嵌入（HWND/NSView/X11/Wayland）列为高风险。
插件扫描在独立进程；参数/MIDI 经 ring；PDC 分阶段。

## M3 增补：in-process 渲染的已落地边界与拆分触发条件（SPEC M3 D1=a）

已落地（2026-11-16）：扫描/实例化元数据查询在独立 weftone-plugin-scan 进程
（崩溃/超时只丢扫描）；渲染 in-process（clap_bridge C shim，宿主侧 glue 零分配）；
实例表 RCU 隔代退役。

已接受的 in-process 风险：
- 插件 process() 崩溃 = 宿主进程崩溃——编辑成果由 M2 autosave 恢复区兜底；
- 插件内部分配/GC 停顿直接威胁音频回调实时性（xrun 监控覆盖）；
- 非沙箱：插件与宿主同地址空间，信任边界为"用户自行安装的插件"；
- 播放中换 plan 丢弃在飞 note 窗口（可能丢 note-off，第三方插件或挂音——
  已知限制，重触发同 key 可恢复）。

升级到 out-of-process（Weftone.PluginHost + 共享内存 ring + 命名管道，AGENTS §4）
的触发条件（任一满足即启动拆分 ADR）：
1. 发布前（硬性：本 Decision 原文要求）；
2. xrun 监控归因到插件 process 期间 GC/分配，超出 §9 性能预算；
3. 出现第三方插件崩溃造成用户工程损失的案例；
4. 需要加载不受信来源插件（如社区分发渠道）时。

## Alternatives
- VST3 优先：marshal 与许可成本高。
- VST2：无合法授权路径。
- 自造插件格式：生态成本过高。
- 永久 in-process：崩溃风险。

## Consequences
早期插件生态有限，需自研插件；CLAP 宿主工作量。

## Verification
CLAP 一致性测试；沙箱崩溃测试；插件扫描 fuzz；in-process 期 xrun/GC 监控归因记录。
