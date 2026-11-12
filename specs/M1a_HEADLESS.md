# SPEC: M1a headless 竖切

状态: Accepted | 里程碑: M1a | 依据: AGENTS.md §13、ADR-0002/0003/0006
拍板: 2026-11-12 D1=a 整数 tick PPQ=96；D2=a vendored dr_wav；D3=a 离线线性插值；D4=a linear pan

## 目标
一个 `.weft` 工程（含采样资产）可经 headless CLI 确定性渲染出 WAV；
编辑操作有命令撤销；输出有黄金音频基线守护。

## 完成定义
`.weft` 工程可确定性渲染出 WAV（同工程 + 同配置 → 逐位一致）。

## 数据模型（Domain，富对象可撤销）

```
ProjectDocument
  ├─ tempo (BPM)、sampleRate
  ├─ channels: Channel[]          // 声部：kind=sine|sampler，volume、pan
  │    └─ （notes 挂在 Pattern 下，按 channel 关联）
  ├─ patterns: Pattern[]          // M1a 实际只用 1 个，但容器先立
  │    └─ notes: Note[] { channelId, tick, key, velocity, lengthTicks }
  ├─ playlist: Clip[] { patternId, startTick }
  └─ assets: AssetRef[] { id, relativePath, sha256 }
```

- 时间单位：整数 tick，PPQ=96（每拍 96 tick，FL 同型），避免浮点累积不确定。
- 命令撤销：`ICommand { Apply(doc), Revert(doc) }` 命令日志 + 事务；
  拖拽类合并（合并策略 M1b 补，M1a 逐条撤销即可）。
- M1a 单线程同步执行（headless），线程边界接口预留（IEngineTransport）。

## .weft 格式 v1（ADR-0006）

zip 容器：`project.json`（schemaVersion=1，上述模型）+ `assets/*.wav`（SHA256 命名）
+ `metadata.json`。加载校验 SHA256；解压防 zip slip；schema 迁移链留接口。

## 编译器（Application 层，新增项目）

`DocumentCompiler.Compile(ProjectDocument) → RenderPlan`：
- 非实时线程执行，全量编译（ADR-0003）；
- RenderPlan：按 tick 排序的扁平事件数组（noteOn 采样位置、时长、gain/pan 预乘、
  asset 样本引用），预分配、无对象图、无第三方类型；
- 同 Document ⇒ 同 RenderPlan（幂等，进测试）。

## Engine 扩展（仅依赖 Abstractions）

- transport：起止 tick、循环（playlist 顺序展开为线性时间轴）、BPM→采样位置换算；
- voice 调度：noteOn 取预分配 voice 池、noteOff/midi 关断、voice stealing 淡出防 click；
- Mixer：channel gain/pan + master gain（常数，M1a 无自动化）；
- 渲染仍是零分配（RtAssert 守护），xrun/耗时直方图 p99 从本轮接入。

## 采样链（wheel-first）

- 解码：vendored `dr_wav`（公共领域）经 `ISampleDecoder` 端口（新增
  `Weftone.Adapters.Codecs`）；M1a 支持 16/24/32f PCM WAV；
- 重采样：资产采样率 ≠ 工程采样率时，加载后离线线性插值到工程采样率
  （质量标注：黄金基线内自洽；r8brain 高质量重采样留 M4）；
- Sampler voice：整采样触发，key→播放速率（M1a 采样库 C4=C5 基准约定）。

## CLI

`weftone project render <path.weft> [--out x.wav] [--from-tick --to-tick]`：
打开 .weft → 编译 → 离线渲染 → WAV。`weftone project info <path.weft>` 概览。

## 竖切节奏（每步可运行可验收）

1. Domain 模型 + 命令撤销 + .weft 存取往返（无音频，测试：往返一致/zip slip/坏包拒绝）
2. 编译器 + transport + 多音符复音 sine 渲染（测试：tick→sample 映射、复音调度、零分配）
3. Sampler（dr_wav + 离线重采样 + voice）+ Mixer（测试：解码往返、重采样正确、mix 数值）
4. 全链：.weft → WAV 导出 + 黄金音频基线 + 工程加载 fuzz（AGENTS.md §10/§11）

## 验收标准

1. 上述 CLI 对示例工程确定性渲染：同进程两次逐位一致；
2. CI 三平台绿：黄金音频对比峰值差 ≤ 1e-4（跨平台容差，ADR/AGENTS.md §10）；
   基线由维护者本机生成并提交（同机位一致标准 ≤ 1e-6 用本地测试覆盖）；
3. 撤销/重做到边界（空栈拒绝）；.weft 往返 schema 等价；
4. zip slip / 畸形 zip / 错误 SHA256 / 非法 schemaVersion 全部拒绝且错误信息可读；
5. 渲染路径分配断言 0（离线与单测模拟回调）；
6. 模块 README 与许可台账（dr_wav）更新。

## 非目标
UI（M1b）、MIDI 输入、自动化曲线（M2）、插件（M3）、实时编辑并发、增量编译。

## 待拍板决策（带推荐）

| # | 决策 | 选项 | 推荐 |
|---|---|---|---|
| D1 | 时间单位 | a) 整数 tick PPQ=96  b) double beats | a（整数确定性、FL 同型） |
| D2 | WAV 解码 | a) vendored dr_wav  b) 纯 C# 自写 | a（wheel-first，公共领域） |
| D3 | 资产重采样 | a) 加载时离线线性插值  b) 现在就上 r8brain | a（L1 够用，黄金基线自洽；b 留 M4） |
| D4 | 声像律 | a) linear  b) constant-power (-3dB) | a（可解释、可逆推；升级走 ADR） |

## 风险

- 黄金基线跨 runner 浮点差异（MathF.Sin 等）：以 1e-4 容差吸收，必要时采样器主导基线；
- schema v1 若遗漏 Pattern/Playlist 关键字段，M1b 需迁移 v2——已按最终工作流形状建模降低概率；
- voice 池大小与复音上限：M1a 固定 64 voices（超限偷声 + 淡出），参数化留 M2。
