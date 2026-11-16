# Weftone.Adapters.Plugins.Clap

CLAP 插件适配器（SPEC M3 D1=a/D2=a，ADR-0004/0007），实现 Abstractions 的
`IPluginScanner`，仅依赖 Weftone.Abstractions。

- `ClapScanner`：每个 `.clap` 在独立 `weftone-plugin-scan` 进程中扫描——
  崩溃/挂死/超时只丢该文件（AGENTS.md §11 硬约束），其余照常收得；
  缓存 `%LocalAppData%\Weftone\plugins\cache.json` 按（路径、大小、mtime）
  失效，失败不入缓存（重建后自动重试）。构造可注入 CLI 路径与缓存路径（测试）。
- `ClapBridgeNative`：`weftone_clap_bridge` P/Invoke（结构布局与
  `native/clap_bridge/weft_clap.c` 逐字段对齐）；库解析同 codecs 先例
  （`WEFTONE_NATIVE_DIR` → 应用目录 → 仓库 `native/build` 回溯）。

配套独立扫描进程：`tools/Weftone.PluginScan`（exe 名 `weftone-plugin-scan`），
stdin/文件参数 → stdout 每行一个 camelCase JSON 描述；退出码 0=完成、2=失败。

测试：tests/Weftone.Adapters.Plugins.Clap.Tests（依赖 native 构建产物，
缺失时 Ignore；CI 先 CMake 后 dotnet test）。
