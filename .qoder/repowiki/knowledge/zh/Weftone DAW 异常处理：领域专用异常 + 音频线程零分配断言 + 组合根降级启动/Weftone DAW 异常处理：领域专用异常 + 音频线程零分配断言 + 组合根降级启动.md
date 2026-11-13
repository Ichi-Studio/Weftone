---
kind: error_handling
name: Weftone DAW 异常处理：领域专用异常 + 音频线程零分配断言 + 组合根降级启动
category: error_handling
scope:
    - '**'
source_files:
    - src/Weftone.Adapters.ProjectStore/InvalidProjectException.cs
    - src/Weftone.Engine/RtAssert.cs
    - src/Weftone.Adapters.Audio.Miniaudio/MiniaudioBackend.cs
    - src/Weftone.Adapters.Codecs/DrWavDecoder.cs
    - src/Weftone.Adapters.ProjectStore/WeftProjectStore.cs
    - src/Weftone.App/App.axaml.cs
    - src/Weftone.App/ViewModels/MainWindowViewModel.cs
    - src/Weftone.Application/AssetPreparer.cs
    - src/Weftone.Application/EditSession.cs
---

## 1. 采用的方式

仓库使用 .NET 原生异常体系（`Exception` 派生类）进行错误传播，没有引入第三方错误库。关键约定体现在三个层面：

- **领域/适配器层**：抛出语义明确的内置异常（`InvalidDataException`、`ArgumentOutOfRangeException`、`NotSupportedException`），并在项目存储层定义自定义 `InvalidProjectException`。
- **实时音频路径**：通过 `RtAssert.Guard` 在 `RT_ASSERT` 编译开关下检测 GC 分配，违规仅计数不抛异常，保证音频回调路径零分配、零异常。
- **应用入口**：在 Avalonia 组合根中捕获特定异常并优雅降级为空白工程，允许用户后续手动打开有效项目。

## 2. 关键文件与包

| 文件 | 职责 |
|---|---|
| `src/Weftone.Adapters.ProjectStore/InvalidProjectException.cs` | 唯一自定义异常类型，封装工程包非法场景（坏 zip / zip slip / schema 不符 / SHA256 不匹配 / 越界值） |
| `src/Weftone.Engine/RtAssert.cs` | 实时音频路径的分配/异常安全断言工具 |
| `src/Weftone.Adapters.Audio.Miniaudio/MiniaudioBackend.cs` | P/Invoke 音频后端，将 C 层返回码转为 `InvalidOperationException` |
| `src/Weftone.Adapters.Codecs/DrWavDecoder.cs` | WAV 解码失败时抛出 `InvalidDataException` |
| `src/Weftone.Adapters.ProjectStore/WeftProjectStore.cs` | 项目加载/校验集中抛 `InvalidProjectException`，并对 `JsonException` 做包装 |
| `src/Weftone.App/App.axaml.cs` | 组合根，统一捕获 `DllNotFoundException` / `InvalidOperationException` / `InvalidDataException` 以降级启动 |
| `src/Weftone.App/ViewModels/MainWindowViewModel.cs` | UI 命令层按操作类型分别 catch `IOException` / `InvalidOperationException` / `InvalidDataException` |
| `src/Weftone.Application/AssetPreparer.cs` | 资源准备阶段 catch `InvalidDataException` / `InvalidOperationException` |
| `src/Weftone.Application/EditSession.cs` | 会话级 catch `IOException` / `UnauthorizedAccessException` |

## 3. 架构与约定

### 3.1 自定义异常：`InvalidProjectException`

位于 `Weftone.Adapters.ProjectStore` 命名空间，是仓库中唯一的业务自定义异常类型，构造器支持 `(string message)` 与 `(string message, Exception inner)` 两种形式。所有项目解析、zip 遍历、schema 版本检查、SHA256 校验、资产缺失等非法输入均统一抛出该类型，例如：

```csharp
throw new InvalidProjectException("缺少 project.json");
throw new InvalidProjectException($"资产 SHA256 不匹配: {asset.Path} 期望 {asset.Sha256} 实际 {actual}");
```

对底层 JSON 解析异常采用包装模式：

```csharp
catch (JsonException ex)
{
    throw new InvalidProjectException($"{entry.FullName} JSON 解析失败: {ex.Message}", ex);
}
```

### 3.2 实时音频路径：`RtAssert` 零分配约束

`RtAssert.Guard(AudioRenderCallback body, Span<float> buffer)` 仅在 `#if RT_ASSERT` 分支内执行 `GC.GetAllocatedBytesForCurrentThread()` 前后对比，若检测到分配则 `Interlocked.Increment(ref _violations)` 计数，**绝不抛出异常**。注释明确声明“音频线程零异常抛出”，这是 AGENTS.md §9 的强制约束。

### 3.3 原生互操作错误：返回值 → `InvalidOperationException`

`MiniaudioBackend.Open/Start/Stop` 将 C 层返回码转换为带详细信息的 `InvalidOperationException`，消息格式统一为 `weft_audio_xxx 失败 rc={rc}: {WeftAudioNative.GetLastError()}`。

参数校验使用 .NET 8+ 的静态方法：`ObjectDisposedException.ThrowIf(_disposed, this)`、`ArgumentNullException.ThrowIfNull(callback)`。

### 3.4 顶层降级策略

组合根 `App.OnFrameworkInitializationCompleted` 在调用 `session.NewDemo()` 时捕获三类异常：

```csharp
catch (Exception ex) when (ex is DllNotFoundException or InvalidOperationException or InvalidDataException)
{
    // native 未构建或解码失败：以空工程启动，用户可手动打开工程
}
```

这意味着：原生库缺失、音频初始化失败、WAV 解码失败都不会阻止应用启动，而是回退到空白 Demo 工程。

UI 层的 `MainWindowViewModel` 同样使用条件 catch 区分 IO 错误与数据损坏：

```csharp
catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException)
```

## 4. 观察到的约定与约束

- **自定义异常范围有限**：仓库只定义了 `InvalidProjectException` 一个业务异常类型；其余场景一律使用 BCL 异常（`InvalidDataException`、`ArgumentOutOfRangeException`、`NotSupportedException`、`InvalidOperationException`）。
- **音频回调路径禁止异常**：`RtAssert` 的注释与实现共同约束了实时路径——任何异常都会破坏音频线程，因此该路径只做计数统计，不抛异常。
- **P/Invoke 错误统一转 `InvalidOperationException`**：C 层返回码不在 C# 侧映射为枚举，而是直接转为带 rc 和 lastError 文本的异常。
- **项目加载错误统一为 `InvalidProjectException`**：从 `WeftProjectStore` 抛出的所有项目相关错误都使用该类型，便于上层按类别捕获。
- **顶层 catch 使用 `when` 守卫过滤**：避免吞掉未知异常，仅捕获预期的故障类型（DLL 缺失、IO 损坏、无效状态）。
- **无全局异常处理器**：未发现 `AppDomain.CurrentDomain.UnhandledException` 或 `DispatcherUnhandledException` 注册，异常由调用方自行处理。
- **无中间件/管道式错误处理**：作为桌面 DAW 应用而非 Web API，不存在 HTTP 中间件概念；错误处理集中在组合根与具体服务方法内。