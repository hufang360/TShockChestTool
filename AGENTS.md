# AGENTS.md

本文件给在本仓库工作的 AI 代理看。保持精简。

## changelog 编写方法（先看这条）

- 文件：根目录 `changelog.md`。
- **原则（重要）：只记「对外净变化」**
  - 判断边界是「是否已 push 到远端 / 发过版」。**两个还未对外的版本之间的调整不记录**
    （改名、先加后删、内部重构、来回改 Bug 都算内部迭代）。
  - changelog 描述的是「相对**上一个已发布版本**」的变化，不是「相对上一次本地提交」。
  - 确认已发布版本：`git log --oneline origin/master -1`（或 `git tag --list`）。
    当前已发布：`v2.0.0`。
- 条目：无序列表，一条一事；太长就换行拆成子条目，用两级缩进 `-` / `  -`。
- 排版（重要）：
  - 只允许两种标题：首行 `# 更新日志`，以及版本标题 `## 20261019（v2.0.0）`；
  - **版本内不写 `###` 大标题**，改用一级列表项当分组名（如 `- 查找` / `- 统计`），
    具体内容缩进一层；这样比一堆标题更紧凑、更好扫读；
  - 篇幅尽量短：整个「未发布」段控制在 30~40 行左右，能一句说清就不拆；
  - 举例子才用第三层缩进，且只留最必要的 1~2 个例子。
- 风格（重要）：
  - 一行别太长（尽量 50 字内），偏结构化，方便阅读；
  - 通俗易懂，少堆术语（读者包括小白），术语就地解释一句；
  - 写清「做了什么、影响是什么」，多用指令/配置示例说明。

## 构建与产物

- 版本号取自 `ChestTool.csproj` 的 `<Version>`；改版本时同步 `changelog.md` 顶部 `## YYYYMMDD（vX.Y.Z）`。
- 构建：`dotnet build ChestTool.csproj -c Release`，产物 `bin/Release/net9.0/ChestTool.dll`。
- **内部测试版 dll 统一放根目录 `Publish/`**（已在 `.gitignore`，不提交），
  命名 `ChestTool-v{版本}-{MMdd}.dll`，如 `ChestTool-v2.0.0-1019.dll`（`1019` 表示 10 月 19 日）：

  ```bash
  dotnet build ChestTool.csproj -c Release
  cp -f bin/Release/net9.0/ChestTool.dll Publish/ChestTool-v2.0.0-1019.dll
  ```

## 发布

- 版本号 `ChestTool.csproj` 的 `<Version>`、`changelog.md` 顶部日期版本、git tag、
  release 附件名四者保持一致（如 `2.0.0` / `20261019（v2.0.0）` / `v2.0.0` / `ChestTool-v2.0.0.dll`）。
- 流程：提交推送 → `git tag v2.0.0` → `git push origin v2.0.0` →
  `gh release create v2.0.0 Publish/ChestTool-v2.0.0.dll --title 2.0.0 --notes-file <(sed ...)`。
- release 说明直接用 `changelog.md` 里对应版本那一节（去掉版本标题行）。
