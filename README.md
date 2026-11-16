# Weftone（纬音）

> **Weft（纬线）+ Tone（音）** —— Pattern 是纬线，Playlist 是织物。
>
> An open-source, cross-platform DAW built from scratch with C# / .NET 10.

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10%20LTS-512BD4.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey.svg)](#)
[![Status](https://img.shields.io/badge/status-M2%20pre--alpha-orange.svg)](#路线图)

## 这是什么

Weftone（纬音）是一个用 C# / .NET 从零构建的**开源、跨平台数字音频工作站（DAW）**，
目标平台 Windows / macOS / Linux。工作流致敬 FL Studio 的
Pattern → Playlist → Mixer 范式，但**代码、UI、DSP、工程格式全部自研**：

- 正向规格驱动开发（spec-first），不做二进制逆向；
- 不复制任何专有资产，不复刻 FL Studio 内部架构；
- 不使用 FL / Fruity / Image-Line 的命名或视觉资产。

## 当前状态：M2（自动化 + Autosave + RCU 阶段）

M0（地基）、M1a（headless 竖切）与 M1b（最小 UI）已完成：加载采样 → 画音符 →
Pattern 循环 / Song 排段 → 混音 → 导出 WAV 全链可用。M2
（[specs/M2_AUTOMATION.md](specs/M2_AUTOMATION.md)）四竖切已完成：

- **自动化曲线（headless + UI）**：通道/master volume/pan 关键点曲线，
  编译为增益断点、渲染块内逐样本斜坡（零分配），lane 自绘与画/拖/删手势，
  黄金音频基线 `m2-automation-golden.wav`；
- **Autosave / Crash Recovery**：空闲 ≥5s 或 ≥50 次编辑触发恢复区原子快照，
  崩溃后启动提示恢复/丢弃；
- **RCU plan 交换**：播放中编辑不停后端、位置连续无静音缺口（并发 fuzz 守护）。

仓库另含：项目章程（[AGENTS.md](AGENTS.md)）、八项已定稿的
[架构决策记录](docs/adr/)、[贡献指南](CONTRIBUTING.md)、
GPLv3 [许可证](LICENSE)与[第三方声明](THIRD_PARTY_NOTICES.md)、
vendored [miniaudio](https://github.com/mackron/miniaudio) 与 dr_wav、
`weftone` CLI（离线渲染 + 实时播放）、引擎/架构/应用/UI 测试与三平台 CI。

## 核心设计

采用**六边形架构（端口与适配器）+ 编译器模式 + 数据导向实时层**：

```
ProjectDocument（可编辑 / 可撤销 / 富对象）
   → Compile（非实时线程）
   → RenderPlan（扁平数组 / 预分配 / 无对象图）
   → AtomicSwap（RCU 双缓冲）
   → RealtimeEngine（音频回调只读）
```

- **编辑模型与实时模型彻底分离**：音频线程永远看不到 Note / Clip / Document；
- **实时纪律**：音频回调零分配、零锁、零 GC、零异常、零 native malloc，
  CI 以分配断言强制（`GC.GetAllocatedBytesForCurrentThread()` 差值 == 0）；
- **确定性渲染**：同配置下输出位一致，支持黄金音频回归；
- **headless 优先**：先 `.weft` 工程 → WAV 离线渲染，再贴 UI；
- **wheel-first**：外围用轮子（音频 I/O、解码、重采样），
  核心自研（音频图、transport、PDC、文档模型、撤销），
  所有第三方依赖经 adapter 隔离，核心不泄漏第三方类型。

## 技术栈

| 领域 | 选型 |
|---|---|
| 运行时 | .NET 10 LTS / C# 14 |
| UI | Avalonia 11+ / SkiaSharp / CommunityToolkit.Mvvm（Piano Roll 等自绘） |
| 音频 I/O | miniaudio（MIT-0，vendored） |
| 插件 | CLAP（第一优先）、LV2；VST3 可选非核心模块；VST2 禁止 |
| 工程格式 | `.weft`（zip 容器：project.json + assets + presets，schema 版本化） |
| 测试 | NUnit / Verify / CsCheck / BenchmarkDotNet / NetArchTest |

## 仓库结构

```
/src        C# 源码（Domain / Abstractions / Application / Engine / Dsp / Adapters / App …）
/native     原生代码（weftone_miniaudio shim、clap_bridge、vendored 库）
/specs      功能规格（SPEC-first）
/docs/adr   架构决策记录（ADR-0001 ~ 0008 已定稿）
/tests      引擎测试 / 架构测试 / 黄金音频回归
/tools      HeadlessRender CLI（weftone）
```

## 路线图

| 里程碑 | 内容 |
|---|---|
| **M0**（已完成） | 仓库 / CI / 架构测试 / miniaudio shim / 正弦出声 / RT 断言 / 渲染 CLI |
| **M1a**（已完成） | headless 竖切：文档模型、`.weft` 存取、命令撤销、编译器、transport、Sampler、Mixer、导出 |
| **M1b**（已完成） | 最小 UI：Piano Roll / Playlist / Mixer / 传输条 / 工程存取 |
| **M2**（已完成） | 自动化曲线（通道 vol/pan + master）、Autosave/Crash Recovery、RCU plan 交换 |
| **M3**（已完成） | CLAP 宿主：扫描隔离进程、插件出声（实例表 RCU）、参数与 state 持久化、参数自动化 |
| M4+ | 拉伸 / 切片、内置插件扩展、进程隔离、性能打磨 |

## 构建

```sh
dotnet build -c Release
dotnet test -c Release
dotnet test -c Debug            # RT_ASSERT 分配断言

cmake -S native -B native/build
cmake --build native/build --config Release
```

## 运行

```sh
# 离线渲染 5 秒 440Hz 正弦 WAV（不依赖 native）
dotnet run --project tools/HeadlessRender -c Release -- render --seconds 5 --out out.wav

# 实时播放（需先构建 native shim）
dotnet run --project tools/HeadlessRender -c Release -- play --seconds 5

# 图形界面（M1b，需先构建 native shim；启动后加载演示工程，点播放出声）
dotnet run --project src/Weftone.App -c Release
```

## 贡献

见 [CONTRIBUTING.md](CONTRIBUTING.md)。贡献协议（DCO 或 CLA）待定
（[ADR-0005](docs/adr/ADR-0005-license-commercialization.md)），
确定前暂不接受外部 PR；问题反馈欢迎。

## 许可

- 主程序 / 引擎 / 内置插件：[GPL-3.0-or-later](LICENSE)
- Weftone.PluginSdk（规划中）：独立 MIT，不引用 GPL 项目代码
- 第三方依赖仅接受宽松许可，台账见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)

## 致谢

- [miniaudio](https://github.com/mackron/miniaudio)（MIT-0）—— 音频设备 I/O
