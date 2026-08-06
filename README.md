# KBase — 本地 Markdown 知识库管理器

> 📚 用 C#/.NET 8 写的本地优先 Markdown 知识库 CLI。
> Markdown 文件存储（Obsidian 标准语法，数据永不锁定）+ SQLite FTS5 全文索引（中文搜索秒出结果）。

## ✨ 功能（v0.1 MVP）

| 命令 | 说明 |
|------|------|
| `kbase init` | 初始化知识库（当前目录或 `--path` 指定） |
| `kbase new "标题" --tag 标签` | 新建笔记（标签写入 YAML frontmatter，Obsidian 兼容） |
| `kbase list [--tag 标签]` | 列出笔记，可按标签过滤 |
| `kbase open "标题"` | 用默认编辑器打开笔记（支持模糊匹配） |
| `kbase search 关键词` | FTS5 全文搜索，天然支持中文 |
| `kbase backlinks "标题"` | 反向链接：谁引用了这篇笔记 |
| `kbase graph [--out 文件]` | 导出笔记关系图谱（JSON，含悬空节点） |
| `kbase reindex` | 扫描 .md 文件，增量同步新增/修改/删除 |
| `kbase stats` | 统计：笔记数、标签数、热门标签、最近更新 |

## 🚀 快速开始

```bash
# 1. 建一个知识库目录
mkdir my-notes && cd my-notes

# 2. 初始化
kbase init

# 3. 写笔记
kbase new "TCP三次握手" --tag 网络 --tag 课程
kbase open "TCP三次握手"        # 用默认编辑器打开

# 4. 搜索（中文两字词也能搜）
kbase search 拥塞

# 5. 管理
kbase list --tag 网络
kbase stats
```

## ✨ 双向链接（v0.2）

- 笔记正文写 `[[目标标题]]` 或 `[[目标标题|显示文字]]` 即可建立链接
- `kbase backlinks "标题"` 查看谁引用了它；`kbase graph` 导出 JSON 图谱
- 标签存在笔记的 YAML frontmatter（`tags:` 列表），任何编辑器手改都行
- 用编辑器直接改文件后运行 `kbase reindex` 同步索引（增/删/改都自动识别）

```markdown
---
tags:
  - 网络
  - 课程
created: 2026-08-06
---

# TCP三次握手

SYN → SYN-ACK → ACK，详见 [[网络基础]]
```

## 🧱 设计

```
src/
├── KBase.Core/            # 领域模型 + 接口（Note、Tag、INoteRepository）
├── KBase.Infrastructure/  # SQLite 实现（FTS5 + trigram 分词）
└── KBase.Cli/             # CLI 入口（System.CommandLine）
tests/
└── KBase.Tests/           # xUnit 单元测试
```

### 核心决策

- **Markdown 文件即笔记**：每篇笔记就是一个 `.md` 文件，任何编辑器/工具都能直接打开，永不锁定
- **YAML frontmatter 存标签**：Obsidian 标准写法，`reindex` 时恢复标签
- **SQLite FTS5 索引**：`trigram` 分词器天然支持中文子串匹配；短查询（<3 字符）自动降级 LIKE，保证"拥塞"这类二字词能搜到
- **`.kbase/` 元数据目录**：数据库放在知识库根目录的 `.kbase/kbase.db`，向上查找——在任何子目录都能用 kbase 命令
- **标签存索引不存文件**：标签在 SQLite 里，`.md` 文件保持纯净

## 🗺️ 路线图

- [x] **v0.1** MVP：init / new / list / open / search / stats
- [x] **v0.2** 双向链接 `[[wikilink]]` + 反向链接 + 图谱导出 + frontmatter 标签 + reindex
- [ ] **v0.3** Web UI（ASP.NET Core）
- [ ] **v0.4** 本地 AI 问答（RAG，nomic-embed-text）

## 🛠️ 开发

```bash
dotnet build KBase.sln
dotnet test
dotnet run --project src/KBase.Cli -- init
```

## 📄 技术栈

- .NET 8 (C# 12)
- Microsoft.Data.Sqlite (FTS5)
- System.CommandLine
- Markdig（v0.2 解析 wikilink 用）
- xUnit

---

Made with ❤️ by Longtaao
