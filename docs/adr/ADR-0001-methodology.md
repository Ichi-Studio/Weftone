# docs/adr/ADR-0001-methodology.md
Status: Accepted | Date: 2026-11-12

## Context
DAW 有硬实时约束（128 samples @48k ≈ 2.67ms），但 UX 手感无法前期冻结。
单人/小团队 + AI Agent 协作，需要可验收的小步交付。

## Decision
采用 Architecture Runway + Spec-first + Vertical Slice：
- 只冻结不可逆决策（线程模型、Document↔Engine 边界、许可、工程格式、插件策略），以 ADR 记录。
- 每周交付可运行竖切（能出声/能导出），不做横向铺层。
- 看板 WIP=2，Trunk-based，短分支小 PR。
- 风险优先：先验证 GC/实时、音频 I/O 延迟、UI 大音符量、插件宿主。
- Headless 优先：先 JSON→WAV，再贴 UI。
- DoD 门禁：SPEC + 测试 + 分配断言 + 性能预算 + 三平台 CI + 文档。

## Alternatives
- 瀑布：需求无法冻结，拒绝。
- 纯 Scrum：实时架构会被 sprint 冲垮，拒绝。
- Shape Up：6 周周期对 RT 风险验证太长，借用 appetite 概念。
- 螺旋/RUP：吸收风险优先与架构中心，拒绝全套文档重量。

## Consequences
需要纪律与文档开销；换取后期不重写、AI 有验收标准、集成风险早暴露。

## Verification
每特性有 SPEC + 验收测试；每周 demo；CI 门禁；ADR 覆盖不可逆决策。
