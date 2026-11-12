# docs/adr/ADR-0008-naming-trademark.md
Status: Accepted | Date: 2026-11-12

## Context
项目需要可注册、跨语言可读、无音频行业碰撞的名称；
不得使用 FL / Fruity / Image-Line 商标；
名称同时作为 C# 根命名空间、NuGet 包前缀、域名与 CLI 名。

## Decision
- 产品名：Weftone
- 中文名：纬音
- 语义：Weft（纬线）+ Tone（音）。Pattern 是纬线，Playlist 是织物，
  直接对应 pattern-based 工作流。
- 词性：臆造词（coined word），商标显著性最强。
- 根命名空间：Weftone.*
- NuGet 包前缀：Weftone.*
- 插件 SDK：Weftone.PluginSdk（MIT，独立仓库或独立目录）
- CLI 名：weftone
- 工程包扩展名：.weft（zip 容器，见 ADR-0006）
- 商标优先类别：第 9 类（软件）、第 15 类（乐器）
- 禁止：FL / Fruity / Image-Line 及任何近似标识；
  不得逐像素复刻其 UI 美术。

## Alternatives（已检索排除）
Tessera / Ostinato / Skein / Kithara / Melos / Segno / Beatloom /
Neume / Stretto / Loopera / Tesselo / Punchroll / Songloom /
Polytone / Damask / Hocket / Tiletone / Sonaut
排除理由：音频或软件品类已被占用，部分为注册商标。
结论：音乐类现成词在 2026 年基本无法注册，造词优先。

## Consequences
- 臆造词有市场教育成本，需靠产品体验建立认知。
- 已知近似：WeftOne（AI 任务板产品），拼写与品类均不同，
  冲突概率低，但正式启用前必须做商标 clearance。
- 名称一旦变更，命名空间/包/域名/商标全部受影响，故一次性锁定。

## Verification
- GitHub org、域名、NuGet 前缀可用性确认。
- 中国商标网 + USPTO + EUIPO 检索（第 9、15 类）。
- CI 增加占位符检查：仓库内不得残留 Daw.* 或 OpenDAW。
