# Weftone.Application

用例编排与编译器（ADR-0003），依赖 Domain + Abstractions。

- `DocumentCompiler`：Document → RenderPlan 全量编译；事件按 StartSample 稳定排序；
  增益/声像离线预乘进每个 RenderVoice。`Compile` 展开 Playlist 线性时间轴（Song）；
  `CompilePattern`（M1b）编译单个 Pattern 并下发循环区间
  （`LoopStartSample/LoopEndSample`，长度按小节向上取整、至少 1 小节）。
  **自动化编译（M2）**：通道 vol/pan 两 lane 的 tick 并集逐点求值并预乘 L/R 为
  `GainBreakpoints` 曲线（首末 clamp 断点；无自动化通道退化为静态预乘，行为位级不变）；
  master volume 曲线同构；曲线声部 RenderVoice 只持 velocity。
- `AutomationMath`（M2）：自动化 lane 的 tick 空间求值（FL 式端点 clamp + 线性插值），
  仅编译期/UI 使用。
- `PlaybackService`（M1b）：实时播放用例。停止-交换-恢复（D2=a）、
  采样率变化经后端工厂重建、`IsLooping` 供 UI 跳过到末尾自动停。
- `EditSession`（M1b）：UI 线程单写者编辑会话——Document + CommandHistory +
  资产临时工作区；命令事务（Begin/EndTransaction）把拖拽手势合并为单条撤销；
  `ImportSample`（解码校验 → 工作区 → ImportSampleCommand 一步注册资产+通道）；
  `ExportWav`（Song 全时间轴离线渲染，float32 WAV，独立离线渲染器实例）。
- `ProjectRenderService` / `WavWriter`：离线导出（M1a 竖切 4，CLI 接入）。
- `RecoveryStore` / `AutosavePolicy`（M2 D4=a）：恢复区（%LocalAppData%\Weftone\recovery）
  每工程一槽（key=路径 SHA256，未保存=untitled）；原子写 *.tmp → File.Replace，
  中断的临时写不破坏旧快照；策略=编辑后空闲 ≥5s 或距上次快照 ≥50 次命令
  （纯函数可测）；EditSession 记 LastEditedAtUtc/EditsSinceSnapshot，
  SnapshotTo 全量含资产且不清脏标记。
- `AssetPreparer`：解码 + 重采样 + AssetRef 映射（M1a 竖切 3）。
- `TickMath`：tick → sample 整数截断映射（PPQ=96）。
- `PanLaw`：线性声像（D4），升级律走 ADR。
