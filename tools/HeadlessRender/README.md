# HeadlessRender（CLI: weftone）

M1a 组合根 CLI：headless 优先（AGENTS.md §3）。

```
weftone render [--freq Hz] [--seconds s] [--out PATH]      离线渲染正弦 WAV（默认，不依赖 native）
weftone play   [--freq Hz] [--seconds s]                   实时播放（需 native shim）
weftone project render <path.weft> [--out PATH]            .weft 工程 → WAV（全链：加载→准备→编译→渲染）
weftone project info <path.weft>                           工程概览（BPM/时长/声部/资产）
weftone demo [--out demo.weft]                             生成演示工程（sine 和弦 + 合成 kick）
```

- `WavWriter` 位于 Weftone.Application；渲染编排见 `ProjectRenderService`（经 IPlanRenderer 端口）。
- 正弦直渲经 `RtAssert.Guard` 包裹并报告 RT 分配违规（非零退出码 1）。
- 退出码：0 成功 / 1 分配违规 / 2 参数错误 / 3 native 缺失 / 4 工程/数据非法。
