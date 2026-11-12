# docs/adr/ADR-0003-document-engine-compiler.md
Status: Accepted | Date: 2026-11-12

## Context
富编辑模型（Note/Clip/Document）与实时扁平数据需求冲突；
撤销/保存/确定性渲染要求编辑模型可序列化。

## Decision
ProjectDocument 由单写者持有；命令日志 + 事务 + 拖拽合并做撤销。
非实时线程 Compile 成 RenderPlan（扁平数组、预分配、无对象图、无第三方类型），
原子交换给引擎。音频线程永远看不到 Document/Note/Clip。
L1 允许全量编译；自动化编译成采样级事件列表；PDC 离线计算。

## Alternatives
- 引擎直读 Document：GC/锁/不确定性，拒绝。
- 全量事件溯源：回放与版本迁移负担，L1 拒绝。
- 跨线程共享可变状态：拒绝。

## Consequences
编译器复杂度；换取 RT 安全、可测、确定性、撤销与保存解耦。

## Verification
架构测试禁 Document 类型进 Engine；黄金音频；headless 确定性渲染。
