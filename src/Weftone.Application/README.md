# Weftone.Application

用例编排与编译器（ADR-0003），依赖 Domain + Abstractions。

- `DocumentCompiler`：Document → RenderPlan 全量编译；事件按 StartSample 稳定排序；
  增益/声像离线预乘进每个 RenderVoice。`Compile` 展开 Playlist 线性时间轴（Song）；
  `CompilePattern`（M1b）编译单个 Pattern 并下发循环区间
  （`LoopStartSample/LoopEndSample`，长度按小节向上取整、至少 1 小节）。
- `PlaybackService`（M1b）：实时播放用例。停止-交换-恢复（D2=a）、
  采样率变化经后端工厂重建、`IsLooping` 供 UI 跳过到末尾自动停。
- `EditSession`（M1b）：UI 线程单写者编辑会话——Document + CommandHistory +
  资产临时工作区；命令事务（Begin/EndTransaction）把拖拽手势合并为单条撤销。
- `ProjectRenderService` / `WavWriter`：离线导出（M1a 竖切 4，CLI 接入）。
- `AssetPreparer`：解码 + 重采样 + AssetRef 映射（M1a 竖切 3）。
- `TickMath`：tick → sample 整数截断映射（PPQ=96）。
- `PanLaw`：线性声像（D4），升级律走 ADR。
