# SPEC: M3 CLAP 宿主

状态: Accepted | 里程碑: M3 | 依据: AGENTS.md §13、ADR-0002/0004/0007、M2 非目标（插件参数自动化 → M3+）
拍板: 2026-11-16 D1=a 扫描独立进程+渲染 in-process；D2=a clap_bridge C shim；
D3=a 乐器+参数 set+参数自动化；D4=a 自研 vendored 测试插件；
D5=a 引擎侧实例表+plan slot 索引（RCU 换表）；D6=a 参数编译为 plan 事件点

## 目标

宿主可加载第三方 CLAP 插件并纳入既有全链：

1. **扫描隔离**：独立进程扫描插件目录，产出元数据缓存（id/名/厂商/参数描述），
   插件崩溃只丢扫描进程（AGENTS.md §11 硬约束）。
2. **可出声**：通道挂 CLAP 乐器插件 → 音符事件 → 插件 process() → 混入通道；
   Pattern/Song 两模式、导出 WAV、黄金基线全部走通。
3. **可控可存**：插件参数可设、随 `.weft` 持久化（含 state chunk）、
   参数自动化复用 M2 曲线框架。
4. **RT 不变量不动摇**：零分配零锁（插件自身分配不由宿主保证，
   但宿主侧 glue 必须零分配）；确定性渲染承诺保留（宿主侧输入完全确定）。

## 完成定义（草案）

- 扫描：标准 CLAP 搜索路径 + 用户目录 → 独立进程枚举 → JSON 缓存；坏插件不影响扫描进程存活；
- 自研测试插件（vendored，见 D4）在 headless 全链出声：`.weft` → WAV 位级确定；
- 参数 set/save/load 往返等价；参数自动化画/拖/删/撤销/持久化与 M2 通道曲线同构；
- 音频回调宿主侧 glue 零分配（RtAssert）；插件事件输入确定性（同工程位一致）；
- 三平台 CI 绿（Linux 用 CLAP 官方示例路径无依赖，自研插件随 CI 构建）；
- in-process 风险与 out-of-process 触发条件写入 ADR-0007 增补（维护者定稿）。

## 架构边界（继承，不新增耦合）

```
Domain        Channel.PluginRef(id/实例槽) + 参数值/自动化 lane + 命令
Abstractions  IPluginScanner / IPluginInstance 工厂端口 / RenderPlan 事件扩展
Application   扫描用例（独立进程编排）、参数/状态持久化、编译器扩展
Engine        引擎侧插件实例表（RCU 换表）+ 音频线程 process 调用 glue
Adapters.*    Weftone.Adapters.Plugins.Clap：clap_bridge P/Invoke + 扫描器
native        clap_bridge（C shim：宿主回调 ABI 扁平化）+ vendored CLAP 头
tools         weftone-plugin-scan（独立扫描进程 CLI）
```

## 关键设计

- **C shim（D2）**：CLAP 为 C ABI；宿主侧须实现 host 回调表（log/timer/
  input-output event streams 等）。C# 直接封送 vtable 回调在 GC/稳定性上风险高，
  参照 weftone_miniaudio 先例以 C shim 把两侧都翻成平坦函数。
- **实例生命周期（D5）**：plan 保持"不可变数据"承诺——插件实例表在引擎侧，
  plan 声部带 PluginSlotIndex；编译产新表 → RCU 原子换入（复用 M2 竖切 4 机制），
  旧实例在非 RT 线程退役。实例按 (pluginId, channel) 复用，编辑不重建状态。
- **参数传递（D6）**：参数值编译为 plan 内参数事件点（与音符同机制），
  process 前按序 apply——保持确定性；RT ring 直推非确定，拒绝。
- **事件流**：音符 → clap_event_note_on/off（velocity/key/channel）；
  宿主侧事件 buffer 预分配 per plan；输出事件（M3 只读不处理，记 D6 外围）。
- **状态持久化**：通道参数值入 project.json；插件二进制 state chunk 存
  `.weft` presets/（SHA256 同资产路径）；加载时 state→实例。
- **PDC**：读取 clap_latency 报告并写入 plan 元数据，M3 不做补偿（沿用 M1 决策），
  UI 提示受影响通道。

## 竖切节奏（每步可运行可验收，待拍板后细化）

1. **扫描竖切**：vendored CLAP 头 + clap_bridge 骨架 + 独立进程扫描 + JSON 缓存 + 崩溃隔离测试。
2. **出声竖切**：自研测试插件 + 通道挂插件 headless 全链 + 黄金基线 + RT 断言。
3. **参数竖切**：参数 set + state chunk 持久化 + UI 参数面板（标准控件）。
4. **自动化竖切**：参数 lane 复用 M2 框架 + 编译参数事件点 + 往返/撤销/基线。

## 非目标

效果器链/多总线/侧链、插件 UI 嵌入（ADR-0007 高危，另立专项）、
out-of-process 渲染（发布前触发）、LV2/VST3、note expression、
参数自动化录制（写曲线的推子）、采样率协商（固定工程采样率）、GUI 线程 timer 支持。

## 风险

- **宿主回调 ABI**：shim 边界封送错误 → 段错误；以自研插件 + 官方 clap-validator 先行；
- **in-process 崩溃半径**：渲染进程内加载 = 插件崩则宿主崩（ADR-0007 接受、发布前拆分）；
  扫描已隔离，M3 需记录已知风险；
- **第三方插件不确定性**：黄金基线只能用自研插件；第三方插件不承诺位级一致；
- **state chunk 大小/版本**：插件升级后 chunk 失配 → 加载失败须优雅降级（丢 state 保工程）；
- **GC 暂停 × 插件 process**：回调期间 GC 触发（插件内部分配）→ xrun 监控重点盯。

## 待拍板决策（带推荐）

| # | 决策 | 选项 | 推荐 |
|---|---|---|---|
| D1 | M3 进程形态 | a) 扫描独立进程 + 渲染 in-process（发布前评估拆分） b) 全 out-of-process（IPC 音频 ring） | a（ADR-0007 仅要求发布前 out-of-process；b 的 IPC 音频路径工作量≈一个里程碑） |
| D2 | CLAP 绑定形态 | a) clap_bridge C shim（回调扁平化） b) 纯 C# vtable 封送 | a（miniaudio 先例；b 的回调封送 + GC 固定风险已在 M0 评估中排除） |
| D3 | M3 参数范围 | a) 乐器 + 参数 set + 参数自动化（复用 M2） b) 仅静态参数 c) 含效果器链 | a（接上 M2 留的"插件参数自动化 M3+"；效果器链留 M5 内置插件扩展同批） |
| D4 | CI 黄金基线插件 | a) 自研 vendored 测试插件（C，sine+gain+tune，GPL，随仓构建） b) 依赖第三方免费插件 | a（可入仓、三平台一致、参数语义可控；b 许可/平台/下载不可控） |
| D5 | 插件实例生命周期 | a) 引擎侧实例表 + plan 带 slot 索引（RCU 换表） b) 每次编译重建实例 | a（b 破坏插件内部状态——滤波器尾巴/随机种子，听感不可接受） |
| D6 | 参数变化传递 | a) 编译为 plan 参数事件点（确定性） b) RT ring 实时直推 | a（保黄金基线/位级一致核心承诺；b 留实时调参预览层另议） |
