using KBase.Core;
using KBase.Infrastructure;

namespace KBase.Tests;

public class NoteRepositoryTests : IDisposable
{
    private readonly string _tmp;
    private readonly SqliteNoteRepository _repo;

    public NoteRepositoryTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "kbase-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
        _repo = new SqliteNoteRepository(_tmp);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { /* 测试清理失败可忽略 */ }
    }

    [Fact]
    public void Init_创建元数据目录和数据库()
    {
        _repo.Init();
        Assert.True(Directory.Exists(Path.Combine(_tmp, ".kbase")));
        Assert.True(File.Exists(Path.Combine(_tmp, ".kbase", "kbase.db")));
    }

    [Fact]
    public void Create_写入Markdown文件并入库()
    {
        var note = _repo.Create("TCP三次握手", ["网络", "课程"], "# TCP三次握手\n\nSYN → SYN-ACK → ACK");

        var file = Path.Combine(_tmp, "TCP三次握手.md");
        Assert.True(File.Exists(file));
        Assert.Contains("SYN", File.ReadAllText(file));

        var list = _repo.List();
        Assert.Single(list);
        Assert.Equal("TCP三次握手", list[0].Title);
        Assert.Equal(["网络", "课程"], list[0].Tags);
    }

    [Fact]
    public void Create_重名自动加后缀()
    {
        _repo.Create("笔记", []);
        var second = _repo.Create("笔记", []);
        Assert.Equal("笔记 (2).md", second.Path);
    }

    [Fact]
    public void Create_非法文件名字符被替换()
    {
        var note = _repo.Create("A/B:C*D", []);
        Assert.True(note.Path.IndexOfAny(Path.GetInvalidFileNameChars()) < 0, "文件名不应含非法字符");
        Assert.True(File.Exists(Path.Combine(_tmp, note.Path)));
    }

    [Fact]
    public void List_按标签过滤()
    {
        _repo.Create("网络笔记", ["网络"]);
        _repo.Create("课程笔记", ["课程"]);
        _repo.Create("无标签", []);

        Assert.Equal(3, _repo.List().Count);
        var tagged = _repo.List("网络");
        Assert.Single(tagged);
        Assert.Equal("网络笔记", tagged[0].Title);
    }

    [Fact]
    public void Search_中文两字词LIKE兜底()
    {
        _repo.Create("拥塞控制", ["网络"], "TCP 拥塞控制算法：慢启动、拥塞避免");
        _repo.Create("其他", [], "无关内容");

        var hits = _repo.Search("拥塞");
        Assert.Single(hits);
        Assert.Equal("拥塞控制", hits[0].Title);
    }

    [Fact]
    public void Search_长词走FTS5()
    {
        _repo.Create("TCP拥塞控制", ["网络"], "慢启动和拥塞避免算法详解");
        _repo.Create("HTTP协议", ["网络"], "超文本传输协议");

        var hits = _repo.Search("拥塞避免");
        Assert.Single(hits);
        Assert.Equal("TCP拥塞控制", hits[0].Title);
    }

    [Fact]
    public void FindByTitle_忽略大小写()
    {
        _repo.Create("TCP协议", []);
        Assert.NotNull(_repo.FindByTitle("tcp协议"));
        Assert.Null(_repo.FindByTitle("UDP协议"));
    }

    [Fact]
    public void Stats_统计正确()
    {
        _repo.Create("A", ["课程", "网络"]);
        _repo.Create("B", ["课程"]);
        _repo.Create("C", []);

        var (count, tags, _) = _repo.Stats();
        Assert.Equal(3, count);
        Assert.Equal(2, tags.Count);
        Assert.Equal("课程", tags[0].Name);
        Assert.Equal(2, tags[0].Count);
    }

    [Fact]
    public void Create_未指定标签也正常()
    {
        var note = _repo.Create("无标签笔记", []);
        Assert.Empty(note.Tags);
        Assert.Single(_repo.List());
    }
}
