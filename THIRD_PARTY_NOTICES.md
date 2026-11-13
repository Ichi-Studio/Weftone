# 第三方声明 / THIRD PARTY NOTICES

维护规则（AGENTS.md §8）：新增/升级依赖必须更新本台账并完成许可评审。

## 产品组件

| 组件 | 版本/来源 | 许可（SPDX） | 用途 | 位置 |
|---|---|---|---|---|
| miniaudio | master@2026-11 vendored | MIT-0（多许可可选，见其 LICENSE） | 音频设备 I/O | `native/vendor/miniaudio/` |
| dr_wav | dr_libs master@2026-11 vendored | MIT-0（多许可可选，见其 LICENSE） | WAV 解码 | `native/vendor/dr_wav/` |
| Avalonia | 11.3.23（NuGet） | MIT | 跨平台 UI 框架（含 Desktop / Themes.Fluent / Fonts.Inter） | `src/Weftone.App/` |
| CommunityToolkit.Mvvm | 8.4.2（NuGet） | MIT | MVVM 源生成器（ObservableProperty / RelayCommand） | `src/Weftone.App/` |

M0/M1 其余产品代码为本项目原创，GPL-3.0-or-later。

## 开发/测试依赖（不分发于产品二进制）

| 组件 | 版本 | 许可（SPDX） |
|---|---|---|
| NUnit | 5.0.0 | MIT |
| NUnit3TestAdapter | 6.3.1 | MIT |
| Microsoft.NET.Test.Sdk | 18.12.0 | MIT |
| NetArchTest.Rules | 1.3.2 | Apache-2.0 |
| Avalonia.Headless / Avalonia.Headless.NUnit | 11.3.23 | MIT |
