# Weftone.App

Avalonia UI 进程（组合根，AGENTS.md §4），引用 Application + Engine + Adapters。
唯一允许引用 Avalonia 的项目（架构测试守护）。

- `Program` / `App`：组合根——EditSession + PlaybackService(miniaudio+PlanRenderer)
  + FileDialogService + MainWindowViewModel；native 缺失时降级空工程启动。
- `MainWindow`：单窗口固定分区（D4=a）——菜单 / 传输条（播放·停止·Pattern 选择·
  位置·BPM·撤销重做）/ Piano Roll / 状态栏。DispatcherTimer(50ms) 轮询播放位置。
- `Controls/PianoRollControl`（M1b D3=a 自绘）：小节/拍网格 + 音符矩形（力度色），
  视口裁剪只画可见范围；手势：空处左拖=画音符、拖身体=移动、拖右缘=改长度、
  右键=删除。命令与事务全部经 ViewModel，控件只发意图。
- `Controls/PianoKeysControl`：128 键侧栏，纵向偏移与 Roll 同步滚动。
- `Controls/PianoRollGeometry`：几何/命中纯函数（无 Avalonia 依赖，headless 单测）。
- `ViewModels/MainWindowViewModel`：MVVM（CommunityToolkit.Mvvm 源生成器）。
  Pattern 循环播放、音符手势事务（拖拽合并单条撤销）、工程存取、脏标记标题。
- `Services/IFileDialogService` / `FileDialogService`：StorageProvider 抽象，
  VM 不见 Avalonia 类型、可 headless 测试。

测试：tests/Weftone.App.Tests（Avalonia.Headless.NUnit 冒烟 + VM/几何纯单测）。
