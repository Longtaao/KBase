using KBase.Core;
using KBase.Infrastructure;

namespace KBase.Tests;

/// <summary>v0.2：双向链接、frontmatter、reindex</summary>
public class LinkAndReindexTests : IDisposable
{
    private readonly string _tmp;
    private readonly SqliteNoteRepository _repo;

    public LinkAndReindexTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "kbase-link-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
        _repo = new SqliteNoteRepository(_tmp);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { }
    }

    private string WriteFile(string relPath, string content)
    {
        var full = Path.Combine(_tmp, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    // ---------- frontmatter ----------

    [Fact]
    public void Create_文件带frontmatter标签()
    {
        _repo.Create("TCP三次握手", ["网络", "课程"], "正文内容");
        var file = File.ReadAllText(Path.Combine(_tmp, "TCP三次握手.md"));
        Assert.StartsWith("---\ntags:\n  - 网络\n  - 课程\n", file);
        Assert.Contains("正文内容", file);
    }

    [Fact]
    public void SplitFrontmatter_无frontmatter时整体是正文()
    {
        var (fm, body) = SqliteNoteRepository.SplitFrontmatter("纯正文\n第二行");
        Assert.Equal("", fm);
        Assert.Equal("纯正文\n第二行", body);
    }

    [Fact]
    public void ParseFrontmatterTags_解析列表()
    {
        var (_, body) = SqliteNoteRepository.SplitFrontmatter("---\ntags:\n  - 网络\n  - 课程\ncreated: 2026-08-06\n---\n\n正文");
        var tags = SqliteNoteRepository.ParseFrontmatterTags("tags:\n  - 网络\n  - 课程\ncreated: 2026-08-06");
        Assert.Equal(["网络", "课程"], tags);
        Assert.Equal("正文", body);
    }

    // ---------- wikilink ----------

    [Fact]
    public void ExtractWikiLinks_解析目标和显示文本()
    {
        var links = SqliteNoteRepository.ExtractWikiLinks("看 [[TCP三次握手]] 和 [[UDP协议|UDP]] 的内容");
        Assert.Equal(["TCP三次握手", "UDP协议"], links);
    }

    [Fact]
    public void ExtractWikiLinks_跳过图片嵌入()
    {
        var links = SqliteNoteRepository.ExtractWikiLinks("图片 ![[截图.png]] 和链接 [[笔记A]]");
        Assert.Single(links);
        Assert.Equal("笔记A", links[0]);
    }

    [Fact]
    public void Backlinks_返回引用者()
    {
        _repo.Create("TCP三次握手", [], "相关内容");
        _repo.Create("网络笔记", [], "详见 [[TCP三次握手]]");

        var links = _repo.Backlinks("TCP三次握手");
        Assert.Single(links);
        Assert.Equal("网络笔记", links[0].Title);
    }

    [Fact]
    public void Backlinks_大小写不敏感()
    {
        _repo.Create("TCP", [], "引用了 [[tcp]]");
        Assert.Single(_repo.Backlinks("TCP"));
    }

    [Fact]
    public void Graph_导出边()
    {
        _repo.Create("A", [], "[[B]] [[C]]");
        _repo.Create("B", [], "[[C]]");

        var edges = _repo.Graph();
        Assert.Equal(3, edges.Count);
        Assert.Contains(("A", "B"), edges);
        Assert.Contains(("B", "C"), edges);
    }

    // ---------- reindex ----------

    [Fact]
    public void Reindex_扫描新文件()
    {
        WriteFile("新笔记.md", "---\ntags:\n  - 手工\n---\n\n手动放的文件");

        var (added, updated, removed) = _repo.Reindex();
        Assert.Equal((1, 0, 0), (added, updated, removed));

        var note = _repo.FindByTitle("新笔记");
        Assert.NotNull(note);
        Assert.Equal(["手工"], note!.Tags);
    }

    [Fact]
    public void Reindex_检测文件修改()
    {
        _repo.Create("笔记", [], "旧内容");
        WriteFile("笔记.md", "---\n---\n\n新内容");

        var (added, updated, removed) = _repo.Reindex();
        Assert.Equal((0, 1, 0), (added, updated, removed));

        var hits = _repo.Search("新内容");
        Assert.Single(hits);
    }

    [Fact]
    public void Reindex_删除消失的文件()
    {
        _repo.Create("要删的", [], "内容");
        File.Delete(Path.Combine(_tmp, "要删的.md"));

        var (added, updated, removed) = _repo.Reindex();
        Assert.Equal((0, 0, 1), (added, updated, removed));
        Assert.Empty(_repo.List());
    }

    [Fact]
    public void Reindex_更新wikilink()
    {
        _repo.Create("源", [], "[[旧目标]]");
        WriteFile("源.md", "---\n---\n\n[[新目标]]");

        _repo.Reindex();
        var edges = _repo.Graph();
        Assert.Single(edges);
        Assert.Equal(("源", "新目标"), edges[0]);
    }

    [Fact]
    public void Reindex_幂等_重复执行无变化()
    {
        _repo.Create("A", [], "[[B]]");
        _repo.Create("B", [], "");

        var (added, updated, removed) = _repo.Reindex();
        Assert.Equal((0, 0, 0), (added, updated, removed));
    }
}
