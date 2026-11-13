---
kind: dependency_management
name: Weftone DAW：NuGet 中央版本管理 + CMake vendored 原生库的双轨依赖治理
category: dependency_management
scope:
    - '**'
source_files:
    - Directory.Packages.props
    - Directory.Build.props
    - Weftone.slnx
    - native/CMakeLists.txt
    - native/weftone_miniaudio/CMakeLists.txt
    - native/weftone_codecs/CMakeLists.txt
    - .github/workflows/ci.yml
    - THIRD_PARTY_NOTICES.md
    - src/Weftone.App/Weftone.App.csproj
    - src/Weftone.Adapters.Audio.Miniaudio/Weftone.Adapters.Audio.Miniaudio.csproj
    - src/Weftone.Adapters.Codecs/Weftone.Adapters.Codecs.csproj
---

## 1. 体系概览

Weftone 采用 **双轨依赖管理**：
- .NET 层通过 NuGet + SolutionX（`.slnx`）+ `Directory.Packages.props` 集中声明与锁定第三方包版本；
- 原生音频层通过 CMake 构建两个共享库，其源码以 **vendored（内联）** 方式随仓库分发（`native/vendor/miniaudio`、`native/vendor/dr_wav`），不依赖外部包管理器。

## 2. .NET 依赖：NuGet 中央版本管理

核心文件：
- `Directory.Packages.props`：启用 `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>` 与 `<CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>`，所有包的 `<Version>` 仅在此处声明，各 `.csproj` 中只写 `<PackageReference Include="..." />`（无版本号）。这是强制约束——若某项目直接写版本号，会触发 MSBuild 错误。
- `Directory.Build.props`：统一目标框架 `net10.0`、`LangVersion=latest`、`Nullable=enable`、`Deterministic=true`、`TreatWarningsAsErrors=true`，并自动在 Debug 配置下定义 `RT_ASSERT`。
- `Weftone.slnx`：SolutionX 清单，显式列出 8 个 src 项目、7 个 tests 项目、1 个 tools 项目，替代传统 `.sln`。

已集中声明的包（来自 `Directory.Packages.props`）：
- 测试：`Microsoft.NET.Test.Sdk 18.12.0`、`NUnit 5.0.0`、`NUnit3TestAdapter 6.3.1`、`NetArchTest.Rules 1.3.2`
- UI（Avalonia 生态，注释注明“MIT / CommunityToolkit.Mvvm MIT，版本经 NuGet 官方源核实”）：`Avalonia 11.3.23`、`Avalonia.Desktop 11.3.23`、`Avalonia.Themes.Fluent 11.3.23`、`Avalonia.Fonts.Inter 11.3.23`、`Avalonia.Headless 11.3.23`、`Avalonia.Headless.NUnit 11.3.23`、`CommunityToolkit.Mvvm 8.4.2`

各 `.csproj` 仅引用包名不带版本，例如 `src/Weftone.App/Weftone.App.csproj` 中的 `<PackageReference Include="Avalonia" />`。Adapters 层（Audio.Miniaudio、Codecs）不引入任何 NuGet 包，仅通过 `ProjectReference` 依赖 `Weftone.Abstractions` 并开启 `AllowUnsafeBlocks`。

## 3. 原生依赖：CMake + vendored 单头库

根 `native/CMakeLists.txt` 聚合两个子工程：
- `weftone_miniaudio`：封装 miniaudio 为共享库 `libweftone_miniaudio`，include 路径指向 `../vendor/miniaudio`。
- `weftone_codecs`：封装 dr_wav 为共享库 `libweftone_codecs`，include 路径指向 `../vendor/dr_wav`。

两个子工程的 CMakeLists 均将输出目录统一重定向到 `${CMAKE_BINARY_DIR}`（跨平台归一化 DLL/.so/.dylib 位置），以便上层 C# 的 `NativeLibrary` 探测逻辑能稳定加载。

vendor 策略由 `README.md` 与 `THIRD_PARTY_NOTICES.md` 共同记录：
- `native/vendor/miniaudio` — master@2026-11，MIT-0
- `native/vendor/dr_wav` — master@2026-11，MIT-0

AGENTS.md §9 明确约定：“Native：CMake + vendored 单头文件库”，即原生第三方库必须以内联形式随仓库提交，不得通过系统包管理器或 git submodule 拉取。

## 4. CI 与可重复性

`.github/workflows/ci.yml` 在 `windows-latest` / `ubuntu-latest` / `macos-latest` 三平台上执行：
1. `cmake -S native -B native/build && cmake --build native/build --config Release` —— 验证原生 shim 可编译。
2. `dotnet build -c Release` / `-c Debug` —— 构建两种配置（Debug 下 `RT_ASSERT` 生效）。
3. `dotnet test -c Release --no-build` / `-c Debug --no-build` —— 运行全部 NUnit 测试。
4. 冒烟：`tools/HeadlessRender` 渲染 1s WAV 并校验文件大小等于 `384044` 字节（44 头 + 48000×1×2×4 PCM 数据）。

CI 固定使用 `actions/setup-dotnet@v4` 安装 `dotnet-version: 10.0.x`，与 `Directory.Build.props` 的 `TargetFramework=net10.0` 保持一致。

## 5. 约束与规则

- **包版本集中声明**：`Directory.Packages.props` 启用 Central Package Management 与 Transitive Pinning，所有 `<PackageVersion>` 仅在此处维护，禁止在各 `.csproj` 中写版本号。（来源：MSBuild 属性 `ManagePackageVersionsCentrally` + `CentralPackageTransitivePinningEnabled`）
- **目标框架统一**：所有项目继承 `Directory.Build.props` 的 `TargetFramework=net10.0`，无需在每个 csproj 中重复声明。
- **原生库 vendored**：miniaudio 与 dr_wav 以 vendor 目录形式随仓库分发，CMake 通过相对路径 `../vendor/...` 引用，不依赖外部包管理器或网络下载。（来源：`native/*/CMakeLists.txt` + `THIRD_PARTY_NOTICES.md` + AGENTS.md §9）
- **原生输出归一**：两个 CMake 子工程都将 LIBRARY/RUNTIME/ARCHIVE 输出目录设为 `${CMAKE_BINARY_DIR}`，确保 C# 侧 NativeLibrary 探测路径一致。（来源：`native/weftone_miniaudio/CMakeLists.txt`、`native/weftone_codecs/CMakeLists.txt`）
- **CI 全平台构建**：GitHub Actions 矩阵覆盖 Windows/Linux/macOS，并在 PR 与 push main 上触发。（来源：`.github/workflows/ci.yml`）
- **WAV 回归断言**：CI 对 headless 渲染输出的 WAV 文件大小做硬编码校验（384044 字节），作为音频管线正确性的快速失败检查。（来源：`.github/workflows/ci.yml` 第 50–58 行）

## 6. 未发现的机制

仓库中未发现 `NuGet.config`、私有 NuGet 源、`GOPRIVATE`、Go module 或 Python pip 等其它语言依赖管理配置；依赖范围严格限定于 .NET/NuGet 与 C/C++ vendored 库。