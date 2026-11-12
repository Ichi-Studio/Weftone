# Weftone.Application

用例编排与编译器（ADR-0003），依赖 Domain + Abstractions。

- `DocumentCompiler`：Document → RenderPlan 全量编译；事件按 StartSample 稳定排序；
  增益/声像离线预乘进每个 RenderVoice。
- `TickMath`：tick → sample 整数截断映射（PPQ=96）。
- `PanLaw`：线性声像（D4），升级律走 ADR。

后续：单写者文档存储、导出用例（M1a 竖切 4 接入 CLI）。
