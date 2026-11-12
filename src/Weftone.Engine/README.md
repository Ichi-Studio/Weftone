# Weftone.Engine

实时渲染层，仅依赖 Weftone.Abstractions（ADR-0002/0003）。

- `SineEngine`：M0 正弦声部。双精度相位累积、零分配；`Callback` 预捕获委托，
  防止音频路径方法组转换分配。
- `RtAssert`：分配断言（AGENTS.md §9）。仅 RT_ASSERT 构建生效，违规只计数
  绝不抛异常；Debug 配置自动定义 RT_ASSERT（见根 Directory.Build.props）。

音频路径硬约束：零分配、零锁、零 LINQ、零装箱、零 string、零 async、
零 DI/反射、零日志、零异常抛出。架构测试守护依赖规则。
