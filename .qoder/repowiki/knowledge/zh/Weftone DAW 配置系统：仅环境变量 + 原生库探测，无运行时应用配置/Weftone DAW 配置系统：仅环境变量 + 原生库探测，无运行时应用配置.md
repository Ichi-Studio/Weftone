---
kind: configuration_system
name: Weftone DAW 配置系统：仅环境变量 + 原生库探测，无运行时应用配置
category: configuration_system
scope:
    - '**'
source_files:
    - src/Weftone.Adapters.Audio.Miniaudio/NativeProbe.cs
    - src/Weftone.Adapters.Codecs/NativeProbe.cs
    - src/Weftone.App/Program.cs
    - tests/Weftone.GoldenAudio/GoldenAudioTests.cs
    - Directory.Build.props
    - Directory.Packages.props
---

## 1. 使用的系统与框架

仓库没有引入任何 .NET 应用级配置框架（如 `Microsoft.Extensions.Configuration`、`appsettings.json`、`IConfiguration`）。整个 Weftone 解决方案是一个 Avalonia 桌面 DAW，其“配置”几乎完全由**进程环境变量**与**原生共享库自动发现**构成。

- 构建/包管理：通过 `Directory.Build.props`、`Directory.Packages.props`、`Weftone.slnx`（SolutionX）统一目标框架与包版本，属于构建期配置而非运行时配置。
- 原生层：CMake 在 `native/weftone_miniaudio` 与 `native/weftone_codecs` 下分别构建两个 C 共享库，作为 .NET P/Invoke 的 ABI 边界。
- 运行时：没有配置文件加载逻辑；唯一的环境变量入口是 `WEFTONE_NATIVE_DIR`。

## 2. 关键文件

- `src/Weftone.Adapters.Audio.Miniaudio/NativeProbe.cs` — miniaudio 后端原生库路径探测
- `src/Weftone.Adapters.Codecs/NativeProbe.cs` — dr_wav 编解码原生库路径探测
- `src/Weftone.App/Program.cs` — Avalonia 应用入口，不加载任何配置
- `tests/Weftone.GoldenAudio/GoldenAudioTests.cs` — 使用 `WEFTONE_GOLDEN_UPDATE=1` 控制黄金音频回归测试模式
- `Directory.Build.props` / `Directory.Packages.props` — 构建期全局属性与包版本

## 3. 架构与约定

### 3.1 原生库发现顺序（两个 NativeProbe 完全一致）

每个适配器都实现一个 `NativeProbe.EnsureInstalled()`，调用一次后通过 `NativeLibrary.SetDllImportResolver` 注册自定义解析器。库查找候选路径按以下严格顺序尝试：

1. `WEFTONE_NATIVE_DIR/<lib>.dll|.dylib|.so`（若环境变量非空）——用于 CI/打包场景覆盖默认位置
2. `AppContext.BaseDirectory/<lib>` —— 部署时随应用目录放置
3. 从 `AppContext.BaseDirectory` 向上回溯父目录，在每个祖先目录中搜索 `native/build` 与 `native/<adapter>/build`，递归枚举匹配文件名

平台后缀规则：Windows → `.dll`；macOS → `.dylib`；其他 → `.so`；非 Windows/macOS 前缀加 `lib`。

### 3.2 环境变量约定

| 变量 | 作用域 | 行为 |
|---|---|---|
| `WEFTONE_NATIVE_DIR` | 所有适配器 | 优先于此目录加载原生库，为空则跳过 |
| `WEFTONE_GOLDEN_UPDATE` | GoldenAudio 测试 | 值为 `"1"` 时更新黄金音频基线 |

### 3.3 无应用配置对象

- `Program.Main` 直接调用 `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)`，没有任何 `IConfiguration` 注入。
- Avalonia 通过 `App.axaml` 声明 UI 资源，`App.axaml.cs` 未包含配置读取逻辑。
- 业务层（Domain/Application/Engine）均不依赖配置抽象；项目格式、音频参数等以代码常量或领域模型表达。

## 4. 约定与约束

- **运行时不存在应用配置文件**：仓库中没有 `*.yaml`、`*.toml`、`*.ini`、`*.cfg`、`appsettings.*` 或 `.env` 文件被程序加载；grep 结果确认无 `IConfiguration`、`appsettings` 引用。
- **原生库必须可被发现**：两个 `NativeProbe` 类注释明确描述了期望的仓库布局（`native/build` 或 `native/<adapter>/build`），这是部署契约的一部分。
- **`EnsureInstalled()` 幂等**：使用 `Interlocked.Exchange(ref _installed, 1)` 保证每个 AppDomain 内只注册一次 `DllImportResolver`。
- **环境变量命名空间**：所有 Weftone 相关环境变量以 `WEFTONE_` 前缀命名（`WEFTONE_NATIVE_DIR`、`WEFTONE_GOLDEN_UPDATE`），避免污染全局环境。
- **构建期配置集中化**：`Directory.Build.props` 与 `Directory.Packages.props` 是 SolutionX 方案下统一目标框架、包版本的唯一来源，各子项目 csproj 保持精简。

总结：该仓库的配置系统极简——运行时仅靠两个环境变量驱动行为，核心职责是定位原生共享库；没有通用的配置加载器、分层配置或特性开关机制。