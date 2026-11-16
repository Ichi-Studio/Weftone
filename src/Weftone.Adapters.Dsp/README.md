# Weftone.Adapters.Dsp

DSP 轮子适配器（ADR-0004 wheel-first），实现 Abstractions 的 `ITimeStretcher`，
仅依赖 Weftone.Abstractions。

- `SignalsmithStretcher`（M4 D2=a）：`weftone_dsp_stretch` P/Invoke；
  离线整段拉伸（不变调），输出帧数 = floor(输入 × durationRatio)，进程内位级确定；
  库解析同 codecs 先例（WEFTONE_NATIVE_DIR → 应用目录 → native/build 回溯）。

native 侧：`native/dsp_stretch/weft_stretch.cpp`（C shim 包 vendored
Signalsmith Stretch，presetDefault + setTransposeFactor(1)）。
跨平台注意：频域算法跨编译器浮点差较大，黄金基线容差独立放宽（2e-2，SPEC M4 风险）。
