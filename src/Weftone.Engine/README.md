# Weftone.Engine

实时渲染层，仅依赖 Weftone.Abstractions（ADR-0002/0003）。

- `PlanRenderer`：RenderPlan 离线/实时两用渲染器。64 复音预分配池，
  池满偷最早结束者（线性淡出防 click），pending 环等位；Sine 双精度相位、
  Sampler 资产线性插值（MIDI 60=原速）。**循环回绕（M1b）**：plan 带
  `LoopStartSample/LoopEndSample` 时 position 到 loopEnd 回 loopStart、
  事件游标归零重触发，活跃声部自然延续尾音，单块内多次回绕安全，全程零分配。
  **自动化斜坡（M2）**：曲线声部按通道 `GainBreakpoints` 分段线性增益
  （块内逐样本插值，与 FadeTail/steal 包络相乘）；master 曲线同理后乘；
  游标回绕二分重定位，无自动化通道走静态预乘路径（行为位级不变）。
  **插件声部（M3）**：note on/off 区间按 256 帧 chunk 派发；参数事件点
  （竖切 4 D6）以阶梯语义按帧序派发（sample 0 首值、点处跳变，插值密度留 M5）、
  回绕/重定位游标重置；输出乘通道静态增益入总线。
  **RCU 交换（M2 竖切 4，ADR-0002）**：`SwapPlan` 仅 volatile 引用写入；
  池声部直接持资产/曲线引用（plan 不可变 ⇒ 在飞声部与换 plan 解耦），
  游标在音频线程内按当前绝对位置惰性二分重定位（playhead 前事件不补触发、
  旧 pending 放弃计入 DroppedEvents）；旧 plan 由 GC 回收，无退役锁。
- `SineEngine`：M0 正弦声部。双精度相位累积、零分配；`Callback` 预捕获委托，
  防止音频路径方法组转换分配。
- `RtAssert`：分配断言（AGENTS.md §9）。仅 RT_ASSERT 构建生效，违规只计数
  绝不抛异常；Debug 配置自动定义 RT_ASSERT（见根 Directory.Build.props）。

音频路径硬约束：零分配、零锁、零 LINQ、零装箱、零 string、零 async、
零 DI/反射、零日志、零异常抛出。架构测试守护依赖规则。
