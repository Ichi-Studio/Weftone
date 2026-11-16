# Weftone.Domain

工程文档模型与命令层，无任何项目依赖（ADR-0003：富对象、可撤销、可序列化；
音频线程永不可见本层类型）。

- `ProjectDocument`：tempo/sampleRate/masterVolume + Channel[] + Pattern[]（Note[]）
  + Playlist（Clip[]）+ AssetRef[]；master volume 自动化 lane（M2）
- `TimeBase.Ppq = 96`：整数 tick（M1a 拍板 D1）
- `AutomationPoint`/`AutomationTarget`/`AutomationLanes`（M2/M3 竖切 4）：关键点升序唯一、
  同 tick 替换、值域随参数（volume [0,2]、pan [-1,1]；插件参数宽松值域由描述端 clamp；
  master 仅 volume）；Channel 持 VolumePoints/PanPoints + 插件参数 lane
  （GetPluginParamPoints(paramId)，paramId 字典）
- `Commands/`：`ICommand`（Apply/Revert 精确互逆）+ `CommandHistory`
  （空栈拒绝、新命令清空 redo；**事务** Begin/EndTransaction 把一次手势的多条命令
  经 `CompositeCommand` 合并为单条撤销，事务中禁撤销/重做/嵌套）
  + AddNote/RemoveNote/MoveNote/ResizeNote（from/to 显式，拖拽预览-后提交）
  + AddClip/RemoveClip/MoveClip（tick + 换轨）
  + AddChannel/ImportSample（资产引用 + sampler 通道，一步撤销）
  + SetTempo/SetChannelMix/SetMasterVolume（from/to 显式）
  + Add/Remove/MoveAutomationPoint（M2：from/to 显式 + 替换捕获，手势事务合并）
- `IProjectStore`：工程存取端口（文档中心，实现在 adapter）
