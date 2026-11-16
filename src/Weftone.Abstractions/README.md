# Weftone.Abstractions

端口与契约层，无任何项目依赖（ADR-0002/0003/0004）。

- `IAudioBackend` / `AudioRenderCallback`：音频输出端口；回调运行在音频实时线程，
  实现与调用方都不得分配/加锁/抛异常。
- `AudioDeviceSpec`：采样率/声道/缓冲帧数。
- `Dsp/ITimeStretcher`（M4）：离线时间拉伸端口（仅 AssetPreparer 预渲染用，非 RT）。
- `Plugins/IPluginScanner`（M3）：插件扫描端口——实现必须在独立进程加载插件
  并维护缓存；`PluginDescriptor`/`ScannedPlugin`/`PluginScanResult` 为格式无关 DTO。
- `Rendering/RenderPlan`：扁平实时计划。`RenderVoice` 增益预乘（M2 起曲线声部
  `GainCurveIndex ≥ 0` 时 GainLeft/Right 只持 velocity，通道 vol/pan 走
  `ChannelGainCurves` 断点插值）；`MasterGainCurve`（M2）null = 静态 `MasterGain`。

Engine 只认识这里的类型；第三方类型禁止进入。
