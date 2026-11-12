# Weftone.Domain

工程文档模型与命令层，无任何项目依赖（ADR-0003：富对象、可撤销、可序列化；
音频线程永不可见本层类型）。

- `ProjectDocument`：tempo/sampleRate + Channel[] + Pattern[]（Note[]）+ Playlist（Clip[]）+ AssetRef[]
- `TimeBase.Ppq = 96`：整数 tick（M1a 拍板 D1）
- `Commands/`：`ICommand`（Apply/Revert 精确互逆）+ `CommandHistory`（空栈拒绝、新命令清空 redo）
  + AddNote/RemoveNote/SetTempo/SetChannelMix
- `IProjectStore`：工程存取端口（文档中心，实现在 adapter）
