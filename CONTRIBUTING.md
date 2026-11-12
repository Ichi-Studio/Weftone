# 贡献指南

## 当前状态
Weftone 处于 M0 地基阶段。贡献协议（DCO 或 CLA）待定（见
[ADR-0005](docs/adr/ADR-0005-license-commercialization.md)），确定前
不接受外部 PR；问题反馈欢迎。

## 开发流程
1. 阅读 [AGENTS.md](AGENTS.md)（硬约束与方法论）→ `docs/adr/` → `specs/`。
2. 无 SPEC 不实现；先写验收测试。
3. 本地验证 Release + Debug 双配置（Debug 自动启用 RT_ASSERT 分配断言）。
4. commit 约定：`feat` / `fix` / `refactor` / `test` / `docs` / `adr` 前缀
   + 中文主题；小步原子提交，禁止 force push。

## 构建与验证
```sh
dotnet build -c Release
dotnet test -c Release
dotnet test -c Debug            # RT_ASSERT 分配断言

cmake -S native -B native/build
cmake --build native/build --config Release

dotnet run --project tools/HeadlessRender -c Release -- render --seconds 5 --out out.wav
dotnet run --project tools/HeadlessRender -c Release -- play --seconds 5
```

## 许可
贡献的代码以 GPL-3.0-or-later 授权（见 [LICENSE](LICENSE)）。
第三方依赖许可见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)；
核心依赖仅接受宽松许可（MIT/BSD/ISC/Apache/公共领域）。
