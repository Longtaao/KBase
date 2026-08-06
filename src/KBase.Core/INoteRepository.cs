namespace KBase.Core;

/// <summary>
/// 笔记仓储接口。MVP 由 SQLite 实现，以后可换其他存储。
/// </summary>
public interface INoteRepository
{
    /// <summary>初始化知识库（建目录 + 建表 + 索引）</summary>
    void Init();

    /// <summary>新建笔记。返回创建后的 Note（含 ID）。</summary>
    Note Create(string title, IEnumerable<string> tags, string content = "");

    /// <summary>列出笔记，可按标签过滤。</summary>
    List<Note> List(string? tag = null);

    /// <summary>按标题精确查找（用于 open）。找不到返回 null。</summary>
    Note? FindByTitle(string title);

    /// <summary>FTS5 全文搜索（支持中文，trigram 分词）。</summary>
    List<Note> Search(string query);

    /// <summary>统计：笔记数、标签数、最近更新。</summary>
    (int NoteCount, List<Tag> Tags, DateTime LatestUpdate) Stats();

    // ---- v0.2 双向链接 ----

    /// <summary>查询引用指定标题的所有笔记（反向链接）。</summary>
    List<Note> Backlinks(string title);

    /// <summary>所有 [[链接]] 边（From 标题 → To 标题），用于图谱导出。</summary>
    List<(string FromTitle, string ToTitle)> Graph();

    /// <summary>
    /// 增量重建索引：扫描 .md 文件，同步新增/修改/删除到数据库，
    /// 解析 YAML frontmatter（标签）和 [[wikilink]]。
    /// </summary>
    (int Added, int Updated, int Removed) Reindex();

    // ---- v0.3 Web UI ----

    /// <summary>按 ID 取笔记（含正文）。</summary>
    Note? Get(string id);

    /// <summary>更新笔记（标题/标签/正文）。重命名时同步移动文件。</summary>
    void Update(string id, string title, IEnumerable<string> tags, string content);

    /// <summary>删除笔记（文件 + 索引）。</summary>
    void Delete(string id);
}
