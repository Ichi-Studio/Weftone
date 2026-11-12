# docs/adr/ADR-0004-ports-adapters-wheel-first.md
Status: Accepted | Date: 2026-11-12

## Context
Wheel-first 要先跑通；三平台后端不同；未来要能换轮子且保留许可灵活性。

## Decision
六边形架构：Domain/Abstractions 无依赖；Engine 仅依赖 Abstractions；
所有轮子经 adapter（IAudioBackend/ISampleDecoder/IResampler/ITimeStretcher/
IPluginHost/IProjectStore）。Core/Engine 不泄漏第三方类型。
外围用轮子（UI/IO/解码/重采样/拉伸/MIDI 文件/序列化/测试），核心自研
（音频图/transport/PDC/文档/撤销/Piano Roll/Playlist/自动化）。
核心第三方依赖只用宽松许可；GPL/专有依赖只作可选非核心模块/外部进程。
基础设施轮子可永久保留。

## Alternatives
- NAudio IWaveProvider 当地基：形状不匹配，拒绝。
- 全部自研：太慢，拒绝。
- 全面微内核化：过度设计，拒绝。

## Consequences
适配器样板代码；换取可替换、可测、跨平台、许可灵活。

## Verification
架构测试；适配器契约测试；许可台账。
