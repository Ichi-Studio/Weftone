# Weftone.GoldenAudio

黄金音频回归（AGENTS.md §10）：DemoProject（sine 和弦 + 合成 kick）全链
store→prepare→compile→render：

- 同进程两次渲染逐位一致（确定性）
- 对 `baselines/m1a-golden.wav` 跨平台峰值差 ≤ 1e-4（内部同机位 ≤1e-6
  由确定性测试隐含覆盖）
- 基线更新：本地 `WEFTONE_GOLDEN_UPDATE=1 dotnet test` 后提交 baselines/
