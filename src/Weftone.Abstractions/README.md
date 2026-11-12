# Weftone.Abstractions

端口与契约层，无任何项目依赖（ADR-0002/0003/0004）。

- `IAudioBackend` / `AudioRenderCallback`：音频输出端口；回调运行在音频实时线程，
  实现与调用方都不得分配/加锁/抛异常。
- `AudioDeviceSpec`：采样率/声道/缓冲帧数。

Engine 只认识这里的类型；第三方类型禁止进入。
