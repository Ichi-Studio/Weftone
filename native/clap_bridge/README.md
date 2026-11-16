# native/clap_bridge

CLAP 宿主桥（SPEC M3 D2=a，ADR-0007）：`weftone_clap_bridge` 共享库，
把 CLAP C ABI 扁平化为少量导出函数，C# 侧经 P/Invoke 调用
（先例：`weftone_miniaudio`）。

- `weft_clap_scan(path, out[], max)`：加载单个 `.clap` → `clap_entry`（major==1
  兼容检查）→ plugin factory → descriptor 字符串拷贝出界；**不实例化插件**
  （AGENTS.md §11 扫描期最小暴露）。返回插件数，失败 -1。
- `weft_clap_plugin_create/process/destroy`（竖切 2）：实例桥——create 时
  init→activate→start_processing 并校验 0 号输出端口为立体声（M3 D3）；
  process 为 RT 零分配路径（事件数组随实例预分配、音频缓冲由调用方传入），
  宿主回调为最小桩；destroy 非具 RT 线程调用。
- 崩溃隔离不在本层：调用方（`weftone-plugin-scan` 独立进程）保证插件崩溃
  只丢扫描进程。
- 头文件：`../vendor/clap/include`（CLAP SDK 1.2.10，MIT，见 THIRD_PARTY_NOTICES）。

## native/test_plugins

随 CI 构建的自研 `.clap`（项目原创 GPL-3.0-or-later）：

- `weftone_test_sine.clap`：确定性 sine 测试插件——16 复音、双精度相位、
  立即 note off、振幅 0.25、audio-ports 立体声声明；CI 黄金基线
  `m3-plugin-golden.wav` 用。
- `weftone_crash.clap`：`entry->init` 即 `abort()`——崩溃隔离测试用。

C++ 编译（MSVC 的 C 模式不支持静态初始化取 const 对象地址，C2099）；
`clap_entry` 以 `extern "C"` 导出。输出 `native/build/*.clap`。
