---
kind: build_system
name: Weftone DAW 构建系统：.NET SolutionX + CMake 多平台 CI
category: build_system
scope:
    - '**'
source_files:
    - Weftone.slnx
    - Directory.Build.props
    - Directory.Packages.props
    - .github/workflows/ci.yml
    - native/CMakeLists.txt
    - src/Weftone.Adapters.Audio.Miniaudio/Weftone.Adapters.Audio.Miniaudio.csproj
    - src/Weftone.Adapters.Codecs/Weftone.Adapters.Codecs.csproj
    - tools/HeadlessRender/HeadlessRender.csproj
---

## 1. 使用的构建系统与工具链

- **.NET 10 / SolutionX**：项目以 `Weftone.slnx`（SolutionX 格式）组织，替代传统 `.sln`；通过 `Directory.Build.props` 集中定义目标框架、语言版本与编译开关。
- **CMake ≥ 3.16**：原生层（dr_wav 解码器、miniaudio 播放后端）在 `native/` 下用 CMake 构建为独立共享库（Windows 上生成 `.dll/.lib`），由上层 .NET P/Invoke 调用。
- **GitHub Actions CI**：`.github/workflows/ci.yml` 在 Windows、Ubuntu、macOS 三矩阵上执行构建、测试与冒烟脚本。
- **NUnit 5 + NetArchTest**：测试套件统一通过 `dotnet test` 驱动，架构依赖规则由 `tests/Weftone.ArchTests` 中的 NetArchTest 断言。

## 2. 关键文件

- `Weftone.slnx` — SolutionX 解决方案清单，按 `/src/`、`/tests/`、`/tools/` 三个 Folder 分组列出全部 csproj。
- `Directory.Build.props` — 全局 MSBuild 属性：`TargetFramework=net10.0`、`LangVersion=latest`、`Nullable=enable`、`ImplicitUsings=enable`、`Deterministic=true`、`TreatWarningsAsErrors=true`、`VersionPrefix=0.1.0`；并在 Debug 配置自动注入 `RT_ASSERT` 宏。
- `Directory.Packages.props` — 中央包版本管理（CPV），启用 `ManagePackageVersionsCentrally` 与 `CentralPackageTransitivePinningEnabled`，集中声明 NUnit、Avalonia、CommunityToolkit.Mvvm 等包版本。
- `.github/workflows/ci.yml` — 三平台 CI 流水线。
- `native/CMakeLists.txt` — 顶层 CMake 入口，聚合 `weftone_miniaudio` 与 `weftone_codecs` 两个子目录。
- `src/Weftone.Adapters.Audio.Miniaudio/*.csproj`、`src/Weftone.Adapters.Codecs/*.csproj` — 两个桥接层 csproj，均开启 `AllowUnsafeBlocks` 并引用 `Weftone.Abstractions`。
- `tools/HeadlessRender/HeadlessRender.csproj` — CLI 工具工程，用于无头渲染与 demo 工程验证。

## 3. 架构与约定

### 3.1 .NET 分层构建

SolutionX 将工程分为三层：
- `src/`：核心分层——Abstractions → Domain → Application → Engine → Adapters.* → App。
- `tests/`：每个 src 模块对应一个 NUnit 测试项目，外加 `Weftone.ArchTests`（架构规则）和 `Weftone.GoldenAudio`（黄金音频回归）。
- `tools/`：`HeadlessRender` 作为独立可执行工具，提供 `render` 与 `project render` 命令。

所有 csproj 不重复声明 TargetFramework/LangVersion/Nullable 等属性，统一继承自根 `Directory.Build.props`。

### 3.2 原生层构建

`native/` 使用 CMake 构建两个独立的 C 共享库：
- `weftone_miniaudio`：封装 miniaudio 音频播放。
- `weftone_codecs`：封装 dr_wav 解码。

CI 中先执行 `cmake -S native -B native/build && cmake --build native/build --config Release`，再执行 `dotnet build -c Release`。原生库产物位于 `native/build/<platform>/`（如 `weftone_codecs.dll`、`weftone_miniaudio.dll`），由对应 Adapter 的 `NativeProbe` 类加载。

### 3.3 调试/发布配置

- **Release**：默认构建配置，禁用 `RT_ASSERT`。
- **Debug**：自动启用 `RT_ASSERT`（见 `Directory.Build.props` 第 14–17 行），用于运行时分配断言。
- CI 同时运行 `-c Release` 与 `-c Debug` 两套构建与测试。

### 3.4 包版本策略

采用 .NET Central Package Management（CPM）：
- `Directory.Packages.props` 中集中声明所有包版本。
- 各 csproj 仅写 `<PackageReference Include="..." />`，不写 Version。
- 启用 `CentralPackageTransitivePinningEnabled` 锁定传递依赖版本。

### 3.5 CI 流水线

`.github/workflows/ci.yml` 定义单 job `build-test`，矩阵覆盖 `windows-latest`、`ubuntu-latest`、`macos-latest`，步骤顺序：
1. checkout
2. setup-dotnet 10.0.x
3. 构建 native shims（CMake）
4. `dotnet build -c Release`
5. `dotnet build -c Debug`
6. `dotnet test -c Release --no-build`
7. `dotnet test -c Debug --no-build`
8. HeadlessRender 冒烟：渲染 1s WAV → 校验文件大小等于 `384044`（44 header + 48000×1×2×4）
9. 全链冒烟：`demo --out artifacts/demo.weft` → `project render artifacts/demo.weft --out artifacts/demo-render.wav`

## 4. 约定与约束

- **目标框架固定为 net10.0**：由 `Directory.Build.props` 全局生效，任何 csproj 不应覆盖此值。
- **警告即错误**：`TreatWarningsAsErrors=true`，编译期不允许任何警告通过。
- **可空引用类型强制启用**：`Nullable=enable` 全局生效。
- **确定性构建**：`Deterministic=true`，保证相同源码产出相同二进制。
- **版本前缀集中管理**：`VersionPrefix=0.1.0` 在根 props 中声明，所有项目继承。
- **RT_ASSERT 仅在 Debug 自动启用**：由 `Directory.Build.props` 条件块注入；Release 可通过 `-p:DefineConstants=RT_ASSERT` 显式叠加（见注释引用 AGENTS.md §9）。
- **原生库必须先行构建**：CI 要求先跑 CMake 再生成 dotnet 产物，Adapter 项目依赖已编译好的 `.dll`。
- **CI 对输出 WAV 做尺寸断言**：`m0-smoke.wav` 必须恰好 384044 字节，`demo-render.wav` 必须非空，否则流水线失败。
- **包版本不得在各 csproj 中分散声明**：CPM 模式下，所有包版本集中在 `Directory.Packages.props`，且启用传递依赖锁定。
- **SolutionX 是官方方案清单**：不使用传统 `.sln`，新增项目需同步注册到 `Weftone.slnx` 对应 Folder 下。