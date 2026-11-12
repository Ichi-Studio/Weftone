# HeadlessRender（CLI: weftone）

M0 组合根 CLI：headless 优先（AGENTS.md §3）。

```
weftone render [--freq Hz] [--seconds s] [--out PATH]   离线渲染 WAV（默认，不依赖 native）
weftone play   [--freq Hz] [--seconds s]                实时播放（需 native shim）
```

- `WavWriter`：WAVE_FORMAT_IEEE_FLOAT 32-bit WAV，流式写出。
- 渲染经 `RtAssert.Guard` 包裹，CLI 报告 RT 分配违规数；非零退出码 1。
- 退出码：0 成功 / 1 分配违规 / 2 参数错误 / 3 native 缺失 / 4 设备失败。
