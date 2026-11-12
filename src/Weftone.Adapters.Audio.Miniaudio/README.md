# Weftone.Adapters.Audio.Miniaudio

IAudioBackend 的 miniaudio 实现（ADR-0004：轮子经适配器隔离）。

- `MiniaudioBackend`：Open/Start/Stop/Dispose；音频线程经反向 P/Invoke 进入托管回调。
  委托与 GCHandle 在 Open 一次性建立，回调路径零分配。
- `NativeProbe`：DllImportResolver 探测 `WEFTONE_NATIVE_DIR` → 应用目录 →
  仓库 `native/weftone_miniaudio/build`。
- `WeftAudioNative`：weft_audio_* C ABI 绑定（cdecl）。

不得依赖 Engine/Domain/Application；不得把 miniaudio 类型泄漏给 Engine。
