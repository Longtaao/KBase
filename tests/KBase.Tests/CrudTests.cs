using KBase.Core;
using KBase.Infrastructure;

namespace KBase.Tests;

/// <summary>v0.3：Get / Update / Delete（Web UI 用）</summary>
public class CrudTests : IDisposable
{
    private readonly string _tmp;
    private readonly SqliteNoteRepository _repo;

    public CrudTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "kbase-crud-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
        _repo = new SqliteNoteRepository(_tmp);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { }
    }

    [Fact]
    public void Get_返回含正文的笔记()
    {
        var created = _repo.Create("笔记", ["标签"], "正文内容");
        var note = _repo.Get(created.Id);
        Assert.NotNull(note);
        Assert.Equal("正文内容", note!.Content);
        Assert.Equal(["标签"], note.Tags);
    }

    [Fact]
    public void Get_不存在的ID返回null()
    {
        Assert.Null(_repo.Get("no-such-id"));
    }

    [Fact]
    public void Update_修改内容标签和标题()
    {
        var created = _repo.Create("旧标题", ["旧标签"], "旧内容");
        _repo.Update(created.Id, "新标题", ["新标签"], "新内容");

        var note = _repo.Get(created.Id);
        Assert.Equal("新标题", note!.Title);
        Assert.Equal(["新标签"], note.Tags);
        Assert.Equal("新内容", note.Content);

        // 文件同步移动 + frontmatter 更新
        Assert.True(File.Exists(Path.Combine(_tmp, "新标题.md")));
        Assert.False(File.Exists(Path.Combine(_tmp, "旧标题.md")));
        Assert.Contains("新内容", File.ReadAllText(Path.Combine(_tmp, "新标题.md")));

        // 搜索索引同步
        Assert.Single(_repo.Search("新内容"));
    }

    [Fact]
    public void Update_不重名时保留原文件()
    {
        var created = _repo.Create("标题", [], "内容");
        _repo.Update(created.Id, "标题", [], "新内容");
        Assert.True(File.Exists(Path.Combine(_tmp, "标题.md")));
    }

    [Fact]
    public void Update_重名冲突自动加后缀()
    {
        var a = _repo.Create("同名", [], "A");
        var b = _repo.Create("同名 (2)", [], "B");
        _repo.Update(b.Id, "同名", [], "B新");

        var note = _repo.Get(b.Id);
        Assert.Equal("同名 (2).md", note!.Path);
        Assert.True(File.Exists(Path.Combine(_tmp, "同名 (2).md")));
    }

    [Fact]
    public void Update_更新wikilink索引()
    {
        var created = _repo.Create("源", [], "[[旧目标]]");
        _repo.Update(created.Id, "源", [], "[[新目标]]");
        var edges = _repo.Graph();
        Assert.Single(edges);
        Assert.Equal(("源", "新目标"), edges[0]);
    }

    [Fact]
    public void Delete_删除文件和索引()
    {
        var created = _repo.Create("要删", [], "内容 [[其他]]");
        var other = _repo.Create("其他", [], "");

        _repo.Delete(created.Id);

        Assert.False(File.Exists(Path.Combine(_tmp, "要删.md")));
        Assert.Null(_repo.Get(created.Id));
        Assert.Single(_repo.List());
        // 反向链接同步清除
        Assert.Empty(_repo.Backlinks("其他"));
        // FTS 同步清除
        Assert.Empty(_repo.Search("内容"));
    }

    [Fact]
    public void Delete_不存在的ID静默忽略()
    {
        _repo.Delete("no-such-id");
        Assert.Empty(_repo.List());
    }
}
