# Weftone.Adapters.ProjectStore

`.weft` v1 工程包存取（ADR-0006），实现 Domain 的 `IProjectStore`。

包结构：`metadata.json`（schemaVersion/generator）+ `project.json` + `assets/*`。

加载校验链：zip 条目白名单（防 zip slip：拒绝根路径/`..`/反斜杠/白名单外条目）
→ schema 版本 → 域规则（引用完整性、值域）→ 资产 SHA256 逐项比对。
非法包统一抛 `InvalidProjectException`。

Save 时按源文件重算 SHA256 回填 AssetRef；资产相对路径由文档定义（限 `assets/` 前缀），
SHA256 命名去重策略留 M1b 再定。
