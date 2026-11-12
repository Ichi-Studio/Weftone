# weftone_miniaudio

miniaudio 的 C shim：以稳定 C ABI 暴露播放设备，供
`Weftone.Adapters.Audio.Miniaudio` P/Invoke。Core/Engine 永不直接接触 miniaudio 类型
（ADR-0004）。M0 限定单设备、单回调。

## C ABI

| 函数 | 说明 |
|---|---|
| `weft_audio_open(sampleRate, channels, bufferFrames, cb, user)` | f32 交错输出；`cb(float* out, uint32 frames, uint32 channels, void* user)` 在音频线程调用 |
| `weft_audio_start()` / `weft_audio_stop()` | 启停 |
| `weft_audio_close()` | 释放设备 |
| `weft_audio_last_error()` | 最近错误字符串（进程内静态缓冲） |

Windows 回调线程动态加载 avrt 挂 MMCSS "Pro Audio"（AGENTS.md §9）。

## 构建

```sh
cmake -S native/weftone_miniaudio -B native/weftone_miniaudio/build
cmake --build native/weftone_miniaudio/build --config Release
```

产物统一输出到 `build/` 根，C# 侧 `NativeProbe` 依序探测：
`WEFTONE_NATIVE_DIR` → 应用目录 → 仓库 `native/weftone_miniaudio/build`。

## 依赖

vendored `../vendor/miniaudio`（MIT-0），见根 THIRD_PARTY_NOTICES.md。
