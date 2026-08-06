using System.CommandLine;
using KBase.Core;
using KBase.Infrastructure;

namespace KBase.Cli;

/// <summary>
/// KBase — 本地 Markdown 知识库管理器（MVP v0.1）
/// 用法：
///   kbase init                    初始化知识库（当前目录）
///   kbase new "标题" --tag 课程    新建笔记
///   kbase list [--tag 课程]        列出笔记
///   kbase open "标题"              打开笔记
///   kbase search 关键词            全文搜索
///   kbase stats                    统计
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var root = new RootCommand("KBase — 本地 Markdown 知识库管理器");

        // ---------- init ----------
        var initCmd = new Command("init", "初始化知识库（当前目录或 --path 指定）");
        var pathOpt = new Option<string>("--path") { Description = "知识库目录，默认当前目录" };
        initCmd.Add(pathOpt);
        initCmd.SetAction(parseResult =>
        {
            var dir = Path.GetFullPath(parseResult.GetValue(pathOpt) ?? Directory.GetCurrentDirectory());
            new SqliteNoteRepository(dir).Init();
            Console.WriteLine($"✅ 知识库已初始化：{dir}");
            Console.WriteLine($"   开始使用：kbase new \"标题\" --tag 标签");
            return 0;
        });

        // ---------- new ----------
        var newCmd = new Command("new", "新建笔记");
        var titleArg = new Argument<string>("title") { Description = "笔记标题" };
        var tagOpt = new Option<string[]>("--tag") { Description = "标签，可重复指定" };
        newCmd.Add(titleArg);
        newCmd.Add(tagOpt);
        newCmd.SetAction(parseResult =>
        {
            var title = parseResult.GetValue(titleArg)!;
            var tags = parseResult.GetValue(tagOpt) ?? [];
            var repo = RequireVault();
            var note = repo.Create(title, tags);
            Console.WriteLine($"✅ 已创建：{note.Path}");
            Console.WriteLine($"   标签：{(note.Tags.Count == 0 ? "(无)" : string.Join(", ", note.Tags))}");
            Console.WriteLine($"   打开编辑：kbase open \"{title}\"");
            return 0;
        });

        // ---------- list ----------
        var listCmd = new Command("list", "列出笔记（可按标签过滤）");
        var listTagOpt = new Option<string>("--tag") { Description = "只显示带此标签的笔记" };
        listCmd.Add(listTagOpt);
        listCmd.SetAction(parseResult =>
        {
            var tag = parseResult.GetValue(listTagOpt);
            var repo = RequireVault();
            var notes = repo.List(tag);
            if (notes.Count == 0)
            {
                Console.WriteLine(tag is null ? "📭 知识库还是空的，用 kbase new 创建第一篇吧"
                                              : $"📭 没有标签为「{tag}」的笔记");
                return 0;
            }
            Console.WriteLine($"📚 共 {notes.Count} 篇" + (tag is null ? "" : $"（标签: {tag}）"));
            foreach (var n in notes)
            {
                var tags = n.Tags.Count == 0 ? "" : $"  [{string.Join(", ", n.Tags)}]";
                Console.WriteLine($"  • {n.Title}{tags}");
            }
            return 0;
        });

        // ---------- open ----------
        var openCmd = new Command("open", "用默认编辑器打开笔记");
        var openArg = new Argument<string>("title") { Description = "笔记标题（支持模糊匹配）" };
        openCmd.Add(openArg);
        openCmd.SetAction(parseResult =>
        {
            var title = parseResult.GetValue(openArg)!;
            var repo = RequireVault();
            var note = repo.FindByTitle(title)
                       ?? repo.List().FirstOrDefault(n =>
                           n.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
            if (note is null)
            {
                Console.WriteLine($"❌ 找不到「{title}」，试试 kbase list 看看有哪些");
                return 1;
            }
            var file = Path.Combine(VaultRoot!, note.Path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file)
            {
                UseShellExecute = true
            });
            Console.WriteLine($"📖 已打开：{note.Title}");
            return 0;
        });

        // ---------- search ----------
        var searchCmd = new Command("search", "全文搜索（支持中文）");
        var queryArg = new Argument<string>("query") { Description = "搜索关键词" };
        searchCmd.Add(queryArg);
        searchCmd.SetAction(parseResult =>
        {
            var query = parseResult.GetValue(queryArg)!;
            var repo = RequireVault();
            var results = repo.Search(query);
            if (results.Count == 0)
            {
                Console.WriteLine($"🔍 没有找到与「{query}」相关的笔记");
                return 0;
            }
            Console.WriteLine($"🔍 「{query}」共 {results.Count} 条结果：");
            foreach (var n in results.Take(20))
                Console.WriteLine($"  • {n.Title}");
            if (results.Count > 20) Console.WriteLine($"  …还有 {results.Count - 20} 条");
            return 0;
        });

        // ---------- stats ----------
        var statsCmd = new Command("stats", "知识库统计");
        statsCmd.SetAction(_ =>
        {
            var repo = RequireVault();
            var (count, tags, latest) = repo.Stats();
            Console.WriteLine($"📊 知识库统计");
            Console.WriteLine($"   笔记数：{count}");
            Console.WriteLine($"   标签数：{tags.Count}");
            Console.WriteLine($"   最近更新：{latest:yyyy-MM-dd HH:mm}");
            if (tags.Count > 0)
                Console.WriteLine($"   热门标签：{string.Join("、", tags.Take(5).Select(t => $"{t.Name}({t.Count})"))}");
            return 0;
        });

        // ---------- backlinks ----------
        var backlinksCmd = new Command("backlinks", "查看谁引用了这篇笔记（反向链接）");
        var backArg = new Argument<string>("title") { Description = "笔记标题" };
        backlinksCmd.Add(backArg);
        backlinksCmd.SetAction(parseResult =>
        {
            var title = parseResult.GetValue(backArg)!;
            var repo = RequireVault();
            var links = repo.Backlinks(title);
            if (links.Count == 0)
            {
                Console.WriteLine($"🔗 还没有笔记引用「{title}」");
                Console.WriteLine($"   在别的笔记里写 [[{title}]] 就能建立链接");
                return 0;
            }
            Console.WriteLine($"🔗 有 {links.Count} 篇笔记引用了「{title}」：");
            foreach (var n in links)
                Console.WriteLine($"  ← {n.Title}");
            return 0;
        });

        // ---------- graph ----------
        var graphCmd = new Command("graph", "导出笔记关系图谱（JSON）");
        var outOpt = new Option<string>("--out") { Description = "输出到文件（默认打印到终端）" };
        graphCmd.Add(outOpt);
        graphCmd.SetAction(parseResult =>
        {
            var repo = RequireVault();
            var edges = repo.Graph();
            var allTitles = repo.List().Select(n => n.Title)
                                      .Concat(edges.Select(e => e.ToTitle))
                                      .Distinct()
                                      .OrderBy(t => t)
                                      .ToList();
            var payload = new
            {
                nodes = allTitles.Select(t => new { id = t, title = t }),
                edges = edges.Select(e => new { from = e.FromTitle, to = e.ToTitle })
            };
            var json = System.Text.Json.JsonSerializer.Serialize(payload,
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                });
            var outPath = parseResult.GetValue(outOpt);
            if (outPath is null)
            {
                Console.WriteLine(json);
            }
            else
            {
                File.WriteAllText(outPath, json);
                Console.WriteLine($"🕸️ 图谱已导出：{Path.GetFullPath(outPath)}");
            }
            return 0;
        });

        // ---------- reindex ----------
        var reindexCmd = new Command("reindex", "扫描 .md 文件，同步新增/修改/删除（含标签和链接）");
        reindexCmd.SetAction(_ =>
        {
            var repo = RequireVault();
            var (added, updated, removed) = repo.Reindex();
            Console.WriteLine($"🔄 重建索引完成：新增 {added}，更新 {updated}，删除 {removed}");
            return 0;
        });

        // ---------- serve ----------
        var serveCmd = new Command("serve", "启动本地 Web UI（浏览器管理笔记）");
        var portOpt2 = new Option<int>("--port") { Description = "端口，默认 8765", DefaultValueFactory = _ => 8765 };
        serveCmd.Add(portOpt2);
        serveCmd.SetAction(parseResult =>
        {
            var vault = FindVaultRoot(Directory.GetCurrentDirectory());
            if (vault is null)
            {
                Console.WriteLine("❌ 当前目录不是知识库（找不到 .kbase）。");
                Console.WriteLine("   先用 kbase init 初始化，或 cd 到知识库目录再试。");
                return 1;
            }
            var port = parseResult.GetValue(portOpt2);
            var webExe = Path.Combine(AppContext.BaseDirectory, "KBase.Web.exe");
            if (!File.Exists(webExe))
            {
                Console.WriteLine($"❌ 找不到 Web 组件：{webExe}");
                return 1;
            }
            var psi = new System.Diagnostics.ProcessStartInfo(webExe)
            {
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory,
            };
            psi.ArgumentList.Add("--vault");
            psi.ArgumentList.Add(vault);
            psi.ArgumentList.Add("--port");
            psi.ArgumentList.Add(port.ToString());
            System.Diagnostics.Process.Start(psi);
            Console.WriteLine($"🌐 KBase Web UI 已启动：http://localhost:{port}");
            Console.WriteLine($"   知识库：{vault}");
            Console.WriteLine("   停止：关掉弹出的 KBase.Web 窗口即可");
            return 0;
        });

        root.Add(initCmd);
        root.Add(serveCmd);
        root.Add(newCmd);
        root.Add(listCmd);
        root.Add(openCmd);
        root.Add(searchCmd);
        root.Add(statsCmd);
        root.Add(backlinksCmd);
        root.Add(graphCmd);
        root.Add(reindexCmd);

        return root.Parse(args).Invoke();
    }

    private static string? VaultRoot;

    /// <summary>向上查找 .kbase 目录，找到返回仓储实例；找不到报错退出。</summary>
    private static INoteRepository RequireVault()
    {
        if (VaultRoot is null)
        {
            VaultRoot = FindVaultRoot(Directory.GetCurrentDirectory());
            if (VaultRoot is null)
            {
                Console.WriteLine("❌ 当前目录不是知识库（找不到 .kbase）。");
                Console.WriteLine("   先用 kbase init 初始化，或 cd 到知识库目录再试。");
                Environment.Exit(1);
            }
        }
        return new SqliteNoteRepository(VaultRoot);
    }

    private static string? FindVaultRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, SqliteNoteRepository.MetaDir)))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
