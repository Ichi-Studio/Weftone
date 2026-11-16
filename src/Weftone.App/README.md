# Weftone.App

Avalonia UI 进程（组合根，AGENTS.md §4），引用 Application + Engine + Adapters。
唯一允许引用 Avalonia 的项目（架构测试守护）。

- `Program` / `App`：组合根——EditSession + PlaybackService(miniaudio+PlanRenderer)
  + FileDialogService + MainWindowViewModel + RecoveryStore；native 缺失时降级空工程启动；
  退出时清恢复区（崩溃无此调用，快照存活供下次恢复）。
- `MainWindow`：单窗口固定分区（D4=a）——恢复提示条（M2：检测到快照时
  「恢复/丢弃」）/ 菜单 / 传输条（播放·停止·Pattern 选择·位置·BPM·撤销重做）/
  Piano Roll / 自动化 lane（M2：下方参数选择 通道×参数+master + 折线 lane，
  横向滚动与 Roll 同步）/ 状态栏。DispatcherTimer(50ms) 轮询播放位置，
  DispatcherTimer(1s) 驱动 autosave。
- `Controls/PianoRollControl`（M1b D3=a 自绘）：小节/拍网格 + 音符矩形（力度色），
  视口裁剪只画可见范围；手势：空处左拖=画音符、拖身体=移动、拖右缘=改长度、
  右键=删除。命令与事务全部经 ViewModel，控件只发意图。
- `Controls/AutomationLaneControl`（M2 自绘）：曲线折线 + 关键点圆点，
  首末点 clamp 平直延伸；手势三件套与音符一致（空处左键=画点、拖点=移动、
  右键=删除），播放线跟随播放头；横向与 Piano Roll 同轴。
- `Controls/PlaylistControl`（M1b 竖切 3）：一轨一 Pattern，clip 块=pattern
  内容长度+1 拍；手势：空轨左键=放置 clip、拖 clip=移动（可换轨）、右键=删除。
- `Controls/MixerPanel`（M1b 竖切 4）：每通道音量竖推子 + pan 横推子 + 名称，
  右端 master；拖动中预览实时反映播放，释放时一条命令入撤销栈（标准控件组合）。
  M2：通道/master 带自动化曲线时推子只读禁用（"A" 标记），
  显示值随播放头按曲线求值更新（不做推子录制）。
  M3 竖切 3：插件通道条内嵌参数滑条（描述经 PluginParameterCatalog，
  值走 SetPluginParameterCommand 事务撤销，拖动实时换 plan）。
- `Controls/PianoKeysControl`：128 键侧栏，纵向偏移与 Roll 同步滚动。
- `Controls/PianoRollGeometry` / `PlaylistGeometry` / `AutomationLaneGeometry`：
  几何/命中纯函数（无 Avalonia 依赖，headless 单测）；时间轴 tick↔pixel 映射共用。
- `ViewModels/MainWindowViewModel`：MVVM（CommunityToolkit.Mvvm 源生成器）。
  Pattern 循环 / Song 线性播放切换（IsSongMode）、音符与 clip 手势事务
  （拖拽合并单条撤销）、自动化手势与目标选择（M2）、混音/master 推子预览-后提交、
  采样导入与 WAV 导出、工程存取、脏标记标题；DocumentEdited 事件驱动自绘控件
  在撤销/重做/换工程后重画。
- `Services/IFileDialogService` / `FileDialogService`：StorageProvider 抽象，
  VM 不见 Avalonia 类型、可 headless 测试。

测试：tests/Weftone.App.Tests（Avalonia.Headless.NUnit 冒烟 + VM/几何纯单测）。
