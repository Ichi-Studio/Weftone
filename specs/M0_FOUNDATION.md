# SPEC: M0 地基（Foundation）

状态: Accepted | 里程碑: M0 | 依据: AGENTS.md §13、ADR-0001～0008

## 目标
搭出可出声、可离线渲染、可被三平台 CI 守护的最小地基。

## 范围
- 仓库与治理：LICENSE（GPL-3.0-or-later）、THIRD_PARTY_NOTICES、CONTRIBUTING、
  Directory.Build.props（Debug 自动定义 RT_ASSERT）/ Directory.Packages.props（CPM）
- native：`native/weftone_miniaudio`（稳定 C ABI shim + CMake + vendored miniaudio）
- C#：Weftone.Abstractions（IAudioBackend 端口）/ Weftone.Engine（SineEngine、RtAssert）/
  Weftone.Adapters.Audio.Miniaudio（反向 P/Invoke + 库探测）/ weftone CLI（render/play）
- 测试：正弦正确性、确定性、分配断言、WAV 往返；架构依赖规则 + 占位符扫描
- CI：三平台矩阵（native 编译 + Release/Debug 测试 + CLI 冒烟 + WAV 尺寸校验）

## 非目标
插件宿主、transport、文档模型、.weft 工程格式、UI、进程隔离（后续里程碑）。

## 验收标准
1. `dotnet build` / `dotnet test` 在 Release 与 Debug（RT_ASSERT）均全绿。
2. 渲染路径托管分配 == 0（RtAssert.Violations == 0，Debug 构建实测生效）。
3. `weftone render --seconds 5` 产出 48000Hz/2ch float32 WAV：
   尺寸 = 44 + 48000×5×2×4 = 1,920,044 字节；样本与 sin(2πfn/sr) 偏差 ≤ 1e-4。
4. 同参数两次渲染逐位一致（确定性，M1a 黄金音频基线的前置）。
5. `weftone play`：有 native 库时打开设备实时播放（听感由维护者确认）；
   无库时给出明确降级提示并 exit 3。
6. 三平台 CI 绿，含 native shim 编译与占位符（旧项目前缀）零残留检查。

## 度量与延后项
- 回调耗时直方图 p99、xrun/GC 暂停监控：M1a 引入 SPSC ring 后接入。
- RT 内 native malloc 审计：人工/工具审计（AGENTS.md §9），M3 插件宿主前完成首审。
