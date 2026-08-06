namespace KBase.Core;

/// <summary>
/// 笔记模型。一张笔记 = 一个 Markdown 文件。
/// </summary>
public class Note
{
    /// <summary>稳定 ID，重命名文件不丢链接（v0.2 双向链接用）</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>标题，也是文件名（去掉非法字符后 + .md）</summary>
    public string Title { get; set; } = "";

    /// <summary>相对路径，如 课程/网络/TCP.md（v0.2 支持子目录）</summary>
    public string Path { get; set; } = "";

    /// <summary>标签列表，如 ["课程", "网络"]</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>Markdown 原文</summary>
    public string Content { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
