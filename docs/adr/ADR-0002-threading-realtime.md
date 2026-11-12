# docs/adr/ADR-0002-threading-realtime.md
Status: Accepted | Date: 2026-11-12

## Context
GC 会暂停所有托管线程；音频回调超时即爆音；原生插件可能崩溃。

## Decision
线程模型：UI 线程 / 文档单写者线程 / 音频 RT 线程 / worker 池。
最终进程形态：Weftone.App（允许 GC）、Weftone.Engine.Server（NativeAOT，稳态零分配）、
Weftone.PluginHost（崩溃隔离）。L1 允许进程内实现，但必须经 IEngineTransport /
IPluginHost 边界。
RT 规则：零分配零锁；SPSC 无锁 ring；双缓冲图交换（RCU，退役在非 RT 线程）；
命令 ring 满返回 busy-retry，meter ring 满丢旧数据；
Windows 挂 MMCSS；denormals 用 FTZ/DAZ 或 flush。
L1 单进程期间监控 GC 暂停与 xrun；出现 dropout 触发进程拆分 ADR。

## Alternatives
- 永久单进程：GC 风险不可控。
- Actor/mailbox：分配不可控，不进 RT。
- 锁/async/Task：优先级反转与分配，拒绝。

## Consequences
后期 IPC 复杂度；换取 GC 隔离、崩溃隔离、可预测延迟。

## Verification
DEBUG/RT_ASSERT 构建启用分配断言；xrun/GC 监控；架构测试禁危险 API。
