# Weftone.ArchTests

- 依赖规则（NetArchTest）：Engine 仅依赖 Abstractions；Abstractions 零内部依赖；
  Adapters 不依赖 Engine/Domain/Application。
- 占位符扫描：源码文件零旧项目前缀残留（AGENTS.md §2，CI 强制）；
  检索 token 运行时拼接避免自匹配。
