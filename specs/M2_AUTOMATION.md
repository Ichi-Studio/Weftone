# SPEC: M2 自动化曲线 + Autosave/Crash Recovery

状态: Accepted | 里程碑: M2 | 依据: AGENTS.md §13、ADR-0002/0003、M1b 延后项
拍板: 2026-11-13 D1=a 自动化+autosave+RCU（四竖切）；D2=a 关键点+线性插值；
D3=a 通道 vol/pan + master；D4=a 恢复区+定时+原子替换

## 目标

两条主线：
1. **自动化曲线**：通道 volume/pan（含 master）随时间的可编辑曲线——画点、拖点、
   边播边听平滑无 zipper；持久化与撤销完备。
2. **Autosave / Crash Recovery**：编辑过程定时快照到恢复区（原子写），
   崩溃/强杀后重启检测并提示恢复。

候选第三线（D1 拍板）：**RCU 双缓冲 plan 交换**（M1b D2=b 明确"留 M2 专项"）——
消除编辑时停止-交换-恢复的 ~3ms 静音；自动化编辑边听场景下该静音会被高频触发。

## 完成定义

- 自动化可画可听：选中通道 volume/pan 曲线，Pattern/Song 两种模式播放均平滑跟随；
- 自动化点编辑（画/移/删）撤销合并为单步；存读盘往返曲线等价；
- RT 不变量不变：音频回调零分配零锁（RtAssert 双配置 0）；
- 自动化黄金音频基线入库（斜坡确定性，跨平台 ≤1e-4）；
- autosave：崩溃后重启可恢复至最近自动快照（内容=当时 Document 全量）；
- RCU 交换：播放中编辑不再停止后端，位置连续无静音缺口（专用并发测试守护）；
- 三平台 CI 绿。

## 架构边界（继承，不新增耦合）

```
Domain       AutomationLane（点列表）+ 点编辑命令（from/to 模式）
Application  编译期把自动化点变 RenderPlan 增益断点数组；autosave 用例
Engine       PlanRenderer 块内增益斜坡插值（零分配）；RCU 交换（若入 M2）
App          自动化 lane 自绘（复用 PianoRoll 网格/裁剪模式）；恢复提示对话框
```

- 音频线程仍只见不可变 RenderPlan；自动化=预计算断点数组，回调内线性插值，无字典/委托。
- schema 继承 v1 加可选字段（先例：masterVolume 双向兼容）；不 bump 版本。

## 自动化数据模型（D2/D3 拍板）

- `AutomationPoint { long Tick; double Value; }`，按 Tick 升序，值域随参数
  （volume [0,2]、pan [-1,1]）。
- 通道持有 `VolumePoints` / `PanPoints`（master 同构挂 Document）。
- 命令：AddPoint / RemovePoint / MovePoint（from/to 显式 + 事务合并，同 M1b 手势模式）。
- 同 tick 点冲突：替换（后者覆盖）；首点前/末点后取端点值（FL 风格 clamp）。

## 渲染（随 D2 派生，不需单独拍板）

- 编译器：点 → 该通道的 `GainBreakpoints[]`（Sample 位置 + 预乘 L/R 值）入 RenderPlan；
  无自动化的通道退化为单断点（现状行为，黄金基线不变）。
- PlanRenderer：每声部按其通道断点数组做分段线性增益（块内逐样本斜坡），
  与既有 FadeTail/steal 包络相乘；零分配（数组随 plan 预分配）。
- **确定性**：同曲线同配置输出位一致——黄金基线扩展必测。

## Autosave / Crash Recovery（D5 拍板）

- 触发：编辑后空闲 ≥N 秒（默认 5s）或距上次快照 ≥M 次命令（默认 50），
  UI 线程执行（全量 Save 到恢复区，M1b 规模 <5ms）。
- 原子写：写 `*.weft.tmp` → File.Replace；恢复区按用户级目录管理
  （Windows %LocalAppData%\Weftone\recovery\，跨平台经 Environment.GetFolderPath）。
- 启动检测：恢复区存在比上次显式保存新的快照 → 提示「恢复 / 丢弃」。
- 保存/另存成功即清理对应恢复项；正常退出清理全部。

## 编辑 UI（复用 M1b 模式）

- Piano Roll 下方新增可切换 automation lane：左侧参数选择（通道×参数），
  曲线折线 + 关键点圆点；手势=画点/拖点/右键删（同音符手势三件套）。
- 视口裁剪与命中测试继续走纯函数 + headless 单测。
- Mixer 推子与自动化并存规则：通道有曲线时推子只读显示当前播放值（M2 不做写曲线的推子录 制——留后续）。

## 线程与 RT 约束（RCU 交换，D1=a）

- 交换设计：不可变 plan 数据（voices/assets/gain 数组）volatile 引用原子换入；
  渲染器 transport 状态（position/事件游标/活跃声部池）不重置，
  事件游标按当前绝对位置在新 plan 上二分重定位。
- 旧 plan 等待 GC 自然回收（不可变 ⇒ 无显式退役锁）；Load 期间音频线程绝不阻塞。
- 并发测试：随机交错 Load/Render 的确定性 fuzz + 位置连续性断言。

## 测试与 CI

- Domain：点命令往返/事务合并/同 tick 覆盖/值域拒绝。
- Application：断点编译确定性（同曲线两次编译位一致）、首末点 clamp、
  自动化往返 schema 等价、autosave 原子性（写一半模拟崩溃 → 旧快照完好）。
- Engine：块内斜坡正确性（解析值对比）、零分配（RtAssert）、RCU 交错 fuzz。
- App：lane 几何/命中纯函数、VM 参数选择与撤销、headless 冒烟、恢复提示流。
- 黄金音频：新增 m2-automation-golden.wav（volume 斜坡 + pan 摆动）。
- CI 不跑性能，基准继续 Explicit 本地。

## 竖切节奏（每步可运行可验收）

1. **自动化 headless 全链**：Domain 点模型/命令 + 编译断点 + 渲染斜坡 + 黄金基线。
2. **自动化编辑 UI**：lane 自绘 + 手势 + 参数选择 + 撤销合并 + 存读盘。
3. **Autosave/Recovery**：快照策略 + 原子写 + 启动恢复流。
4. **RCU 交换**：plan 数据原子换 + 游标重定位 + 交错 fuzz + 静音消除验证。

## 非目标

MIDI 录制自动化、LFO/公式曲线、插件参数自动化（M3+）、per-note 自动化、
增量编译、独立文档线程+快照（无痛点，留 M6 前置评估）、master limiter、
多撤销栈/分支撤销、协作/云端。

## 风险

- **斜坡乘入的位置对齐**：声部在断点间跨越块边界需连续插值，边界 off-by-one
  是黄金基线最容易翻车处——先写解析期望再实现。
- **RCU 游标重定位**：播放中编辑使事件序变化，漏触发/重复触发需 fuzz 守护；
  这是 ADR-0002 级线程改动，若拍板含入且实现遇阻，升级规则回维护者。
- **autosave 与用户保存竞态**：快照期间用户恰好 Save——UI 线程串行化天然无竞态
  （单写者），但需防恢复区与工程目录双写混乱。
- **自动化编辑高频换 plan**：RCU 到位前（竖切 4 之前），拖点期间沿用 M1b 策略——
  手势落点才重编译交换，避免拖动中反复 3ms 静音。

## 待拍板决策（带推荐）

| # | 决策 | 选项 | 推荐 |
|---|---|---|---|
| D1 | M2 范围 | a) 自动化 + autosave + RCU 交换（四竖切） b) 自动化 + autosave（三竖切，RCU 留 M2.5/M6 触发） | a（M1b D2 已注明"RCU 留 M2 专项"；自动化边听编辑会高频触发 3ms 静音，正是其止痛场景；若竖切 4 实现遇阻按升级规则暂停） |
| D2 | 曲线数据模型 | a) 关键点+线性插值 b) 贝塞尔/样条曲线对象 c) 步进/LFO 采样 | a（FL 风格 event points；确定性、RT 编译与黄金基线都最简；复杂曲线 M5） |
| D3 | 自动化参数范围 | a) 通道 volume/pan + master volume b) 仅通道 volume c) 任意参数框架（插件参数预留） | a（覆盖混音核心且模型同构；插件参数自动化等 M3 有 SDK 后再泛化） |
| D4 | autosave 策略 | a) 恢复区目录 + 定时/编辑触发 + 原子替换 + 启动提示 b) 工程同目录 .bak c) 命令日志(journal)增量恢复 | a（M2 规模全量快照 <5ms，简单可靠；journal 留 M6 崩溃兜底强化） |
