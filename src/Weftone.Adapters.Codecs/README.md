# Weftone.Adapters.Codecs

采样解码/重采样适配器（ADR-0004 wheel-first）。

- `DrWavDecoder : ISampleDecoder`：经 weftone_codecs shim（vendored dr_wav）
  解码 WAV（16/24/32f/32i PCM）为 f32 交错；整文件解码，仅离线路径。
- `LinearResampler : IResampler`：M1a 拍板 D3 离线线性插值；r8brain 留 M4 替换。
- `NativeProbe`：weftone_codecs 库探测（WEFTONE_NATIVE_DIR → 应用目录 → native/build）。

不得依赖 Engine/Domain/Application；不得把 dr_wav 类型泄漏给上游。
