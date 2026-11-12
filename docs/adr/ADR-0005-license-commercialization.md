# docs/adr/ADR-0005-license-commercialization.md
Status: Accepted（DCO/CLA 待决，仅阻塞外部贡献） | Date: 2026-11-12

## Context
要开源、防闭源 fork、保留商业化与插件生态；VST3/ASIO 许可复杂。

## Decision
- 主程序/引擎/自家 GPL 插件：GPLv3-or-later。
- Weftone.PluginSdk：独立 MIT，不引用 GPL 项目代码。
- 核心第三方依赖只用宽松许可；GPL/专有依赖只作可选非核心模块，需 ADR 评审。
- VST3：可选非核心模块，GPLv3 路径发布前评审；VST2 禁止；ASIO 单独评审。
- 双许可发行版必须剔除 GPL/专有可选模块。
- 外部贡献接入前必须定 DCO 或 CLA；想保留双许可选 CLA。
- 商标尽早注册；GPLv3 与 Mac App Store 冲突，macOS 直接分发/公证。
- 付费闭源插件是独立产品，走 MIT SDK + CLAP + 独立进程，不进 GPL 仓库。
- 音频内容/预设/采样包可专有许可。GPL 允许收费；已发布 GPL 版本不可撤回。
- 不分发闭源依赖；用户自装第三方插件不算分发依赖。

## Alternatives
- 全 MIT：允许闭源 fork，无法用 GPL 轮子。
- 全 GPL 含 SDK：伤害插件生态。
- AGPL：桌面项目网络条款过重。

## Consequences
合规开销；双许可受第三方 GPL 限制；需维护 notices 与 SPDX。

## Verification
许可台账 + SPDX + CI 许可扫描；贡献协议门禁。
