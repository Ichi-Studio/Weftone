# docs/adr/ADR-0006-project-format.md
Status: Accepted | Date: 2026-11-12

## Context
需要自有开放格式，git 友好，可迁移，可恢复崩溃。

## Decision
zip 工程包：project.json + assets/（SHA256 命名）+ presets/ + metadata。
schema 版本 + 迁移；相对路径；L1 用 JSON，后续可加二进制缓存。
M2 加自动保存/崩溃恢复；L1 不持久化撤销日志。
解压防 zip slip；工程内不执行脚本。

## Alternatives
- 类 .flp 二进制：不透明，拒绝。
- SQLite：可行但 L1 偏重。
- 全量事件溯源：早期负担。

## Consequences
大工程 JSON 性能一般；后续二进制缓存；需迁移语料库。

## Verification
往返测试；迁移语料库；zip 加载 fuzz。
