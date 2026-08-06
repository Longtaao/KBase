# 📖 KBase 使用说明书

> KBase — 本地 Markdown 知识库管理器（C#/.NET 8）
> 版本：v0.3 | GitHub：https://github.com/Longtaao/KBase

---

## 一、KBase 是什么？

一个**本地优先**的 Markdown 笔记管理工具，用 C#/.NET 8 编写。

- 📄 **笔记就是普通 .md 文件** — 任何编辑器都能打开，数据永不锁定
- 🔍 **SQLite FTS5 全文索引** — 中文搜索秒出结果（"拥塞"两字词也能搜）
- 🔗 **双向链接** — `[[笔记标题]]` 互相关联，一键看反向链接
- 🌐 **Web UI** — 浏览器里管理笔记，手机/电脑都能用
- 🏷️ **标签系统** — YAML frontmatter 存标签（Obsidian 同款语法）

## 二、安装与构建

```bash
# 需要 .NET 8 SDK
git clone https://github.com/Longtaao/KBase.git
cd KBase
dotnet build KBase.sln
```

构建后：
- CLI 程序：`src/KBase.Cli/bin/Debug/net8.0/KBase.Cli.exe`
- Web 程序：`src/KBase.Web/bin/Debug/net8.0/KBase.Web.exe`

## 三、快速上手（3 步）

```bash
# 1️⃣ 建一个知识库目录（比如 D:\我的笔记）
mkdir D:\我的笔记 && cd D:\我的笔记

# 2️⃣ 初始化
kbase init

# 3️⃣ 写第一篇笔记
kbase new "TCP三次握手" --tag 网络 --tag 课程
```

## 四、CLI 命令大全

### kbase init — 初始化知识库
```bash
kbase init                    # 在当前目录初始化
kbase init --path D:\笔记     # 在指定目录初始化
```
会在目录下创建 `.kbase/` 文件夹（数据库和索引都在里面）。

### kbase new — 新建笔记
```bash
kbase new "标题"                      # 无标签
kbase new "标题" --tag 课程 --tag 网络 # 多个标签
```
生成的笔记文件带 YAML frontmatter：
```markdown
---
tags:
  - 课程
  - 网络
created: 2026-08-06
---

（正文写在这里）
```

### kbase list — 列出笔记
```bash
kbase list              # 全部（按最近更新排序）
kbase list --tag 网络   # 只看某个标签
```

### kbase open — 打开笔记
```bash
kbase open "TCP三次握手"   # 用系统默认编辑器打开
```
支持模糊匹配，比如 `kbase open TCP`。

### kbase search — 全文搜索
```bash
kbase search 拥塞        # 中文两字词 OK
kbase search 三次握手    # 长词走 FTS5 索引
```

### kbase backlinks — 反向链接
```bash
kbase backlinks "TCP三次握手"
# 输出：哪些笔记里写了 [[TCP三次握手]]
```

### kbase graph — 笔记关系图谱
```bash
kbase graph                      # 打印 JSON 到终端
kbase graph --out graph.json     # 导出到文件
```
JSON 结构：`nodes`（节点）+ `edges`（谁链接了谁），可喂给任意图谱可视化工具。

### kbase reindex — 同步文件变化
```bash
kbase reindex
```
用其他编辑器（或 Obsidian）直接改了 .md 文件后运行它：
- 新文件 → 自动入库
- 修改 → 更新内容/标签/链接索引
- 删除 → 移除索引

### kbase stats — 统计
```bash
kbase stats
# 笔记数、标签数、最近更新、热门标签
```

### kbase serve — 启动 Web UI
```bash
kbase serve                # 默认端口 8765
kbase serve --port 9000    # 自定义端口
```
浏览器打开 **http://localhost:8765** 使用图形界面。

## 五、Web UI 使用

| 功能 | 操作 |
|------|------|
| 搜索笔记 | 顶部搜索框，支持中文 |
| 按标签浏览 | 点标签徽章过滤 |
| 看笔记 | 点击列表项，Markdown 渲染 + 反向链接侧栏 |
| 新建 | 右上角"➕ 新建笔记" |
| 编辑 | 详情页"✏️ 编辑"（标题/标签/正文） |
| 删除 | 详情页"🗑️ 删除"（有确认提示） |
| 笔记互跳 | 正文里的 `[[笔记标题]]` 直接点击跳转 |

## 六、核心概念

### 双向链接（wikilink）
```markdown
# 在任意笔记里写：
详细内容见 [[TCP三次握手]]
带显示文字的写法：[[TCP三次握手|三次握手过程]]
```
- `kbase backlinks` 和 Web 详情页会显示"谁引用了我"
- 引用了但还不存在的笔记 → graph 里显示为悬空节点

### 标签
- 存在笔记开头的 YAML frontmatter 里（Obsidian 兼容）
- 用编辑器直接改 frontmatter 的 tags，`kbase reindex` 后生效
- 标签里不要用英文逗号（逗号是分隔符）

### 数据存放
```
D:\我的笔记\
├── 笔记1.md          ← 你的笔记文件（可自由编辑）
├── 笔记2.md
└── .kbase\           ← 索引数据库（别手动改）
    └── kbase.db
```
**备份 = 拷贝整个文件夹**。删掉 .kbase 不会丢笔记，`kbase reindex` 会重建索引（但标签会丢失，因为标签在索引里——建议备份整个文件夹）。

## 七、常见问题

**Q：搜索中文没结果？**
A：两字词走 LIKE 兜底应该能搜到。如果改过文件，先 `kbase reindex`。

**Q：用 Obsidian 打开知识库文件夹可以吗？**
A：可以！笔记格式完全兼容（frontmatter 标签 + wikilink 都是 Obsidian 标准）。Obsidian 里写笔记，KBase 里搜索/管理，两边自动同步（改完跑 `kbase reindex`）。

**Q：kbase 命令提示"不是知识库"？**
A：先 `cd` 到知识库目录（有 `.kbase` 的目录），或先 `kbase init`。

**Q：Web UI 打不开？**
A：确认 `kbase serve` 的输出显示"已启动"，浏览器访问 http://localhost:8765 ；换端口用 `kbase serve --port 9000`。

**Q：想删除一篇笔记？**
A：CLI 删文件 + `kbase reindex`，或 Web UI 详情页的删除按钮。

## 八、路线图

| 版本 | 内容 | 状态 |
|------|------|------|
| v0.1 | CLI 核心：init/new/list/open/search/stats | ✅ 2026-08-06 |
| v0.2 | 双向链接 + 反向链接 + 图谱 + frontmatter + reindex | ✅ 2026-08-06 |
| v0.3 | Web UI（kbase serve） | ✅ 2026-08-06 |
| v0.4 | 本地 AI 问答（RAG，nomic-embed-text） | 📅 规划中 |

## 九、开发信息

```bash
dotnet build KBase.sln    # 编译
dotnet test               # 跑测试（31 个）
```

```
src/
├── KBase.Core/            # 领域模型 + 接口
├── KBase.Infrastructure/  # SQLite 实现（FTS5 + links）
├── KBase.Cli/             # 命令行入口
└── KBase.Web/             # Web UI（ASP.NET Core Razor Pages）
tests/
└── KBase.Tests/           # xUnit 测试
```

---

*Made with ❤️ by Longtaao & 若若 — 2026-08-06*
