# docs/adr/ADR-0007-plugin-strategy.md
Status: Accepted | Date: 2026-11-12

## Context
开源优先、三平台、.NET 宿主 VST3 marshal 困难、许可复杂。

## Decision
CLAP 第一（MIT，C ABI）；LV2 第二（ISC）；VST3 可选非核心模块需评审；
VST2 禁止；AU 后期。
L1 内置插件用 C# DSP；后续 CLAP 导出用 native shim 或 Rust/C++。
发布前插件宿主必须 out-of-process（或明确记录风险）。
插件 UI 嵌入（HWND/NSView/X11/Wayland）列为高风险。
插件扫描在独立进程；参数/MIDI 经 ring；PDC 分阶段。

## Alternatives
- VST3 优先：marshal 与许可成本高。
- VST2：无合法授权路径。
- 自造插件格式：生态成本过高。
- 永久 in-process：崩溃风险。

## Consequences
早期插件生态有限，需自研插件；CLAP 宿主工作量。

## Verification
CLAP 一致性测试；沙箱崩溃测试；插件扫描 fuzz。
