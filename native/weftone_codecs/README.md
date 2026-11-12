# weftone_codecs

音频解码 shim：vendored `dr_wav`（公共领域/MIT-0）解码 WAV → f32 交错 PCM。
一次性整文件解码，仅离线路径使用（资产准备），非实时。

## C ABI

| 函数 | 说明 |
|---|---|
| `weft_wav_decode(data, size, &samples, &channels, &sampleRate, &frames)` | 返回 0 成功；samples 由 malloc 分配，调用方以 `weft_wav_free` 释放 |
| `weft_wav_free(samples)` | 释放解码缓冲 |

## 构建

经 native 根统一构建（产物落 `native/build/`）：

```sh
cmake -S native -B native/build
cmake --build native/build --config Release
```

依赖 vendored `../vendor/dr_wav`（取自 mackron/dr_libs）。
