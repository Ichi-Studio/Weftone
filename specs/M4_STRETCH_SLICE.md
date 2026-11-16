# SPEC: M4 拉伸 / 切片

状态: Accepted | 里程碑: M4 | 依据: AGENTS.md §13（M4 拉伸/切片）、ADR-0004 wheel-first（DSP 轮子：Signalsmith Stretch / r8brain）、M1a 重采样先例
拍板: 2026-11-16 D1=a stretch+切片（三竖切）；D2=a Signalsmith Stretch vendored；
D3=a 导入时预渲染档位变体（RT 零新增）；D4=a 资产级 SlicePoints；
D5=a key=切片序号（60 起）+通道开关；D6=a 固定六档 {0.5,0.75,1.0,1.25,1.5,2.0}

## 目标

对采样内容两条编辑轴：

1. **时间拉伸（time-stretch）**：通道以任意速率/长度播放采样而不变调（或变调不变速），
   品质优于 M1a 线性重采样；RT 零分配、确定性可入黄金基线。
2. **切片（slicing）**：在一个采样上标记切片点，Pattern 音符按键触发对应切片
   （beat-slice 工作流：一段鼓循环 → 键盘切片演奏）。

## 完成定义（草案）

- stretch：通道采样以非 1.0 速率播放走 Signalsmith 预渲染路径，
  同工程渲染位级确定；黄金基线新增 stretch 用例；
- 切片：采样资产可加/删/移切片点（事务撤销），音符按 key 映射切片触发，
  Pattern/Song/导出全链一致；.weft 往返切片点；
- RT 不变量：音频回调零分配（预渲染变体资产随 plan）；
- 三平台 CI 绿（vendored C++ 库三平台编译）。

## 架构边界（继承）

```
Domain        采样资产 SlicePoints + 命令；通道 StretchMode/Semitones
Application   AssetPreparer 预渲染 stretch 变体（离线，Signalsmith）
              编译器：stretch 通道→变体资产选择；切片→note 切片映射
Engine        Sampler 声部已有（变体=普通资产）；切片=StartSample/Length 偏移
Adapters      Weftone.Adapters.Dsp.Stretch（Signalsmith P/Invoke）
native        dsp_stretch shim + vendored signalsmith-stretch（MIT）
```

## 关键设计

- **预渲染变体（D3=a 方向）**：AssetPreparer 按通道 stretch 设置离线产出变体资产
  （如速率档位 0.5/0.75/1.0/1.5/2.0 + 音分微调走线性重采样残余），plan 只引用
  变体索引——渲染期与 M1a Sampler 完全同路径（零新增 RT 代码）。
- **切片数据（D4=a 方向）**：切片点存 AssetRef 级（SampleSpace：样本帧位置列表）；
  通道 Kind=Sampler + SlicingEnabled 时，音符 key = 切片序号偏移（C 起），
  LengthTicks→门长照旧；StartSample 由切片点在编译期算定。
- **UI（D5）**：波形+切片编辑面板（复用 lane 自绘模式）与 Piano Roll
  切片演奏（key 分配可视化）；手势三件套同 M2。

## 竖切节奏（草案）

1. **stretch headless**：vendored Signalsmith + dsp_stretch shim + AssetPreparer
   变体 + 黄金基线。
2. **切片模型与全链**：SlicePoints + 命令 + 编译映射 + .weft 往返 + 基线。
3. **切片/拉伸 UI**：波形切片编辑面板 + Piano Roll 映射 + 通道属性。

## 非目标

实时连续 stretch 参数化、pitch-shift 独立插件、slice 自动检测（瞬态检测留 M5）、
音频反向、grain/glitch 效果、 stretched preview 拖拽实时变体生成。

## 风险

- Signalsmith C++ 三平台 vendoring/编译矩阵（CMake）；
- 变体资产内存/包体积（档位数量与上限需 Limits）；
- 切片点帧↔tick 双坐标（编辑在帧域、触发在 tick 域）off-by-one；
- 预渲染耗时阻塞导入（大采样多档位→后台线程与进度 UI，M4 简化为同步+上限）。

## 待拍板决策（带推荐）

| # | 决策 | 选项 | 推荐 |
|---|---|---|---|
| D1 | M4 范围 | a) stretch + 切片（三竖切） b) 仅 stretch c) 仅切片 | a（两者共享采样编辑主线，SPEC 已拆竖切可裁剪） |
| D2 | stretch 引擎 | a) Signalsmith Stretch（MIT，vendored） b) r8brain（高质量重采样，非 stretch） c) 自研相位声码器 | a（ADR-0004 已列；b 不解决不变速；c 工作量≈独立里程碑） |
| D3 | stretch 供给模型 | a) 导入时预渲染档位变体（RT 零新增） b) 实时每声部 stretch | a（RT 零分配与确定性优先；b 留 M6+ 或插件化） |
| D4 | 切片数据归属 | a) 资产级 SlicePoints（同资产多通道共享） b) 通道级 | a（切片是内容属性；b 重复且漂移） |
| D5 | 切片触发映射 | a) key=切片序号（60 起）+ 通道开关 b) 专用 Pattern 事件类型 | a（复用 Note/编译映射，无 schema 新事件类型） |
| D6 | 变体档位 | a) {0.5,0.75,1.0,1.25,1.5,2.0} 固定六档 b) 用户任意速率四舍五入近邻 c) 连续+实时插值 | a（内存可控、确定性最强；b 同 a 实现+UI 自由感；留档位常量可调） |
